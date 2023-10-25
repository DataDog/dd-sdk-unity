# # Contributing

First of all, thanks for contributing!

This document provides some basic guidelines for contributing to this
repository. To propose improvements, feel free to submit a PR or open an Issue.

## Found a bug?
For any urgent matters (such as outages) or issues concerning the Datadog
service or UI, contact our support team via https://docs.datadoghq.com/help/ for
direct, faster assistance.

You may submit a bug report concerning the Datadog Plugin for Unity by opening
a GitHub Issue. Use appropriate template and provide all listed details to help
us resolve the issue.

## Prerequisites

### Install Unity
Install [Unity](https://unity.com/download)

You'll need the following modules to be added as well:
* Android Build Support
* iOS Build Support.

### Install the .NET SDK

Install the [.NET SDK](https://dotnet.microsoft.com/en-us/download)

## Building for iOS

Some of these steps will be automated in the near future, but are currently manual.

### Install xcpretty

```bash
gem install xcpretty
```

### Build the xcframeworks

Build all of the iOS xcframeworks using Carhage:

```bash
# From modules/dd-sdk-ios
./tools/distribution/build-xcframework.sh
```

Copy the resulting frameworks to `packages/Datadog.Unity/Plugins/iOS`. Note the trailing tilde (`~`) on each framework is intentional to prevent Unity from attempting to embed the individual framework files manually.

```bash
#from modules/dd-sdk-ios
cp -r build/xcframeworks/CrashReporter.xcframework ../../packages/Datadog.Unity/Plugins/iOS/CrashReporter.xcframework~
cp -r build/xcframeworks/DatadogCore.xcframework ../../packages/Datadog.Unity/Plugins/iOS/DatadogCore.xcframework~
cp -r build/xcframeworks/DatadogCrashReporting.xcframework ../../packages/Datadog.Unity/Plugins/iOS/DatadogCrashReporting.xcframework~
cp -r build/xcframeworks/DatadogInternal.xcframework ../../packages/Datadog.Unity/Plugins/iOS/DatadogInternal.xcframework~
cp -r build/xcframeworks/DatadogLogs.xcframework ../../packages/Datadog.Unity/Plugins/iOS/DatadogLogs.xcframework~
cp -r build/xcframeworks/DatadogRUM.xcframework ../../packages/Datadog.Unity/Plugins/iOS/DatadogRUM.xcframework~
```

After creating the XCode project, disable Bitcode for all Unity targets.

### Tools Prerequisites

If you need to use any of python scripts in tools, you will need Python 3 and `GitPython` package


## Building for Android

```
NOTE: These steps are temporary until we can find a better way to include the dd-android-sdk.aar. We may still want to depend on the External Dependency Manager,
but we hope we won't need to have end users install it manually.
```

Install the [External Dependency Manager for Unity](https://github.com/googlesamples/unity-jar-resolver) by downloadng the tar.gz release and adding it as a Unity package in the Unity Package Manager.

Under `Project Setting` → `Player` → `Android` → `Publishing Settings` check both `Custom Main Gradle Template` and `Custom Gradle Properties Template`.
