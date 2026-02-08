// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Datadog.Unity.Flags
{
    /// <summary>
    /// Lightweight JSON serialization helper to avoid external dependencies.
    /// </summary>
    internal static class JsonHelper
    {
        public static string Escape(string value)
        {
            if (value == null)
            {
                return "null";
            }

            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.AppendFormat("\\u{0:x4}", (int)c);
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        public static string ValueToJson(object value)
        {
            if (value == null)
            {
                return "null";
            }

            if (value is bool boolVal)
            {
                return boolVal ? "true" : "false";
            }

            if (value is int intVal)
            {
                return intVal.ToString(CultureInfo.InvariantCulture);
            }

            if (value is long longVal)
            {
                return longVal.ToString(CultureInfo.InvariantCulture);
            }

            if (value is double doubleVal)
            {
                return doubleVal.ToString("G", CultureInfo.InvariantCulture);
            }

            if (value is float floatVal)
            {
                return floatVal.ToString("G", CultureInfo.InvariantCulture);
            }

            if (value is string strVal)
            {
                return Escape(strVal);
            }

            if (value is IDictionary dict)
            {
                var sb = new StringBuilder();
                sb.Append('{');
                var first = true;
                foreach (DictionaryEntry entry in dict)
                {
                    if (!first)
                    {
                        sb.Append(',');
                    }
                    sb.Append(Escape(entry.Key.ToString()));
                    sb.Append(':');
                    sb.Append(ValueToJson(entry.Value));
                    first = false;
                }
                sb.Append('}');
                return sb.ToString();
            }

            if (value is IList list)
            {
                var sb = new StringBuilder();
                sb.Append('[');
                for (var i = 0; i < list.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }
                    sb.Append(ValueToJson(list[i]));
                }
                sb.Append(']');
                return sb.ToString();
            }

            return Escape(value.ToString());
        }

        public static string DictionaryToJson(Dictionary<string, object> dict)
        {
            if (dict == null || dict.Count == 0)
            {
                return "{}";
            }

            var sb = new StringBuilder();
            sb.Append('{');
            var first = true;
            foreach (var kvp in dict)
            {
                if (!first)
                {
                    sb.Append(',');
                }
                sb.Append(Escape(kvp.Key));
                sb.Append(':');
                sb.Append(ValueToJson(kvp.Value));
                first = false;
            }
            sb.Append('}');
            return sb.ToString();
        }
    }
}
