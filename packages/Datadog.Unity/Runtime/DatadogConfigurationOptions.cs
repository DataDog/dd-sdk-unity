// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Datadog.Unity
{
    public enum DatadogSite
    {
        [InspectorName("us1")]
        Us1,
        [InspectorName("us3")]
        Us3,
        [InspectorName("us5")]
        Us5,
        [InspectorName("eu1")]
        Eu1,
        [InspectorName("us1Fed")]
        Us1Fed,
        [InspectorName("ap1")]
        Ap1,
    }

    /// <summary>
    /// Defines the policy when batching data together.
    /// Smaller batches will means smaller but more network requests,
    /// whereas larger batches will mean fewer but larger network requests.
    /// </summary>
    public enum BatchSize
    {
        Small,
        Medium,
        Large,
    }

    /// <summary>
    /// Defines the frequency at which batch uploads are tried.
    /// </summary>
    public enum UploadFrequency
    {
        Frequent,
        Average,
        Rare,
    }

    /// <summary>
    /// The Consent enum class providing the possible values for the Data Tracking Consent flag.
    /// </summary>
    public enum TrackingConsent
    {
        /// <summary>
        /// The permission to persist and dispatch data to the Datadog Endpoints was granted.
        /// Any previously stored pending data will be marked as ready for sent.
        /// </summary>
        Granted,

        /// <summary>
        /// Any previously stored pending data will be deleted and any Log, Rum, Trace event will
        /// be dropped from now on without persisting it in any way.
        /// </summary>
        NotGranted,

        /// <summary>
        /// Any Log, Rum, Trace event will be persisted in a special location and will be pending there
        /// until we will receive one of the [TrackingConsent.Granted] or
        /// [TrackingConsent.NotGranted] flags.
        /// Based on the value of the consent flag we will decide what to do
        /// with the pending stored data.
        /// </summary>
        Pending,
    }

    public class DatadogConfigurationOptions : ScriptableObject
    {
        public static readonly string DefaultDatadogSettingsPath = $"Assets/Resources/{_DefaultDatadogSettingsFileName}.asset";

        // Field should be private
#pragma warning disable SA1401

        public bool Enabled;
        public string ClientToken;
        public DatadogSite Site;
        public string CustomEndpoint;
        public LogType DefaultLoggingLevel;
        public BatchSize BatchSize;
        public UploadFrequency UploadFrequency;
        public bool ForwardUnityLogs;
        public bool RumEnabled;
        public string RumApplicationId;
        public bool AutomaticSceneTracking;
        public float TelemetrySampleRate;

        private const string _DefaultDatadogSettingsFileName = "DatadogSettings";

        public static DatadogConfigurationOptions Load()
        {
            return Resources.Load<DatadogConfigurationOptions>(_DefaultDatadogSettingsFileName);
        }
    }
}
