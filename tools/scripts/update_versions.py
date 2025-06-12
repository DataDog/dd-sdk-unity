#!/usr/bin/python3

# -----------------------------------------------------------
# Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
# This product includes software developed at Datadog (https://www.datadoghq.com/).
# Copyright 2023-Present Datadog, Inc.
# -----------------------------------------------------------

# Script for updating the plugin to deployed versions of the Android and iOS
# native libraries

import argparse

import unity_dependencies


def main():
    arg_parser = argparse.ArgumentParser()
    arg_parser.add_argument("--platform", required=True, choices=["android", "ios"])
    arg_parser.add_argument("--version", required=True)

    args = arg_parser.parse_args()

    if args.platform == "android":
        unity_dependencies.update_android_version(args.version)
    elif args.platform == "ios":
        unity_dependencies.update_ios_version(args.version)

if __name__ == "__main__":
    main()
