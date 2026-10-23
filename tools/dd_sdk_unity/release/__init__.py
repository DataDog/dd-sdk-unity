# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""CI release operations."""

PACKAGE_PATH = "packages/Datadog.Unity"
CHANGELOG_PATH = f"{PACKAGE_PATH}/CHANGELOG.md"
ASSEMBLY_PATH = f"{PACKAGE_PATH}/Runtime/AssemblyInfo.cs"
NATIVE_HISTORY_PATH = "NATIVE_SDK_VERSIONS.md"
IOS_PIN_PATH = f"{PACKAGE_PATH}/Editor/iOS/IosDependencyVersion.json"
ANDROID_PIN_PATH = f"{PACKAGE_PATH}/Editor/Android/AndroidDependencyVersion.json"
