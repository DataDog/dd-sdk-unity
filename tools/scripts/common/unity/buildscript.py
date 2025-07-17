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
from dataclasses import dataclass
from enum import Enum
from typing import List

from pydantic import BaseModel
from ruamel.yaml import YAML
from jinja2 import Template

from .asset import write_asset_metadata


class ProjectBuildConfiguration(BaseModel):
    scenes: List[str]

    @classmethod
    def require(cls, path: str) -> 'ProjectBuildConfiguration':
        with open(path) as fp:
            data = YAML().load(fp)
        return cls(**data)


class BuildScriptTemplate(str, Enum):
    BUILD_COMMANDS_CS = 'BuildCommands.cs'
    ENABLE_CLEARTEXT_TRAFFIC_POST_PROCESSOR_CS = 'EnableCleartextTrafficPostProcessor.cs'


@dataclass
class InjectedBuildScript:
    template_path: str
    cs_path: str

    def write(self, build_config: ProjectBuildConfiguration) -> List[str]:
        # Load the template for our C# source and render it from our project's config
        with open(self.template_path) as fp:
            template = Template(fp.read())
        cs_text = template.render(build_config=build_config)

        # Make sure that our target directory exists but the files we're about to write
        # do not: the caller should make sure the destination directory is clean
        cs_meta_path = self.cs_path + '.meta'
        assert not os.path.isfile(self.cs_path), f'Build script file already exists: {self.cs_path}'
        assert not os.path.isfile(cs_meta_path), f'Build script file already exists: {cs_meta_path}'
        assert os.path.isdir(os.path.dirname(self.cs_path)), f'Build script directory does not exist: {os.path.dirname(self.cs_path)}'

        # Write the .cs source file
        with open(self.cs_path, 'w') as fp:
            fp.write(cs_text)

        # Write the accompanying .cs.meta file
        try:
            write_asset_metadata(cs_meta_path)
        except:
            # Don't leave a dangling .cs file if we fail
            os.remove(self.cs_path)
            raise

        # Return the paths to both files so they can be cleaned up
        return [self.cs_path, cs_meta_path]
    
    @classmethod
    def new(cls, template_id: BuildScriptTemplate, injected_script_dir: str) -> 'InjectedBuildScript':
        templates_dir = os.path.join(os.path.dirname(__file__), 'buildscripts')
        template_path = os.path.join(templates_dir, template_id.value + '.jinja')
        if not os.path.isfile(template_path):
            raise RuntimeError(f'BuildScriptTemplate {template_id.value} has no template file at {template_path}')
        cs_path = os.path.join(injected_script_dir, template_id.value)
        return cls(template_path, cs_path)
