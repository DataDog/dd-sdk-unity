# Datadog Unity

## Installing

Currently, the Datadog Unity SDK needs the [External Dependency Manager for Unity (EDM4U)](https://github.com/googlesamples/unity-jar-resolver)
in order to resolve Android dependencies. First, install this package using the Unity Package Manager per their instructions.

You will also need to configure your project to use [Gradle templates](https://docs.unity3d.com/Manual/gradle-templates.html), and enable both
`Custom Main Template` and `Custom Gradle Properties Template`.  You may also need to add the following block between your `dependencies` and `android`
blocks in your `mainTempalte.gradle`:

```groovy
buildscript {
    dependencies {
        classpath "org.jetbrains.kotlin:kotlin-gradle-plugin:1.6.21"
    }
}
```

After, you can add the Datadog Unity package.

## Setup

After adding the Datadog Unity SDK, you can configure Datadog from your Project Settings.

Add your Client Token and Application ID here, and configure your Datadog site.

# Using Datadog

## Setting Tracking Consent

In order to be compliant with data protection and privacy policies, the Datadog Unity SDK requires setting a tracking consent value.

The trackingConsent setting can be one of the following values:

  * TrackingConsent.pending: The Flutter RUM SDK starts collecting and batching the data but does not send it to Datadog. The Flutter RUM SDK waits for the new tracking consent value to decide what to do with the batched data.
  * TrackingConsent.granted: The Flutter RUM SDK starts collecting the data and sends it to Datadog.
  * TrackingConsent.notGranted: The Flutter RUM SDK does not collect any data. No logs, traces, or RUM events are sent to Datadog.

Before Datadog will send any data, we need to confirm the user's `Tracking Consent`. This is set to `TrackingConsent.Pending` during initialization,
and needs to be set to `TrackingConsent.Granted` before Datadog will send any information.

```cs
DatadogSdk.Instance.SetTrackingConsent(TrackingConsent.Granted);
```

For more information on Tracking Consent see

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

You can also create additional loggers for more fine grained control of thresholds, service names, logger names, or to supply additional
attributes.

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
