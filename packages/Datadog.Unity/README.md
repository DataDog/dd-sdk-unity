# Datadog Unity

## Overview

The Datadog Unity SDK supports logging and crash reporting for Android and iOS apps built on Unity.

[//]: # (Repo Note)

## Installing

1. Install [External Dependency Manager for Unity (EDM4U)](https://github.com/googlesamples/unity-jar-resolver). This can be done using [Open UPM](https://openupm.com/packages/com.google.external-dependency-manager/).

2. Add the Datadog SDK Unity package from its Git URL at [https://github.com/DataDog/unity-package](https://github.com/DataDog/unity-package).  The package url is `https://github.com/DataDog/unity-package.git`.

> [!NOTE]
> Datadog plans on adding support for Open UPM after Beta.

4. Configure your project to use [Gradle templates](https://docs.unity3d.com/Manual/gradle-templates.html), and enable both `Custom Main Template` and `Custom Gradle Properties Template`.

5. If you build and receive `Duplicate class` errors (common in Unity 2022.x) add the following block in the `dependencies` block in your `mainTemplate.gradle`:

   ```groovy
   constraints {
        implementation("org.jetbrains.kotlin:kotlin-stdlib-jdk8:1.8.0") {
            because("kotlin-stdlib-jdk8 is now a part of kotlin-stdlib")
        }
   }
   ```

## Additional Setup and Documentation

For additional documentation on how to setup the Datadog SDK, refer to [Datadog official documentation](https://docs.datadoghq.com/real_user_monitoring/mobile_and_tv_monitoring/setup/unity/)

## Contributing

Pull requests are welcome. First, open an issue to discuss what you would like to change.

For more information, read the [Contributing guidelines](https://github.com/DataDog/dd-sdk-unity/blob/main/CONTRIBUTING.md).

## License

For more information, see [Apache License, v2.0](https://github.com/DataDog/dd-sdk-unity/blob/main/LICENSE).
