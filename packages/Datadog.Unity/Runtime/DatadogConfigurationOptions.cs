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
        public const string _DatadogSettingsPath = "Assets/Editor/DatadogSettings.asset";

        [SerializeField]
        public bool Enabled;

        [SerializeField]
        public string ClientToken;

        [SerializeField]
        public DatadogSite Site;

        [SerializeField]
        public LogType DefaultLoggingLevel;

        public static DatadogConfigurationOptions GetOrCreate()
        {
            var options = AssetDatabase.LoadAssetAtPath<DatadogConfigurationOptions>(_DatadogSettingsPath);
            if (options == null)
            {
                options = ScriptableObject.CreateInstance<DatadogConfigurationOptions>();
                options.Enabled = true;
                options.Site = DatadogSite.us1;
                options.DefaultLoggingLevel = LogType.Log;

                if (!Directory.Exists("Assets/Editor"))
                {
                    Directory.CreateDirectory("Assets/Editor");
                }
                AssetDatabase.CreateAsset(options, _DatadogSettingsPath);
                AssetDatabase.SaveAssets();
            }
            return options;
        }
    }

}

