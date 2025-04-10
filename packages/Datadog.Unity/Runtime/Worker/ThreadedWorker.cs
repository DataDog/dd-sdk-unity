// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections.Concurrent;
using System.Threading;
using Datadog.Unity.Core;
using UnityEngine;

namespace Datadog.Unity.Worker
{
    internal class ThreadedWorker : DatadogWorker
    {
        private BlockingCollection<IDatadogWorkerMessage> _workQueue = new();
        private Thread _workerThread;

        public override void Start()
        {
            if(_workerThread != null)
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
                Debug.Log("Stopping already stopped worker?");
                return;
            }

            _workQueue.CompleteAdding();
            _workerThread.Join();

            // Clear out thread and create a new work queue so
            // this worked can be re-used (although it shouldn't be)
            _workerThread = null;
            _workQueue = new();
        }

        public override void AddMessage(IDatadogWorkerMessage message)
        {
            _workQueue.Add(message);
        }

        private void ThreadWorker()
        {
#if UNITY_ANDROID
            AndroidJNI.AttachCurrentThread();
#endif

            while(!_workQueue.IsCompleted)
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
                    Debug.Log("Stopping worker.");
                }
            }

            Debug.Log("Stopped!");
        }
    }
}
