// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System.Collections.Generic;
using System.Runtime.InteropServices;
using Datadog.Unity.Core;
using Datadog.Unity.Logs;

namespace Datadog.Unity.Desktop
{
    // Mirrors dd_attribute_t from dd-sdk-cpp include-c/datadog/attribute.h.
    // The struct is 16 bytes: 4-byte type enum + 4 bytes padding + 8-byte union.
    // Strings, arrays, and objects are reference-counted privately; always call
    // dd_attribute_free after use.
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    internal struct DdAttribute
    {
        [FieldOffset(0)] public int Type;

        [FieldOffset(8)] public long Int64;

        [FieldOffset(8)] public ulong UInt64;

        [FieldOffset(8)] public double Float;

        [FieldOffset(8)] public System.IntPtr Ptr;
    }

    internal static class DdAttributes
    {
        internal static DdAttribute MakeAttribute(object value, IInternalLogger logger)
        {
            return value switch
            {
                null => dd_attribute_null(),
                string s => dd_attribute_string(s),
                int i => dd_attribute_int(i),
                long l => dd_attribute_int(l),
                double d => dd_attribute_double(d),
                float f => dd_attribute_double(f),
                bool b => dd_attribute_bool(b),
                Dictionary<string, object> dict => BuildAttributeObject(dict, logger),
                _ => dd_attribute_string(value.ToString()),
            };
        }

        internal static DdAttribute BuildAttributeObject(Dictionary<string, object> attrs, IInternalLogger logger)
        {
            var obj = dd_attribute_object((System.UIntPtr)attrs.Count);
            foreach (var kv in attrs)
            {
                var val = MakeAttribute(kv.Value, logger);
                dd_attribute_object_property_set(ref obj, kv.Key, ref val);
                dd_attribute_free(ref val);
            }

            return obj;
        }

        [DllImport("dd_native")]
        internal static extern DdAttribute dd_attribute_null();

        [DllImport("dd_native")]
        internal static extern DdAttribute dd_attribute_bool([MarshalAs(UnmanagedType.I1)] bool value);

        [DllImport("dd_native")]
        internal static extern DdAttribute dd_attribute_int(long value);

        [DllImport("dd_native")]
        internal static extern DdAttribute dd_attribute_double(double value);

        [DllImport("dd_native")]
        internal static extern DdAttribute dd_attribute_string([MarshalAs(UnmanagedType.LPUTF8Str)] string value);

        [DllImport("dd_native")]
        internal static extern DdAttribute dd_attribute_object(System.UIntPtr initialCapacity);

        [DllImport("dd_native")]
        internal static extern void dd_attribute_object_property_set(ref DdAttribute obj,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name, ref DdAttribute value);

        [DllImport("dd_native")]
        internal static extern void dd_attribute_free(ref DdAttribute attr);
    }
}
