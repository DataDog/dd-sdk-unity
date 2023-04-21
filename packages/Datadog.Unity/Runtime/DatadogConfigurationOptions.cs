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
        public const string _DefaultDatadogSettingsPath = "Assets/Editor/DatadogSettings.asset";

        [SerializeField]
        public bool Enabled;

        [SerializeField]
        public string ClientToken;

        [SerializeField]
        public DatadogSite Site;

        [SerializeField]
        public LogType DefaultLoggingLevel;

        public static DatadogConfigurationOptions GetOrCreate(string settingsPath = null)
        {
            settingsPath ??= _DefaultDatadogSettingsPath;
            var options = AssetDatabase.LoadAssetAtPath<DatadogConfigurationOptions>(settingsPath);
            if (options == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));

                options = ScriptableObject.CreateInstance<DatadogConfigurationOptions>();
                options.ClientToken = string.Empty;
                options.Enabled = true;
                options.Site = DatadogSite.us1;
                options.DefaultLoggingLevel = LogType.Log;

                AssetDatabase.CreateAsset(options, settingsPath);
                AssetDatabase.SaveAssets();
            }
            return options;
        }
    }

}

