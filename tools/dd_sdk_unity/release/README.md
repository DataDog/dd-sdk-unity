# CI releases

`dd-unity-release` runs independently of the old interactive script. The repository
contains preparation, assembly, publication, integration, and the GitLab jobs. Remote rollout and a live
rehearsal are still required before cutover; the old command remains available
until that rehearsal passes.
During CI testing, **publish-release** and **integrate-release** are hard-coded to
preview their operations with read-only credentials. Enable live publication only
after testing.

## Maintainer workflow

1. Push the intended SDK changes to `develop` or a branch matching
   `release/<name>/vN.x` or `release/<name>/vN.N.x`.
   Use conventional PR titles/commit subjects: `feat:` selects
   minor; other classified changes select patch. Breaking markers do not select major.
2. Run **prepare-release** in that push pipeline.
   To override versions, open the job by its name instead of the play button and
   add the optional job variables `BASE_VERSION` and `RELEASE_VERSION` as exact
   `X.Y.Z` values. For the first migration, use `BASE_VERSION=1.7.1` and
   `RELEASE_VERSION=2.0.0`. Leave them empty for automatic version selection.
3. Review the generated PR into `release-prep/X.Y.Z`, including its linked
   baseline-to-code-cut comparison. Edit release notes with normal commits, then
   merge the PR. Additional SDK changes require cancelling and preparing again.
4. Use the **new candidate pipeline for the merged PR's SHA**. Its automatic
   **assemble-package** builds the full payload once and retains `package.tar.gz`.
   Assembly also runs in ordinary CI pipelines, using the checked-out commit.
   A merged preparation PR is required for publication, not assembly.
5. Run **publish-release** in that same pipeline. It consumes the assembly artifact,
   checks the review, unpacks the archive, publishes the signed package commit and
   version tags, and creates the GitHub release.
6. **integrate-release** runs automatically after publication and opens the integration
   PR. If it fails, retry that job independently of publication; it needs no package artifact.
7. Merge the integration PR with a **merge commit**, resolving conflicts while
   preserving newer development changes and higher versions. Do not squash/rebase.
   Integration never publishes another release.

Starting a manual job trusts the tooling at that pipeline's revision. Project
permissions determine who may start these jobs. The source and package policies
bind credentials to this GitLab project and selected branch context; they do not
prove that arbitrary branch code contains a manual job.

Release sources must contain the JSON dependency pins. Assembly bundles the
SHA-256-verified XCFrameworks from the iOS pin.

There is no source `main`, retained release branch, pre-bump, `Unreleased` section,
publication resource group, or dedicated release functional-test stage. Existing
Unity packaging/build jobs remain unchanged; consumer coverage is a separate follow-up.

## Commands and package artifacts

```sh
dd-unity-release prepare --base-version 1.7.1 --release-version 2.0.0
dd-unity-release assemble
dd-unity-release publish --dry-run
dd-unity-release integrate --dry-run
```

Preparation records the selected baseline and source cut in the PR. The merged
commit supplies the target version, reviewed notes, and native pins; preparation
does not write CI artifacts. Assembly packages `CI_COMMIT_SHA` from the existing
CI checkout and writes `package.tar.gz`, without querying a preparation PR or
requesting release credentials. GitLab downloads that archive into publication
through `needs:artifacts` in the same pipeline. Publication reads the version from
`CI_COMMIT_BRANCH`, uses `CI_COMMIT_SHA` and the merged preparation PR, and installs
the archive into the distribution checkout. It does not repeat version inference,
fetch native assets, or assemble again. Integration reads the published source tag;
a dry run before publication uses the current candidate commit. No manifest or
additional handoff files are needed.

The source baseline comes from `package.json` or `--base-version`, its exact source
tag, and the corresponding public GitHub release and package tag. No additional
completion files are needed, including for historical releases. An unprefixed or
unrecognized title defaults to a fix while preserving the original title for review.

Both preview commands share live checks/local staging, but never invoke the signer,
mutate refs/releases/PRs or clean up branches. Both modes use the same preparation
and log output. Dry runs stop before
remote writes and log which operation was skipped. Source commits, PR details,
and diffs print directly in the CI job log. Each operation has its own dry-run job.

`prepare --notes-file PATH` accepts an already reviewed draft instead of calling AI
Gateway. The normal CI job groups/drafts/cleans up Unity changes and legacy working
notes through AI Gateway. Changed native SDK versions link to their GitHub releases
in a separate section, appended after drafting. Upstream native changelogs are not
fetched or supplied to AI. Version selection stays deterministic. No live AI
request is part of the local tests.

## Recovery and cleanup

After a failed publication, inspect the failed job, source/package tags, distribution
`main`, and GitHub release before starting another release. Rerun the matching
**publish-release** job while its original assembly artifact is retained. Matching
source tags, signed package commits, and GitHub releases are reused;
conflicting identities stop rather than overwrite. The tags and GitHub release
identify completed publications after temporary candidate branches are removed.
An expired unpublished artifact requires assembly again and a corresponding
publish job. A retry compares the staged Git tree with an existing package commit
to reuse it after a partial publication.
Editing a GitHub release description does not invalidate its tags; the approved
changelog remains in Git.

Check a completed publication without its original archive using
`dd-unity-release publish --version X.Y.Z`; it logs the source/package commits and
release link. If only integration failed, retry **integrate-release** in GitLab,
or run:

```sh
dd-unity-release integrate --version X.Y.Z
```

That command reads the source tag and does not require preparation
artifacts or temporary branches. Maintainers may edit an existing integration PR;
automation reuses it rather than overwriting conflict resolutions. Publication deletes
`release-prep/X.Y.Z` after the tags and GitHub release are created.
The source tag retains the commit for integration and retries. GitHub automatically
deletes the preparation and integration PR source branches; no post-merge cleanup
job is needed.

Closing an unmerged preparation PR cancels that candidate. Cancel its jobs and
remove only unused automation refs before preparing the same version again. Once
publication has created output, use recovery instead of deleting/replacing tags.
If preparation fails before opening a PR, inspect and remove its unused
`release-prep/X.Y.Z` and `release-notes/X.Y.Z-*` refs before retrying. Verify that no
publication started; preparation does not add a separate recovery state machine.

## Modules

- `cli.py`: command arguments and dispatch.
- `models.py`: in-memory release inputs.
- `preparation.py`: version/note preparation, review PR, and metadata checks against Git.
- `release_version.py`: baseline selection, minor/patch inference, exact overrides.
- `changelog.py`: logical inventory, AI prose, and ordered versioned history.
- `package.py`: reusable assembly, pinned native download, package archives.
- `publish.py`: publication checks, resumable signed publication, and preview.
- `integration.py`: ancestry-preserving integration PR, normalization, and preview.
- `release_state.py`: published source/package tags and GitHub release lookup.
- `git_ops.py`: GitPython operations and encapsulated `commit-headless` signing.
- `github_ops.py`: PyGithub refs, PRs, releases, and streaming asset operations.

## Deployment prerequisites

- Build/publish the ARM64 Linux image with the manual **ci-image** job. The image
  build, assembly, and release jobs use ARM64 runners. Its Dockerfile is
  [ci/Dockerfile.gitlab](../../../ci/Dockerfile.gitlab): GBI Ubuntu 26.04, Python 3.14,
  Git, `commit-headless` 3.4.0, `dd-octo-sts` 1.11.4, and `authanywhere`. Build-time
  checks run as UID 501. Increment `CURRENT_CI_IMAGE` when changing the image.
  An actual ARM64 image build/Linux runtime check is pending; the local daemon was stopped.
- Merge the source repository's `.github/chainguard/self.release*.sts.yaml` policies.
  Install [the package policy](../../../ci/unity-package.release.sts.yaml) as
  `.github/chainguard/dd-sdk-unity.release.sts.yaml` in `unity-package`. Confirm the
  project's identity/selected refs are allowed by dd-octo-sts and branch rules.
  Tokens are short-lived and revoked in `after_script`; dry runs receive source
  read permissions only. Assembly requests no release credentials.
- Rehearse the direct manual jobs and GitLab's assembly artifact handoff,
  maintainer access, native asset download, and full binary signing/publication in
  disposable repositories. Confirm the artifact-size limit and 30-day retention.
- After rehearsal and documentation cutover, remove `scripts/prepare_release.py`,
  its old entry point, and its interactive-only helpers. The new tooling never wraps it.

## Local checks

```sh
.venv/bin/pytest
.venv/bin/ruff check
.venv/bin/ty check
dd-unity-release --help
```

Tests use real temporary Git repositories with fake GitHub/signing responses. They
exercise normal release, hotfix ancestry, cancellation, partial
publication, dry runs, version/history integration, modes/symlinks, and package
round trips without creating production refs, PRs, releases, or model requests.
Local-server lifecycle tests use temporary local processes.
