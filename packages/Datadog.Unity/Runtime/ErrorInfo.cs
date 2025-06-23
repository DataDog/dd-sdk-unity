// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;

namespace Datadog.Unity
{
    /// <summary>
    /// Data type used to provide error information to Datadog APIs. Describes a single
    /// error that has occurred in your application at runtime.
    /// </summary>
    public class ErrorInfo
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ErrorInfo"/> class from the
        /// given error details.
        /// </summary>
        /// <param name="type">An arbitrary string identifying the kind of error this is; typically the name of an Exception type.</param>
        /// <param name="message">The message accompanying this error.</param>
        /// <param name="stackTrace">The stack trace generated with this error, if any.</param>
        public ErrorInfo(string type, string message, string stackTrace)
        {
            Type = type;
            Message = message;
            StackTrace = stackTrace;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ErrorInfo"/> class from the
        /// given Exception.
        /// </summary>
        /// <param name="e">The exception to be recorded as an error.</param>
        public ErrorInfo(Exception e)
        {
            Type = e.GetType().Name;
            Message = e.Message;
            StackTrace = e.StackTrace;
        }

        public string Type { get; }

        public string Message { get; }

        public string StackTrace { get; }

        /// <summary>
        /// Allows implicit conversion from Exception to ErrorInfo, allowing functions
        /// that accept an ErrorInfo parameter to be called interchangeably with
        /// arguments of type ErrorInfo or Exception.
        /// </summary>
        /// <param name="e">Exception value to be converted to ErrorInfo.</param>
        public static implicit operator ErrorInfo(Exception e)
        {
            return new ErrorInfo(e);
        }
    }
}
