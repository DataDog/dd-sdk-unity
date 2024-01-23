#!/usr/bin/python3

# -----------------------------------------------------------
# Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
# This product includes software developed at Datadog (https://www.datadoghq.com/).
# Copyright 2023-Present Datadog, Inc.
# -----------------------------------------------------------

# Script for updating the plugin to deployed versions of the Android and iOS
# native libraries

import argparse
import os
import shutil
import subprocess
import git
import xml.etree.ElementTree as et

IOS_MODULE_PATH = "../../modules/dd-sdk-ios"
UNITY_PLUGIN_PATH = "../../packages/Datadog.Unity/Plugins"
UNITY_DEPENDENCIES_FILE = "../../packages/Datadog.Unity/Editor/DatadogDependencies.xml"

def _update_android_version(version: str):
    tree = et.parse(UNITY_DEPENDENCIES_FILE)
    root = tree.getroot()

    for item in root.findall("./androidPackages/androidPackage"):
        if "spec" in item.attrib and item.attrib['spec'].startswith("com.datadoghq"):
            spec = item.attrib["spec"]
            items = spec.split(":")
            items[2] = version
            print(f"Updating {items[1]} to {version}")
            item.attrib["spec"] = str.join(":", items)

    tree.write(UNITY_DEPENDENCIES_FILE)


def _update_ios_version(version: str):
    repo = git.Repo("../../")

    # Update git subodule
    ios_submodule = next((x for x in repo.submodules if x.name == "modules/dd-sdk-ios"), None)
    if ios_submodule is None:
        print("Could not find the iOS sdk submodule to update")
        return

    for origin in ios_submodule.module().remotes:
        origin.fetch()

    if version not in ios_submodule.module().tags:
        print(f"Could not find tag `{version}` in iOS submodule.")
        return

    version_tag = ios_submodule.module().tags[version]
    ios_submodule.module().head.reference = version_tag
    print(f"Resetting dd-sdk-ios to tag {version}")
    ios_submodule.module().head.reset(index=True, working_tree=True)

    # Build carthage
    print("Running carthage build...")
    process = subprocess.Popen(["./tools/distribution/build-xcframework.sh"],
                               stdout=subprocess.PIPE, universal_newlines=True, cwd=IOS_MODULE_PATH)
    for line in process.stdout:
        print(f"{line}")

    # Copy frameworks
    frameworks = [ "CrashReporter", "DatadogCore", "DatadogCrashReporting", "DatadogInternal", "DatadogLogs", "DatadogRUM" ]
    for framework in frameworks:
        src = os.path.join(IOS_MODULE_PATH, "Carthage", "Build", f'{framework}.xcframework')
        dest = os.path.join(UNITY_PLUGIN_PATH, "iOS", f"{framework}.xcframework~")
        if os.path.exists(dest):
            shutil.rmtree(dest)
        print(f"Copying ${src} => {dest}")
        shutil.copytree(src, dest)

def main():
    arg_parser = argparse.ArgumentParser()
    arg_parser.add_argument("--platform", required=True, choices=["android", "ios"])
    arg_parser.add_argument("--version", required=True)

    args = arg_parser.parse_args()

    if args.platform == "android":
        _update_android_version(args.version)
    elif args.platform == "ios":
        _update_ios_version(args.version)

if __name__ == "__main__":
    main()
