# Contributing

First of all, thanks for contributing!

This document provides some basic guidelines for contributing to this repository. To propose improvements, feel free to submit a PR or [open an issue][issues].

[issues]: https://github.com/DataDog/dd-sdk-unity/issues

## Found a bug?

For any urgent matters (such as outages) or issues concerning the Datadog service or UI, contact our support team via https://docs.datadoghq.com/help/ for direct, faster assistance.

You may submit a bug report concerning the Datadog Unity SDK by [opening a GitHub Issue][issues]. Use appropriate template and provide all listed details to help us resolve the issue.

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

To build the SDK for Android, you'll also need to:

- In Unity Hub, ensure that all relevant Unity Editor installs include the **Android Build Support** components, along with **OpenJDK** and **Android SDK & NDK Tools**.
- For Android emulator support in scripts: install the [Android SDK][android-sdk] and the [`cmdline-tools`][cmdline-tools] package.
    - To verify: `$ANDROID_HOME` should be set, and `$ANDROID_HOME/cmdline-tools/latest` should exist.

[android-sdk]: https://developer.android.com/studio
[cmdline-tools]: https://developer.android.com/tools

### iOS prerequisites

To build the SDK for iOS, you'll also need to:

- In Unity Hub, ensure that all relevant Unity Editor installs include the **iOS Build Support** component.
- Install [`Xcode`][xcode].
    - To verify: `xcodebuild -version`
- Ensure that you've configured Xcode for automatic signing by authenticating with your Apple ID.
- Install [`xcbeautify`][xcbeautify] via `brew install xcbeautify`
    - To verify: `xcbeautify --version`
- Ensure that you have [Ruby][ruby] installed on your system.
    - To verify: `ruby --version`, `gem --version`

#### Troubleshooting iOS Resolver

Ruby is required by [External Dependency Manager for Unity (EDM4U)][edm4u] when targeting iOS in Unity projects. EDM4U uses Ruby to install the [`cocoapods`][cocoapods] gem, which it then uses to manage iOS dependencies during the build process.

EDM4U can sometimes fail to resolve the `pod` binary that it installs. If you get errors about CocoaPods within Unity (in a window titled "iOS Resolver"), try the following workaround:

- Verify that `pod` has been installed: `pod --version`
    - If it's not installed: `gem install cocoapods --user-install`
- Symlink the `pod` binary to one of the hardcoded search paths used by EDM4U:
    - e.g. `sudo ln -s $(which pod)`
- Restart Unity.

[xcode]: https://developer.apple.com/xcode/
[xcbeautify]: https://github.com/cpisciotta/xcbeautify
[ruby]: https://www.ruby-lang.org/en/downloads/
[edm4u]: https://github.com/googlesamples/unity-jar-resolver?tab=readme-ov-file#external-dependency-manager-for-unity
[cocoapods]: https://cocoapods.org/

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

When you open a pull request, your changes will be validated exhaustively against our CI pipeline. As you work, though, it's helpful to run tests locally as well.

[test-src]: ./packages/Datadog.Unity/Tests/

### Unity versions

We currently run tests against these versions of Unity:

| Test Suite                             | Unity Project                              | Unity Version |
|----------------------------------------|--------------------------------------------|---------------|
| [`unit_test`][unit-test]               | [`test_scaffolds/2021 LTS`][scaffold-2021] | Unity 2021.3  |
| [`unit_test`][unit-test]               | [`samples/Datadog Sample`][datadog-sample] | Unity 2022.3  |
| [`unit_test`][unit-test]               | [`test_scaffolds/6000 LTS`][scaffold-6000] | Unity 6000.1  |
| [`integration_test`][integration-test] | [`samples/Datadog Sample`][datadog-sample] | Unity 2022.3  |

Our test scripts use the [Unity Hub][unity-hub] binary to locate and manage installed versions of the Unity Editor. If a test script is unable to locate the required version of the editor, it will exit with an error.

To install the latest release of a specific editor version, along with all required components for testing the Datadog SDK, you may use the [`install_unity`][install-unity] script:

```bash
# Install the latest version release of Unity 2022.3 via Unity Hub
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

When you run any of these commands from the root of this repository, the script will locate the approriate version of the Unity Editor, then boot a headless instance of that editor. That editor instance will run `EditMode` tests, then `PlayMode` tests, and then it will exit.

Note that if you already have the target project open in Unity, test scripts will fail to run, as Unity does not permit the same project to be open in multiple editor instances. If you wish to run unit tests without closing the editor, you may run them directly via (`Window` &rarr; `General` &rarr; `Test Runner`).

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

Running integration tests manually is not trivial, given the extra setup steps that are handled by the script. If you wish to manually recreate the integration test environment:

- Start a mock server with `./run-script init_mock_server --start --port 5000`
- Configure the Unity project's Datadog Settings with a **Custom Endpoint** URL
- Ensure that the remaining Datadog Settings match the values specified via `DatadogRuntimeConfig` in [`integration_test.py`][integration-test]
- If desired, start a simulator with `./run-script start_simulator --platform android`
