# Datadog Unity

## Overview

The Datadog Unity SDK supports logging and crash reporting for Android and iOS apps built on Unity.

## Installing

To install the Datadog Unity SDK:

1. Install the [External Dependency Manager for Unity (EDM4U)](https://github.com/googlesamples/unity-jar-resolver) using the Unity Package Manager per their instructions. This is required for resolving Android dependencies.
2. Configure your project to use [Gradle templates](https://docs.unity3d.com/Manual/gradle-templates.html), and enable both
`Custom Main Template` and `Custom Gradle Properties Template`. You may also need to add the following block between your `dependencies` and `android`
blocks in your `mainTemplate.gradle`:

   ```groovy
   buildscript {
       dependencies {
           classpath "org.jetbrains.kotlin:kotlin-gradle-plugin:1.6.21"
       }
   }
   ```

3. You can now add the Datadog Unity package.

## Setup

1. After adding the Datadog Unity SDK, you can configure Datadog from your Project Settings.

2. Add your client token and application ID here, and configure your Datadog site.

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
