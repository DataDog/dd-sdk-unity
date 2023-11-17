// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using Datadog.Unity.Worker;

namespace Datadog.Unity.Rum
{
    internal class DdWorkerProxyRum : IDdRum
    {
        private readonly DatadogWorker _worker;
        private readonly IDateProvider _dateProvider;

        public DdWorkerProxyRum(DatadogWorker worker, IDateProvider dateProvider = null)
        {
            _dateProvider = dateProvider ?? new DefaultDateProvider();
            _worker = worker;
        }

        public void StartView(string key, string name = null, Dictionary<string, object> attributes = null)
        {
            InternalHelpers.Wrap("StartView",
                () =>
                {
                    _worker.AddMessage(new DdRumProcessor.StartViewMessage(_dateProvider.Now, key, name, attributes));
                });
        }

        public void StopView(string key, Dictionary<string, object> attributes = null)
        {
            InternalHelpers.Wrap("StopView",
                () => { _worker.AddMessage(new DdRumProcessor.StopViewMessage(_dateProvider.Now, key, attributes)); });
        }

        public void AddAction(RumUserActionType type, string name, Dictionary<string, object> attributes = null)
        {
            InternalHelpers.Wrap("AddAction",
                () =>
                {
                    _worker.AddMessage(
                        new DdRumProcessor.AddUserActionMessage(_dateProvider.Now, type, name, attributes));
                });
        }

        public void StartAction(RumUserActionType type, string name, Dictionary<string, object> attributes = null)
        {
            InternalHelpers.Wrap("StartAction",
                () =>
                {
                    _worker.AddMessage(
                        new DdRumProcessor.StartUserActionMessage(_dateProvider.Now, type, name, attributes));
                });
        }

        public void StopAction(RumUserActionType type, string name, Dictionary<string, object> attributes = null)
        {
            InternalHelpers.Wrap("StopAction",
                () =>
                {
                    _worker.AddMessage(
                        new DdRumProcessor.StopUserActionMessage(_dateProvider.Now, type, name, attributes));
                });
        }

        public void AddError(Exception error, RumErrorSource source, Dictionary<string, object> attributes = null)
        {
            InternalHelpers.Wrap("StopView",
                () =>
                {
                    _worker.AddMessage(
                        new DdRumProcessor.AddErrorMessage(_dateProvider.Now, error, source, attributes));
                });
        }

        public void StartResource(string key, RumHttpMethod httpMethod, string url,
            Dictionary<string, object> attributes = null)
        {
            InternalHelpers.Wrap("StartResource",
                () =>
                {
                    _worker.AddMessage(
                        new DdRumProcessor.StartResourceLoadingMessage(_dateProvider.Now, key, httpMethod, url,
                            attributes));
                });
        }

        public void StopResource(string key, RumResourceType kind, int? statusCode = null, long? size = null,
            Dictionary<string, object> attributes = null)
        {
            InternalHelpers.Wrap("StopResource",
                () =>
                {
                    _worker.AddMessage(
                        new DdRumProcessor.StopResourceLoadingMessage(_dateProvider.Now, key, kind, statusCode, size,
                            attributes));
                });
        }

        public void StopResourceWithError(string key, string errorType, string errorMessage,
            Dictionary<string, object> attributes = null)
        {
            InternalHelpers.Wrap("StopResourceWithError",
                () =>
                {
                    _worker.AddMessage(new DdRumProcessor.StopResourceLoadingWithErrorMessage(
                        _dateProvider.Now, key, errorType, errorMessage, attributes));
                });
        }

        public void StopResource(string key, Exception error, Dictionary<string, object> attributes = null)
        {
            InternalHelpers.Wrap("StopResource",
                () =>
                {
                    var errorType = error?.GetType()?.ToString();
                    var errorMessage = error?.Message;

                    _worker.AddMessage(new DdRumProcessor.StopResourceLoadingWithErrorMessage(
                        _dateProvider.Now, key, errorType, errorMessage, attributes));
                });
        }

        public void AddAttribute(string key, object value)
        {
            InternalHelpers.Wrap("AddAttribute",
                () => { _worker.AddMessage(new DdRumProcessor.AddAttributeMessage(key, value)); });
        }

        public void RemoveAttribute(string key)
        {
            InternalHelpers.Wrap("RemoveAttribute",
                () => { _worker.AddMessage(new DdRumProcessor.RemoveAttributeMessage(key)); });
        }

        public void AddFeatureFlagEvaluation(string key, object value)
        {
            InternalHelpers.Wrap("AddFeatureFlagEvaluation",
                () => { _worker.AddMessage(new DdRumProcessor.AddFeatureFlagEvaluationMessage(key, value)); });
        }

        public void StopSession()
        {
            InternalHelpers.Wrap("StopSession",
                () => { _worker.AddMessage(new DdRumProcessor.StopSessionMessage()); });
        }
    }
}
