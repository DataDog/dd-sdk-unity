# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""Package file selection, native-stage boundaries, and deterministic archives."""

import os
import tarfile
from unittest.mock import Mock

import pytest

from dd_sdk_unity.scripts.common.git import GitOperations

from . import ASSEMBLY_PATH, PACKAGE_PATH, package


@pytest.fixture
def source_package(tmp_path, repository, make_commit):
    source = tmp_path / "source"
    documents = {
        f"{PACKAGE_PATH}/package.json": '{"version":"1.7.2"}',
        ASSEMBLY_PATH: '[assembly: AssemblyVersion("1.7.2")]\n[assembly: AssemblyInformationalVersion("1.7.2")]\n',
        f"{PACKAGE_PATH}/README.md": "# Package\n[//]: # (Repo Note)\n",
        f"{PACKAGE_PATH}/Runtime/Code.cs": "committed SDK source",
        f"{PACKAGE_PATH}/Tests/NotShipped.cs": "tests",
        f"{PACKAGE_PATH}/Tests.meta": "test metadata",
        f"{PACKAGE_PATH}/Plugins/iOS/Stale.xcframework/stale": "stale generated payload",
        f"{PACKAGE_PATH}/NOTICE.txt": "committed notice",
        f"{PACKAGE_PATH}/Editor/com.datadoghq.unity.editor.asmdef": "{}",
        f"{PACKAGE_PATH}/Editor/com.datadoghq.unity.Editor.asmdef.meta": "guid: preserved-editor-guid\n",
        "tools/snippets/deployment_repo.md": "Distribution instructions.\n",
    }
    for name, text in documents.items():
        path = source / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text)
    commit = make_commit("a" * 40, documents)
    repository.working_tree_dir = str(source)
    repository.commit.return_value = commit
    return GitOperations(repository), commit.hexsha


def test_assemble_uses_committed_source_and_delegates_native_staging(
    source_package, tmp_path, monkeypatch
):
    git, sha = source_package
    pin = Mock(modules=("DatadogCore",))
    monkeypatch.setattr(package, "pins", Mock(return_value=(pin, Mock())))
    extract = Mock(side_effect=lambda archive, output, pin: output.mkdir(parents=True))
    monkeypatch.setattr(package, "extract_native", extract)
    (git.root / PACKAGE_PATH / "Runtime/Code.cs").write_text("uncommitted edits")
    archive = tmp_path / "native.zip"
    output = tmp_path / "package"

    package.assemble(git, sha, archive, output)

    assert (output / "Runtime/Code.cs").read_text() == "committed SDK source"
    assert not (output / "Tests").exists() and not (output / "Tests.meta").exists()
    assert not (output / "Plugins/iOS/Stale.xcframework/stale").exists()
    assert (output / "NOTICE.txt").read_text() == "committed notice"
    assert (output / "README.md").read_text() == "# Package\nDistribution instructions.\n"
    assert (
        output / "Editor/com.datadoghq.unity.editor.asmdef.meta"
    ).read_text() == "guid: preserved-editor-guid\n"
    extract.assert_called_once_with(archive, output / "Plugins/iOS", pin)


def test_archive_is_reproducible_when_source_timestamps_change(tmp_path):
    payload = tmp_path / "payload"
    payload.mkdir()
    source = payload / "file.txt"
    source.write_text("package data")
    first, second = tmp_path / "first.tar.gz", tmp_path / "second.tar.gz"
    package.archive_package(payload, first)
    os.utime(source, (10, 10))
    package.archive_package(payload, second)
    assert first.read_bytes() == second.read_bytes()
    with tarfile.open(first) as archive:
        entry = archive.extractfile("./file.txt")
        assert entry is not None
        assert entry.read() == b"package data"


@pytest.mark.parametrize("attribute", ["AssemblyVersion", "AssemblyInformationalVersion"])
def test_check_versions_rejects_stale_prerelease_metadata(tmp_path, attribute):
    (tmp_path / "Runtime").mkdir()
    (tmp_path / "package.json").write_text('{"version":"2.0.0-preview.1"}')
    values = {"AssemblyVersion": "2.0.0", "AssemblyInformationalVersion": "2.0.0-preview.1"}
    values[attribute] = "1.7.1"
    (tmp_path / "Runtime/AssemblyInfo.cs").write_text(
        "\n".join(f'[assembly: {name}("{value}")]' for name, value in values.items())
    )
    with pytest.raises(ValueError, match="differs"):
        package.check_versions(tmp_path, "2.0.0-preview.1")


def test_install_replaces_payload_but_preserves_distribution_administration(tmp_path):
    checkout, payload = tmp_path / "checkout", tmp_path / "payload"
    (checkout / ".github").mkdir(parents=True)
    (checkout / ".github/CODEOWNERS").write_text("owners")
    (checkout / ".gitignore").write_text("ignore rules")
    (checkout / "old.cs").write_text("old payload")
    payload.mkdir()
    (payload / "new.cs").write_text("new payload")
    package.install(payload, checkout)
    assert not (checkout / "old.cs").exists()
    assert (checkout / "new.cs").read_text() == "new payload"
    assert (checkout / ".github/CODEOWNERS").read_text() == "owners"
    assert (checkout / ".gitignore").read_text() == "ignore rules"
