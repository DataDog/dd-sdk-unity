# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""CI release command entry point."""

import argparse
import os
import tempfile
from contextlib import ExitStack
from pathlib import Path

import requests
from git import Repo
from github import Auth, Github

from dd_sdk_unity.scripts.common.log import init_logger

from .changelog import draft_notes, gateway_prompt, native_release_notes, version_key
from .git_ops import GitOperations
from .github_ops import GitHubOperations
from .integration import integrate
from .package import assemble_package, native_archive, pins
from .preparation import open_preparation, prepare_plan
from .publish import publish


def _version(value: str) -> str:
    try:
        version_key(value)
    except ValueError as error:
        raise argparse.ArgumentTypeError(str(error)) from error
    return value


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser(
        description="Prepare, assemble, publish, and integrate Unity releases"
    )
    commands = result.add_subparsers(dest="command", required=True)

    prepare = commands.add_parser("prepare", help="Capture source and open the metadata review PR")
    prepare.add_argument("--base-version", type=_version, default=None)
    prepare.add_argument("--release-version", type=_version, default=None)

    commands.add_parser("assemble", help="Assemble the current source commit")

    publication = commands.add_parser("publish", help="Publish the assembled package")
    publication.add_argument("--dry-run", action="store_true")

    integration = commands.add_parser("integrate", help="Open/reuse the merge-back PR")
    integration.add_argument("--dry-run", action="store_true")

    return result


def run(args: argparse.Namespace) -> None:
    with ExitStack() as stack:
        if args.command == "assemble":
            with tempfile.TemporaryDirectory(prefix="unity-release-") as scratch:
                root = Path(scratch)
                git = GitOperations(Repo(os.getenv("CI_PROJECT_DIR", ".")))
                source_sha = os.environ["CI_COMMIT_SHA"]
                ios, _ = pins(git, source_sha)
                archive = native_archive(ios, root / "Datadog.xcframework.zip")
                assemble_package(git, source_sha, archive, Path("package.tar.gz"))
            return
        source_token = os.getenv("SOURCE_GITHUB_TOKEN") or os.getenv("GITHUB_TOKEN")
        read_only = getattr(args, "dry_run", False)
        source_client = stack.enter_context(
            Github(auth=Auth.Token(source_token) if source_token else None, per_page=100)
        )
        source = GitHubOperations(
            source_client.get_repo("DataDog/dd-sdk-unity"), read_only=read_only
        )
        if args.command == "integrate":
            integrate(
                source,
                version=_version(os.environ["CI_COMMIT_BRANCH"].removeprefix("release/")),
                source_token=source_token,
                dry_run=args.dry_run,
                source_sha=os.environ["CI_COMMIT_SHA"],
            )
            return
        if args.command == "publish":
            package_token = os.getenv("PACKAGE_GITHUB_TOKEN") or os.getenv("GITHUB_TOKEN")
            package_client = stack.enter_context(
                Github(auth=Auth.Token(package_token) if package_token else None, per_page=100)
            )
            package = GitHubOperations(
                package_client.get_repo("DataDog/unity-package"), read_only=read_only
            )
            publish(
                source,
                package,
                Path("package.tar.gz"),
                version=_version(os.environ["CI_COMMIT_BRANCH"].removeprefix("release/")),
                source_sha=os.environ["CI_COMMIT_SHA"],
                package_token=package_token,
                dry_run=args.dry_run,
            )
            return
        with tempfile.TemporaryDirectory(prefix="unity-release-") as scratch:
            root = Path(scratch)
            git = GitOperations.clone(
                source.repository.clone_url,
                root / "source",
                revision=os.environ["CI_COMMIT_SHA"],
            )
            if not source_token:
                raise ValueError("SOURCE_GITHUB_TOKEN is required for preparation")
            plan = prepare_plan(
                git,
                source,
                git.repository.head.commit.hexsha,
                base_version=args.base_version,
                release_version=args.release_version,
            )
            notes, groups = draft_notes(
                plan.release.changes,
                native_release_notes(git, plan, gateway_prompt),
                gateway_prompt,
            )
            open_preparation(git, source, plan, notes, token=source_token, groups=groups)


def main() -> None:
    args = parser().parse_args()
    init_logger()
    try:
        run(args)
    except (ValueError, RuntimeError, KeyError, requests.RequestException) as error:
        raise SystemExit(f"Release {args.command} failed: {error}") from None
