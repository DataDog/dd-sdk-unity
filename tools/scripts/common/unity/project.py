import os
import sys
import shutil
from pathlib import Path
from contextlib import contextmanager
from dataclasses import dataclass
from typing import Optional, List, Generator

from ruamel.yaml import YAML

from ..log import get_default_logger

from .install import UnityInstall, resolve_unity_install, UnityVersion, UnityLicenseStatus
from .hub import UnityHub
from .asset import AssetModification, AssetRevertFunc
from .buildscript import BuildScriptTemplate, InjectedBuildScript, ProjectBuildConfiguration
from .runtimescript import RuntimeScript, InjectedRuntimeScript


@dataclass
class UnityProject:
    path: str
    editor: UnityInstall

    def run(self, *args: str):
        log = get_default_logger()
        result = self.editor.run_batchmode(self.path, '-quit', *args)
        if result.exitcode == 0:
            log.info('Unity command finished successfully.')
        elif result.license_status != UnityLicenseStatus.VALID:
            log.error('Unity failed to acquire a license.')
            sys.exit(86)
        else:
            raise RuntimeError(f'Unity build exited with status code {result.exitcode}')

    @contextmanager
    def modified_assets(self, modifications: List[AssetModification]) -> Generator[None, None, None]:
        modifications = AssetModification.merge(modifications)
        revert_funcs: List[AssetRevertFunc] = []
        try:
            for mod in modifications:
                revert_func = mod.apply(self.path)
                revert_funcs.append(revert_func)
            yield
        finally:
            for revert_func in revert_funcs:
                revert_func()

    @contextmanager
    def injected_scripts(self, build_scripts: List[BuildScriptTemplate], runtime_scripts: List[RuntimeScript]) -> Generator[None, None, None]:
        with self.injected_build_scripts(build_scripts):
            with self.injected_runtime_scripts(runtime_scripts):
                yield

    @contextmanager
    def injected_build_scripts(self, scripts: List[BuildScriptTemplate]) -> Generator[None, None, None]:
        # Early-out if we have no build scripts to inject
        if not scripts:
            yield
            return

        # A DatadogBuild.yml file in the root of the project indicates that it supports
        # injected Datadog build scripts, and provides project-specific details like
        # the scenes to include in the build: load that config
        build_config_path = os.path.join(self.path, 'DatadogBuild.yml')
        if not os.path.isfile(build_config_path):
            raise RuntimeError(f'Project {os.path.basename(self.path)} can not be used with injected build scripts; missing configuration file at {build_config_path}')
        build_config = ProjectBuildConfiguration.require(build_config_path)

        # Require that the project has a preexisting Assets directory
        assets_dir = os.path.join(self.path, 'Assets')
        if not os.path.isdir(assets_dir):
            raise RuntimeError(f'Project has no Assets dir: {assets_dir}')
        
        # Check for an existing Assets/Editor directory, and store our injected
        # Editor-only scripts in a DatadogBuild subdirectory
        assets_editor_dir = os.path.join(assets_dir, 'Editor')
        datadog_build_dir = os.path.join(assets_editor_dir, 'DatadogBuild')
        if os.path.isdir(datadog_build_dir):
            raise RuntimeError(f'Project has an existing directory for injected editor-only build scripts: {datadog_build_dir}')

        # If Assets/Editor doesn't exist, create it (and clean it up when finished)
        directory_to_recursively_delete = ''
        if not os.path.isdir(assets_editor_dir):
            os.mkdir(assets_editor_dir)
            directory_to_recursively_delete = assets_editor_dir
        
        # Create Assets/Editor/DatadogBuild, and prepare to clean it up unless we're
        # already prepared to delete its parent directory
        try:
            os.mkdir(datadog_build_dir)
            if not directory_to_recursively_delete:
                directory_to_recursively_delete = datadog_build_dir
        except:
            if directory_to_recursively_delete == assets_editor_dir:
                os.rmdir(assets_editor_dir)
            raise

        # Our transient script directory is ready: write the scripts, then yield, and
        # finally clean everything up when finished
        try:
            injected_scripts = [InjectedBuildScript.new(s, datadog_build_dir) for s in scripts]
            for script in injected_scripts:
                script.write(build_config)
            yield
        finally:
            if directory_to_recursively_delete:
                assert Path(directory_to_recursively_delete).is_relative_to(Path(assets_dir))
                shutil.rmtree(directory_to_recursively_delete)

                directory_meta_path = directory_to_recursively_delete + '.meta'
                if os.path.isfile(directory_meta_path):
                    os.remove(directory_meta_path)
    
    @contextmanager
    def injected_runtime_scripts(self, scripts: List[RuntimeScript]) -> Generator[None, None, None]:
        # Early-out if there's nothing to inject
        if not scripts:
            yield
            return

        # Require that the project has a preexisting Assets directory
        assets_dir = os.path.join(self.path, 'Assets')
        if not os.path.isdir(assets_dir):
            raise RuntimeError(f'Project has no Assets dir: {assets_dir}')
        
        # Use a throwaway directory to contain runtime scripts injected by Datadog
        # build automation
        injected_scripts_dir_name = 'DatadogBuildRuntimeScripts'
        injected_scripts_dir = os.path.join(assets_dir, injected_scripts_dir_name)
        if os.path.isdir(injected_scripts_dir):
            raise RuntimeError(f'Project has an existing directory for injected runtime scripts: {injected_scripts_dir}')
        os.mkdir(injected_scripts_dir)

        # Write each of our scripts into the project, yield, then delete the entire
        # directory on exit
        try:
            injected_scripts = [InjectedRuntimeScript.new(s, injected_scripts_dir) for s in scripts]
            for script in injected_scripts:
                script.write()
            yield
        finally:
            shutil.rmtree(injected_scripts_dir)
        
        
    @classmethod
    def resolve(cls, path: str, preferred_unity_version_prefix: Optional[str] = None) -> 'UnityProject':
        # Require that the ProjectVersion.txt file exists
        project_version_path = os.path.join(path, 'ProjectSettings', 'ProjectVersion.txt')
        if not os.path.isfile(project_version_path):
            raise RuntimeError(f'Unity project not found: {project_version_path} does not exist')

        # Get a list of Unity installations from Unity Hub
        hub = UnityHub.require()
        installs = hub.list_installs()

        # If we want to use a specific version of Unity, override ProjectVersion.txt
        if preferred_unity_version_prefix:
            install = resolve_unity_install(installs, preferred_unity_version_prefix)
            if not install:
                raise RuntimeError(f'No installed version of Unity matches {preferred_unity_version_prefix}')
            return UnityProject(path=path, editor=install)
        
        # Otherwise, read ProjectVersion.txt to get the version the project was
        # authored with
        with open(project_version_path) as fp:
            data = YAML().load(fp)
        editor_version_str = data.get('m_EditorVersion', '')
        if not editor_version_str:
            raise RuntimeError(f'Failed to read m_EditorVersion from {project_version_path}')
        editor_version = UnityVersion.parse(editor_version_str)

        # Find a Unity install that exactly matches that version
        install = resolve_unity_install(installs, str(editor_version))
        if not install:
            raise RuntimeError(f'Unity project {os.path.basename(path)} requires Unity {editor_version}, but no such version of Unity is installed')
        
        return UnityProject(path=path, editor=install)
