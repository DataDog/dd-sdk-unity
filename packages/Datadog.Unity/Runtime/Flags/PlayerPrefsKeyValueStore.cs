// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using UnityEngine;

namespace Datadog.Unity.Flags
{
    internal class PlayerPrefsKeyValueStore : IKeyValueStore
    {
        public string GetString(string key, string defaultValue) =>
            PlayerPrefs.GetString(key, defaultValue);

        public void SetString(string key, string value) =>
            PlayerPrefs.SetString(key, value);

        public void DeleteKey(string key) =>
            PlayerPrefs.DeleteKey(key);

        public void Save() =>
            PlayerPrefs.Save();
    }
}
