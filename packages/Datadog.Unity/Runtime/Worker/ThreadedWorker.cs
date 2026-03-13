// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections.Concurrent;
using System.Threading;
using Datadog.Unity.Core;
using Datadog.Unity.Logs;
using UnityEngine;

namespace Datadog.Unity.Worker
{
    internal class ThreadedWorker : DatadogWorker
    {
        private readonly IInternalLogger _logger;
        private BlockingCollection<IDatadogWorkerMessage> _workQueue = new();
        private Thread _workerThread;

        // Used for testing
        public bool IsAlive => _workerThread != null && _workerThread.IsAlive;

        public ThreadedWorker(IInternalLogger logger)
        {
            _logger = logger;
        }

        public override void Start()
        {
            if (_workerThread != null)
            {
                // Already started! Don't start twice!
                return;
            }

            _workerThread = new(() => { ThreadWorker(); });
            _workerThread.Start();
        }

        public override void Stop()
        {
            if (_workerThread == null)
            {
                return;
            }

            _workQueue.CompleteAdding();
            _workerThread.Join();

            // Clear out thread and create a new work queue so
            // this worker can be re-used (although it shouldn't be)
            _workerThread = null;
            _workQueue = new();
        }

        public override void AddMessage(IDatadogWorkerMessage message)
        {
            // Only restart the worker thread if it was previously started
            if (_workerThread != null)
            {
                if (!_workerThread.IsAlive)
                {
                    _workerThread = null;
                    Start();
                    _logger.TelemetryDebug("Worker thread was stopped and restarted!");
                }
            }

            _workQueue.Add(message);
        }

        // For internal testing. Force the thread to stop as if something went wrong.
        internal void Kill()
        {
            _workerThread.Abort();
            _workerThread.Join();
        }

        private void ThreadWorker()
        {
#if UNITY_ANDROID
            AndroidJNI.AttachCurrentThread();
#endif

            while (!_workQueue.IsCompleted)
            {
                try
                {
                    var message = _workQueue.Take();
                    if (message != null)
                    {
                        HandleMessage(message);
                    }
                }
                catch (InvalidOperationException)
                {
                    // This is an expected exception and is thrown when the work queue
                    // is completed while .Take is waiting on a new item.
                    _logger.Log(DdLogLevel.Debug, "Shutting down worker thread.");
                }
                catch (Exception e)
                {
                    // Since we're already on the worker thread, send telemetry information
                    // directly without going through the work queue. This should also
                    // hopefully log things even if Telemetry is the issue.
                    var message = DdTelemetryProcessor.TelemetryErrorMessage.Create(
                        "Exception on worker thread",
                        e.StackTrace,
                        e.GetType().ToString());
                    HandleMessage(message);
                }
            }

            _logger.Log(DdLogLevel.Debug, "Worker Thread Stopped.");
        }
    }
}
