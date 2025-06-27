import os
import re
import sys
import argparse
import tempfile
import subprocess
from typing import List, Tuple

import git

from common.log import init_logger, get_default_logger
from common.versions import Version, VersionBump, read_external_dependency_versions, SdkVersionTable, modify_package_json, modify_assemblyinfo, write_external_dependency_versions, ExternalDependencyVersions
from common.commit import CommitInfo
from common.github import resolve_latest_release_version, get_file_contents, get_releases_between


__github_org__ = 'DataDog'
__dev_repo_name__ = 'dd-sdk-unity'
__dev_repo_trunk_branch_name__ = 'develop'
__release_repo_name__ = 'unity-package'
__unreleased_changes_heading__ = '## Unreleased'


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
            subprocess.check_call(['git', '--no-pager', 'diff', '--staged', '--unified=0', '--color-words'], cwd=repo_root)
            continue
        if choice == 'commit':
            return True
        return False


def prepare_release(local_dev_repo_root: str, version_bump_str: str, dd_sdk_ios_version_str: str, dd_sdk_android_version_str: str, force: bool) -> int:
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
    _log_section('Preparing dd-sdk-unity repo...')
    
    # Get the path to our development repo: this defaults to the same repo that we're
    # running this script in, but it can be overridden by the user to target a clean
    # copy that they've made elsewhere
    if local_dev_repo_root == 'auto':
        local_dev_repo_root = os.path.normpath(os.path.join(os.path.dirname(__file__), '..', '..'))
    log.info(f'Using development repo: {local_dev_repo_root}')

    # This is the dd-sdk-unity repo that we'll prepare the release in; ensure that it's
    # up to date with the latest changes
    local_dev_repo = git.Repo(local_dev_repo_root)
    log.info(f'Fetching from origin in {__dev_repo_name__}...')
    local_dev_repo.remote().fetch()

    # Require that we're in the trunk branch that we canonically release changes from
    if local_dev_repo.active_branch.name != __dev_repo_trunk_branch_name__:
        log.error(f'{__dev_repo_name__} is not in branch {__dev_repo_trunk_branch_name__}!')
        log.error(f'Current branch: {local_dev_repo.active_branch.name}')
        log.error(f'Please stash any local changes and run `git checkout {__dev_repo_trunk_branch_name__}`.')
        return 1
    
    # Require that we have no local changes
    if local_dev_repo.is_dirty():
        # TODO: This does NOT validate that we're at latest in develop
        log.error(f'{__dev_repo_name__} is dirty!')
        log.error(f'Please stash or revert your local changes.')
        return 1
    
    # Read the CHANGELOG.md file in dd-sdk-unity and find the list of unreleased changes
    local_changelog_path = os.path.join(local_dev_repo_root, 'packages', 'Datadog.Unity', 'CHANGELOG.md')
    with open(local_changelog_path) as fp:
        original_changelog_text = fp.read()
    unreleased_changes_text = _read_unreleased_changes(original_changelog_text)
    if unreleased_changes_text.strip() == '':
        log.error(f"CHANGELOG.md does not list any changes to be released!")
        log.error(f"Please check for an '{__unreleased_changes_heading__}' heading in {local_changelog_path}.")
        return 1
    log.info('Unity SDK changes to be released:')
    print(unreleased_changes_text.strip())

    # Read the existing contents of NATIVE_SDK_VERSIONS.md
    native_sdk_versions_path = os.path.join(local_dev_repo_root, 'NATIVE_SDK_VERSIONS.md')
    with open(native_sdk_versions_path) as fp:
        native_sdk_versions = SdkVersionTable.parse(fp.read())

    # Make sure we have other required files present before we proceed
    package_json_path = os.path.join(local_dev_repo_root, 'packages', 'Datadog.Unity', 'package.json')
    if not os.path.isfile(package_json_path):
        raise RuntimeError(f'File not found: {package_json_path}')
    assemblyinfo_cs_path = os.path.join(local_dev_repo_root, 'packages', 'Datadog.Unity', 'Runtime', 'AssemblyInfo.cs')
    if not os.path.isfile(assemblyinfo_cs_path):
        raise RuntimeError(f'File not found: {assemblyinfo_cs_path}')
    datadog_dependencies_xml_path = os.path.join(local_dev_repo_root, 'packages', 'Datadog.Unity', 'Editor', 'DatadogDependencies.xml')
    if not os.path.isfile(datadog_dependencies_xml_path):
        raise RuntimeError(f'File not found: {datadog_dependencies_xml_path}')

    ### Determine how to label our new release of the Unity SDK based on commit history etc.
    _log_section('Resolving new Unity SDK release version...')
    
    # Check the current published version of 'DataDog/unity-package' on GitHub
    prev_release_version = resolve_latest_release_version(__github_org__, __release_repo_name__)
    log.info(f'Last published release of {__release_repo_name__} was: {prev_release_version}')

    # Find the tag in dd-sdk-unity/develop from which that release was made
    prev_release_tag = next((t for t in local_dev_repo.tags if t.name == prev_release_version), None)
    if not prev_release_tag:
        raise RuntimeError(f'Failed to find a commit tagged {prev_release_version} in {__dev_repo_name__}')
    log.info(f'Release {prev_release_version} was cut from {__dev_repo_name__} at commit {prev_release_tag.commit}')

    # Parse the details of all commits in dd-sdk-unity/develop made since the last release
    intervening_commits: List[CommitInfo] = []
    for commit in local_dev_repo.iter_commits(f'{prev_release_tag.commit}..develop'):
        commit_info = CommitInfo.parse(str(commit.message))
        intervening_commits.append(commit_info)

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
        for commit in feature_commits:
            log.info(f'- {commit.headline}')
        suggested_version_bump = max(suggested_version_bump, VersionBump.MINOR)
    breaking_commits = [x for x in intervening_commits if x.bump == VersionBump.MAJOR]
    if breaking_commits:
        log.info(f'{len(feature_commits)} commit(s) introduce breaking changes:')
        for commit in feature_commits:
            log.info(f'- {commit.headline}')
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

    # Verify that we don't already have a tag in this repo for the new version
    existing_tag = next((t for t in local_dev_repo.tags if t.name == str(new_version)), None)
    if existing_tag:
        raise RuntimeError(f'Tag {new_version} already exists in {__dev_repo_name__}')
    
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
    needs_dd_sdk_ios_upgrade = _needs_dependency_upgrade('dd-sdk-ios', dd_sdk_ios_version, prev_release_dependency_versions.dd_sdk_ios)
    needs_dd_sdk_android_upgrade = _needs_dependency_upgrade('dd-sdk-android', dd_sdk_android_version, prev_release_dependency_versions.dd_sdk_android)

    # If we're upgrading the iOS or Android dependencies, get a list of all the
    # releases made in their respective repos since our last Unity release, and collect
    # the list of changes published in each of those releases
    ios_changelog_text = ''
    if needs_dd_sdk_ios_upgrade:
        ios_changelog_text = _prepare_ios_changelog(prev_release_dependency_versions.dd_sdk_ios, dd_sdk_ios_version)
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
    chore_branch_name = f'chore/release-{new_version}'
    chore_branch = local_dev_repo.create_head(chore_branch_name)
    chore_branch.checkout()
    log.info(f'Working in branch {chore_branch_name}.')

    # Update CHANGELOG.md with our final set of changes
    log.info(f'Writing final CHANGELOG to: {local_changelog_path}')
    new_changelog_text = _apply_release_changes(original_changelog_text, new_version, new_changelog_lines)
    with open(local_changelog_path, 'w') as fp:
        fp.write(new_changelog_text)
    local_dev_repo.git.add(local_changelog_path)

    # Update NATIVE_SDK_VERSIONS.md to record the canonical dependency versions in use
    # as of this release
    log.info(f'Updating NATIVE_SDK_VERSIONS.md with an entry for version {new_version}')
    native_sdk_versions.set(new_version, dd_sdk_ios_version, dd_sdk_android_version)
    new_native_sdk_versions_text = native_sdk_versions.render()
    with open(native_sdk_versions_path, 'w') as fp:
        fp.write(new_native_sdk_versions_text)
    local_dev_repo.git.add(native_sdk_versions_path)

    # Commit these changes to the local branch
    _log_section(f'Committing changes to {chore_branch_name}...')
    chore_commit_message = f'chore(release): Prepare version {new_version} for release'
    log.info(f'Pending changes will be committed in branch {chore_branch_name} with message:')
    log.info(f'- {chore_commit_message}')
    if not force and not _prompt_commit(local_dev_repo_root):
        log.error('Abort.')
        return 3
    local_dev_repo.git.commit('-m', chore_commit_message)
    log.info(f'Committed changes to {chore_branch_name}.')

    ### Make changes that will only exist in the release, such as baking in versions
    _log_section('Making release-only changes to Unity package...')

    # Create a release-only branch, inheriting the changes committed to our chore branch
    release_branch_name = f'release/{new_version}'
    release_branch = local_dev_repo.create_head(release_branch_name)
    release_branch.checkout()
    log.info(f'Working in branch {release_branch_name}.')

    # Modify the UPM package version in package.json
    log.info(f'Updating version to {new_version} in: {package_json_path}')
    modify_package_json(package_json_path, new_version)
    local_dev_repo.git.add(package_json_path)

    # Update the AssemblyVersion attribute in AssemblyInfo.cs
    log.info(f'Updating AssemblyVersion to {new_version} in: {assemblyinfo_cs_path}')
    modify_assemblyinfo(assemblyinfo_cs_path, new_version)
    local_dev_repo.git.add(assemblyinfo_cs_path)

    # Bake the latest explicit dd-sdk-ios and dd-sdk-android versions into
    # DatadogDependencies.xml, which configures EDM4U
    log.info(f'Updating EDM4U dependency versions in: {datadog_dependencies_xml_path}')
    write_external_dependency_versions(
        datadog_dependencies_xml_path,
        ExternalDependencyVersions(
            dd_sdk_ios=dd_sdk_android_version,
            dd_sdk_android=dd_sdk_android_version,
        ),
    )
    local_dev_repo.git.add(datadog_dependencies_xml_path)

    # Commit these changes to the local branch
    _log_section(f'Committing changes to {release_branch_name}...')
    release_commit_message = f'Publish version {new_version}'
    log.info(f'Pending changes will be committed in branch {release_branch_name} with message:')
    log.info(f'- {release_commit_message}')
    log.info(f"This commit to {__dev_repo_name__} will also be tagged '{new_version}'.")
    if not force and not _prompt_commit(local_dev_repo_root):
        log.error('Abort.')
        return 3
    local_dev_repo.git.commit('-m', release_commit_message)
    log.info(f'Committed changes to {release_branch_name}.')

    # Create an annotated tag (lightweight tags can not be GPG-signed) to identify the
    # commit in dd-sdk-unity that matches the state of the package as of this release
    local_dev_repo.create_tag(str(new_version), message=f'Release version {new_version}')
    log.info(f'Tagged latest commit as {new_version}.')

    log.info('OK.')
    return 0


if __name__ == '__main__':

    parser = argparse.ArgumentParser(description='Cuts a new release')
    parser.add_argument('--local-dev-repo', default='auto', help='Path to the root of the dd-sdk-unity repo where a new chore/release-X.Y.Z branch will be created; defaults to the repo containing this script.')
    parser.add_argument('--version-bump', default='auto', choices=['major', 'minor', 'patch', 'auto'], help='Type of version bump to use; defaults to major if commits since last release contain breaking changes; minor if they contain feature changes; patch otherwise.')
    parser.add_argument('--dd-sdk-ios-version', default='auto', help='Version of the Datadog iOS SDK to target as a dependency; defaults to the latest release.')
    parser.add_argument('--dd-sdk-android-version', default='auto', help='Version of the Datadog Android SDK to target as a dependency; defaults to the latest release.')
    parser.add_argument('--force', '-f', action='store_true', help='If set, suppresses confirmation prompts that normally appear before changes are committed and pushed.')
    args = parser.parse_args()

    sys.exit(prepare_release(args.local_dev_repo, args.version_bump, args.dd_sdk_ios_version, args.dd_sdk_android_version, args.force))
