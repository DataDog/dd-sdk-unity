// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using UnityEngine.Networking;

namespace Datadog.Unity.Rum
{
    public class TrackedUnityWebRequest
    {
        private UnityWebRequest _innerRequest;

        public bool disposeCertificateHandlerOnDispose
        {
            get => _innerRequest.disposeCertificateHandlerOnDispose;
            set => _innerRequest.disposeCertificateHandlerOnDispose = value;
        }

        public bool disposeDownloadHandlerOnDispose
        {
            get => _innerRequest.disposeDownloadHandlerOnDispose;
            set => _innerRequest.disposeDownloadHandlerOnDispose = value;
        }

        public bool disposeUploadHandlerOnDispose
        {
            get => _innerRequest.disposeUploadHandlerOnDispose;
            set => _innerRequest.disposeUploadHandlerOnDispose = value;
        }

        public TrackedUnityWebRequest()
        {
            _innerRequest = new UnityWebRequest();
        }

        public TrackedUnityWebRequest(string url)
        {
            _innerRequest = new UnityWebRequest(url);
        }

        public TrackedUnityWebRequest(Uri uri)
        {
            _innerRequest = new UnityWebRequest(uri);
        }

        public TrackedUnityWebRequest(string url, string method)
        {
            _innerRequest = new UnityWebRequest(url, method);
        }
    }
}
