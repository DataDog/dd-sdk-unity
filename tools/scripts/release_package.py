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
import github as gh

import update_versions as uv

REPO_ROOT = "../../"
PACKAGE_LOCATION = f"{REPO_ROOT}packages/Datadog.Unity"

def _verify_source_git_repo(version: str) -> bool:
    repo = git.Repo(".")

    if repo.is_dirty():
        print("Source repo is dirty -- please commit or stash any changes.")

    # Check if the repo has this version tag already
    if version in repo.tags:
        print(f"Package already has version ${version}")
        return False

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
        full_path = os.path.join(dest, path)
        # Well, it'll be okay if we don't delete the .git directory
        if path.startswith(".git"):
            continue

        if os.path.isfile(full_path):
            os.remove(full_path)
        elif os.path.isdir(full_path):
            shutil.rmtree(full_path)

    shutil.copytree(
        PACKAGE_LOCATION,
        dest,
        dirs_exist_ok=True,
        ignore=shutil.ignore_patterns('Tests', 'Tests.meta')
    )

def _modify_package_version(dest: str, version: str):
    package_path = os.path.join(dest, "package.json")

    with open(package_path, "r") as json_file:
        package_json = json.load(json_file)
        package_json["version"] = version

    with open(package_path, "w") as json_file:
        json.dump(package_json, json_file, indent=2)

def _update_android_versions(version: str, github_token: str):
    if version is None:
        # Need to get the latest version from Github
        gh_auth = gh.Auth.Token(github_token)
        github = gh.Github(auth=gh_auth)

        repo = github.get_repo("Datadog/dd-sdk-android")
        release = repo.get_latest_release()
        version = release.tag_name
        print(f"Read latest Android SDK release as {version}")

        github.close()

    uv._update_android_version(version)

def _branch(dest: str, branch_name: str):
    repo = git.Repo(dest)

    branch = repo.create_head(branch_name)
    branch.checkout()

def _commit_and_tag(dest: str, version: str):
    repo = git.Repo(dest)

    repo.git.add('--all')
    repo.index.commit(f'Publish version {version}')
    repo.create_tag(version)

    # TODO: Push

def main():
    arg_parser = argparse.ArgumentParser()
    arg_parser.add_argument("--version", required=True, help="The version we're publishing")
    arg_parser.add_argument("--ios-version",
                            required=False,
                            help="Update iOS SDK to the specified version before publishing. Defaults to commit in the sub-module.")
    arg_parser.add_argument("--android-version",
                            required=False,
                            help="Update the Android SDK to the specified version before publishing. Defaults to latest Github release.")

    arg_parser.add_argument("--dest", required=True, help="The destination directory to deploy to. This should the publishing git repo.")
    arg_parser.add_argument("--no-commit", help="Don't commit or tag either repo", action="store_true")
    args = arg_parser.parse_args()

    github_token = os.environ["GITHUB_TOKEN"]
    if github_token is None:
        print(f"GITHUB_TOKEN not set.")

    if not os.path.isdir(PACKAGE_LOCATION):
        print(f"Could not find package at {PACKAGE_LOCATION}. Are you running from the script's directory?")
        return

    version = args.version
    # if not _verify_source_git_repo(version):
    #    return

    if not _verify_dest_git_repo(args.dest, version):
        return

    branch_name = f"release/{version}"
    print(f"Creating release branch '{branch_name}'")
    _branch(REPO_ROOT, branch_name)
    _modify_package_version(PACKAGE_LOCATION, version)
    if args.ios_version:
        print(f'Updating iOS to version {args.ios_version} and rebuilding.')
        uv._update_ios_version(args.ios_version)
    _update_android_versions(args.android_version, github_token)

    if not args.no_commit:
        print(f"Tagging source repo with {version}")
        _commit_and_tag(REPO_ROOT, args.version)

    print(f"Copying package files...")
    _copy_package_files(args.dest)
    if not args.no_commit:
        print(f"Committing and tagging version {args.version}")
        _commit_and_tag(args.dest, args.version)


if __name__ == "__main__":
    main()
