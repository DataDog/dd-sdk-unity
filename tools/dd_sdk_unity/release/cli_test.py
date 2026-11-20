# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""Release CLI assembly dispatch."""

from pathlib import Path
from unittest.mock import Mock

from . import cli

SOURCE = "a" * 40


def test_assemble_uses_ci_checkout_and_never_opens_source_api(tmp_path, monkeypatch):
    monkeypatch.chdir(tmp_path)
    monkeypatch.setenv("CI_PROJECT_DIR", "ci-checkout")
    monkeypatch.setenv("CI_COMMIT_SHA", SOURCE)
    git = Mock()
    monkeypatch.setattr(cli, "Repo", Mock(return_value="repository"))
    monkeypatch.setattr(cli, "GitOperations", Mock(return_value=git))
    factory = Mock(side_effect=AssertionError("Source API used"))
    monkeypatch.setattr(cli, "GitHubOperations", factory)
    assets = {"ios": Mock(), "windows": Mock()}
    load = Mock(return_value=assets)
    monkeypatch.setattr(cli.native_assets, "load", load)
    archives = [tmp_path / "ios.zip", tmp_path / "windows.zip"]
    monkeypatch.setattr(cli.native_assets, "fetch", Mock(side_effect=archives))
    assemble = Mock()
    monkeypatch.setattr(cli, "assemble_package", assemble)

    cli.run(cli.parser().parse_args(["assemble"]))

    assemble.assert_called_once_with(
        git, SOURCE, dict(zip(assets, archives)), Path("package.tar.gz")
    )
    reader = load.call_args.args[0]
    reader("pin.json")
    git.read.assert_called_once_with(SOURCE, "pin.json")
