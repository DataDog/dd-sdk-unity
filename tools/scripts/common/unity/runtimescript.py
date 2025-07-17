"""
Code for temporarily injecting C# build scripts into a Unity project.

Unity does not have an externally-configurable build system, nor does it provide an
out-of-the-box means of building a project from the command-line. Instead, automating
build tasks is accomplished by writing editor-only scripts within the Unity project
itself, then running the Unity editor in batch mode to invoke those scripts.

Managing these scripts on a per-project basis is cumbersome and error-prone, so we
instead inject .cs (and .cs.meta) files into the project for the lifetime of the build.
"""
import os
import shutil
from dataclasses import dataclass
from enum import Enum
from typing import List

from .asset import write_asset_metadata


class RuntimeScript(str, Enum):
    INTEGRATION_TEST_RUNNER_CS = 'IntegrationTestRunner.cs'


@dataclass
class InjectedRuntimeScript:
    src_path: str
    dst_path: str

    def write(self) -> List[str]:
        # Make sure that our target directory exists but the files we're about to write
        # do not: the caller should make sure the destination directory is clean
        cs_meta_path = self.dst_path + '.meta'
        assert not os.path.isfile(self.dst_path), f'Runtime script file already exists: {self.dst_path}'
        assert not os.path.isfile(cs_meta_path), f'Runtime script file already exists: {cs_meta_path}'
        assert os.path.isdir(os.path.dirname(self.dst_path)), f'Runtime script directory does not exist: {os.path.dirname(self.dst_path)}'

        # Write the .cs source file
        shutil.copy(self.src_path, self.dst_path)        

        # Write the accompanying .cs.meta file
        try:
            write_asset_metadata(cs_meta_path)
        except:
            # Don't leave a dangling .cs file if we fail
            os.remove(self.dst_path)
            raise

        # Return the paths to both files so they can be cleaned up
        return [self.dst_path, cs_meta_path]
    
    @classmethod
    def new(cls, script_filename: RuntimeScript, injected_script_dir: str) -> 'InjectedRuntimeScript':
        templates_dir = os.path.join(os.path.dirname(__file__), 'runtimescripts')
        src_path = os.path.join(templates_dir, script_filename)
        if not os.path.isfile(src_path):
            raise RuntimeError(f'RuntimeScript {script_filename.value} has no source file at {src_path}')
        dst_path = os.path.join(injected_script_dir, script_filename.value)
        return cls(src_path, dst_path)
