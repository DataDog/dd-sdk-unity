// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Debug = UnityEngine.Debug;

namespace Datadog.Unity
{
    public class DatadogExceptionProcessor
    {
        public string ProcessStackTrace(Exception e)
        {
            var stackTrace = new StackTrace(e, true);
            var nativeStackTrace = GetNativeStackTrace(e);

            if (stackTrace.FrameCount != nativeStackTrace.Frames.Length)
            {
                return null;
            }

            return null;
        }

        private NativeStackTrace GetNativeStackTrace(Exception e)
        {
            NativeStackTrace nativeStackTrace = null;
            var gcHandle = GCHandle.Alloc(e);

            var imageUuidBuffer = IntPtr.Zero;
            var imageNameBuffer = IntPtr.Zero;
            try
            {
                var handlePtr = GCHandle.ToIntPtr(gcHandle);
                var exceptionAddress = Il2CppGcHandleGetTarget(handlePtr);

                il2cpp_native_stack_trace(exceptionAddress, out var frameAddresses, out var numFrames, out imageUuidBuffer, out imageNameBuffer);

                var imageUuid = imageUuidBuffer == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(imageUuidBuffer);
                var imageName = imageNameBuffer == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(imageNameBuffer);

                var frames = new IntPtr[numFrames];
                Marshal.Copy(frameAddresses, frames, 0, numFrames);

                var sb = new StringBuilder("[");
                sb.Append(string.Join(",", frames));
                sb.Append("]");
                Debug.Log(sb.ToString());

                nativeStackTrace = new NativeStackTrace()
                {
                    Frames = frames,
                    ImageName = imageName,
                    ImageUuid = imageUuid,
                };
            }
            finally
            {
                il2cpp_free(imageUuidBuffer);
                il2cpp_free(imageNameBuffer);

                gcHandle.Free();
            }

            return nativeStackTrace;
        }

#if UNITY_2023
        private static IntPtr Il2CppGcHandleGetTarget(IntPtr gchandle) => il2cpp_gchandle_get_target(gchandle);

        [DllImport("__Internal")]
        private static extern IntPtr il2cpp_gchandle_get_target(IntPtr gchandle);
#else
        private static IntPtr Il2CppGcHandleGetTarget(IntPtr gchandle) => il2cpp_gchandle_get_target(gchandle.ToInt32());

        [DllImport("__Internal")]
        private static extern IntPtr il2cpp_gchandle_get_target(int gchandle);
#endif

        [DllImport("__Internal")]
        private static extern void il2cpp_free(IntPtr ptr);

        // Note: Not available before 2021. Revisit if we have to support earlier than 2021
        [DllImport("__Internal")]
        private static extern void il2cpp_native_stack_trace(IntPtr exc, out IntPtr addresses, out int numFrames, out IntPtr imageUUID, out IntPtr imageName);
    }

    class NativeStackTrace
    {
        public IntPtr[] Frames { get; set; }

        public string ImageUuid { get; set; }

        public string ImageName { get; set;  }
    }
}
