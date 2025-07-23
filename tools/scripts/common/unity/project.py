"""
Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
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
from .injected_script import ProjectBuildConfiguration, InjectedScript, InjectedScriptContext


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
    def injected_scripts(self, cs_relpaths: List[str]) -> Generator[None, None, None]:
        # Early-out if we have no scripts to inject
        if not cs_relpaths:
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
        
        # Load the source of all scripts we want to inject into the project, rendering
        # templates etc. as necessary
        scripts: List[InjectedScript] = []
        for relpath in cs_relpaths:
            script = InjectedScript.load(relpath, build_config)
            scripts.append(script)

        # Defer to InjectedScriptContext, which will manage creation and deletion of
        # all required files and directories upon enter and exit
        with InjectedScriptContext(self.path, scripts):
            yield
        
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
