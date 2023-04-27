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

        public static DatadogConfigurationOptions Load()
        {
            return Resources.Load<DatadogConfigurationOptions>(_DefaultDatadogSettingsFileName);
        }
    }

}

