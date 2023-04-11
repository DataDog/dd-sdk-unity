using System;
using System.Runtime.InteropServices;

namespace Datadog.Unity.iOS
{
    public static class DatadogiOSBridge
    {
        [DllImport("__Internal")]
        public static extern void DatadogBridge_Init();
    }
}


