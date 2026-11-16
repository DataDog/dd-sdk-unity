# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""Unity importer metadata for staged SDK assets."""

import uuid
from pathlib import Path
from textwrap import dedent

from dd_sdk_unity.scripts.common.versions.sdk_pin import NativeArtifact

PLATFORM_DATA = {
    "ios": """
    - first:
        Editor: Editor
      second:
        enabled: 0
        settings:
          DefaultValueInitialized: true
    - first:
        iPhone: iOS
      second:
        enabled: 1
        settings:
          AddToEmbeddedBinaries: true
    """,
    "windows": """
    - first:
        Editor: Editor
      second:
        enabled: 1
        settings:
          CPU: x86_64
          OS: Windows
          DefaultValueInitialized: true
    - first:
        Standalone: Win
      second:
        enabled: 0
        settings:
          CPU: None
    - first:
        Standalone: Win64
      second:
        enabled: 1
        settings:
          CPU: x86_64
    """,
    "linux": """
    - first:
        Editor: Editor
      second:
        enabled: 1
        settings:
          CPU: x86_64
          OS: Linux
          DefaultValueInitialized: true
    - first:
        Standalone: Linux64
      second:
        enabled: 1
        settings:
          CPU: x86_64
    """,
    "macos": """
    - first:
        Editor: Editor
      second:
        enabled: 1
        settings:
          CPU: AnyCPU
          OS: OSX
          DefaultValueInitialized: true
    - first:
        Standalone: OSXUniversal
      second:
        enabled: 1
        settings:
          CPU: AnyCPU
    """,
}


def write_metadata(asset: NativeArtifact, package: Path) -> None:
    destination = package / asset.destination
    paths = {destination / name for name in asset.files}
    for path in list(paths):
        paths.update(
            parent for parent in path.parents if parent.is_relative_to(package / "Plugins")
        )
    for path in sorted(paths):
        meta = Path(f"{path}.meta")
        if meta.exists():
            continue
        plugin = path.suffix in {".xcframework", ".dll", ".so", ".dylib"}
        name = path.name if path.suffix == ".xcframework" else path.relative_to(package).as_posix()
        guid = uuid.uuid5(uuid.NAMESPACE_URL, f"com.datadoghq.unity/{name}").hex
        contents = f"fileFormatVersion: 2\nguid: {guid}\n"
        if plugin:
            contents += dedent(f"""
                PluginImporter:
                  externalObjects: {{}}
                  serializedVersion: 2
                  iconMap: {{}}
                  executionOrder: {{}}
                  defineConstraints: []
                  isPreloaded: 0
                  isOverridable: {int(asset.platform == "ios")}
                  isExplicitlyReferenced: 0
                  validateReferences: 1
                  platformData:
                  - first:
                      Any:
                    second:
                      enabled: 0
                      settings: {{}}
                """).lstrip("\n")
            contents += "".join(
                "  " + line + "\n"
                for line in dedent(PLATFORM_DATA[asset.platform]).lstrip("\n").splitlines()
            )
        else:
            if path.is_dir():
                contents += "folderAsset: yes\n"
            importer = "TextScriptImporter" if path.suffix in {".txt", ".md"} else "DefaultImporter"
            contents += f"{importer}:\n  externalObjects: {{}}\n"
        contents += "  userData:\n  assetBundleName:\n  assetBundleVariant:\n"
        meta.write_text(contents)
