"""
Utility code for reading and writing the various file formats in the dd-sdk-unity
repository that encode version information.
"""
from .semver import Version, VersionBump
from .sdkversions import SdkVersionTable, SdkVersionTableRow
from .edm4u_deps import ExternalDependencyVersions, read_external_dependency_versions, write_external_dependency_versions
from .package import modify_package_json
from .assemblyinfo import modify_assemblyinfo


__all__ = [
    'Version',
    'VersionBump',
    'SdkVersionTable',
    'SdkVersionTableRow',
    'ExternalDependencyVersions',
    'read_external_dependency_versions',
    'write_external_dependency_versions',
    'modify_package_json',
    'modify_assemblyinfo',
]
