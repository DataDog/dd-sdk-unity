#!/usr/bin/python3

# -----------------------------------------------------------
# Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
# This product includes software developed at Datadog (https://www.datadoghq.com/).
# Copyright 2023-Present Datadog, Inc.
# -----------------------------------------------------------

# Script for updating the plugin to deployed versions of the Android and iOS
# native libraries

import argparse
import xml.etree.ElementTree as et

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
    tree = et.parse(UNITY_DEPENDENCIES_FILE)
    root = tree.getroot()

    for item in root.findall("./iosPods/iosPod"):
        if "name" in item.attrib and item.attrib['name'].startswith("Datadog"):
            item.attrib["version"] = version

    tree.write(UNITY_DEPENDENCIES_FILE)

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
