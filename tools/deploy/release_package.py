#!/usr/bin/python3

# -----------------------------------------------------------
# Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
# This product includes software developed at Datadog (https://www.datadoghq.com/).
# Copyright 2023-Present Datadog, Inc.
# -----------------------------------------------------------

import argparse
import json
import os
import shutil

import git

PACKAGE_LOCATION = "../../packages/Datadog.Unity"

def _verify_dest_git_repo(dest: str, version: str) -> bool:
    repo = git.Repo(dest)

    if repo.is_dirty():
        print("Destination repo is dirty -- please commit or stash any changes.")
        return False

    # Check if the repo has this version tag already
    if version in repo.tags:
        print(f"Package already has version ${version}")
        return False

    return True

def _copy_package_files(dest: str):
    # Remove everything from the destination directory. It'll be okay I promise.
    for path in os.listdir(dest):
        # Well, it'll be okay if we don't delete the .git directory
        if path.startswith(".git"):
            continue

        if os.path.isfile(path):
            os.remove(path)
        elif os.path.isdir(path):
            shutil.rmtree(path)

    shutil.copytree(PACKAGE_LOCATION, dest, dirs_exist_ok=True)

def _modify_package_version(dest: str, version: str):
    package_path = os.path.join(dest, "package.json")

    with open(package_path, "r") as json_file:
        package_json = json.load(json_file)
        package_json["version"] = version

    with open(package_path, "w") as json_file:
        json.dump(package_json, json_file, indent=2)

def _commit_and_tag(dest: str, version: str):
    repo = git.Repo(dest)

    repo.git.add('--all')
    repo.index.commit(f'Publish version {version}')
    repo.create_tag(version)

    # TODO: Push

def main():
    arg_parser = argparse.ArgumentParser()
    arg_parser.add_argument("--version", required=True, help="The version we're publishing")
    arg_parser.add_argument("--dest", required=True, help="The destination directory to deploy to. This should the publishing git repo.")
    arg_parser.add_argument("--no-commit", help="Don't commit or tag in the destination repo", action="store_true")
    args = arg_parser.parse_args()

    if not os.path.isdir(PACKAGE_LOCATION):
        print(f"Could not find package at {PACKAGE_LOCATION}. Are you running from the script's directory?")

    if not _verify_dest_git_repo(args.dest, args.version):
        return

    print(f"Copying package files...")
    _copy_package_files(args.dest)
    _modify_package_version(args.dest, args.version)
    if not args.no_commit:
        print(f"Committing and tagging version {args.version}")
        _commit_and_tag(args.dest, args.version)


if __name__ == "__main__":
    main()
