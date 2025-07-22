// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System.IO;
using System.Xml;
using UnityEditor;
using UnityEditor.Android;
using UnityEngine;

namespace Datadog.Unity.Build
{
    public class EnableCleartextTrafficPostProcessor : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 0;

        private const string AndroidXmlSchema = "http://schemas.android.com/apk/res/android";
        private const string AndroidXmlNamespace = "android";

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            // We're given the path to root of the 'unityLibrary' Gradle project, but we need to modify
            // the AndroidManifest.xml file for the 'launcher' project, which exists alongside it
            string gradleRoot = Path.GetFullPath(Path.Combine(path, ".."));
            string launcherSrcMain = Path.Combine(gradleRoot, "launcher", "src", "main");
            string manifestPath = Path.Combine(launcherSrcMain, "AndroidManifest.xml");
            if (!File.Exists(manifestPath))
            {
                Debug.LogError($"Launcher AndroidManifest.xml not found at: {manifestPath}");
                return;
            }

            // Read and parse launcher/AndroidManifest.xml
            XmlDocument manifestDoc = new XmlDocument();
            manifestDoc.Load(manifestPath);
            XmlNamespaceManager nsMgr = new XmlNamespaceManager(manifestDoc.NameTable);
            nsMgr.AddNamespace("android", AndroidXmlSchema);

            // Find the <application> element for our app
            XmlNode manifestNode = manifestDoc.SelectSingleNode("/manifest");
            XmlNode applicationNode = manifestNode?.SelectSingleNode("application");
            if (applicationNode == null)
            {
                Debug.LogError($"No <application> element found in: {manifestPath}");
                return;
            }

            // Mutate the application node to ensure that our app will be permitted to send cleartext
            // (i.e. non-TLS) HTTP requests
            SetApplicationAttribute(manifestDoc, applicationNode, "usesCleartextTraffic", "true");

            // Write our updated XML manifest back to disk
            manifestDoc.Save(manifestPath);
        }

        private void SetApplicationAttribute(XmlDocument manifestDoc, XmlNode applicationNode, string name, string value)
        {
            string namespacedAttributeName = $"{AndroidXmlNamespace}:{name}";
            XmlAttribute attr = applicationNode.Attributes[namespacedAttributeName];
            if (attr == null)
            {
                attr = manifestDoc.CreateAttribute(AndroidXmlNamespace, name, AndroidXmlSchema);
                attr.Value = value;
                applicationNode.Attributes.Append(attr);
            }
            else
            {
                attr.Value = value;
            }
            Debug.Log($"Modified <application> element ({AndroidXmlNamespace}:{name}=\"{value}\")");
        }
    }
}
