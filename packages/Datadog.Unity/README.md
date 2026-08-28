# Datadog Unity

## Overview

The Datadog Unity SDK supports Real User Monitoring (RUM), logging, and crash reporting for Android and iOS apps built on Unity.

[//]: # (Repo Note)

## Install via OpenUPM

The Datadog Unity SDK is available on the [OpenUPM registry](https://openupm.com/packages/com.datadoghq.unity/). You can install it using the `openupm` command line tool.

```bash
openupm add com.datadoghq.unity
```

## Manual Installation

1. Install [External Dependency Manager for Unity (EDM4U)](https://github.com/googlesamples/unity-jar-resolver). This can be done using [Open UPM](https://openupm.com/packages/com.google.external-dependency-manager/).

2. Add the Datadog SDK Unity package from its Git URL at [https://github.com/DataDog/unity-package](https://github.com/DataDog/unity-package). The package url is `https://github.com/DataDog/unity-package.git`.

4. Configure your project to use [Gradle templates](https://docs.unity3d.com/Manual/gradle-templates.html), and enable both `Custom Main Template` and `Custom Gradle Properties Template`.

5. If you build and receive `Duplicate class` errors (common in Unity 2022.x), add the following block in the `dependencies` block in your `mainTemplate.gradle`:

   ```groovy
   constraints {
        implementation("org.jetbrains.kotlin:kotlin-stdlib-jdk8:1.8.0") {
            because("kotlin-stdlib-jdk8 is now a part of kotlin-stdlib")
        }
   }
   ```

## Additional Setup and Documentation

For further instructions on how to set up the Datadog SDK, refer to the [RUM Unity Monitoring Setup documentation](https://docs.datadoghq.com/real_user_monitoring/mobile_and_tv_monitoring/setup/unity/).

## Feature Flags assignment requests

Feature Flags assignment requests have no SDK-added timeout or retries by
default (the underlying transport or platform may still impose its own bounds).
The high-level configuration accepts independent convenience settings:

```csharp
DdFlags.Enable(new FlagsConfiguration(
    assignmentRequestTimeoutSeconds: 2,
    assignmentRequestRetryCount: 2));
```

For lower-level control, compose an assignment-only transport. A supplied
transport is used verbatim, replacing the scalar timeout and retry settings:

```csharp
var assignmentTransport = AssignmentRequestTransports.Default
    .WithTimeout(2)
    .WithRetry(2);

DdFlags.Enable(new FlagsConfiguration(assignmentTransport));
```

`WithTimeout(0)` returns the wrapped transport unchanged. Timeout values above
2,147,483 seconds are capped for compatibility across Unity runtimes. Timeout
covers the complete buffered response; at the deadline it requests cancellation
and completes promptly. Retry creates a fresh native request for every attempt,
retries transport failures, HTTP 408, and HTTP 5xx, and does not retry HTTP 429.
The Unity SDK creates and disposes every `UnityWebRequest`; custom transports
exchange immutable, fully formed request values (including the HTTP method) and
fully buffered response values, retain ownership of their own resources, and
must observe cancellation promptly.

## Contributing

Pull requests are welcome. First, open an issue to discuss what you would like to change.

For more information, read the [Contributing guidelines](https://github.com/DataDog/dd-sdk-unity/blob/main/CONTRIBUTING.md).

## License

For more information, see [Apache License, v2.0](https://github.com/DataDog/dd-sdk-unity/blob/main/LICENSE).
