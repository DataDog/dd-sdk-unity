"""
Utility code for modifying AssemblyInfo.cs, which controls the version string that will
be baked into C# assemblies when the project is built.
"""
from typing import IO, List

from .semver import Version


def modify_assemblyinfo(path: str, new_version: Version):
    with open(path) as fp:
        new_text = _modify_assemblyinfo_impl(fp, new_version)
    with open(path, 'w') as fp:
        fp.write(new_text)


def _modify_assemblyinfo_impl(infile: IO[str], new_version: Version) -> str:
    lines: List[str] = []
    for line in infile.read().splitlines():
        if line.startswith('[assembly: AssemblyVersion'):
            lines.append(f'[assembly: AssemblyVersion("{new_version}")]')
        else:
            lines.append(line)
    return '\n'.join(lines) + '\n'
