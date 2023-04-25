using System;
using System.Runtime.InteropServices;

namespace Datadog.Unity.iOS
{
    public class DatadogiOSLogger
    {
        private string _loggerId;

        public void Log(string message)
        {
            DatadogLoggingBridge.DatadogLogging_Log(_loggerId, message);
        }

        private DatadogiOSLogger(string loggerId)
        {
            _loggerId = loggerId;
        }

        public static DatadogiOSLogger Create()
        {
            var loggerId = DatadogLoggingBridge.DatadogLogging_CreateLog();
            if (loggerId != null)
            {
                return new DatadogiOSLogger(loggerId);
            }
            return null;
        }
    }

    internal static class DatadogLoggingBridge
    {
        [DllImport("__Internal")]
        public static extern string DatadogLogging_CreateLog();

        [DllImport("__Internal")]
        public static extern void DatadogLogging_Log(string loggerId, string message);
    }
}


