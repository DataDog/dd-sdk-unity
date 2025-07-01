# -----------------------------------------------------------
# Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
# This product includes software developed at Datadog (https://www.datadoghq.com/).
# Copyright 2023-Present Datadog, Inc.
# -----------------------------------------------------------

# Helpers for reading from and writing to DatadogDepdendencies.xml, which keeps
# track of the target SDK versions for Android and iOS

import xml.etree.ElementTree as et

UNITY_PLUGIN_PATH = "../../packages/Datadog.Unity/Plugins"
UNITY_DEPENDENCIES_FILE = "../../packages/Datadog.Unity/Editor/DatadogDependencies.xml"

def update_android_version(version: str):
    tree = et.parse(UNITY_DEPENDENCIES_FILE)
    root = tree.getroot()

    for item in root.findall("./androidPackages/androidPackage"):
        if "spec" in item.attrib and item.attrib['spec'].startswith("com.datadoghq"):
            spec = item.attrib["spec"]
            items = spec.split(":")
            items[2] = version
            print(f"Updating {items[1]} to {version}")
            item.attrib["spec"] = str.join(":", items)

    repository_element = root.find("./androidPackages/repositories")
    if repository_element is not None:
        for repo in repository_element.findall("./repository"):
            if repo.text is not None  and "maven-snapshots" in repo.text:
                repository_element.remove(repo)

    tree.write(UNITY_DEPENDENCIES_FILE)

def update_ios_version(version: str):
    tree = et.parse(UNITY_DEPENDENCIES_FILE)
    root = tree.getroot()

    for item in root.findall("./iosPods/iosPod"):
        if "name" in item.attrib and item.attrib['name'].startswith("Datadog"):
            item.attrib["version"] = version

    tree.write(UNITY_DEPENDENCIES_FILE)

def get_current_android_version():
    tree = et.parse(UNITY_DEPENDENCIES_FILE)
    root = tree.getroot()
    version = None

    for item in root.findall("./androidPackages/androidPackage"):
        if "spec" in item.attrib and item.attrib['spec'].startswith("com.datadoghq"):
            spec = item.attrib["spec"]
            items = spec.split(":")
            if version == None: 
                version = items[2]
            elif version != items[2]:
                print(f"Warning: Found mismatching Android versions: {version} =/= {items[2]}")

    return version

def get_current_ios_version():
    tree = et.parse(UNITY_DEPENDENCIES_FILE)
    root = tree.getroot()
    version = None

    for item in root.findall("./iosPods/iosPod"):
        if "name" in item.attrib and item.attrib['name'].startswith("Datadog"):
            fileVersion = item.attrib["version"]
            if version == None: 
                version = fileVersion
            elif version != fileVersion:
                print(f"Warning: Found mismatching iOS versions: {version} =/= {fileVersion}")

    return version
