# Datadog Unity

The Datadog Unity SDK supports Real User Monitoring (RUM), logging, and crash reporting for Android and iOS apps built on Unity.

Datadog does not support desktop (Windows, Mac, or Linux), console, or web deployments from Unity. If you have a game or application and want to use Datadog RUM to monitor its performance, create a ticket with Datadog support.

## Setup

### Unity Setup

To get started using the Datadog Unity SDK:

1. Install the [External Dependency Manager for Unity (EDM4U)](https://github.com/googlesamples/unity-jar-resolver). Datadog uses EDM4U to manage dependencies on Android and iOS.
2. Configure your project to use Gradle templates, and enable both the Custom Main Template and Custom Gradle Properties Template.
3. If you build and receive any duplicate class errors (common in Unity 2022.x), add the following block in the dependencies block of your `mainTemplate.gradle` file:

```groovy
constraints {
     implementation("org.jetbrains.kotlin:kotlin-stdlib-jdk8:1.8.0") {
         because("kotlin-stdlib-jdk8 is now a part of kotlin-stdlib")
     }
}
```

4. Ensure your Android application is using the [IL2CPP backend](https://docs.unity3d.com/6000.0/Documentation/Manual/scripting-backends-il2cpp.html) for Android builds.

### Create an application in Datadog

1. In Datadog, navigate to [Digital Experience > Add an Application](https://app.datadoghq.com/rum/application/create).
2. Choose Unity as the application type.
3. Provide an application name to generate a unique Datadog application ID and client token.
4. To disable automatic user data collection for either client IP or geolocation data, uncheck the boxes for those settings.


### Modify Datadog settings in Unity

After installing the Datadog Unity SDK, you need to set Datadog’s settings in the Unity UI. Navigate to your Project Settings and click on the Datadog section on the left hand side. You will see the following screen:

<img src="images/datadog-setup-ui.avif">

- The *Client Token* is required for any data to be sent to Datadog.
- The *RUM Application Id* is required to use any RUM features.

### Setting Tracking Consent

In order to be compliant with data protection and privacy policies, the Datadog Unity SDK requires setting a tracking consent value.

The `trackingConsent` setting can be one of the following values:

* `TrackingConsent.Pending`: The Unity SDK starts collecting and batching the data but does not send it to Datadog. The Unity SDK waits for the new tracking consent value to decide what to do with the batched data.
* `TrackingConsent.Granted`: The Unity SDK starts collecting the data and sends it to Datadog.
* `TrackingConsent.NotGranted`: The Unity SDK does not collect any data. No logs are sent to Datadog.

Before Datadog sends any data, we need to confirm the user’s Tracking Consent. This is set to `TrackingConsent.Pending` during initialization, and needs to be set to `TrackingConsent.Granted` before Datadog sends any information.

```csharp
DatadogSdk.Instance.SetTrackingConsent(TrackingConsent.Granted);
```

## Additional Setup and Documentation

For further instructions on how to set up the Datadog SDK, refer to the [RUM Unity Monitoring Setup documentation](https://docs.datadoghq.com/real_user_monitoring/mobile_and_tv_monitoring/setup/unity/).
