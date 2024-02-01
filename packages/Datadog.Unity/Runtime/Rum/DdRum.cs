// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;

namespace Datadog.Unity.Rum
{
    public enum RumErrorSource
    {
        Source,
        Network,
        WebView,
        Console,
        Custom,
    }

    public enum RumHttpMethod
    {
        Post,
        Get,
        Head,
        Put,
        Delete,
        Patch,
    }

    public enum RumResourceType
    {
        Document,
        Image,
        Xhr,
        Beacon,
        Css,
        Fetch,
        Font,
        Js,
        Media,
        Other,
        Native,
    }

    public enum RumUserActionType
    {
        Tap,
        Scroll,
        Swipe,
        Custom,
    }

    public interface IDdRum
    {
        /// <summary>
        /// Notifies that the View identified by <c>key</c> starts being presented to the
        /// user. This view will show as <c>name</c> in the RUM explorer, and defaults to
        /// <c>key</c> if it is not provided.
        /// </summary>
        /// <param name="key">The identifying key of this view.</param>
        /// <param name="name">The name of this view. Defaults to <c>key</c>.</param>
        /// <param name="attributes">Any extra attributes to associate with this view.</param>
        public void StartView(string key, string name = null, Dictionary<string, object> attributes = null);

        /// <summary>
        /// Notifies that the View identified by [key] stops being presented to the user.
        /// </summary>
        /// <param name="key">The identifying key of the view to stop. Must match the value sent to <see cref="IDdRum.StartView"/>.</param>
        /// <param name="attributes">Any extra attributes to associate with this view.</param>
        public void StopView(string key, Dictionary<string, object> attributes = null);

        /// <summary>
        /// Register the occurrence of a User Action.
        ///
        /// This is used to a track discrete User Actions (e.g. "tap") specified by <c>type</c>.
        /// </summary>
        /// <param name="type">The type of the action.</param>
        /// <param name="name">The name of the action.</param>
        /// <param name="attributes">Any attributes to associate with this action</param>
        public void AddAction(RumUserActionType type, string name, Dictionary<string, object> attributes = null);

        /// <summary>
        /// Notifies that a User Action of [type] has started, named [name]. This is
        /// used to track long running user actions (e.g. "scroll"). Such a User
        /// Action must be stopped with <see cref="IDdRum.StopAction"/>, and will be stopped
        /// automatically if it lasts for more than 10 seconds.
        /// </summary>
        /// <param name="type">The type of the action.</param>
        /// <param name="name">The name of the action.</param>
        /// <param name="attributes">Any attributes to associate with this action</param>
        public void StartAction(RumUserActionType type, string name, Dictionary<string, object> attributes = null);

        /// <summary>
        /// Notifies that the User Action of <c>type</c>, named <c>name</c> has stopped.
        /// This is used to stop tracking long running user actions (e.g. "scroll"),
        /// started with <see cref="IDdRum.StartAction" />.
        /// </summary>
        /// <param name="type">The type of the action.</param>
        /// <param name="name">The name of the action.</param>
        /// <param name="attributes">Any attributes to associate with this action</param>
        public void StopAction(RumUserActionType type, string name, Dictionary<string, object> attributes = null);

        /// <summary>
        /// Notifies that the Exception occurred in currently presented View, with an origin of [source].
        /// </summary>
        /// <param name="error">The exception that occurred.</param>
        /// <param name="source">The source of the error.</param>
        /// <param name="attributes">Any additional attributes to associate with the error.</param>
        public void AddError(Exception error, RumErrorSource source, Dictionary<string, object> attributes = null);

        /// <summary>
        /// Notifies that the a Resource identified by <c>key</c> started being loaded from
        /// given <c>url</c> using the specified <c>httpMethod</c>.
        ///
        /// Note that <c>key</c> must be unique among all Resources being currently loaded,
        /// and should be sent to <see cref="IDdRum.StopResource"/> or <see cref="IDdRum.StopResourceWithError"/>
        /// when resource loading is complete.
        /// </summary>
        /// <param name="key">The key identifying this resource.</param>
        /// <param name="httpMethod">The HTTP method of the resource.</param>
        /// <param name="url">The URL this resource is fetching.</param>
        /// <param name="attributes">Any attributes to attach to this resource.</param>
        public void StartResource(string key, RumHttpMethod httpMethod, string url,
            Dictionary<string, object> attributes = null);

        /// <summary>
        /// Notifies that the Resource identified by <c>key</c> stopped being loaded
        /// successfully and supplies additional information about the Resource loaded.
        /// </summary>
        /// <param name="key">The key identifying the resource to be stopped.</param>
        /// <param name="kind">The kind of resource.</param>
        /// <param name="statusCode">The status code returned when fetching the resource.</param>
        /// <param name="size">The size of the resource.</param>
        /// <param name="attributes">Any attributes to attach to this resource.</param>
        public void StopResource(string key, RumResourceType kind, int? statusCode = null, long? size = null,
            Dictionary<string, object> attributes = null);

        public void StopResourceWithError(string key, string errorType, string errorMessage,
            Dictionary<string, object> attributes = null);

        /// <summary>
        /// Notifies that the Resource identified by <c>key</c> stopped being loaded with an
        /// Exception.
        /// </summary>
        /// <param name="key">The key identifying the resource to be stopped.</param>
        /// <param name="error">The exception that occurred when loading the resource.</param>
        /// <param name="attributes">Any attributes to attach to this resource.</param>
        public void StopResource(string key, Exception error, Dictionary<string, object> attributes = null);

        /// <summary>
        /// Adds a custom attribute to all future events sent by the RUM monitor.
        /// </summary>
        /// <param name="key">The key identifying the attribute.</param>
        /// <param name="value">The value of the attribute.</param>
        public void AddAttribute(string key, object value);

        /// <summary>
        /// Removes a custom attribute from all future events sent by the RUM
        /// monitor. Events created prior to this call will not lose this attribute.
        /// </summary>
        /// <param name="key">The key of the attribute to remove.</param>
        public void RemoveAttribute(string key);

        /// <summary>
        /// Adds the result of evaluating a feature flag to the view.
        /// Feature flag evaluations are local to the active view and are cleared when the view is stopped.
        /// </summary>
        /// <param name="key">The key identifying the feature flag.</param>
        /// <param name="value">The value the feature flag evaluated to.</param>
        public void AddFeatureFlagEvaluation(string key, object value);

        /// <summary>
        /// Stops the current session. A new session will start in response to a call
        /// to <see cref="IDdRum.StartView"/>, <see cref="IDdRum.AddAction"/>, or <see cref="IDdRum.StartAction"/>.
        /// If the session is started because of a call to [addAction] or [startAction], the
        /// last known view is restarted in the new session.
        /// </summary>
        public void StopSession();
    }

    #region NoOp Implementation

    internal class DdNoOpRum : IDdRum
    {
        public void StartView(string key, string name = null, Dictionary<string, object> attributes = null)
        {
        }

        public void StopView(string key, Dictionary<string, object> attributes = null)
        {
        }

        public void AddAction(RumUserActionType type, string name, Dictionary<string, object> attributes = null)
        {
        }

        public void StartAction(RumUserActionType type, string name, Dictionary<string, object> attributes = null)
        {
        }

        public void StopAction(RumUserActionType type, string name, Dictionary<string, object> attributes = null)
        {
        }

        public void AddError(Exception error, RumErrorSource source, Dictionary<string, object> attributes = null)
        {
        }

        public void StartResource(string key, RumHttpMethod httpMethod, string url,
            Dictionary<string, object> attributes = null)
        {
        }

        public void StopResource(string key, RumResourceType kind, int? statusCode = null, long? size = null,
            Dictionary<string, object> attributes = null)
        {
        }

        public void StopResourceWithError(string key, string errorType, string errorMessage, Dictionary<string, object> attributes = null)
        {
        }

        public void StopResource(string key, Exception error, Dictionary<string, object> attributes = null)
        {
        }

        public void AddAttribute(string key, object value)
        {
        }

        public void RemoveAttribute(string key)
        {
        }

        public void AddFeatureFlagEvaluation(string key, object value)
        {
        }

        public void StopSession()
        {
        }
    }

    #endregion
}
