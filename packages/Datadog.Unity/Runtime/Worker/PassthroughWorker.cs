// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

namespace Datadog.Unity.Worker
{
    // Executes messages immediately on the calling thread.
    // TODO: Refactor so that using a worker is only necessary on platforms that need it.
    internal class PassthroughWorker : DatadogWorker
    {
        public override void Start()
        {

        }

        public override void Stop()
        {

        }

        public override void AddMessage(IDatadogWorkerMessage message)
        {
            HandleMessage(message);
        }
    }
}
