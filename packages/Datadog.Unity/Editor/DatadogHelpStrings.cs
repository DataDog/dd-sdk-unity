// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2024-Present Datadog, Inc.

// ReSharper disable InconsistentNaming
namespace Datadog.Unity.Editor
{
    /// <summary>
    /// Class to make editing tooltips and help strings easier.
    /// </summary>
    public static class DatadogHelpStrings
    {
        public const string EnabledTooltip =
            "Enable or disable the Datadog Unity SDK. When disabled, the SDK will not send any data to Datadog.";

        public const string OutputSymbolsTooltip =
            "Whether to output symbol files as part of the build. Uploading these files to Datadog enables symbolication "
            + "and file/line mapping features in Datadog Error Tracking.";

        public const string ClientTokenTooltip = "This is your API key from Datadog.";

        public const string EnvTooltip =
            "The environment name that will be sent with each event. This can be used to filter your events on "
            + "different environments (e.g. \"staging\" vs. \"production\").";

        public const string ServiceNameTooltip =
            "The service name for your application. If this is not set it will  be set to your application's package " +
            "name pr bundle name (e.g.: com.example.android)";

        public const string SiteTooltip = "The Datadog site to send data to.";

        public const string BatchSizeTooltip =
            "Sets the preferred size of batched data uploaded to Datadog. This value impacts the size and number of " +
            "requests performed by the SDK (small batches mean more requests, but each request becomes smaller in size).";

        public const string UploadFrequencyTooltip =
            "Sets the preferred frequency of uploading data to Datadog.";

        public const string BatchProcessingLevelTooltip =
            "Defines the maximum amount of batches processed sequentially without a delay within one reading/uploading cycle.";

        public const string CrashReportingEnabledTooltip = "Enables crash reporting in the RUM SDK.";

        public const string ForwardUnityLogsTooltip =
            "Whether to forward logs made from Unity’s Debug.Log calls to Datadog’s default logger.";

        public const string RemoteLogThresholdTooltip =
            "The level at which the default logger forwards logs to Datadog. Logs below this level are not sent.";

        public const string EnableRumTooltip =
            "Whether to enable sending data from Datadog’s Real User Monitoring APIs";

        public const string RUMApplicationIdTooltip =
            "The RUM Application ID created for your application on Datadog’s website.";

        public const string EnableSceneTrackingTooltip =
            "Whether Datadog should automatically track new Views by intercepting Unity’s SceneManager loading.";

        public const string SessionSampleRateTooltip =
            "The percentage rate at which sessions are sampled. A value of 100 means all sessions are sampled and sent to " +
            "Datadog. 50 means 50% of sessions are sampled and sent to Datadog.";

        public const string TraceSampleRateTooltip =
            "The percentage rate at which distributed traces are sampled. A value of 100 means all traces are sampled and sent to " +
            "Datadog. 50 means 50% of traces are sampled and sent to Datadog.";

        public const string TraceContextInjectionTooltip =
            "Defines whether the context for a distributed trace should be injected into all requests or only into requests that are sampled in.";

        public const string TrackNonFatalAnrsTooltip =
            "(Android Only) Wether to track non-fatal ANRs (Application Not Responding) errors. The \"SDK Default\" option disables ANR" +
            "detection on Android 30+ because it would create too much noise over fatal ANRs. On Android 29 and below, however, " +
            "the reporting of non-fatal ANRs is enabled by default, as fatal ANRs cannot be reported on those versions.";

        public const string TrackNonFatalAppHangsTooltip =
            "(iOS Only) Whether to track non-fatal app hangs. App hangs are detected when the app is unresponsive for a certain amount of time.";

        public const string NonFatalAppHangThresholdTooltip =
            "The amount of time in seconds that the app must be unresponsive before it is considered a non-fatal app hang.";

        public const string FirstPartyHostsTooltip =
            "To enable distributed tracing, you must specify which hosts are considered “first party” and have trace information injected.";

        public const string CustomEndpointTooltip =
            "Send data to a custom endpoint instead of the default Datadog endpoint. This is useful for proxying data through a custom server.";

        public const string SdkVerbosityTooltip =
            "The level of debugging information the Datadog SDK should output. Higher levels will output more information.";

        public const string TelemetrySampleRateTooltip =
            "The percentage rate at which Datadog sends internal telemetry data. A value of 100 means all telemetry data is sampled and sent to Datadog.";
    }
}
