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
        us1,
        [InspectorName("us3")]
        us3,
        [InspectorName("us5")]
        us5,
        [InspectorName("eu1")]
        eu1,
        [InspectorName("us1Fed")]
        us1Fed,
        [InspectorName("ap1")]
        ap1,
    }

    /// Defines the policy when batching data together.
    /// Smaller batches will means smaller but more network requests,
    /// whereas larger batches will mean fewer but larger network requests.
    public enum BatchSize
    {
        Small,
        Medium,
        Large
    }

    /// Defines the frequency at which batch uploads are tried.
    public enum UploadFrequency
    {
        Frequent,
        Average,
        Rare,
    }

    /// The Consent enum class providing the possible values for the Data Tracking Consent flag.
    public enum TrackingConsent
    {
        /// The permission to persist and dispatch data to the Datadog Endpoints was granted.
        /// Any previously stored pending data will be marked as ready for sent.
        Granted,
        /// Any previously stored pending data will be deleted and any Log, Rum, Trace event will
        /// be dropped from now on without persisting it in any way.
        NotGranted,
        /// Any Log, Rum, Trace event will be persisted in a special location and will be pending there
        /// until we will receive one of the [TrackingConsent.Granted] or
        /// [TrackingConsent.NotGranted] flags.
        /// Based on the value of the consent flag we will decide what to do
        /// with the pending stored data.
        Pending,
    }

    public class DatadogConfigurationOptions : ScriptableObject
    {
        public const string _DefaultDatadogSettingsFileName = "DatadogSettings";
        public static string _DefaultDatadogSettingsPath = $"Assets/Resources/{_DefaultDatadogSettingsFileName}.asset";

        [SerializeField]
        public bool Enabled;

        [SerializeField]
        public string ClientToken;

        [SerializeField]
        public DatadogSite Site;

        [SerializeField]
        public LogType DefaultLoggingLevel;

        [SerializeField]
        public BatchSize BatchSize;

        [SerializeField]
        public UploadFrequency UploadFrequency;

        public static DatadogConfigurationOptions Load()
        {
            return Resources.Load<DatadogConfigurationOptions>(_DefaultDatadogSettingsFileName);
        }
    }

}

