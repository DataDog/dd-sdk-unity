"""
Code for temporarily modifying .asset files in a Unity project.

Unity stores build configuration in serialized assets within the project, including
many critical build-time settings (e.g. targeting iOS simulator vs device). Unity does
not provide any other means of configuring these settings, so we must overlay transient
changes to asset properties for the duration of our builds.
"""
import os
import io
import uuid
import time
from dataclasses import dataclass
from typing import Tuple, Union, List, Callable, Dict

from ruamel.yaml import YAML
from ruamel.yaml.comments import CommentedMap

AssetRevertFunc = Callable[[], None]
AssetPropertyValue = Union[str, int, float, bool]


@dataclass
class AssetPropertyChange:
    name: str
    value: AssetPropertyValue

    _asset_relpath: str

    def apply(self, asset_properties: CommentedMap):
        # Require the existing asset to already have a value for this property, even if
        # it's not set
        if self.name not in asset_properties:
            raise ValueError(f'Unexpected format for {self._asset_relpath}: no existing property is named {self.name}')
        
        # Coerce bool to YAML int for consistency with Unity
        value = int(self.value) if isinstance(self.value, bool) else self.value

        # Modify our in-memory YAML with the desired value
        asset_properties[self.name] = value


@dataclass
class AssetModification:
    relpath: str
    property_changes: List[AssetPropertyChange]

    @staticmethod
    def merge(modifications: List['AssetModification']) -> List['AssetModification']:
        # Condense all modifications targeting the same file to a single object
        modifications_by_relpath: Dict[str, AssetModification] = {}
        for mod in modifications:
            # If this is our first modification for this file, index it and continue
            existing = modifications_by_relpath.get(mod.relpath)
            if not existing:
                modifications_by_relpath[mod.relpath] = mod
                continue
            
            # Otherwise, extend the set of changes in the existing modification
            new_property_names = {p.name for p in mod.property_changes}
            old_changes = [p for p in existing.property_changes if p.name not in new_property_names]
            existing.property_changes = old_changes + mod.property_changes
        
        # Build a final result list, preserving original order
        result: List[AssetModification] = []
        for relpath in (mod.relpath for mod in modifications):
            assert relpath in modifications_by_relpath
            result.append(modifications_by_relpath[relpath])
        return result

    def apply(self, project_path: str) -> AssetRevertFunc:
        # Require the file to already exist; we don't support constructing entirely new
        # .asset files during builds
        asset_path = os.path.join(project_path, self.relpath)
        if not os.path.isfile(asset_path):
            raise ValueError(f'Unable to modify {self.relpath}: no such file exists at {asset_path}')
        
        # Read the full contents of the file as text, so we can restore it to its exact
        # original form when finished
        with open(asset_path) as fp:
            original_asset_text = fp.read()

        def revert():
            with open(asset_path, 'w') as fp:
                fp.write(original_asset_text)

        # Initialize a YAML parser that will maintain Unity's custom tags and
        # formatting, and parse the contents of the .asset file
        yaml = YAML()
        yaml.preserve_quotes = True
        data = yaml.load(io.StringIO(original_asset_text))

        # Unity assets have a single top-level object value, with the key being the
        # name of the asset type: grab the set of YAML attributes nested underneath
        # that object that represent our asset's properties
        assert len(data) == 1, f"Expected {self.relpath} to have 1 root key, got {len(data)} keys"
        asset_type = next(iter(data.keys()))
        asset_properties = data[asset_type]

        # Apply all of our property modifications to the in-memory YAML representation
        # of our asset
        for change in self.property_changes:
            change.apply(asset_properties)

        # Write our updated YAML to disk in place of the original .asset file
        with open(asset_path, 'w') as fp:
            yaml.dump(data, fp)

        # Return a thunk that will revert the file to its original contents
        return revert

    @classmethod
    def new(cls, relpath: str, changes: List[Tuple[str, AssetPropertyValue]]) -> 'AssetModification':
        property_changes = [AssetPropertyChange(name, value, relpath) for name, value in changes]
        return cls(relpath, property_changes)


def write_asset_metadata(meta_path: str):
    lines = [f'{key}: {value}' for key, value in [
        ('fileFormatVersion', 2),
        ('guid', uuid.uuid4().hex),
        ('timeCreated', int(time.time())),
    ]]
    with open(meta_path, 'w') as fp:
        fp.write('\n'.join(lines) + '\n')
