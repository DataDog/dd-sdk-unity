// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using Datadog.Unity.Worker;

namespace Datadog.Unity.Logs
{
    public abstract class DdLogger
    {
        private RateBasedSampler _sampler;
        private DdLogLevel _logLevel;

        internal DdLogger(DdLogLevel logLevel, float sampleRate)
        {
            _sampler = new RateBasedSampler(sampleRate / 100.0f);
            _logLevel = logLevel;
        }

        /// <summary>
        /// Sends a <c>debug</c> log message.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <param name="attributes">Any attributes to attach to this log.</param>
        /// <param name="error">An optional error to associate with this log.</param>
        public void Debug(string message, Dictionary<string, object> attributes = null, Exception error = null)
        {
            Log(DdLogLevel.Debug, message, attributes, error);
        }

        /// <summary>
        /// Sends a <c>info</c> log message.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <param name="attributes">Any attributes to attach to this log.</param>
        /// <param name="error">An optional error to associate with this log.</param>
        public void Info(string message, Dictionary<string, object> attributes = null, Exception error = null)
        {
            Log(DdLogLevel.Info, message, attributes, error);
        }

        /// <summary>
        /// Sends a <c>notice</c> log message.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <param name="attributes">Any attributes to attach to this log.</param>
        /// <param name="error">An optional error to associate with this log.</param>
        public void Notice(string message, Dictionary<string, object> attributes = null, Exception error = null)
        {
            Log(DdLogLevel.Notice, message, attributes, error);
        }

        /// <summary>
        /// Sends a <c>warn</c> log message.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <param name="attributes">Any attributes to attach to this log.</param>
        /// <param name="error">An optional error to associate with this log.</param>
        public void Warn(string message, Dictionary<string, object> attributes = null, Exception error = null)
        {
            Log(DdLogLevel.Warn, message, attributes, error);
        }

        /// <summary>
        /// Sends an <c>error</c> log message.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <param name="attributes">Any attributes to attach to this log.</param>
        /// <param name="error">An optional error to associate with this log.</param>
        public void Error(string message, Dictionary<string, object> attributes = null, Exception error = null)
        {
            Log(DdLogLevel.Error, message, attributes, error);
        }

        /// <summary>
        /// Sends a <c>critical</c> log message.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <param name="attributes">Any attributes to attach to this log.</param>
        /// <param name="error">An optional error to associate with this log.</param>
        public void Critical(string message, Dictionary<string, object> attributes = null, Exception error = null)
        {
            Log(DdLogLevel.Critical, message, attributes, error);
        }

        /// <summary>
        /// Sends a log message with the supplied level.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <param name="attributes">Any attributes to attach to this log.</param>
        /// <param name="error">An optional error to associate with this log.</param>
        public void Log(DdLogLevel level, string message, Dictionary<string, object> attributes = null, Exception error = null)
        {
            InternalHelpers.Wrap("Log", () =>
            {
                if (level >= _logLevel && _sampler.Sample())
                {
                    PlatformLog(level, message, attributes, error);
                }
            });
        }

        /// <summary>
        /// Add a tag to all future logs sent by this logger.
        ///
        /// The tag will take the form "key:value" or "key" if no value is provided.
        ///
        /// Tags must start with a letter and after that may contain the following
        /// characters: Alphanumerics, Underscores, Minuses, Colons, Periods, Slashes.
        /// Other special characters are converted to underscores.
        ///
        /// Tags must be lowercase, and can be at most 200 characters. If the tag you
        /// provide is longer, only the first 200 characters will be used.
        ///
        /// See also: <see href="https://docs.datadoghq.com/tagging/#defining-tags">Defining Tags</see>.
        /// </summary>
        /// <param name="tag">The tag to add.</param>
        /// <param name="value">The optional value of the tag.</param>
        public abstract void AddTag(string tag, string value = null);

        /// <summary>
        /// Remove a given <c>tag</c> from all future logs sent by this logger.
        ///
        /// Previous logs won't lose the this tag if they were created prior to this call.
        /// </summary>
        /// <param name="tag">The tag to remove.</param>
        public abstract void RemoveTag(string tag);

        /// <summary>
        /// Remove all tags with the given <c>tag</c> from all future logs sent by this logger.
        ///
        /// Previous logs won't lose the this tag if they were created prior to this call.
        /// </summary>
        /// <param name="key">The key for the tags to remove</param>
        public abstract void RemoveTagsWithKey(string key);

        /// <summary>
        /// Add a custom attribute to all future logs sent by this logger.
        ///
        /// Values can be nested up to 10 levels deep. Keys using more than 10 levels
        /// will be sanitized by SDK.
        /// </summary>
        /// <param name="key">The key of the attribute to add.</param>
        /// <param name="value">The value of the attribute.</param>
        public abstract void AddAttribute(string key, object value);

        /// <summary>
        /// Remove a custom attribute from all future logs sent by this logger.
        ///
        /// Previous logs won't lose the attribute value associated with this <c>key</c> if
        /// they were created prior to this call.
        /// </summary>
        /// <param name="key">The key for the attribute to remove.</param>
        public abstract void RemoveAttribute(string key);

        internal abstract void PlatformLog(DdLogLevel level, string message, Dictionary<string, object> attributes = null, Exception error = null);
    }

    internal class DdNoOpLogger : DdLogger
    {
        public DdNoOpLogger()
            : base(DdLogLevel.Critical, 0.0f)
        {
        }

        public override void AddAttribute(string key, object value)
        {
        }

        public override void AddTag(string tag, string value = null)
        {
        }

        public override void RemoveAttribute(string key)
        {
        }

        public override void RemoveTag(string tag)
        {
        }

        public override void RemoveTagsWithKey(string key)
        {
        }

        internal override void PlatformLog(DdLogLevel level, string message, Dictionary<string, object> attributes = null, Exception error = null)
        {
        }
    }
}
