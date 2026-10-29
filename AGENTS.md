# Repository Instructions

Read [CONTRIBUTING.md](CONTRIBUTING.md) for setup and development workflows.

## Repository layout

- `packages/Datadog.Unity/` contains the SDK: `Runtime/` for runtime code,
  `Editor/` for Editor tooling and build hooks, and `Tests/` for SDK tests.
- `samples/Datadog Sample/` is the default development and test project.
- `tools/dd_sdk_unity/` contains Python tooling, the integration-test mock server,
  and release automation. Console commands are defined in `pyproject.toml`.
- `ci/` contains GitLab job definitions included by `.gitlab-ci.yml`.

## Unity and C#

- Keep SDK code compatible with the minimum Unity version in
  `packages/Datadog.Unity/package.json`. Raising that minimum is a deliberate change.
- Follow `.editorconfig` and the surrounding C# style. Keep platform code within
  the existing assembly definitions and conditional compilation boundaries.
- Preserve Unity asset GUIDs. Include the corresponding `.meta` files when adding,
  moving, or deleting assets and directories under the Unity projects and package.
- Keep changes focused. Exclude incidental Editor changes to scenes, project
  settings, package manifests, and generated IDE files.

## Documentation and comments

- Follow the [documentation and comment guide](CONTRIBUTING.md#documentation-and-comments).
- Keep package documentation customer-facing. Put contributor workflows in
  `CONTRIBUTING.md` and API contracts in C# XML documentation.
- Leave changelog entries, package and assembly versions, and
  `NATIVE_SDK_VERSIONS.md` to the release workflow, unless the task explicitly
  concerns release metadata.

## Tooling and tests

- Install the Python tooling as described in `CONTRIBUTING.md`. Use the
  `dd-unity-*` console commands for repository workflows.
- For Python changes, run `ruff check`, `ty check`, and `pytest` from the repo root.
- For C# changes, run `dd-unity-unit-test`.
- Close the target project in the Unity Editor before running headless tests.
- Use NUnit and the existing NSubstitute setup for C# tests. Prefer parameterized
  cases that report failures separately and assert observable behavior.
- Integration tests belong in `packages/Datadog.Unity/Tests/Integration/`, inherit
  `IntegrationTestBase`, and use the `integration` category.
- Validate platform-specific changes in the affected player with
  `dd-unity-integration-test --platform android`, `--platform ios`, or
  `--platform webgl`; see `CONTRIBUTING.md` for prerequisites and target options.
  Editor tests alone do not validate native bridges or WebGL assemblies.
- Use `dd-unity-integration-test-setup` to manage the mock server separately.
- Report which checks ran and any validation that remains outstanding.

## Commit messages and pull requests

- Follow the [commit message guide](CONTRIBUTING.md#commit-messages).
- Use a Conventional Commit for the main change before requesting review, and
  use a Conventional Commit PR title so release tooling can classify the change.
- Review follow-up commits can use descriptive subjects.
