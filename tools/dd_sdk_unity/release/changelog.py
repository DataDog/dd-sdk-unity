# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""Logical change inventory and versioned changelog/history rendering."""

import json
import os
import re
from collections.abc import Callable
from concurrent.futures import ThreadPoolExecutor

import requests
from git import Repo
from pydantic import BaseModel, ConfigDict

from dd_sdk_unity.scripts.common.git import GitOperations
from dd_sdk_unity.scripts.common.github import GitHubOperations
from dd_sdk_unity.scripts.common.log import get_default_logger
from dd_sdk_unity.scripts.common.versions.sdkversions import SdkVersionTable

from . import ANDROID_PIN_PATH, IOS_PIN_PATH, NATIVE_HISTORY_PATH
from .changelog_prompts import (
    NATIVE_SDK_NOTES_PROMPT,
    ChangelogEntryList,
    PRGroup,
    run_changelog_entry_list_prompt,
    run_cleanup_prompt,
    run_grouped_prs_prompt,
)
from .models import VERSION_PATTERN, Change, Preparation

CONVENTIONAL_TITLE = re.compile(
    r"^(feat|fix|perf|refactor|docs|chore|build|ci|test|style|revert)"
    r"(?:\([^()\n]+\))?!?:\s+\S"
)
HEADINGS = re.compile(rf"^## ({VERSION_PATTERN})\s*$", re.MULTILINE)


def version_key(value: str) -> tuple[int, ...]:
    if not re.fullmatch(VERSION_PATTERN, value):
        raise ValueError(f"Expected an exact X.Y.Z version, got {value!r}")
    return tuple(int(part) for part in value.split("."))


def classify(title: str, body: str = "", **fields) -> Change:
    match = CONVENTIONAL_TITLE.match(title)
    return Change(
        title=title,
        body=body,
        # Historical/unprefixed titles are fixes, without rewriting them.
        kind=match.group(1) if match else "fix",
        **fields,
    )


def collect_changes(
    repo: Repo, github: GitHubOperations, base: str, source: str
) -> tuple[Change, ...]:
    """Collect unique PRs from merge and squash commit subjects in the release range."""
    merge_pattern = re.compile(r"Merge pull request #(\d+)")
    squash_pattern = re.compile(r"\(#(\d+)\)$")
    pr_numbers = set()
    for commit in repo.iter_commits(f"{base}..{source}"):
        subject = str(commit.message).splitlines()[0]
        match = merge_pattern.match(subject) or squash_pattern.search(subject)
        if match:
            pr_numbers.add(int(match.group(1)))
    numbers = sorted(pr_numbers)
    log = get_default_logger()
    log.info(f"Found {len(numbers)} merged PRs")
    log.info("Fetching PR details...")

    with ThreadPoolExecutor(max_workers=8) as executor:
        pulls = tuple(executor.map(github.repository.get_pull, numbers))
    return tuple(
        classify(pull.title, pull.body or "", number=number)
        for number, pull in zip(numbers, pulls)
    )


def sections(text: str) -> tuple[str, dict[str, str]]:
    matches = list(HEADINGS.finditer(text))
    prefix = text[: matches[0].start()].rstrip() if matches else text.rstrip()
    result = {}
    for index, match in enumerate(matches):
        name = match.group(1)
        if name in result:
            raise ValueError(f"Duplicate changelog version {name}")
        end = matches[index + 1].start() if index + 1 < len(matches) else len(text)
        result[name] = text[match.end() : end].strip()
    return prefix, result


def merge_sections(text: str, additions: dict[str, str]) -> str:
    prefix, existing = sections(text)
    for name, body in additions.items():
        version_key(name)
        if name in existing and existing[name] != body.strip():
            raise ValueError(f"Conflicting published changelog section {name}")
        existing[name] = body.strip()
    return (
        prefix
        + "\n\n"
        + "\n\n".join(
            f"## {name}\n\n{existing[name]}".rstrip()
            for name in sorted(existing, key=version_key, reverse=True)
        )
        + "\n"
    )


class NativeNotes(BaseModel):
    model_config = ConfigDict(extra="forbid")
    entries: list[str]


def draft_notes(
    changes: tuple[Change, ...],
    native_notes: str,
    prompt: Callable[[str, type[BaseModel]], BaseModel],
) -> tuple[str, tuple[PRGroup, ...]]:
    """Group PR titles, draft notes from each group's PR details, then clean up the entries."""
    significant_prs = [
        change
        for change in changes
        if not change.title.startswith("chore:") or "BREAKING" in change.title
    ]
    groups = []
    changelog = ChangelogEntryList(breaking_changes=[], features=[], fixes=[])
    if significant_prs:
        groups = run_grouped_prs_prompt(prompt, significant_prs).groups
        for group in groups:
            numbers = {pr.number for pr in group.prs}
            get_default_logger().info(
                f"{group.label} ({','.join(str(number) for number in sorted(numbers))})"
            )
            group_prs = [pr for pr in significant_prs if pr.number in numbers]
            entries = run_changelog_entry_list_prompt(prompt, group.label, group_prs)
            changelog.breaking_changes += entries.breaking_changes
            changelog.features += entries.features
            changelog.fixes += entries.fixes
        changelog = run_cleanup_prompt(prompt, changelog)
    blocks = []
    for title, entries in (
        ("Breaking Changes", changelog.breaking_changes),
        ("Features", changelog.features),
        ("Fixes", changelog.fixes),
    ):
        if entries:
            blocks.append(f"### {title}\n\n" + "\n".join(f"- {entry.text}" for entry in entries))
    if native_notes:
        blocks.append(native_notes)
    notes = "\n\n".join(blocks) or "- Maintenance release; no significant changes."
    return notes, tuple(groups)


def gateway_prompt(text: str, schema: type[BaseModel]) -> BaseModel:
    token = os.environ.get("AI_GATEWAY_TOKEN")
    if not token:
        raise ValueError("AI_GATEWAY_TOKEN is required for release notes")
    response = requests.post(
        "https://ai-gateway.us1.ddbuild.io/v1/responses",
        headers={
            "Authorization": f"Bearer {token}",
            "provider": "openai",
            "source": "claude-code",
            "org-id": "2",
        },
        json={
            "model": "gpt-6-luna",
            "max_output_tokens": 4096,
            "text": {
                "format": {
                    "type": "json_schema",
                    "name": schema.__name__,
                    "strict": True,
                    "schema": schema.model_json_schema(),
                }
            },
            "input": [{"role": "user", "content": text}],
        },
        timeout=180,
    )
    response.raise_for_status()
    answer = response.json()
    if answer.get("status") != "completed":
        raise ValueError(f"AI draft was incomplete: {answer.get('status')}")
    blocks = [
        block["text"]
        for item in answer["output"]
        if item["type"] == "message"
        for block in item["content"]
        if block["type"] == "output_text"
    ]
    if len(blocks) != 1:
        raise ValueError("Expected one structured AI response")
    return schema.model_validate_json(blocks[0])


def base_native_versions(git: GitOperations, plan: Preparation) -> dict[str, str]:
    """Read baseline pins, using its version history when pin files are absent."""
    previous = {}
    for label, path in (("iOS", IOS_PIN_PATH), ("Android", ANDROID_PIN_PATH)):
        try:
            pin = git.read(plan.release.base_sha, path)
        except KeyError:
            history = SdkVersionTable.parse(
                git.read(plan.release.base_sha, NATIVE_HISTORY_PATH).decode()
            )
            row = next(
                (
                    row
                    for row in history.rows
                    if str(row.unity_version) == plan.release.base_version
                ),
                None,
            )
            if row is None:
                raise ValueError(f"Base native history has no {plan.release.base_version} entry")
            previous[label] = row.ios_version_str if label == "iOS" else row.android_version_str
        else:
            previous[label] = json.loads(pin)["version"]
    return previous


def native_release_notes(
    git: GitOperations,
    plan: Preparation,
    prompt: Callable[[str, type[BaseModel]], BaseModel],
) -> str:
    """Link changed native versions and summarize upgrades relevant to Unity users."""
    log = get_default_logger()
    previous = base_native_versions(git, plan)
    blocks = []
    for label, name, before, after in (
        ("iOS", "dd-sdk-ios", previous["iOS"], plan.ios.version),
        ("Android", "dd-sdk-android", previous["Android"], plan.android.version),
    ):
        if before == after:
            continue
        blocks.append(
            f"- {label} SDK: {before} → "
            f"[{after}](https://github.com/DataDog/{name}/releases/tag/{after})."
        )
        if version_key(after) <= version_key(before):
            continue
        with GitHubOperations(f"DataDog/{name}") as native:
            text = native.get_file_contents(f"refs/tags/{after}", "CHANGELOG.md")
        headings = list(
            re.finditer(rf"^#{{1,2}}\s*({VERSION_PATTERN})(?=\s|/|$).*$", text, re.MULTILINE)
        )
        versions = {match.group(1) for match in headings}
        if after not in versions:
            log.warning(f"Skipping {label} summary: could not find changelog version {after}.")
            continue
        selected = []
        for index, match in enumerate(headings):
            if version_key(before) < version_key(match.group(1)) <= version_key(after):
                end = headings[index + 1].start() if index + 1 < len(headings) else len(text)
                selected.append(text[match.start() : end].strip())
        if label == "iOS":
            bundled = plan.ios.modules
        else:
            bundled = json.loads(git.read(plan.release.source_sha, ANDROID_PIN_PATH))["artifacts"]
        result = prompt(
            NATIVE_SDK_NOTES_PROMPT
            + json.dumps(
                {
                    "sdk": label,
                    "from_version": before,
                    "to_version": after,
                    "bundled": bundled,
                    "changelog": "\n\n".join(selected),
                }
            ),
            NativeNotes,
        )
        if not isinstance(result, NativeNotes):
            raise TypeError("Expected native SDK notes")
        for entry in result.entries:
            if "\n" in entry or not entry.strip():
                raise ValueError("Native changelog entries must be nonempty single lines")
            blocks.append(f"  - {entry}")
    return "### Native SDK updates\n\n" + "\n".join(blocks) if blocks else ""
