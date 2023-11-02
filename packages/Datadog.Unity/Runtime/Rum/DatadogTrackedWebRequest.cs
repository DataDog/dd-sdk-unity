// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using UnityEngine.Networking;

namespace Datadog.Unity.Rum
{
    /// <summary>
    /// DatadogWebRequest is a wrapper around UnityWebRequest that allows us to track the request.
    /// </summary>
    public class DatadogTrackedWebRequest : IDisposable
    {
        private UnityWebRequest _innerRequest;

        public DatadogTrackedWebRequest()
        {
            _innerRequest = new UnityWebRequest();
        }

        /// <summary>
        /// Create a DatadogTrackedWebRequest from an existing UnityWebRequest. To ensure that this
        /// funcitons properly, the DatadogTrackedWebRequest should be created before any operations
        /// are performed on the wrapped request, and the wrapped request should not be used after.
        /// </summary>
        /// <param name="wrappedRequest">The request to wrap.</param>
        public DatadogTrackedWebRequest(UnityWebRequest wrappedRequest)
        {
            _innerRequest = wrappedRequest;
        }

        public DatadogTrackedWebRequest(string url)
        {
            _innerRequest = new UnityWebRequest(url);
        }

        public DatadogTrackedWebRequest(Uri uri)
        {
            _innerRequest = new UnityWebRequest(uri);
        }

        public DatadogTrackedWebRequest(string url, string method)
        {
            _innerRequest = new UnityWebRequest(url, method);
        }

        public DatadogTrackedWebRequest(Uri uri, string method)
        {
            _innerRequest = new UnityWebRequest(uri, method);
        }

        public DatadogTrackedWebRequest(string url, string method, DownloadHandler downloadHandler, UploadHandler uploadHandler)
        {
            _innerRequest = new UnityWebRequest(url, method, downloadHandler, uploadHandler);
        }

        public DatadogTrackedWebRequest(Uri uri, string method, DownloadHandler downloadHandler, UploadHandler uploadHandler)
        {
            _innerRequest = new UnityWebRequest(uri, method, downloadHandler, uploadHandler);
        }

        public UnityWebRequest innerRequest => _innerRequest;

        public CertificateHandler certificateHandler
        {
            get => _innerRequest.certificateHandler;
            set => _innerRequest.certificateHandler = value;
        }

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

        public ulong downloadedBytes => _innerRequest.downloadedBytes;

        public DownloadHandler downloadHandler
        {
            get => _innerRequest.downloadHandler;
            set => _innerRequest.downloadHandler = value;
        }

        public float downloadProgress => _innerRequest.downloadProgress;

        public string error => _innerRequest.error;

        public bool isDone => _innerRequest.isDone;

        public bool isModifiable => _innerRequest.isModifiable;

        public string method
        {
            get => _innerRequest.method;
            set => _innerRequest.method = value;
        }

        public int redirectLimit
        {
            get => _innerRequest.redirectLimit;
            set => _innerRequest.redirectLimit = value;
        }

        public long responseCode => _innerRequest.responseCode;

        public UnityWebRequest.Result result => _innerRequest.result;

        public int timeout
        {
            get => _innerRequest.timeout;
            set => _innerRequest.timeout = value;
        }

        public ulong uploadedBytes => _innerRequest.uploadedBytes;

        public UploadHandler uploadHandler
        {
            get => _innerRequest.uploadHandler;
            set => _innerRequest.uploadHandler = value;
        }

        public float uploadProgress => _innerRequest.uploadProgress;

        public string url
        {
            get => _innerRequest.url;
            set => _innerRequest.url = value;
        }

        public Uri uri
        {
            get => _innerRequest.uri;
            set => _innerRequest.uri = value;
        }

        public bool useHttpContinue
        {
            get => _innerRequest.useHttpContinue;
            set => _innerRequest.useHttpContinue = value;
        }

        public void Abort()
        {
            _innerRequest.Abort();
        }

        public UnityWebRequestAsyncOperation SendWebRequest()
        {
            // TODO: Add tracking here, maybe return custom AsyncOperation?
            return _innerRequest.SendWebRequest();
        }

        public string GetRequestHeader(string name)
        {
            return _innerRequest.GetRequestHeader(name);
        }

        public void SetRequestHeader(string name, string value)
        {
            _innerRequest.SetRequestHeader(name, value);
        }

        public string GetResponseHeader(string name)
        {
            return _innerRequest.GetResponseHeader(name);
        }

        public void Dispose()
        {
            _innerRequest?.Dispose();
        }

        public static DatadogTrackedWebRequest Delete(string url)
        {
            return new DatadogTrackedWebRequest(UnityWebRequest.Delete(url));
        }

        public static DatadogTrackedWebRequest Delete(Uri uri)
        {
            return new DatadogTrackedWebRequest(UnityWebRequest.Delete(uri));
        }

        public static DatadogTrackedWebRequest Get(string url)
        {
            return new DatadogTrackedWebRequest(UnityWebRequest.Get(url));
        }

        public static DatadogTrackedWebRequest Get(Uri uri)
        {
            return new DatadogTrackedWebRequest(UnityWebRequest.Get(uri));
        }

        public static DatadogTrackedWebRequest Head(string url)
        {
            return new DatadogTrackedWebRequest(UnityWebRequest.Head(url));
        }

        public static DatadogTrackedWebRequest Head(Uri uri)
        {
            return new DatadogTrackedWebRequest(UnityWebRequest.Head(uri));
        }

        public static DatadogTrackedWebRequest Post(string url, string postData, string contentType)
        {
            return new DatadogTrackedWebRequest(UnityWebRequest.Post(url, postData, contentType));
        }

        public static DatadogTrackedWebRequest Post(Uri uri, string postData, string contentType)
        {
            return new DatadogTrackedWebRequest(UnityWebRequest.Post(uri, postData, contentType));
        }

        public static DatadogTrackedWebRequest PostWwwForm(string url, string form)
        {
            return new DatadogTrackedWebRequest(UnityWebRequest.PostWwwForm(url, form));
        }

        public static DatadogTrackedWebRequest PostWwwForm(Uri uri, string form)
        {
            return new DatadogTrackedWebRequest(UnityWebRequest.PostWwwForm(uri, form));
        }
    }
}
