using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;

namespace Datadog.Unity.Editor.Tests
{
    public class DatadogConfigurationOptionsTests
    {
        private string _assetPath;

        [SetUp]
        public void SetUp()
        {
            _assetPath = Path.Combine("Assets", Guid.NewGuid().ToString(), "DatadogSettings.asset");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(Path.GetDirectoryName(_assetPath));
        }

        [Test]
        public void GetOrCreateCreatesAssetCreatesAsset()
        {
            Assert.IsFalse(File.Exists(_assetPath));

            DatadogConfigurationOptions.GetOrCreate(_assetPath);

            Assert.IsTrue(File.Exists(_assetPath));
        }

        [Test]
        public void GetOrCreateCreatesAssetCreatesOptionsWithProperDefaults()
        {
            Assert.IsFalse(File.Exists(_assetPath));

            var options = DatadogConfigurationOptions.GetOrCreate(_assetPath);

            Assert.IsTrue(options.Enabled);
            Assert.IsEmpty(options.ClientToken);
            Assert.AreEqual(options.Site, DatadogSite.us1);
            Assert.AreEqual(options.DefaultLoggingLevel, LogType.Log);
        }
    }
}
