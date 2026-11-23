// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Runtime.InteropServices;

namespace Datadog.Unity.Desktop
{
    internal static partial class DatadogNative
    {
        [DllImport("ddsdkcpp", EntryPoint = "dd_attribute_set_bool", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void SetAttributeBool(ref Attribute attribute, [MarshalAs(UnmanagedType.I1)] bool value);

        [DllImport("ddsdkcpp", EntryPoint = "dd_attribute_set_int", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void SetAttributeInt(ref Attribute attribute, long value);

        [DllImport("ddsdkcpp", EntryPoint = "dd_attribute_set_uint", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void SetAttributeUInt(ref Attribute attribute, ulong value);

        [DllImport("ddsdkcpp", EntryPoint = "dd_attribute_set_double", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void SetAttributeDouble(ref Attribute attribute, double value);

        [DllImport("ddsdkcpp", EntryPoint = "dd_attribute_set_string", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void SetAttributeString(ref Attribute attribute, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

        [DllImport("ddsdkcpp", EntryPoint = "dd_attribute_init_object", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void InitAttributeObject(ref Attribute attribute, UIntPtr initialCapacity);

        [DllImport("ddsdkcpp", EntryPoint = "dd_attribute_init_array", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void InitAttributeArray(ref Attribute attribute, UIntPtr initialCapacity);

        [DllImport("ddsdkcpp", EntryPoint = "dd_attribute_object_property_set", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void SetAttributeProperty(ref Attribute attribute, [MarshalAs(UnmanagedType.LPUTF8Str)] string key, [In] ref Attribute value);

        [DllImport("ddsdkcpp", EntryPoint = "dd_attribute_array_push", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void PushAttributeArray(ref Attribute attribute, [In] ref Attribute value);

        [DllImport("ddsdkcpp", EntryPoint = "dd_attribute_free", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void FreeAttribute(ref Attribute attribute);

        // dd_attribute_t: a 32-bit type followed by an eight-byte scalar/pointer union.
        [StructLayout(LayoutKind.Sequential)]
        internal struct Attribute
        {
            internal int Type;
            internal long Value;
        }
    }
}
