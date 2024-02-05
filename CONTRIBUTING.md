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

### Update xcframeworks

You can update and build a specific version of the iOS libraries by using the script in `tools/scripts`:

```bash
python3 update_versions.py --platform ios --version 2.6.0
```

This will automatically check out the release tag, build the required `.xcframework` files and copy them to the correct locations.

### Manually building xcframeworks

If you want to update to a specific commit for dd-sdk-ios, you can update the git submodule held in `modules/dd-sdk-ios`.  Then, you can build all of the iOS xcframeworks:

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

### Tools Prerequisites

If you need to use any of python scripts in tools, you will need Python 3 and `GitPython` package
