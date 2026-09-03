# Contributing

First of all, thanks for contributing!

This document provides some basic guidelines for contributing to this repository. To propose improvements, feel free to submit a PR or [open an issue][issues].

[issues]: https://github.com/DataDog/dd-sdk-unity/issues

## Found a bug?

For any urgent matters (such as outages) or issues concerning the Datadog service or UI, contact our support team through https://docs.datadoghq.com/help/ for direct assistance.

To submit a bug report concerning the Datadog Unity SDK, [open a GitHub Issue][issues]. Use the appropriate template and provide all listed details to help us resolve the issue.

## Prerequisites

To work on the Datadog Unity SDK, you'll need to:

- Install [Unity Hub][unity-hub].
- Install the [.NET SDK][dotnet-sdk].
- Ensure that you have [Python 3.10][python] installed on your system.
    - To verify: `python3 --version`

[unity-hub]: https://unity.com/download
[dotnet-sdk]: https://dotnet.microsoft.com/en-us/download
[python]: https://www.python.org/downloads/

### Android prerequisites

To build the SDK for Android:

- In Unity Hub, ensure that all relevant Unity Editor installs include the **Android Build Support** components, along with **OpenJDK** and **Android SDK & NDK Tools**.
- For Android emulator support in scripts, install the [Android SDK][android-sdk] and the [`cmdline-tools`][cmdline-tools] package.
    - To verify the SDK has successfully installed, check that `$ANDROID_HOME` is set, and `$ANDROID_HOME/cmdline-tools/latest` exists.

[android-sdk]: https://developer.android.com/studio
[cmdline-tools]: https://developer.android.com/tools

### iOS prerequisites

To build the SDK for iOS:

- In Unity Hub, ensure that all relevant Unity Editor installs include the **iOS Build Support** component.
- Install [`Xcode`][xcode].
    - To verify Xcode successfully installed, run: `xcodebuild -version`
- Ensure that you've configured Xcode for automatic signing by authenticating with your Apple ID.
- Install [`xcbeautify`][xcbeautify] via `brew install xcbeautify`
    - To verify xcbeautify successfully installed, run: `xcbeautify --version`

Datadog's own iOS dependency (dd-sdk-ios) is a prebuilt XCFramework vendored directly into the `Datadog.Unity` package via Unity's native Plugin importer — neither CocoaPods nor SPM runs to resolve it. Ruby/CocoaPods are **not** required for Datadog's own dependency (EDM4U's Android-side Gradle dependency resolution is unaffected and still applies until Phase 3).

Before your first iOS build in a fresh clone, stage the XCFramework once:

```bash
./run-script ios_xcframework stage
```

The staged bundles under `packages/Datadog.Unity/Plugins/iOS/` are `.gitignore`d in this dev repo (fetched on demand, not committed to `dd-sdk-unity`'s own git history). To check the staged state offline without re-fetching:

```bash
./run-script ios_xcframework verify
```

The pinned dd-sdk-ios version, its expected SHA-256, and its expected module list are the single source of truth in [`packages/Datadog.Unity/Editor/iOS/IosDependencyVersion.json`][ios-dependency-version-json].

[xcode]: https://developer.apple.com/xcode/
[xcbeautify]: https://github.com/cpisciotta/xcbeautify
[ios-dependency-version-json]: ./packages/Datadog.Unity/Editor/iOS/IosDependencyVersion.json

#### Troubleshooting iOS dependencies

If an iOS build fails with a message from `IosXcframeworkPreprocessBuild` naming missing XCFramework module(s) under `packages/Datadog.Unity/Plugins/iOS/`, stage the XCFramework and try again:

```bash
./run-script ios_xcframework stage
```

EDM4U (`com.google.external-dependency-manager`) still exists in `packages/Datadog.Unity/package.json` and still manages the Android dependency until Phase 3. Seeing an EDM4U window appear during an Android build is expected; seeing CocoaPods/`pod` resolution attempted during an iOS build is not — Datadog's iOS dependency no longer goes through EDM4U or CocoaPods at all.

#### Updating the pinned dd-sdk-ios version

Bumping the pinned dd-sdk-ios version is a deliberate, manual maintainer action:

```bash
./run-script update_ios_version <version>
```

This fetches the target version's XCFramework, re-verifies that all currently-vendored modules are still present in the bundle (failing loudly and leaving the pin untouched if any are missing), stages and structurally verifies the result, and only then rewrites `IosDependencyVersion.json` with the new version and its digest. Useful flags:

- `--dry-run` — perform the fetch/verify/stage steps and print the pin that would be written, without writing it.
- `--force` — re-run the bump even if the target version already matches the current pin.

`NATIVE_SDK_VERSIONS.md` and the changelog are updated by the release flow, not by this script.

Committing the vendored XCFramework into the `unity-package` release payload is release-automation work owned by Phase 4 — no release step is documented here.

## Repository overview

> The following instructions describe how to develop and test changes to the Datadog Unity SDK itself. For instructions on adding Datadog SDK functionality to your own Unity project, see the documentation in the [`DataDog/unity-package`][unity-package] repository.

At a high level, the `dd-sdk-unity` repository is organized like so:

- [`samples/`][samples]: Unity projects used for running tests and showcasing example usage.
- [`test_scaffolds/`][test-scaffolds]: Additional Unity projects used to test compatibility with a wider range of Unity versions.
- [`tools/mock_server/`][mock-server]: Flask app used by integration tests to record HTTP requests sent by the SDK and validate expected usage.
- [`tools/scripts/`][scripts]: Python scripts used to automate common development tasks.
- [`packages/Datadog.Unity/`][package-root]: The source of the Datadog Unity package, which is deployed to the root of the [`unity-package`][unity-package] repo.

To get started, open `samples/Datadog Sample` in Unity 2023, then open the same project in your IDE of choice to begin editing the source of the `Datadog.Unity` package.

[unity-package]: https://github.com/DataDog/unity-package?tab=readme-ov-file#datadog-unity
[samples]: ./samples/
[test-scaffolds]: ./test_scaffolds/
[mock-server]: ./tools/mock_server/
[scripts]: ./tools/scripts/
[package-root]: ./packages/Datadog.Unity/

## Running tests locally

The Datadog SDK includes a suite of tests in [`packages/Datadog.Unity/Tests`][test-src]. All significant code changes to the SDK must have adequate test coverage.

When you open a pull request, your changes are validated exhaustively against our CI pipeline. As you work, though, it's helpful to run tests locally as well.

[test-src]: ./packages/Datadog.Unity/Tests/

### Unity versions

We currently run tests against these versions of Unity:

| Test Suite                             | Unity Project                              | Unity Version |
|----------------------------------------|--------------------------------------------|---------------|
| [`unit_test`][unit-test]               | [`test_scaffolds/2021 LTS`][scaffold-2021] | Unity 2021.3  |
| [`unit_test`][unit-test]               | [`samples/Datadog Sample`][datadog-sample] | Unity 2022.3  |
| [`unit_test`][unit-test]               | [`test_scaffolds/6000 LTS`][scaffold-6000] | Unity 6000.1  |
| [`integration_test`][integration-test] | [`samples/Datadog Sample`][datadog-sample] | Unity 2022.3  |

Our test scripts use the [Unity Hub][unity-hub] binary to locate and manage installed versions of the Unity Editor. If a test script is unable to locate the required version of the editor, it exits with an error.

To install the latest release of a specific editor version, along with all required components for testing the Datadog SDK, you can use the [`install_unity`][install-unity] script:

```bash
# Install the latest version release of Unity 2022.3 through Unity Hub
./run-script install_unity 2022.3
```

[unit-test]: ./tools/scripts/unit_test.py
[integration-test]: ./tools/scripts/integration_test.py
[install-unity]: ./tools/scripts/install_unity.py
[datadog-sample]: ./samples/Datadog%20Sample/
[demo-data]: ./samples/Demo%20Data/
[scaffold-2021]: ./test_scaffolds/2021%20LTS/
[scaffold-6000]: ./test_scaffolds/6000%20LTS/
[tests-src]: ./packages/Datadog.Unity/Tests/

### Unit tests

The [`unit_test`][unit-test] script runs all tests except for those in the `Integration` namespace.

```bash
# Run Datadog SDK unit tests against the default 'Datadog Sample' project, with Unity 2022
./run-script unit_test

# Run the same tests in the '6000 LTS' project, with any version of Unity 6
./run-script unit_test --project 'test_scaffolds/6000 LTS' --unity-version 6000

# Run the same tests in the '2021 LTS' project, requiring an exact Unity 2021 build
./run-script unit_test --project 'test_scaffolds/2021 LTS' --unity-version 2021.3.44f1
```

When you run any of these commands from the root of this repository, the script locates the appropriate version of the Unity Editor, then boots a headless instance of that editor. That editor instance runs `EditMode` tests, then `PlayMode` tests, and then it exits.

**Note**: If you already have the target project open in Unity, test scripts will fail to run, as Unity does not permit the same project to be open in multiple editor instances. If you wish to run unit tests without closing the editor, you can run them directly through (`Window` &rarr; `General` &rarr; `Test Runner`).

Unit test results are written in JUnit format to `unit-test-<mode>.xml`. If all tests pass, the script will exit with a status code of 0.

### Integration tests

The [`integration_test`][integration-test] script runs all tests in the `Integration` namespace.

```bash
# Run integration tests on Android, using an AVD
./run-script integration_test --platform android

# Run integration tests on iOS, using a physically-connected iPhone
./run-script integration_test --platform ios --target device
```

As with the unit test script, these commands launch the Unity Editor in headless mode to invoke the tests, but the integration test script also performs some additional setup:

- It launches a mock server that records all incoming HTTP requests from the SDK and allows the test to inspect and validate the set of requests received.
- It configures the Unity project to use that mock server in lieu of the Datadog intake endpoint, while also ensuring that the project's Datadog settings are configured with the expected feature set.
- It ensures that the tests run on supported platforms (Android or iOS), thereby exercising the client functionality of the underlying Android and iOS SDKs.

Integration test results are written in JUnit format to `integration-test-<platform>.xml`. If all tests pass, the script will exit with a status code of 0.

#### Debugging integration tests

Running integration tests manually is not trivial, given the extra setup steps that are handled by the script. If you want to manually recreate the integration test environment:

- Start a mock server with `./run-script init_mock_server --start --port 5000`
- Configure the Unity project's Datadog Settings with a **Custom Endpoint** URL
- Ensure that the remaining Datadog Settings match the values specified through `DatadogRuntimeConfig` in [`integration_test.py`][integration-test]
- If desired, start a simulator with `./run-script start_simulator --platform android`
