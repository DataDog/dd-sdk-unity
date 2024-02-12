# Datadog Unity

## Overview

The Datadog Unity SDK supports logging and crash reporting for Android and iOS apps built on Unity.

[//]: # (Repo Note)

## Installing

1. Install [External Dependency Manager for Unity (EDM4U)](https://github.com/googlesamples/unity-jar-resolver). This can be done using [Open UPM](https://openupm.com/packages/com.google.external-dependency-manager/).

2. Add the Datadog SDK Unity package from its Git URL at [https://github.com/DataDog/unity-package](https://github.com/DataDog/unity-package).

> [!NOTE]
> Datadog plans on adding support for Open UPM after Closed Beta.

3. Configure your project to use [Gradle templates](https://docs.unity3d.com/Manual/gradle-templates.html), and enable both `Custom Main Template` and `Custom Gradle Properties Template`.

4. If you build and recieve `Duplicate class` errors (common in Unity 2022.x) add the following block in the `dependencies` block in your `mainTemplate.gradle`:

   ```groovy
   constraints {
        implementation("org.jetbrains.kotlin:kotlin-stdlib-jdk8:1.8.0") {
            because("kotlin-stdlib-jdk8 is now a part of kotlin-stdlib")
        }
   }
   ```

## Setup

1. In Datadog, navigate to [UX Monitoring > Setup & Configuration > New Application](https://app.datadoghq.com/rum/application/create)

2. Choose `Unity` as the application type. If you do not see `Unity` as an application type, please reach out to your CSM to be added to the Unity beta.

3. After adding the Datadog Unity SDK, configure Datadog from your Project Settings:
    a. Enable Datadog and RUM
    b. Copy your `Client Token` and `Application Id` into the fields in the settings window.
    c. Verify that your `Site` is correct.

# Using Datadog

## Setting Tracking Consent

In order to be compliant with data protection and privacy policies, the Datadog Unity SDK requires setting a tracking consent value.

The `trackingConsent` setting can be one of the following values:

  * `TrackingConsent.Pending`: The Unity SDK starts collecting and batching the data but does not send it to Datadog. The Unity SDK waits for the new tracking consent value to decide what to do with the batched data.
  * `TrackingConsent.Granted`: The Unity SDK starts collecting the data and sends it to Datadog.
  * `TrackingConsent.NotGranted`: The Unity SDK does not collect any data. No logs are sent to Datadog.

Before Datadog sends any data, we need to confirm the user's `Tracking Consent`. This is set to `TrackingConsent.Pending` during initialization,
and needs to be set to `TrackingConsent.Granted` before Datadog sends any information.

```cs
DatadogSdk.Instance.SetTrackingConsent(TrackingConsent.Granted);
```

## Logging

You can intercept and send logs from Unity's default debug logger by enabling the option and threshold in your projects settings.

Datadog maps the Unity levels to the following in Datadog's Logging Levels:

| Unity LogType  | Datadog Log Level |
| -------------- | ----------------- |
| Log            |  Info             |
| Error          |  Error            |
| Assert         |  Critical         |
| Warning        |  Warn             |
| Exception      |  Critical         |

You can access this default logger to add attributes or tags through the `DatadogSdk.DefaultLogger` property.

You can also create additional loggers for more fine grained control of thresholds, service names, logger names, or to supply additional attributes.

```cs
var logger = DatadogSdk.Instance.CreateLogger(new DatadogLoggingOptions()
{
    SendNetworkInfo = true,
    DatadogReportingThreshold = DdLogLevel.Debug,
});
logger.Info("Hello from Unity!");

logger.Debug("Hello with attributes", new()
{
    { "my_attribute", 122 },
    { "second_attribute", "with_value" },
    { "bool_attribute", true },
    {
        "nested_attribute", new Dictionary<string, object>()
        {
            { "internal_attribute", 1.234 },
        }
    },
});
```

## Real User Monitoring (RUM)

### Manual Scene (View) Tracking

To manually track new Scenes (`Views` id Datadog), use the `StartVeiw` and `StopView` methods:

```cs
public void Start()
{
    DatadogSdk.Instance.Rum.StartView("My View", new()
    {
        { "view_attribute": "active" }
    });
}
```

Starting a new view automatically ends the previous view.

### Automatic Scene Tracking

You can also set `Enable Automatic Scene Tracking` in your Project Settings to enable automatically tracking active scenes. This uses Unity's `SceneManager.activeSceneChanged` event to automatically start new scenes.

### Web Requests / Resource Tracking

Datadog offers `DatadogTrackedWebRequest`, which is a `UnityWebRequest` wrapper intended to be a drop-in replacement for `UnityWebRequest`. `DatadogTrackedWebRequest` enables [Datadog Distributed Tracing](https://docs.datadoghq.com/real_user_monitoring/connect_rum_and_traces/?tab=browserrum).

To enable Datadog Distributed Tracing, you must set the `First Party Hosts` in your project settings to a domain that supports distributed tracing. You can also modify the sampling rate for distributed tracing by setting the `Tracing Sampling Rate`.

`First Party Hosts` does not allow wildcards, but matches any subdomains for a given domain. For example, api.example.com matches staging.api.example.com and prod.api.example.com, but not news.example.com.

## Contributing

Pull requests are welcome. First, open an issue to discuss what you would like to change.

For more information, read the [Contributing guidelines](https://github.com/DataDog/dd-sdk-unity/blob/main/CONTRIBUTING.md).

## License

For more information, see [Apache License, v2.0](https://github.com/DataDog/dd-sdk-unity/blob/main/LICENSE).
