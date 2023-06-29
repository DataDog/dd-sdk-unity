// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using Datadog.Unity.Logs;
using UnityEngine;

namespace Datadog.Unity
{
    internal class DdUnityLogHandler : ILogHandler
    {
        private readonly IDdLogger _ddLogger;
        private ILogHandler _defaultLogHandler = null;

        public DdUnityLogHandler(IDdLogger datadogLogger)
        {
            _ddLogger = datadogLogger;
        }

        public void Attach()
        {
            if (Debug.unityLogger.logHandler == this)
            {
                return;
            }

            _defaultLogHandler = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = this;
        }

        public void Detach()
        {
            if (Debug.unityLogger.logHandler != this)
            {
                return;
            }

            Debug.unityLogger.logHandler = _defaultLogHandler;
        }

        public void LogException(Exception exception, UnityEngine.Object context)
        {
            try
            {
                _ddLogger.Critical(exception.Message, error: exception);
            }
            finally
            {
                // Pass exception onto Unity
                _defaultLogHandler.LogException(exception, context);
            }
        }

        public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
        {
            try
            {
                var logLevel = DdLogHelpers.LogTypeToDdLogLevel(logType);
                var message = args.Length == 0 ? format : string.Format(format, args);
                _ddLogger.Log(logLevel, message);
            }
            finally
            {
                // Pass exception onto Unity
                _defaultLogHandler.LogFormat(logType, context, format, args);
            }
        }
    }
}
