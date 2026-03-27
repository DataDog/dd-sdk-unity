// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OpenFeature.Model;

namespace Datadog.Unity.Flags.OpenFeature
{
    /// <summary>
    /// Converts Datadog flag values to OpenFeature <see cref="Value"/> types.
    /// </summary>
    internal static class FlagValueConverter
    {
        /// <summary>
        /// Converts a Datadog flag value to an OpenFeature <see cref="Value"/>.
        /// Returns null if the input is null.
        /// </summary>
        internal static Value ToOpenFeatureValue(object obj) => obj switch
        {
            null => null,
            bool b => new Value(b),
            int i => new Value(i),
            long l => new Value((double)l),
            double d => new Value(d),
            float f => new Value((double)f),
            string s => new Value(s),
            JObject jObj => JObjectToValue(jObj),
            JArray jArr => JArrayToValue(jArr),
            JValue jVal => ToOpenFeatureValue(jVal.Value),
            System.Collections.Generic.Dictionary<string, object> dict => DictionaryToValue(dict),
            System.Collections.Generic.IList<object> list => ListToValue(list),
            _ => new Value(obj.ToString()),
        };

        private static Value JObjectToValue(JObject jObj)
        {
            var converted = new Dictionary<string, Value>();
            foreach (var prop in jObj.Properties())
            {
                var val = ToOpenFeatureValue(prop.Value);
                if (val != null) converted[prop.Name] = val;
            }
            return new Value(new Structure(converted));
        }

        private static Value JArrayToValue(JArray jArr)
        {
            var values = new List<Value>(jArr.Count);
            foreach (var item in jArr)
            {
                var val = ToOpenFeatureValue(item);
                if (val != null) values.Add(val);
            }
            return new Value(values);
        }

        private static Value DictionaryToValue(Dictionary<string, object> dict)
        {
            var converted = new Dictionary<string, Value>();
            foreach (var kvp in dict)
            {
                var val = ToOpenFeatureValue(kvp.Value);
                if (val != null) converted[kvp.Key] = val;
            }
            return new Value(new Structure(converted));
        }

        private static Value ListToValue(IList<object> list)
        {
            var values = new List<Value>(list.Count);
            foreach (var item in list)
            {
                var val = ToOpenFeatureValue(item);
                if (val != null) values.Add(val);
            }
            return new Value(values);
        }
    }
}
