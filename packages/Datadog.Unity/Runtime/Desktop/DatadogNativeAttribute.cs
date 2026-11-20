// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections;
using System.Collections.Generic;

namespace Datadog.Unity.Desktop
{
    internal sealed class DatadogNativeAttribute : IDisposable
    {
        private const int MaxDepth = 64;

        internal DatadogNative.Attribute Value;

        internal DatadogNativeAttribute(object value)
        {
            Value = Convert(value, 0);
        }

        public void Dispose()
        {
            DatadogNative.FreeAttribute(ref Value);
        }

        private static DatadogNative.Attribute Convert(object value, int depth)
        {
            if (depth > MaxDepth)
            {
                throw new ArgumentException("Attribute nesting exceeds 64 levels; check for cyclic containers.");
            }

            var result = default(DatadogNative.Attribute);
            try
            {
                switch (value)
                {
                    case null:
                        break;
                    case bool boolean:
                        DatadogNative.SetAttributeBool(ref result, boolean);
                        break;
                    case byte or sbyte or short or ushort or int or long:
                        DatadogNative.SetAttributeInt(ref result, System.Convert.ToInt64(value));
                        break;
                    case uint or ulong:
                        DatadogNative.SetAttributeUInt(ref result, System.Convert.ToUInt64(value));
                        break;
                    case IntPtr integer:
                        DatadogNative.SetAttributeInt(ref result, integer.ToInt64());
                        break;
                    case UIntPtr unsigned:
                        DatadogNative.SetAttributeUInt(ref result, unsigned.ToUInt64());
                        break;
                    case float or double or decimal:
                        DatadogNative.SetAttributeDouble(ref result, System.Convert.ToDouble(value));
                        break;
                    case string text:
                        DatadogNative.SetAttributeString(ref result, text);
                        break;
                    case IDictionary<string, object> map:
                        DatadogNative.InitAttributeObject(ref result, (UIntPtr)(uint)map.Count);
                        foreach (var item in map)
                        {
                            AddProperty(ref result, item.Key, item.Value, depth);
                        }

                        break;
                    case IReadOnlyDictionary<string, object> map:
                        DatadogNative.InitAttributeObject(ref result, (UIntPtr)(uint)map.Count);
                        foreach (var item in map)
                        {
                            AddProperty(ref result, item.Key, item.Value, depth);
                        }

                        break;
                    case IDictionary map:
                        DatadogNative.InitAttributeObject(ref result, (UIntPtr)(uint)map.Count);
                        foreach (DictionaryEntry item in map)
                        {
                            if (!(item.Key is string key))
                            {
                                throw new ArgumentException("Attribute dictionary keys must be strings.");
                            }

                            AddProperty(ref result, key, item.Value, depth);
                        }

                        break;
                    case IEnumerable items:
                        DatadogNative.InitAttributeArray(ref result, UIntPtr.Zero);
                        foreach (var item in items)
                        {
                            var child = Convert(item, depth + 1);
                            try
                            {
                                DatadogNative.PushAttributeArray(ref result, ref child);
                            }
                            finally
                            {
                                DatadogNative.FreeAttribute(ref child);
                            }
                        }

                        break;
                    default:
                        DatadogNative.SetAttributeString(ref result, value.ToString());
                        break;
                }

                return result;
            }
            catch
            {
                DatadogNative.FreeAttribute(ref result);
                throw;
            }
        }

        private static void AddProperty(ref DatadogNative.Attribute attribute, string key, object value, int depth)
        {
            var child = Convert(value, depth + 1);
            try
            {
                DatadogNative.SetAttributeProperty(ref attribute, key, ref child);
            }
            finally
            {
                DatadogNative.FreeAttribute(ref child);
            }
        }
    }
}
