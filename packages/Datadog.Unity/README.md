# Datadog Unity

## Overview

The Datadog Unity SDK supports Real User Monitoring (RUM), logging, and crash reporting for Android and iOS apps built on Unity.

[//]: # (Repo Note)

## Install via OpenUPM

The Datadog Unity SDK is available on the [OpenUPM registry](https://openupm.com/packages/com.datadoghq.unity/). You can install it via the `openupm` command line tool.

```bash
openupm add com.datadoghq.unity
```

## Manual Installation

1. Install [External Dependency Manager for Unity (EDM4U)](https://github.com/googlesamples/unity-jar-resolver). This can be done using [Open UPM](https://openupm.com/packages/com.google.external-dependency-manager/).

2. Add the Datadog SDK Unity package from its Git URL at [https://github.com/DataDog/unity-package](https://github.com/DataDog/unity-package).  The package url is `https://github.com/DataDog/unity-package.git`.

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

## Contributing

Pull requests are welcome. First, open an issue to discuss what you would like to change.

For more information, read the [Contributing guidelines](https://github.com/DataDog/dd-sdk-unity/blob/main/CONTRIBUTING.md).

## License

For more information, see [Apache License, v2.0](https://github.com/DataDog/dd-sdk-unity/blob/main/LICENSE).
