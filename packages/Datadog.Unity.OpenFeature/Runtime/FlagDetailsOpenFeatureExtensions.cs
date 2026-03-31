// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System.Collections.Generic;
using Datadog.Unity.Flags;
using Newtonsoft.Json.Linq;
using OpenFeature.Model;

namespace Datadog.Unity.Flags.OpenFeature
{
    /// <summary>
    /// Extension methods that convert <see cref="FlagDetails{T}"/> values to
    /// OpenFeature <see cref="Value"/> types.
    /// </summary>
    public static class FlagDetailsOpenFeatureExtensions
    {
        private const int MaxConversionDepth = 100;

        /// <summary>
        /// Converts the flag value in this <see cref="FlagDetails{T}"/> to an
        /// OpenFeature <see cref="Value"/>. Returns null if the value is null.
        /// Structure values are converted recursively up to a maximum depth of
        /// <see cref="MaxConversionDepth"/> levels; values beyond that depth are
        /// replaced with their <c>ToString()</c> representation.
        /// </summary>
        public static Value AsOpenFeatureValue<T>(this FlagDetails<T> self)
        {
            return ToValue(self.Value, 0);
        }

        private static Value ToValue(object obj, int depth) => obj switch
        {
            null => null,
            bool b => new Value(b),
            int i => new Value(i),
            long l => new Value((double)l),
            double d => new Value(d),
            float f => new Value((double)f),
            string s => new Value(s),
            JObject jObj => depth < MaxConversionDepth
                ? JObjectToValue(jObj, depth + 1)
                : new Value(jObj.ToString()),
            JArray jArr => depth < MaxConversionDepth
                ? JArrayToValue(jArr, depth + 1)
                : new Value(jArr.ToString()),
            JValue jVal => ToValue(jVal.Value, depth),
            System.Collections.Generic.Dictionary<string, object> dict => depth < MaxConversionDepth
                ? DictionaryToValue(dict, depth + 1)
                : new Value(dict.ToString()),
            System.Collections.Generic.IList<object> list => depth < MaxConversionDepth
                ? ListToValue(list, depth + 1)
                : new Value(list.ToString()),
            _ => new Value(obj.ToString()),
        };

        private static Value JObjectToValue(JObject jObj, int depth)
        {
            var converted = new Dictionary<string, Value>();
            foreach (var prop in jObj.Properties())
            {
                var val = ToValue(prop.Value, depth);
                if (val != null) converted[prop.Name] = val;
            }
            return new Value(new Structure(converted));
        }

        private static Value JArrayToValue(JArray jArr, int depth)
        {
            var values = new List<Value>(jArr.Count);
            foreach (var item in jArr)
            {
                var val = ToValue(item, depth);
                if (val != null) values.Add(val);
            }
            return new Value(values);
        }

        private static Value DictionaryToValue(Dictionary<string, object> dict, int depth)
        {
            var converted = new Dictionary<string, Value>();
            foreach (var kvp in dict)
            {
                var val = ToValue(kvp.Value, depth);
                if (val != null) converted[kvp.Key] = val;
            }
            return new Value(new Structure(converted));
        }

        private static Value ListToValue(IList<object> list, int depth)
        {
            var values = new List<Value>(list.Count);
            foreach (var item in list)
            {
                var val = ToValue(item, depth);
                if (val != null) values.Add(val);
            }
            return new Value(values);
        }
    }
}
