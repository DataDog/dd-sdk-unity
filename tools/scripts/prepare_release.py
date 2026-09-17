"""
Prepares a new release in the `unity-package` repo, cut from the current state of
`develop` in `dd-sdk-unity`. Changes are made to your local copies of those two
repositories, and you will be prompted before each local change is committed, and once
again before the final set of commits/tags is pushed to GitHub.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import os
import re
import sys
import shutil
import argparse
import tempfile
import subprocess
from typing import List, Tuple, Set

import git

import ios_xcframework
from common.log import init_logger, get_default_logger
from common.versions import Version, VersionBump, read_external_dependency_versions, SdkVersionTable, modify_package_json, modify_assemblyinfo, write_external_dependency_versions, ExternalDependencyVersions, read_ios_xcframework_pin, write_ios_xcframework_pin, IosXcframeworkPin, IOS_DEPENDENCY_VERSION_RELPATH
from common.commit import CommitInfo
from common.github import resolve_latest_release_version, get_file_contents, get_releases_between


__github_org__ = 'DataDog'
__dev_repo_name__ = 'dd-sdk-unity'
__dev_repo_trunk_branch_name__ = 'develop'
__dev_repo_package_subdir__ = os.path.join('packages', 'Datadog.Unity')
__release_repo_name__ = 'unity-package'
__release_repo_trunk_branch_name__ = 'main'
__unreleased_changes_heading__ = '## Unreleased'


def _require_clean_repo(root: str, branch_name: str) -> git.Repo:
    repo_name = os.path.basename(root)
    log = get_default_logger()

    # Verify that the directory exists
    if not os.path.isdir(root):
        raise RuntimeError(f'{repo_name} not found at: {root}')
    
    # Fetch the latest changes so we have up-to-date git state
    repo = git.Repo(root)
    log.info(f'Fetching from origin in {repo_name}...')
    repo.remote().fetch()

    # Require that we already have the trunk branch checked out
    if repo.active_branch.name != branch_name:
        log.error(f'{repo_name} is not in branch {branch_name}!')
        log.error(f'Current branch: {repo.active_branch.name}')
        log.error(f'Please stash any local changes and run `git checkout {branch_name}`.')
        raise RuntimeError(f'{repo_name} is not in branch {branch_name}')
    
    # Require that we're at the latest revision reflected in GitHub, i.e. we're not
    # behind remote or ahead of remote
    remote_commit: git.Commit = repo.refs[f'origin/{branch_name}'].commit  # type: ignore
    if repo.head.commit != remote_commit:
        log.error(f'{repo_name} is not up to date with origin/{branch_name}!')
        log.error(f'Local commit: {repo.head.commit}')
        log.error(f'Remote commit: {remote_commit}')
        log.error(f'Please run `git pull origin {branch_name} to update.')
        raise RuntimeError(f'{repo_name} is not up to date with origin/{branch_name}')
    
    # Require that we have no local edits or deletes
    if repo.is_dirty():
        log.error(f'{repo_name} is dirty!')
        log.error(f'Please stash or revert your local changes.')
        raise RuntimeError(f'{repo_name} is dirty')
    
    # Require that there are no untracked files which aren't caught by .gitignore
    if repo.untracked_files:
        log.error(f'{repo_name} has untracked files!')
        for file in repo.untracked_files:
            log.error(f'- {file}')
        log.error(f'Please delete these files, add them, or modify .gitignore.')
        raise RuntimeError(f'{repo_name} has untracked files')

    return repo


def _read_unreleased_changes(unity_changelog_text: str) -> str:
    lines = unity_changelog_text.splitlines()
    unreleased_heading_index = lines.index(__unreleased_changes_heading__)
    if unreleased_heading_index < 0:
        return ''
    lines = lines[unreleased_heading_index+1:]
    next_heading_index = next((i for i, s in enumerate(lines) if s.startswith('#')))
    lines = lines[:next_heading_index]
    return '\n'.join(lines).strip() + '\n'


def _apply_release_changes(unity_changelog_text: str, version: Version, changelog_lines: List[str]) -> str:
    lines = unity_changelog_text.splitlines()
    unreleased_heading_index = lines.index(__unreleased_changes_heading__)
    assert unreleased_heading_index >= 0
    lines[unreleased_heading_index] = f'## {version}'
    next_heading_index = next((i for i, s in enumerate(lines) if i > unreleased_heading_index and s.startswith('#')))
    lines_before, lines_after = lines[:unreleased_heading_index+1], lines[next_heading_index:]
    lines = lines_before + [''] + changelog_lines + [''] + lines_after
    return '\n'.join(lines) + '\n'


def _resolve_dependency_version(repo_name: str, version_str: str) -> Version:
    """
    Determines which version of an external dependency (e.g. dd-sdk-ios) we should use.
    """
    log = get_default_logger()

    latest_version = resolve_latest_release_version(__github_org__, repo_name)
    desired_version = latest_version if version_str == 'auto' else Version.parse(version_str)

    if desired_version == latest_version:
        log.info(f'Using {repo_name} version {latest_version} (latest).')
        return desired_version

    log.warning(f'Using {repo_name} version {desired_version} (overridden; latest is {latest_version}).')
    return desired_version


def _needs_dependency_upgrade(repo_name: str, target_ver: Version, prev_release_ver: Version) -> bool:
    log = get_default_logger()

    if prev_release_ver < target_ver:
        log.info(f'Previous release used {repo_name} {prev_release_ver}; will upgrade to {target_ver}.')
        return True
        
    if prev_release_ver == target_ver:
        log.info(f'Previous release already uses target {repo_name} version {target_ver}.')
        return False
    
    raise RuntimeError(f'Target {repo_name} version {target_ver} is a downgrade from version used in previous release ({prev_release_ver})')


def _read_prev_release_ios_version(prev_release_version: Version, prev_release_dependency_versions: ExternalDependencyVersions) -> Version:
    """
    Resolves the dd-sdk-ios version pinned in the previous release. Reads from the
    new Editor/iOS/IosDependencyVersion.json config first (Phase 2 and later
    releases); falls back to the legacy Editor/DatadogDependencies.xml <iosPod>
    entries (via the already-parsed prev_release_dependency_versions argument's iOS
    field) for releases published before that config existed, since those releases
    have no JSON pin to read.
    """
    log = get_default_logger()
    try:
        prev_release_ios_pin_json = get_file_contents(__github_org__, __release_repo_name__, str(prev_release_version), 'Editor/iOS/IosDependencyVersion.json')
        pin = read_ios_xcframework_pin(prev_release_ios_pin_json)
        log.info(f'Read previous release dd-sdk-ios version {pin.version} from Editor/iOS/IosDependencyVersion.json.')
        return pin.version
    except Exception as json_error:
        legacy_dd_sdk_ios_version = prev_release_dependency_versions.dd_sdk_ios
        if legacy_dd_sdk_ios_version is None:
            raise RuntimeError(
                f'Failed to resolve dd-sdk-ios version used in previous release {prev_release_version}: '
                f'could not read Editor/iOS/IosDependencyVersion.json ({json_error}), and '
                f'Editor/DatadogDependencies.xml has no <iosPod> entries to fall back to.'
            )
        log.warning(f'Could not read Editor/iOS/IosDependencyVersion.json from previous release {prev_release_version}: {json_error}')
        log.warning(f'Falling back to legacy Editor/DatadogDependencies.xml <iosPod> entries: dd-sdk-ios {legacy_dd_sdk_ios_version}.')
        return legacy_dd_sdk_ios_version


def _prepare_ios_changelog(prev_version: Version, target_version: Version) -> str:
    repo_name = 'dd-sdk-ios'

    lines: List[str] = []
    lines.append(f'; Changes from {repo_name} releases since {prev_version}, up to and including {target_version}:')
    lines.append('; Blank lines and lines beginning with `;` will be ignored.')
    lines.append('; To accept a line into the Unity SDK changelog, leave it uncommented.')
    lines.append('; To reject a line from the Unity SDK changelog, delete it or comment it out with `;`.')
    lines.append(';')

    def _remove_pr_refs(line: str) -> str:
        i = line.index(' (#')
        return line[:i] if i >= 0 else line

    releases = get_releases_between(__github_org__, repo_name, prev_version, target_version)
    for release in releases:
        lines.append(f'; # {release.name}')
        lines.append(';')
        for line in release.body.splitlines():
            if line.strip().startswith('* '):
                lines.append(_remove_pr_refs(line))
            else:
                lines.append(f'; {line}')
        lines.append(';')

    return '\n'.join(lines) + '\n'


def _prepare_android_changelog(prev_version: Version, target_version: Version) -> str:
    repo_name = 'dd-sdk-android'

    lines: List[str] = []
    lines.append(f'; Changes from {repo_name} releases since {prev_version}, up to and including {target_version}:')
    lines.append('; Blank lines and lines beginning with `;` will be ignored.')
    lines.append('; To accept a line into the Unity SDK changelog, leave it uncommented.')
    lines.append('; To reject a line from the Unity SDK changelog, delete it or comment it out with `;`.')
    lines.append(';')

    category_regex = re.compile(r'^\* \[([A-Z]+)\] (.*)')

    def _parse_category(line: str) -> Tuple[str, str]:
        match = category_regex.match(line)
        if match:
            return match.group(1), match.group(2)
        return '', line[2:] if line.startswith('* ') else line

    def _remove_pr_refs(line: str) -> str:
        i = line.index(' See [#')
        return line[:i] if i >= 0 else line

    releases = get_releases_between(__github_org__, repo_name, prev_version, target_version)
    for release in releases:
        lines.append(f'; RELEASE {release.name}')
        lines.append(';')

        prev_category = ''
        for line in release.body.splitlines():
            if line.strip().startswith('* '):
                category, text = _parse_category(line)
                if category != prev_category:
                    if prev_category != '':
                        lines.append(';')
                    lines.append(f'; Tagged as [{category}]:')
                prev_category = category
                prefix = '' if category in ['FEATURE', 'BUGFIX'] else '; '
                lines.append(f'{prefix}* {_remove_pr_refs(text)}')
            else:
                lines.append(f'; {line}')
                prev_category = ''
        lines.append(';')

    return '\n'.join(lines) + '\n'


def _condense_edited_changelog(text: str) -> List[str]:
    keep_lines: List[str] = []
    for line in text.splitlines():
        stripped_line = line.strip()
        if not stripped_line:
            continue
        if line.startswith(';'):
            continue
        if not stripped_line.startswith('* '):
            continue
        line = line.replace('\t', '  ')
        bullet_index = line.index('*')
        assert bullet_index >= 0
        if bullet_index % 2 == 1:
            line = line[1:]
        keep_lines.append(line)
    return keep_lines


def _prompt_user_to_edit(initial_text: str) -> str:
    editor = os.getenv('EDITOR', 'vim')
    with tempfile.NamedTemporaryFile(mode='w+', suffix='.ini', delete=False) as fp:
        fp.write(initial_text)
        temp_path = fp.name

    try:
        subprocess.run([editor, temp_path], check=True)
        with open(temp_path, 'r') as fp:
            edited_text = fp.read()
        return edited_text
    finally:
        os.remove(temp_path)


def _prompt_commit(repo_root: str) -> bool:
    log = get_default_logger()
    subprocess.check_call(['git', 'status'], cwd=repo_root)
    log.info(f'git working directory: {repo_root}')
    while True:
        log.info("To see a summary of pending changes, enter 'status' or 'diff'.")
        log.info("When ready to proceed, enter 'commit'.")
        choice = input("> ")
        if choice == '':
            continue
        if choice == 'status':
            subprocess.check_call(['git', 'status'], cwd=repo_root)
            continue
        if choice == 'diff':
            subprocess.check_call(['git', 'diff', '--staged'], cwd=repo_root)
            continue
        if choice == 'commit':
            return True
        return False


def prepare_release(dev_repo_root: str, release_repo_root: str, version_bump_str: str, dd_sdk_ios_version_str: str, dd_sdk_android_version_str: str, force: bool) -> int:
    log = init_logger()
    num_sections_logged = 0

    def _log_section(s: str):
        nonlocal num_sections_logged
        if num_sections_logged > 0:
            log.info('')
        log.info(s)
        log.info('=' * len(s))
        num_sections_logged += 1

    # Validate args
    if version_bump_str not in ('major', 'minor', 'patch', 'auto'):
        raise ValueError("Desired version bump must be specified as one of 'major', 'minor', 'patch', or 'auto'")
    
    ### Check our working copy of the dd-sdk-unity repo to ensure that it's in a clean state
    _log_section(f'Preparing {__dev_repo_name__} repo...')

    # Get the path to our development repo: this defaults to the same repo that we're
    # running this script in, but it can be overridden by the user to target a clean
    # copy that they've made elsewhere
    if dev_repo_root == 'auto':
        dev_repo_root = os.path.normpath(os.path.join(os.path.dirname(__file__), '..', '..'))
    log.info(f'Using development repo: {dev_repo_root}')

    def _dev_package_path(*args: str) -> str:
        return os.path.join(dev_repo_root, __dev_repo_package_subdir__, *args)

    # This is the dd-sdk-unity repo that we'll prepare the release in; ensure that it's
    # up to date with the latest changes
    dev_repo = _require_clean_repo(dev_repo_root, __dev_repo_trunk_branch_name__)
    
    # Read the CHANGELOG.md file in dd-sdk-unity and find the list of unreleased changes
    dev_changelog_path = _dev_package_path('CHANGELOG.md')
    with open(dev_changelog_path) as fp:
        original_changelog_text = fp.read()
    unreleased_changes_text = _read_unreleased_changes(original_changelog_text)
    if unreleased_changes_text.strip() == '':
        log.error(f"CHANGELOG.md does not list any changes to be released!")
        log.error(f"Please check for an '{__unreleased_changes_heading__}' heading in {dev_changelog_path}.")
        return 1
    log.info('Unity SDK changes to be released:')
    print(unreleased_changes_text.strip())

    # Read the existing contents of NATIVE_SDK_VERSIONS.md
    dev_native_sdk_versions_path = os.path.join(dev_repo_root, 'NATIVE_SDK_VERSIONS.md')
    with open(dev_native_sdk_versions_path) as fp:
        native_sdk_versions = SdkVersionTable.parse(fp.read())

    # Make sure we have other required files present before we proceed
    dev_package_json_path = _dev_package_path('package.json')
    if not os.path.isfile(dev_package_json_path):
        raise RuntimeError(f'File not found: {dev_package_json_path}')
    dev_assemblyinfo_cs_path = _dev_package_path('Runtime', 'AssemblyInfo.cs')
    if not os.path.isfile(dev_assemblyinfo_cs_path):
        raise RuntimeError(f'File not found: {dev_assemblyinfo_cs_path}')
    dev_datadog_dependencies_xml_path = _dev_package_path('Editor', 'DatadogDependencies.xml')
    if not os.path.isfile(dev_datadog_dependencies_xml_path):
        raise RuntimeError(f'File not found: {dev_datadog_dependencies_xml_path}')
    dev_ios_dependency_version_json_path = os.path.join(dev_repo_root, IOS_DEPENDENCY_VERSION_RELPATH)
    if not os.path.isfile(dev_ios_dependency_version_json_path):
        raise RuntimeError(f'File not found: {dev_ios_dependency_version_json_path}')
    
    # Read the snippet that gets pasted into the release repo's README, then find the
    # source README.md file, find the '[//]: # (Repo Note)' line, and replace it with
    # the snippet, giving us the final text of the README to include in the release
    # package
    dev_snippet_path = os.path.join(dev_repo_root, 'tools', 'snippets', 'deployment_repo.md')
    with open(dev_snippet_path) as fp:
        dev_snippet_text = fp.read()
    dev_package_readme_path = _dev_package_path('README.md')
    release_readme_text = ''
    with open(dev_package_readme_path) as fp:
        for line in fp.readlines():
            if line.startswith('[//]: # (Repo Note)'):
                release_readme_text += dev_snippet_text
            else:
                release_readme_text += line

    ### Check our working copy of the unity-package repo to ensure that it's in a clean state
    _log_section(f'Preparing {__release_repo_name__} repo...')

    # Get the path to the release repo
    if release_repo_root == 'auto':
        release_repo_root = os.path.normpath(os.path.join(os.path.dirname(dev_repo_root), __release_repo_name__))
    log.info(f'Using release repo: {release_repo_root}')

    # This is the unity-package repo where we'll copy the updated package contents once
    # they're ready; ensure that it's up to date with the latest changes
    release_repo = _require_clean_repo(release_repo_root, __release_repo_trunk_branch_name__)

    ### Determine how to label our new release of the Unity SDK based on commit history etc.
    _log_section('Resolving new Unity SDK release version...')
    
    # Check the current published version of 'DataDog/unity-package' on GitHub
    prev_release_version = resolve_latest_release_version(__github_org__, __release_repo_name__)
    log.info(f'Last published release of {__release_repo_name__} was: {prev_release_version}')

    # Find the tag in dd-sdk-unity/develop from which that release was made
    prev_release_tag = next((t for t in dev_repo.tags if t.name == prev_release_version), None)
    if not prev_release_tag:
        raise RuntimeError(f'Failed to find a commit tagged {prev_release_version} in {__dev_repo_name__}')
    log.info(f'Release {prev_release_version} was cut from {__dev_repo_name__} at commit {prev_release_tag.commit}')

    # Parse the details of all commits in dd-sdk-unity/develop made since the last release
    intervening_commits: List[CommitInfo] = []
    jira_refs: Set[str] = set()
    issue_refs: Set[str] = set()
    for commit in dev_repo.iter_commits(f'{prev_release_tag.commit}..develop'):
        commit_info = CommitInfo.parse(str(commit.message))
        intervening_commits.append(commit_info)
        for ref in commit_info.refs:
            if re.match(r'^[a-zA-Z]+\-[0-9]+$', ref):
                jira_refs.add(ref.upper())
            elif re.match(r'^#([0-9]+)$', ref):
                issue_refs.add(ref[1:])

    # Make sure we have at least one commit to release
    if not intervening_commits:
        log.warning(f'No commits have been made to {__dev_repo_name__}/{__dev_repo_trunk_branch_name__} since the last release!')
        log.warning('There is nothing to release.')
        return 2
    log.info(f'{len(intervening_commits)} commit(s) have been made to {__dev_repo_trunk_branch_name__} since the last release.')

    # Determine which version (major/minor/patch) to bump based on commit messages
    suggested_version_bump = VersionBump.PATCH
    feature_commits = [x for x in intervening_commits if x.bump == VersionBump.MINOR]
    if feature_commits:
        log.info(f'{len(feature_commits)} commit(s) introduce feature changes:')
        for commit_info in feature_commits:
            log.info(f'- {commit_info.headline}')
        suggested_version_bump = max(suggested_version_bump, VersionBump.MINOR)
    breaking_commits = [x for x in intervening_commits if x.bump == VersionBump.MAJOR]
    if breaking_commits:
        log.info(f'{len(feature_commits)} commit(s) introduce breaking changes:')
        for commit_info in feature_commits:
            log.info(f'- {commit_info.headline}')
        suggested_version_bump = VersionBump.MAJOR

    # If the user explicitly specified a desired version bump, use it
    version_bump = suggested_version_bump
    if version_bump_str != 'auto':
        version_bump = {
            'major': VersionBump.MAJOR,
            'minor': VersionBump.MINOR,
            'patch': VersionBump.PATCH,
        }[version_bump_str]
        # Warn if (e.g.) the user wants a minor release but commit history indicated breaking changes
        if version_bump < suggested_version_bump:
            log.warning(f'You requested a bump to the {version_bump} version; but commit history suggests that the {suggested_version_bump} version should be incremented.')
            log.warning(f'Proceeding with {version_bump} bump as directed.')
    log.info(f'Bumping {version_bump} version for next release.')

    # Determine the version of the release we're cutting now
    new_version = prev_release_version.bump(version_bump)
    log.info(f'Next release will be version {new_version}.')

    # Verify that we don't already have a tag in either repo for the new version
    dev_existing_tag = next((t for t in dev_repo.tags if t.name == str(new_version)), None)
    if dev_existing_tag:
        raise RuntimeError(f'Tag {new_version} already exists in {__dev_repo_name__}')
    release_existing_tag = next((t for t in release_repo.tags if t.name == str(new_version)), None)
    if release_existing_tag:
        raise RuntimeError(f'Tag {new_version} already exists in {__release_repo_name__}')
    
    ### Handle updates to external dependencies, including updating the CHANGELOG
    _log_section('Detecting changes to Android and iOS SDKs...')
    
    # Determine which versions of our external dependencies to target
    dd_sdk_ios_version = _resolve_dependency_version('dd-sdk-ios', dd_sdk_ios_version_str)
    dd_sdk_android_version = _resolve_dependency_version('dd-sdk-android', dd_sdk_android_version_str)

    # Check the EDM4U dependencies in the last published Unity SDK release to determine
    # whether our target versions of the Android and iOS SDKs are newer than what was
    # used in that release
    prev_release_dependencies_xml = get_file_contents(__github_org__, __release_repo_name__, str(prev_release_version), 'Editor/DatadogDependencies.xml')
    prev_release_dependency_versions = read_external_dependency_versions(prev_release_dependencies_xml)
    # Resolve the previous release's dd-sdk-ios version via _read_prev_release_ios_version's
    # JSON-primary/XML-fallback logic (see the helper above for the two attempted paths).
    prev_release_ios_version = _read_prev_release_ios_version(prev_release_version, prev_release_dependency_versions)
    needs_dd_sdk_ios_upgrade = _needs_dependency_upgrade('dd-sdk-ios', dd_sdk_ios_version, prev_release_ios_version)
    needs_dd_sdk_android_upgrade = _needs_dependency_upgrade('dd-sdk-android', dd_sdk_android_version, prev_release_dependency_versions.dd_sdk_android)

    # If we're upgrading the iOS or Android dependencies, get a list of all the
    # releases made in their respective repos since our last Unity release, and collect
    # the list of changes published in each of those releases
    ios_changelog_text = ''
    if needs_dd_sdk_ios_upgrade:
        ios_changelog_text = _prepare_ios_changelog(prev_release_ios_version, dd_sdk_ios_version)
    android_changelog_text = ''
    if needs_dd_sdk_android_upgrade:
        android_changelog_text = _prepare_android_changelog(prev_release_dependency_versions.dd_sdk_android, dd_sdk_android_version)

    # Prompt the user to select which changes they want to keep, giving them a chance
    # to copy-edit as they see fit
    ios_changelog_lines: List[str] = []
    if ios_changelog_text:
        _log_section('Selecting changes from iOS release notes...')
        ios_changelog_text = _prompt_user_to_edit(ios_changelog_text)
        ios_changelog_lines = _condense_edited_changelog(ios_changelog_text)
        for line in ios_changelog_lines:
            log.info(line)

    android_changelog_lines: List[str] = []
    if android_changelog_text:
        _log_section('Selecting changes from Android release notes...')
        android_changelog_text = _prompt_user_to_edit(android_changelog_text)   
        android_changelog_lines = _condense_edited_changelog(android_changelog_text)
        for line in android_changelog_lines:
            log.info(line)

    # Build the final list of changes that will be written to the Unity SDK's CHANGELOG.md
    _log_section('Preparing changes for Unity SDK release notes...')
    new_changelog_text = f'; Changes in pending dd-sdk-unity release {new_version}:\n;\n'
    new_changelog_text += unreleased_changes_text
    if needs_dd_sdk_ios_upgrade:
        new_changelog_text += f'* Upgrade Datadog iOS SDK to version {dd_sdk_ios_version}\n'
        if ios_changelog_lines:
            for line in ios_changelog_lines:
                new_changelog_text += f'  {line}\n'
    if needs_dd_sdk_android_upgrade:
        new_changelog_text += f'* Upgrade Datadog Android SDK to version {dd_sdk_android_version}\n'
        if android_changelog_lines:
            for line in android_changelog_lines:
                new_changelog_text += f'  {line}\n'
    new_changelog_text = _prompt_user_to_edit(new_changelog_text)
    new_changelog_lines = _condense_edited_changelog(new_changelog_text)
    if not new_changelog_lines:
        raise RuntimeError('Unity changelog is empty after user edits; aborting')
    for line in new_changelog_lines:
        log.info(line)

    ### Make changes that will persist in dd-sdk-unity
    _log_section('Updating dd-sdk-unity in preparation for release...')

    # Create a new branch in dd-sdk-unity to prepare the release: this branch will be
    # merged back into the trunk branch, so it contains changes that will persist in the
    # development repo
    dev_chore_branch_name = f'chore/release-{new_version}'
    dev_chore_branch = dev_repo.create_head(dev_chore_branch_name)
    dev_chore_branch.checkout()
    log.info(f'Working in branch {dev_chore_branch_name}.')

    # Update CHANGELOG.md with our final set of changes
    log.info(f'Writing final CHANGELOG to: {dev_changelog_path}')
    new_changelog_text = _apply_release_changes(original_changelog_text, new_version, new_changelog_lines)
    with open(dev_changelog_path, 'w') as fp:
        fp.write(new_changelog_text)
    dev_repo.git.add(dev_changelog_path)

    # Update NATIVE_SDK_VERSIONS.md to record the canonical dependency versions in use
    # as of this release
    log.info(f'Updating NATIVE_SDK_VERSIONS.md with an entry for version {new_version}')
    native_sdk_versions.set(new_version, dd_sdk_ios_version, dd_sdk_android_version)
    new_native_sdk_versions_text = native_sdk_versions.render()
    with open(dev_native_sdk_versions_path, 'w') as fp:
        fp.write(new_native_sdk_versions_text)
    dev_repo.git.add(dev_native_sdk_versions_path)

    # Commit these changes to the local branch
    _log_section(f'Committing changes to {dev_chore_branch_name}...')
    chore_commit_message = f'chore(release): Prepare version {new_version} for release'
    log.info(f'Pending changes will be committed in branch {dev_chore_branch_name} with message:')
    log.info(f'- {chore_commit_message}')
    if not force and not _prompt_commit(dev_repo_root):
        log.error('Abort.')
        return 3
    dev_repo.git.commit('-m', chore_commit_message)
    log.info(f'Committed changes to {dev_chore_branch_name}.')

    ### Make changes that will only exist in the release, such as baking in versions
    _log_section('Making release-only changes to Unity package...')

    # Create a release-only branch, inheriting the changes committed to our chore branch
    dev_release_branch_name = f'release/{new_version}'
    dev_release_branch = dev_repo.create_head(dev_release_branch_name)
    dev_release_branch.checkout()
    log.info(f'Working in branch {dev_release_branch_name}.')

    # Modify the UPM package version in package.json
    log.info(f'Updating version to {new_version} in: {dev_package_json_path}')
    modify_package_json(dev_package_json_path, new_version)
    dev_repo.git.add(dev_package_json_path)

    # Update the AssemblyVersion attribute in AssemblyInfo.cs
    log.info(f'Updating AssemblyVersion to {new_version} in: {dev_assemblyinfo_cs_path}')
    modify_assemblyinfo(dev_assemblyinfo_cs_path, new_version)
    dev_repo.git.add(dev_assemblyinfo_cs_path)

    # Bake the latest explicit dd-sdk-android version into DatadogDependencies.xml,
    # which configures EDM4U for Android only; dd-sdk-ios is no longer written here
    # (dd_sdk_ios=None) since EDM4U no longer resolves iOS pods — its version pin is
    # written to IosDependencyVersion.json below instead.
    log.info(f'Updating EDM4U dependency versions in: {dev_datadog_dependencies_xml_path}')
    write_external_dependency_versions(
        dev_datadog_dependencies_xml_path,
        ExternalDependencyVersions(
            dd_sdk_ios=None,
            dd_sdk_android=dd_sdk_android_version,
        ),
    )
    dev_repo.git.add(dev_datadog_dependencies_xml_path)

    # Bake the latest explicit dd-sdk-ios version into IosDependencyVersion.json instead
    # of DatadogDependencies.xml, and fetch/stage/verify that exact version now so the
    # release payload ships with a validated XCFramework bundle rather than merely an
    # updated pin. Plugins/iOS/*.xcframework is .gitignore'd in this dev repo, so the
    # file-copy step below stages these modules into the release repo explicitly, by
    # module name, rather than relying on `git ls-files`.
    log.info(f'Updating dd-sdk-ios version pin in: {dev_ios_dependency_version_json_path}')
    with open(dev_ios_dependency_version_json_path) as fp:
        existing_ios_pin = read_ios_xcframework_pin(fp.read())
    expected_ios_sha256 = existing_ios_pin.sha256 if existing_ios_pin.version == dd_sdk_ios_version else None
    if existing_ios_pin.version != dd_sdk_ios_version:
        log.warning(
            f'dd-sdk-ios version changed from {existing_ios_pin.version} to {dd_sdk_ios_version}; '
            'fetching and verifying it now rather than leaving a stale digest for a different version.'
        )
    new_ios_sha256 = ios_xcframework.fetch(
        log, str(dd_sdk_ios_version), expected_ios_sha256, force=False, allow_unknown_sha256=True,
    )
    # Stage into the selected --local-dev-repo checkout, not ios_xcframework's own
    # module-level PLUGINS_IOS_DIR (which is relative to this script's own location and
    # may be a different checkout entirely).
    dev_plugins_ios_dir = _dev_package_path('Plugins', 'iOS')
    ios_xcframework.stage(log, str(dd_sdk_ios_version), existing_ios_pin.modules, plugins_ios_dir=dev_plugins_ios_dir)
    ios_xcframework.verify(log, existing_ios_pin.modules, plugins_ios_dir=dev_plugins_ios_dir)
    write_ios_xcframework_pin(
        dev_ios_dependency_version_json_path,
        IosXcframeworkPin(
            version=dd_sdk_ios_version,
            sha256=new_ios_sha256,
            modules=existing_ios_pin.modules,
        ),
    )
    dev_repo.git.add(dev_ios_dependency_version_json_path)

    # Commit these changes to the local branch
    _log_section(f'Committing changes to {dev_release_branch_name}...')
    release_commit_message = f'Publish version {new_version}'
    log.info(f'Pending changes will be committed in branch {dev_release_branch_name} with message:')
    log.info(f'- {release_commit_message}')
    log.info(f"This commit to {__dev_repo_name__} will also be tagged '{new_version}'.")
    if not force and not _prompt_commit(dev_repo_root):
        log.error('Abort.')
        return 3
    dev_repo.git.commit('-m', release_commit_message)
    log.info(f'Committed changes to {dev_release_branch_name}.')

    # Create an annotated tag (lightweight tags can not be GPG-signed) to identify the
    # commit in dd-sdk-unity that matches the state of the package as of this release
    dev_repo.create_tag(str(new_version), message=f'Release version {new_version}')
    log.info(f'Tagged latest commit as {new_version}.')

    ### Copy from dd-sdk-unity/packages/Datadog.Unity to unity-package
    _log_section(f'Copying package files to {__release_repo_name__}...')

    # Blow away every top-level file or directory in the release repo, preserving the
    # .git directory
    for name in os.listdir(release_repo_root):
        if name == '.git':
            continue
        abspath = os.path.join(release_repo_root, name)
        if os.path.isfile(abspath):
            os.remove(abspath)
        else:
            shutil.rmtree(abspath)

    # Get a list of tracked files in the dev repo (respecting .gitignore), filter out
    # dev-repo-only files, and copy everything else to the release repo
    dev_tracked_files: List[str] = dev_repo.git.ls_files(__dev_repo_package_subdir__).splitlines()
    for repo_relpath in dev_tracked_files:
        # Convert 'packages/Datadog.Unity/Plugins/iOS' to 'Plugins/iOS'
        package_relpath = os.path.relpath(repo_relpath, __dev_repo_package_subdir__)
        # Exclude '.github/CODEOWNERS' etc.
        if package_relpath.startswith('.git'):
            continue
        # Exclude Unity package tests
        if package_relpath.startswith('Tests/') or package_relpath.startswith('Tests.'):
            continue

        # File belongs in the release package: prepare to copy it from the dev repo
        src_abspath = _dev_package_path(package_relpath)
        dst_abspath = os.path.join(release_repo_root, package_relpath)
        dst_dirpath = os.path.dirname(dst_abspath)
        os.makedirs(dst_dirpath, exist_ok=True)

        # README.md is modified to include an extra snippet; write it directly
        if package_relpath == 'README.md':
            with open(dst_abspath, 'w') as fp:
                fp.write(release_readme_text)
            log.info(f'Wrote modified {package_relpath}.')
            continue

        # For all other files, copy them normally
        shutil.copy(src_abspath, dst_abspath)
        log.info(f'Copied {package_relpath}.')

    # Copy the vendored dd-sdk-ios XCFramework modules into the release payload.
    # Plugins/iOS/*.xcframework (and its .meta) is intentionally .gitignore'd in this
    # dev repo, so `git ls-files` above never lists them; copy them explicitly here, by
    # the module list just fetched/staged/verified above.
    release_plugins_ios_dir = os.path.join(release_repo_root, 'Plugins', 'iOS')
    for module in existing_ios_pin.modules:
        module_dirname = f'{module}.xcframework'
        src_module_dir = os.path.join(dev_plugins_ios_dir, module_dirname)
        if not os.path.isdir(src_module_dir):
            raise RuntimeError(
                f'{src_module_dir} not found after staging dd-sdk-ios {dd_sdk_ios_version}; '
                'cannot publish a release without the vendored XCFramework it depends on.'
            )
        dst_module_dir = os.path.join(release_plugins_ios_dir, module_dirname)
        shutil.copytree(src_module_dir, dst_module_dir)
        log.info(f'Copied {module_dirname}.')

        src_meta_path = f'{src_module_dir}.meta'
        if os.path.exists(src_meta_path):
            shutil.copy(src_meta_path, f'{dst_module_dir}.meta')
            log.info(f'Copied {module_dirname}.meta.')
        else:
            log.warning(
                f'{src_meta_path} not found; the release repo will get a fresh .meta '
                f'(new GUID) for {module_dirname} the next time it is opened in Unity.'
            )

    # Stage all file changes
    release_repo.git.add('--all')

    # Commit these changes to the local branch
    _log_section(f'Committing changes to {__release_repo_name__}/{__release_repo_trunk_branch_name__}...')
    log.info(f'Pending changes will be committed in branch {__release_repo_trunk_branch_name__} with message:')
    log.info(f'- {release_commit_message}')
    log.info(f"This commit to {__release_repo_name__} will also be tagged '{new_version}'.")
    if not force and not _prompt_commit(release_repo_root):
        log.error('Abort.')
        return 3
    release_repo.git.commit('-m', release_commit_message)
    log.info(f'Committed changes to {__release_repo_name__}/{__release_repo_trunk_branch_name__}.')

    # Create a tag in the release repo
    release_repo.create_tag(str(new_version), message=f'Release version {new_version}')
    log.info(f'Tagged latest commit as {new_version}.')

    ### Prompt for final confirmation, then push everything to GitHub
    _log_section('⚠️ RELEASE IS PREPARED TO PUSH ⚠️')

    log.info(f'The following operations will be performed:')
    log.info('')
    log.info(f'- in {__dev_repo_name__}:')
    log.info(f'  ({dev_repo_root})')
    log.info(f'  - git push origin {dev_chore_branch_name}')
    log.info(f'  - git push origin {dev_release_branch_name}')
    log.info(f'  - git push origin refs/tags/{new_version}')
    log.info('')
    log.info(f'- in {__release_repo_name__}:')
    log.info(f'  ({release_repo_root})')
    log.info(f'  - git push origin {__release_repo_trunk_branch_name__}')
    log.info(f'  - git push origin refs/tags/{new_version}')
    log.info('')
    skip_push = False
    if not force:
        log.info("To proceed with the release, enter 'push'. Any other input will abort.")
        while True:
            choice = input("> ")
            if choice == '':
                continue
            if choice == 'push':
                break
            if choice == 'skip':
                skip_push = True
                break
            log.error('Abort.')
            return 4
    
    log.info(f'{__dev_repo_name__}: pushing branch {dev_chore_branch_name} to origin...')
    if not skip_push:
        dev_repo.remote().push(f'{dev_chore_branch_name}:{dev_chore_branch_name}')
    log.info(f'{__dev_repo_name__}: pushing branch {dev_release_branch_name} to origin...')
    if not skip_push:
        dev_repo.remote().push(f'{dev_release_branch_name}:{dev_release_branch_name}')
    log.info(f'{__dev_repo_name__}: pushing tag {new_version} to origin...')
    if not skip_push:
        dev_repo.remote().push(f'refs/tags/{new_version}')
    log.info(f'{__release_repo_name__}: pushing branch {__release_repo_trunk_branch_name__} to origin...')
    if not skip_push:
        release_repo.remote().push(f'{__release_repo_trunk_branch_name__}:{__release_repo_trunk_branch_name__}')
    log.info(f'{__release_repo_name__}: pushing tag {new_version} to origin...')
    if not skip_push:
        release_repo.remote().push(f'refs/tags/{new_version}')
    log.info('All changes pushed.')

    ### Summarize the state we've left the repo(s) in    
    _log_section('🚀 RELEASE IS READY! 🚀')

    log.info(f'Release {new_version} is now ready! Summary of changes:')
    log.info(f'- {__release_repo_name__}:')
    log.info(f'  - ✅ Package changes are now 🚀LIVE🚀 in {__release_repo_trunk_branch_name__}!')
    log.info(f'  - ⚠️ A GitHub release has not yet been published.')
    log.info(f'- {__dev_repo_name__}:')
    log.info(f'  - ✅ Branch {dev_release_branch_name} has been pushed for posterity.')
    log.info(f'  - ✅ Branch {dev_chore_branch_name} has been pushed to record persistent changes.')
    log.info(f'  - ⚠️ No changes have been made to {__dev_repo_trunk_branch_name__}; {dev_chore_branch_name} must still be merged.')
    log.info('')

    ### Print instructions for remaining manual steps
    _log_section('⚠️ COMPLETE THESE MANUAL STEPS TO FINISH THE RELEASE! ⚠️')

    log.info(f'1. Draft a GitHub release in {__release_repo_name__}')
    log.info(f'   - https://github.com/{__github_org__}/{__release_repo_name__}/releases/new')
    log.info(f'   - Select tag: {new_version}')
    log.info(f'   - Enter release notes copied from {new_version} section of CHANGELOG:')
    log.info(f'   - https://raw.githubusercontent.com/{__github_org__}/{__release_repo_name__}/refs/heads/{__release_repo_trunk_branch_name__}/CHANGELOG.md')
    log.info('')

    log.info(f'2. Open a PR in {__dev_repo_name__} to merge {dev_chore_branch_name} into {__dev_repo_trunk_branch_name__}.')
    log.info(f'   - https://github.com/{__github_org__}/{__dev_repo_name__}/compare/{__dev_repo_trunk_branch_name__}...{dev_chore_branch_name}')
    log.info('')

    log.info(f'3. Remediate any GitHub issues addressed by this release.')
    log.info(f'   - https://github.com/{__github_org__}/{__dev_repo_name__}/issues')
    if issue_refs:
        log.info(f'   Possible issue references identified from {__dev_repo_name__} commits:')
        for issue_num in issue_refs:
            log.info(f'   - https://github.com/{__github_org__}/{__dev_repo_name__}/issues/{issue_num}')
    log.info('')

    log.info(f'4. Close any JIRA tickets resolved by this release.')
    if jira_refs:
        log.info(f'   Possible JIRA references identified from {__dev_repo_name__} commits:')
        for jira_workitem_id in jira_refs:
            log.info(f'   - https://datadoghq.atlassian.net/browse/{jira_workitem_id}')
    log.info('')

    log.info(f'5. Announce the release in Slack.')
    log.info('    - #rum-unity')
    log.info('    - #rum-sdk-announce')
    log.info('```')
    log.info(f"🚀 We've shipped Unity SDK {new_version}! Notable changes include:")
    upgrades: List[str] = []
    if needs_dd_sdk_ios_upgrade:
        upgrades.append(f'iOS SDK {dd_sdk_ios_version}')
    if needs_dd_sdk_android_upgrade:
        upgrades.append(f'Android SDK {dd_sdk_android_version}')
    if upgrades:
        log.info(f'- Upgraded to {" and ".join(upgrades)}')
    log.info('- [SUMMARIZE THE IMPORTANT CHANGES HERE]')
    log.info(f"See full release notes [here](https://github.com/{__github_org__}/{__release_repo_name__}/releases/tag/{new_version}).")
    log.info('```')
    log.info('')

    log.info(f'6. Return to the {__dev_repo_trunk_branch_name__} branch and go about your day.')
    log.info(f'   - git checkout {__dev_repo_trunk_branch_name__}')
    log.info(f'   - git pull')
    log.info('')

    log.info(f'✅ Release {new_version} prepared!')
    return 0


if __name__ == '__main__':

    parser = argparse.ArgumentParser(description='Cuts a new release')
    parser.add_argument('--local-dev-repo', default='auto', help='Path to the root of the dd-sdk-unity repo from which a new release will be cut; defaults to the repo containing this script.')
    parser.add_argument('--local-release-repo', default='auto', help="Path to the root of the unity-package repo where the release-ready package will be deployed; defaults to '<local-dev-repo>/../unity-package'.")
    parser.add_argument('--version-bump', default='auto', choices=['major', 'minor', 'patch', 'auto'], help='Type of version bump to use; defaults to major if commits since last release contain breaking changes; minor if they contain feature changes; patch otherwise.')
    parser.add_argument('--dd-sdk-ios-version', default='auto', help='Version of the Datadog iOS SDK to target as a dependency; defaults to the latest release.')
    parser.add_argument('--dd-sdk-android-version', default='auto', help='Version of the Datadog Android SDK to target as a dependency; defaults to the latest release.')
    parser.add_argument('--force', '-f', action='store_true', help='If set, suppresses confirmation prompts that normally appear before changes are committed and pushed.')
    args = parser.parse_args()

    sys.exit(prepare_release(args.local_dev_repo, args.local_release_repo, args.version_bump, args.dd_sdk_ios_version, args.dd_sdk_android_version, args.force))
