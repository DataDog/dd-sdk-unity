// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Datadog.Unity.Logs;
using Datadog.Unity.Rum;
using Datadog.Unity.Worker;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Scripting;

[assembly: UnityEngine.Scripting.Preserve]
[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]

namespace Datadog.Unity.iOS
{
    [Preserve]
    public static class DatadogInitialization
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void InitializeDatadog()
        {
            var options = DatadogConfigurationOptions.Load();
            if (options.Enabled)
            {
                var platform = new DatadogiOSPlatform();
                platform.Init(options);
                DatadogSdk.InitWithPlatform(platform, options);
            }
        }
    }

    internal class DatadogiOSPlatform : IDatadogPlatform
    {
        private Dictionary<string, long> _moduleLoadAddresses = new Dictionary<string, long>();
        private bool _shouldTranslateCsStacks = false;

        public void Init(DatadogConfigurationOptions options)
        {
            Datadog_UpdateTelemetryConfiguration(Application.unityVersion);

            // Debug builds have full file / line info and should not be translated, and if you're not outputting symbols
            // there will be no way to perform the translation, so avoid it.
            _shouldTranslateCsStacks = options.OutputSymbols && options.PerformNativeStackMapping && !Debug.isDebugBuild;
        }

        public void SetVerbosity(CoreLoggerLevel logLevel)
        {
            Datadog_SetSdkVerbosity((int)logLevel);
        }

        public void SetUserInfo(string id, string name, string email, Dictionary<string, object> extraInfo)
        {
            var jsonAttributes = extraInfo != null ? JsonConvert.SerializeObject(extraInfo) : null;
            Datadog_SetUserInfo(id, name, email, jsonAttributes);
        }

        public void AddUserExtraInfo(Dictionary<string, object> extraInfo)
        {
            if (extraInfo == null)
            {
                // Don't bother calling to platform
                return;
            }

            var jsonAttributes = JsonConvert.SerializeObject(extraInfo);
            Datadog_AddUserExtraInfo(jsonAttributes);
        }

        public void SetTrackingConsent(TrackingConsent trackingConsent)
        {
            Datadog_SetTrackingConsent((int)trackingConsent);
        }

        public DdLogger CreateLogger(DatadogLoggingOptions options, DatadogWorker worker)
        {
            var innerLogger = DatadogiOSLogger.Create(this, options);
            return new DdWorkerProxyLogger(worker, innerLogger);
        }

        public void AddLogsAttributes(Dictionary<string, object> attributes)
        {
            if (attributes == null)
            {
                SendErrorTelemetry("Unexpected null passed to AddLogsAttributes", null, "DatadogUnityError");
                return;
            }

            var jsonAttributes = JsonConvert.SerializeObject(attributes);
            Datadog_AddLogsAttributes(jsonAttributes);
        }

        public void RemoveLogsAttribute(string key)
        {
            if (key == null)
            {
                // Not an error, but don't bother calling to platform
                return;
            }

            Datadog_RemoveLogsAttributes(key);
        }

        public IDdRum InitRum(DatadogConfigurationOptions options)
        {
            return new DatadogiOSRum(this);
        }

        public void SendDebugTelemetry(string message)
        {
            Datadog_SendDebugTelemetry(message);
        }

        public void SendErrorTelemetry(string message, string stack, string kind)
        {
            Datadog_SendErrorTelemetry(message, stack, kind);
        }

        public void ClearAllData()
        {
            Datadog_ClearAllData();
        }

        public string GetNativeStack(Exception error)
        {
            // Don't perform this action if Datadog wasn't instructed to output symbols
            if (!_shouldTranslateCsStacks || error is null)
            {
                return null;
            }

            string resultStack = null;
            try
            {
                if (Il2CppErrorHelper.GetNativeStackFrames(
                        error,
                        out IntPtr[] frames,
                        out string imageUuid,
                        out string imageName))
                {
                    var imageLoadAddress = 0L;
                    if (_moduleLoadAddresses.ContainsKey(imageUuid))
                    {
                        imageLoadAddress = _moduleLoadAddresses[imageUuid];
                    }
                    else
                    {
                        // Reformat image UUID so it can be parsed by the native code
                        var standardImageUuid = ReformatUuid(imageUuid);
                        imageLoadAddress = Datadog_FindImageLoadAddress(standardImageUuid);
                        if (imageLoadAddress > 0)
                        {
                            _moduleLoadAddresses[imageUuid] = imageLoadAddress;
                        }
                    }

                    if (imageLoadAddress <= 0)
                    {
                        // Couldn't find the image load address, so we can't supply the stack trace
                        return null;
                    }

                    var moduleName = Path.GetFileNameWithoutExtension(imageName);
                    // Strip off weird \u0001 character that appears at the end of module names
                    moduleName = moduleName.Replace("\u0001", string.Empty);


                    // Format of iOS Native stack trace is:
                    // <frame number> <module name> <absolute address> <relative address> + <offset>
                    // Addresses are in hex, but offset is in decimal.
                    StringBuilder stackBuilder = new StringBuilder();
                    for (int i = 0; i < frames.Length; i++)
                    {
                        var frame = frames[i].ToInt64();
                        var isAbsolute = frame > imageLoadAddress;

                        // TODO: Check to see if we get absolute addresses outside of the supplied image
                        var absoluteAddress = isAbsolute ? frame : frame + imageLoadAddress;
                        var offset = absoluteAddress - imageLoadAddress;
                        stackBuilder.Append(
                            $"{i,-3} {moduleName,-32} 0x{absoluteAddress:x16} 0x{imageLoadAddress:x8} + {offset}\n");
                    }


                    resultStack = stackBuilder.ToString();
                }
            }
            catch (Exception e)
            {
                SendErrorTelemetry("Failed to get native stack", e.StackTrace, e.GetType().ToString());
            }

            return resultStack;
        }

        private static string ReformatUuid(string imageUuid)
        {
            var sb = new StringBuilder(imageUuid);
            sb.Insert(8, '-');
            sb.Insert(13, '-');
            sb.Insert(18, '-');
            sb.Insert(23, '-');
            return sb.ToString();
        }

        [DllImport("__Internal")]
        private static extern void Datadog_SetSdkVerbosity(int logLevel);

        [DllImport("__Internal")]
        private static extern void Datadog_SetTrackingConsent(int trackingConsent);

        [DllImport("__Internal")]
        private static extern void Datadog_SetUserInfo(string id, string name, string email, string extraInfo);

        [DllImport("__Internal")]
        private static extern void Datadog_AddLogsAttributes(string attributes);

        [DllImport("__Internal")]
        private static extern void Datadog_RemoveLogsAttributes(string key);

        [DllImport("__Internal")]
        private static extern void Datadog_AddUserExtraInfo(string extraInfo);

        [DllImport("__Internal")]
        private static extern void Datadog_SendDebugTelemetry(string message);

        [DllImport("__Internal")]
        private static extern void Datadog_SendErrorTelemetry(string message, string stack, string kind);

        [DllImport("__Internal")]
        private static extern void Datadog_ClearAllData();

        [DllImport("__Internal")]
        private static extern void Datadog_UpdateTelemetryConfiguration(string unityVersion);

        [DllImport("__Internal")]
        private static extern long Datadog_FindImageLoadAddress(string uuid);
    }
}
