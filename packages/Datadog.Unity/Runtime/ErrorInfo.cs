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
        public const string DefaultErrorType = "UnknownError";

        /// <summary>
        /// Initializes a new instance of the <see cref="ErrorInfo"/> class from the
        /// given error details.
        /// </summary>
        /// <param name="type">An arbitrary string identifying the kind of error this is; typically the name of an Exception type.</param>
        /// <param name="message">The message accompanying this error.</param>
        /// <param name="stackTrace">The stack trace generated with this error, if any.</param>
        public ErrorInfo(string type, string message, string stackTrace = null)
        {
            // The details of this error were provided by the user; there's no
            // associated Exception
            Exception = null;

            Type = string.IsNullOrEmpty(type) ? DefaultErrorType : type;
            Message = message ?? string.Empty;
            StackTrace = stackTrace;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ErrorInfo"/> class from the
        /// given Exception.
        /// </summary>
        /// <param name="e">The exception to be recorded as an error.</param>
        public ErrorInfo(Exception e)
        {
            // Storing a reference to the managed Exception object allows us to
            // reconstruct a native callstack via il2cpp_native_stack_trace
            Exception = e;

            // Cache error details pulled from exception
            if (e != null)
            {
                Type = e.GetType().FullName ?? DefaultErrorType;
                Message = e.Message ?? string.Empty;
                StackTrace = e.StackTrace;
            }
            else
            {
                Type = DefaultErrorType;
                Message = string.Empty;
            }
        }

        /// <summary>
        /// Gets the Exception wrapped by this ErrorInfo, if any. May be null if the
        /// ErrorInfo was not initialized from an Exception.
        /// </summary>
        public Exception Exception { get; }

        /// <summary>
        /// Gets the type name associated with this error; guaranteed to be non-null;
        /// guaranteed to be non-empty.
        /// </summary>
        public string Type { get; }

        /// <summary>
        /// Gets the message associated with this error; guaranteed to be non-null.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Gets the stack trace accompanying this error; may be null.
        /// </summary>
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
