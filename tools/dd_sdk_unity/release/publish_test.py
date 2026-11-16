# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""Publication approval, recovery, and remote-write boundaries."""

from pathlib import Path
from unittest.mock import Mock

import pytest

from . import publish as publication

SOURCE = "a" * 40
MAIN = "b" * 40
PUBLISHED = "c" * 40


@pytest.fixture
def publication_inputs(tmp_path, monkeypatch):
    source, package = Mock(), Mock()
    source.reference_sha.return_value = None
    package.reference_sha.side_effect = lambda ref: MAIN if ref == "heads/main" else None
    package.release.return_value = None
    package.repository.clone_url = "https://github.invalid/distribution"
    distribution = Mock(root=tmp_path / "distribution")
    distribution.read.return_value = b'{"version":"1.7.1"}'
    distribution.repository.git.diff.return_value = "publication diff"
    distribution.push.return_value = PUBLISHED
    clone = Mock(return_value=distribution)
    monkeypatch.setattr(publication.GitOperations, "clone", clone)
    monkeypatch.setattr(publication, "reviewed_pull", Mock())
    monkeypatch.setattr(publication, "check_versions", Mock())
    monkeypatch.setattr(publication, "install", Mock())

    def extract(archive, output):
        output.mkdir()
        (output / "CHANGELOG.md").write_text("# Change Log\n\n## 1.7.2\n\n- Reviewed notes.\n")

    monkeypatch.setattr(publication, "extract_package", Mock(side_effect=extract))
    return source, package, distribution


@pytest.mark.parametrize("existing", ["source-tag", "package-tag", "package-release"])
def test_existing_publication_stops_before_unpacking(publication_inputs, monkeypatch, existing):
    source, package, distribution = publication_inputs
    unpack = Mock()
    monkeypatch.setattr(publication, "extract_package", unpack)
    if existing == "source-tag":
        source.reference_sha.return_value = SOURCE
    elif existing == "package-tag":
        package.reference_sha.return_value = PUBLISHED
        package.reference_sha.side_effect = None
    else:
        package.release.return_value = Mock()

    with pytest.raises(ValueError, match="already exists"):
        publication.publish(
            source,
            package,
            Path("package.tar.gz"),
            version="1.7.2",
            source_sha=SOURCE,
            package_token="token",
        )

    unpack.assert_not_called()
    distribution.push.assert_not_called()


def test_publish_requires_review_before_unpacking(publication_inputs, monkeypatch):
    source, package, _distribution = publication_inputs
    unpack = Mock()
    monkeypatch.setattr(publication, "extract_package", unpack)
    monkeypatch.setattr(
        publication, "reviewed_pull", Mock(side_effect=ValueError("No merged preparation PR"))
    )
    with pytest.raises(ValueError, match="No merged preparation PR"):
        publication.publish(
            source,
            package,
            Path("package.tar.gz"),
            version="1.7.2",
            source_sha=SOURCE,
            package_token="token",
        )
    unpack.assert_not_called()


@pytest.mark.parametrize("push_fails", [False, True])
def test_publish_releases_the_signed_package_only_after_push(publication_inputs, push_fails):
    source, package, distribution = publication_inputs
    if push_fails:
        distribution.push.side_effect = RuntimeError("push failed")
        with pytest.raises(RuntimeError, match="push failed"):
            publication.publish(
                source,
                package,
                Path("package.tar.gz"),
                version="1.7.2",
                source_sha=SOURCE,
                package_token="token",
            )
        package.create_release.assert_not_called()
        source.delete_branch.assert_not_called()
    else:
        publication.publish(
            source,
            package,
            Path("package.tar.gz"),
            version="1.7.2",
            source_sha=SOURCE,
            package_token="token",
        )
        package.create_release.assert_called_once_with("1.7.2", PUBLISHED, "- Reviewed notes.")
        source.delete_branch.assert_called_once_with("release/1.7.2", SOURCE)
    source.create_reference.assert_called_once_with("tags/1.7.2", SOURCE)
    distribution.push.assert_called_once_with(
        package, branch="main", expected_parent=MAIN, token="token"
    )
    distribution.repository.git.add.assert_called_once_with("--all", "--force")


def test_publish_dry_run_does_not_change_remote_state(publication_inputs, caplog):
    caplog.set_level("INFO")
    source, package, distribution = publication_inputs
    publication.publish(
        source,
        package,
        Path("package.tar.gz"),
        version="1.7.2",
        source_sha=SOURCE,
        package_token="token",
        dry_run=True,
    )
    distribution.commit.assert_called_once()
    source.create_reference.assert_not_called()
    distribution.push.assert_not_called()
    package.create_release.assert_not_called()
    source.delete_branch.assert_not_called()
    assert "Reviewed notes." in caplog.text
    assert "publication diff" in caplog.text
