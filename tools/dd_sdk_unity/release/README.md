# CI releases

`dd-unity-release` provides preparation, assembly, publication, and integration
commands for the GitLab jobs. Remote rollout and a live rehearsal are still required.
During CI testing, **publish-release** and **integrate-release** are hard-coded to
use `--dry-run` with their normal release credentials. Remove those temporary flags
after testing to enable live publication and integration.

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
3. Review the generated PR into `release/X.Y.Z`, including its linked
   changes and native SDK pin deltas. Edit release notes with normal commits, then
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
CI downloads native release assets through `DEPOT_GITHUB_RELEASES_BASE` without
GitHub API lookups. Local downloads use GitHub when that variable is unset.

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

Preparation shows the selected baseline and source cut in the PR for review. The merged
commit supplies the target version, reviewed notes, and native pins; preparation
does not write CI artifacts. Assembly packages `CI_COMMIT_SHA` from the existing
CI checkout and writes `package.tar.gz`, without querying a preparation PR or
requesting release credentials. GitLab downloads that archive into publication
through `needs:artifacts` in the same pipeline. Publication reads the version from
`CI_COMMIT_BRANCH`, uses `CI_COMMIT_SHA` and the merged preparation PR, and installs
the archive into the distribution checkout. Release notes come from its packaged
changelog. Publication does not recheck the preparation baseline or restrict changes
made in the reviewed PR. It does not repeat version inference,
fetch native assets, or assemble again. Integration uses the same pipeline's
`CI_COMMIT_SHA` after publication. No manifest or
additional handoff files are needed.

The source baseline comes from `package.json` or `--base-version` and its exact source
tag. An unprefixed or
unrecognized title defaults to a fix while preserving the original title for review.
Hotfixes target the latest published version. Patching older release lines is outside
this workflow.

Both preview commands share live checks/local staging, but never invoke the signer,
mutate refs/releases/PRs or clean up branches. Both modes use the same preparation
and log output. Dry runs stop before
remote writes and log which operation was skipped. Source commits, PR details,
and diffs appear directly in the CI job log. Each operation has its own dry-run job.

Preparation follows the C++ release tool: extract unique PR numbers from merge and
squash commit subjects, then fetch each PR's title and body once with eight workers.
Commits without a PR number are not changelog inputs. Ordinary `chore:` PRs skip
the model; breaking chores remain included. The C++ grouping, synthesis, and cleanup
prompts and response schemas are used with the SDK name changed to Unity.
Grouping uses numbers and titles, synthesis uses titles and bodies, and cleanup
deduplicates the combined entries. The PR lists those groups and any other PRs
excluded from drafting, using bare PR references. No file paths are sent to the model.
Native SDK versions are compared using the base commit's
dependency pins, falling back to its `NATIVE_SDK_VERSIONS.md` entry when those pin
files are absent. Native SDK upgrades add one summary call per upgraded SDK,
using only its changelog entries newer than the baseline and through the pinned version.
Filtered Unity-relevant sub-bullets appear under fixed native release links, outside
the Unity cleanup pass. Unchanged pins and rollbacks make no upstream fetch or AI call.
Upstream changelogs are read at their pinned Git tags without cloning: GitRetriever's
file API with `CI_JOB_TOKEN` in CI, or PyGithub locally. Version selection stays
deterministic. No live AI request is part of the local tests.

## Recovery and cleanup

Publication does not resume partial releases. If a job fails after writing remote
state, inspect its log, source/package tags, distribution `main`, and GitHub release.
Recover or clean up those outputs manually before rerunning. An existing version
tag or GitHub release stops publication. An expired unpublished artifact requires
assembly again and a corresponding publish job.

If only integration failed, retry **integrate-release** in GitLab. It uses the
pipeline's source SHA and does not require package artifacts or temporary branches.
Maintainers may edit an existing integration PR;
automation reuses it rather than overwriting conflict resolutions. Publication deletes
`release/X.Y.Z` after the tags and GitHub release are created.
GitHub automatically deletes the preparation and integration PR source branches;
no post-merge cleanup job is needed.

Closing an unmerged preparation PR cancels that candidate. Cancel its jobs and
remove only unused automation refs before preparing the same version again. Once
publication has created output, inspect and recover manually before rerunning.
If preparation fails before opening a PR, inspect and remove its unused
`release/X.Y.Z` and `release-notes/X.Y.Z-*` refs before retrying. Verify that no
publication started; preparation does not add a separate recovery state machine.

## Modules

- `cli.py`: command arguments and dispatch.
- `models.py`: in-memory release inputs.
- `preparation.py`: version/note preparation and the review PR.
- `release_version.py`: baseline selection, minor/patch inference, exact overrides.
- `changelog.py`: logical inventory, AI prose, and ordered versioned history.
- `changelog_prompts.py`: C++ PR grouping, synthesis, and cleanup prompts and schemas.
- `package.py`: reusable assembly, pinned native download, package archives.
- `publish.py`: signed publication and preview.
- `integration.py`: ancestry-preserving integration PR, normalization, and preview.
- `git_ops.py`: GitPython operations and encapsulated `commit-headless` signing.
- `github_ops.py`: PyGithub refs, PRs, and releases.

## Deployment prerequisites

- Build/publish the amd64 Linux image with the manual **ci-image** job. The image
  build, assembly, and release jobs use amd64 runners. Its Dockerfile is
  [ci/Dockerfile.gitlab](../../../ci/Dockerfile.gitlab): GBI Ubuntu 26.04, Python 3.14,
  Git, `commit-headless` 3.4.0, `dd-octo-sts` 1.11.4, and `authanywhere`. Build-time
  checks run as UID 501. Increment `CURRENT_CI_IMAGE` when changing the image.
- Merge the source repository's `.github/chainguard/self.release.sts.yaml` policy.
  Land `.github/chainguard/dd-sdk-unity.release.sts.yaml` in
  [unity-package](https://github.com/DataDog/unity-package) before publication. Confirm the
  project's identity/selected refs are allowed by dd-octo-sts and branch rules.
  Tokens are short-lived and revoked in `after_script`. Assembly requests no release
  credentials.
- Rehearse the direct manual jobs and GitLab's assembly artifact handoff,
  maintainer access, native asset download, and full binary signing/publication in
  disposable repositories. Confirm the artifact-size limit and 30-day retention.

## Local checks

```sh
.venv/bin/pytest
.venv/bin/ruff check
.venv/bin/ty check
dd-unity-release --help
```

Tests use real temporary Git repositories with fake GitHub/signing responses. They
exercise normal release, hotfix integration, cancellation, publication failures,
dry runs, version/history integration, modes/symlinks, and package
round trips without creating production refs, PRs, releases, or model requests.
Local-server lifecycle tests use temporary local processes.
