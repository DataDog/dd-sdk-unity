"""
Code for temporarily injecting C# build scripts into a Unity project.

Unity does not have an externally-configurable build system, nor does it provide an
out-of-the-box means of building a project from the command-line. Instead, automating
build tasks is accomplished by writing editor-only scripts within the Unity project
itself, then running the Unity editor in batch mode to invoke those scripts.

Managing these scripts on a per-project basis is cumbersome and error-prone, so we
instead inject .cs (and .cs.meta) files into the project for the lifetime of the build.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import os
import shutil
import time
import uuid
from dataclasses import dataclass, field
from types import TracebackType
from typing import List, Optional, Type, Set, Literal

from pydantic import BaseModel
from ruamel.yaml import YAML
from jinja2 import Template

from ..log import get_default_logger

__injected_src_dir__ = os.path.join(os.path.dirname(__file__), 'injected')


class ProjectBuildConfiguration(BaseModel):
    scenes: List[str]

    @classmethod
    def require(cls, path: str) -> 'ProjectBuildConfiguration':
        with open(path) as fp:
            data = YAML().load(fp)
        return cls(**data)


@dataclass
class InjectedScript:
    relpath: str
    text: str

    @classmethod
    def load(cls, relpath: str, build_config: ProjectBuildConfiguration) -> 'InjectedScript':
        src_cs_path = os.path.join(__injected_src_dir__, relpath)
        src_cs_template_path = src_cs_path + '.jinja'
        if os.path.isfile(src_cs_template_path):
            with open(src_cs_template_path) as fp:
                template = Template(fp.read())
            text = template.render(build_config=build_config)
            return cls(relpath, text)
        
        if not os.path.isfile(src_cs_path):
            raise RuntimeError(f'No source file found for build script at {src_cs_path}')
        with open(src_cs_path) as fp:
            text = fp.read()
        return cls(relpath, text)


@dataclass
class InjectedScriptContext:
    project_path: str
    scripts: List[InjectedScript]

    _directory_relpaths_to_remove: Set[str] = field(default_factory=set)
    _file_relpaths_to_remove: Set[str] = field(default_factory=set)

    def _parent_will_be_removed(self, relpath: str) -> bool:
        for dir_relpath in self._directory_relpaths_to_remove:
            if relpath.startswith(dir_relpath + os.sep):
                return True
        return False

    def __enter__(self) -> None:
        log = get_default_logger()

        # Clear state on enter
        self._directory_relpaths_to_remove = set()
        self._file_relpaths_to_remove = set()

        # Before making any filesystem changes, ensure that we're not going to
        # overwrite any existing files in the project
        for script in self.scripts:
            dst_cs_path = os.path.join(self.project_path, script.relpath)
            if os.path.exists(dst_cs_path):
                raise RuntimeError(f'Injected script file already exists: {dst_cs_path}')
        
        # Collect a list of all directories (relative to the project root) that we
        # might need to create in order to contain our scripts
        dst_directories: Set[str] = set()
        for script in self.scripts:
            dir_relpath = os.path.dirname(script.relpath)
            while dir_relpath:
                dst_directories.add(dir_relpath)
                dir_relpath = os.path.dirname(dir_relpath)

        # Iterate over those directories in lexicographical, parent-first order: if the
        # target directory already exists in the project, that's fine; but if not,
        # create it (along with a .meta file) and record it for deletion later
        for dir_relpath in sorted(dst_directories):
            dst_dir = os.path.join(self.project_path, dir_relpath)
            if not os.path.exists(dst_dir):
                log.info(f'Creating temporary script dir: {dst_dir}')
                os.mkdir(dst_dir)
                if not self._parent_will_be_removed(dir_relpath):
                    self._directory_relpaths_to_remove.add(dir_relpath)

                log.info(f'Creating .meta file for temporary script dir: {dst_dir}')
                dst_meta_path = dst_dir + '.meta'
                write_asset_metadata(dst_meta_path)
                if not self._parent_will_be_removed(dir_relpath + '.meta'):
                    self._file_relpaths_to_remove.add(dir_relpath + '.meta')

        # We've now ensured that for each target file, the parent directory exists, and
        # no existing file is present in the project, so we can proceed with writing
        # each .cs file and its accompanying .cs.meta
        for script in self.scripts:
            dst_cs_path = os.path.join(self.project_path, script.relpath)
            log.info(f'Creating temporary script file: {dst_cs_path}')
            with open(dst_cs_path, 'w') as fp:
                fp.write(script.text)
            if not self._parent_will_be_removed(script.relpath):
                self._file_relpaths_to_remove.add(script.relpath)

            dst_cs_meta_path = dst_cs_path + '.meta'
            log.info(f'Creating .meta file for temporary script file: {dst_cs_meta_path}')
            write_asset_metadata(dst_cs_meta_path)
            if not self._parent_will_be_removed(script.relpath + '.meta'):
                self._file_relpaths_to_remove.add(script.relpath + '.meta')

    def __exit__(self, exc_type: Optional[Type[BaseException]], exc_val: Optional[BaseException], exc_tb: Optional[TracebackType]) -> Literal[False]:
        log = get_default_logger()
        for file_relpath in self._file_relpaths_to_remove:
            file_path = os.path.join(self.project_path, file_relpath)
            log.info(f'Deleting temporary file: {file_path}')
            os.remove(file_path)
        for dir_relpath in self._directory_relpaths_to_remove:
            dir_path = os.path.join(self.project_path, dir_relpath)
            log.info(f'Deleting temporary script directory: {dir_path}')
            shutil.rmtree(dir_path)
        return False


def write_asset_metadata(meta_path: str):
    lines = [f'{key}: {value}' for key, value in [
        ('fileFormatVersion', 2),
        ('guid', uuid.uuid4().hex),
        ('timeCreated', int(time.time())),
    ]]
    with open(meta_path, 'w') as fp:
        fp.write('\n'.join(lines) + '\n')
