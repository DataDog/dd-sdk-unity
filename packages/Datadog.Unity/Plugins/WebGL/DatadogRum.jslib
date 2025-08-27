// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.
"use strict";

let ddRumLib = {
    DDRum_InitRum: function(rawConfiguration) {
        if (!DD_RUM) {
            return false;
        }

        let self = this;
        this._rumPlugin = {
            name: 'DatadogUnityWeb',
            onRumStart: function(options) {
                self.addEvent = options.addEvent;
            }
        };

        // Init internal helpers
        this.getEventRelativeTime = (timestampMs) => {
            if (this._navigationStart === undefined) {
                this._navigationStart = window.performance.timing.navigationStart;
            }

            if (!this._navigationStart) {
                return timestampMs;
            }
            return (timestampMs - this._navigationStart);
        };

        let configStr = UTF8ToString(rawConfiguration);
        let config = JSON.parse(configStr);
        // Replace regex strings in allowedTracingUrls with actual JS RegExp
        config.allowedTracingUrls = config.allowedTracingUrls.map((val) => {
            return {
                match: RegExp(val.match),
                propagatorTypes: val.propagatorTypes
            };
        });
        config.plugins = [this._rumPlugin];

        DD_RUM.init(config);
        return true;
    },

    DDRum_AddAttribute: function(rawAttributes) {
        let attributeStr = UTF8ToString(rawAttributes);
        let attribute = JSON.parse(attributeStr);
        DD_RUM.setGlobalContextProperty(attribute.key, attribute.value);
    },

    DDRum_RemoveAttribute: function(rawKey) {
        let key = UTF8ToString(rawKey);
        DD_RUM.removeGlobalContextProperty(key);
    },

    DDRum_AddError: function(rawErrorKind, rawErrorMessage, rawErrorStackTrace, rawAttributes) {
        let attributesStr = UTF8ToString(rawAttributes);
        let attributes = JSON.parse(attributesStr) ?? {};

        let jsError = null;
        jsError = new Error(UTF8ToString(rawErrorMessage));
        jsError.name = UTF8ToString(rawErrorKind);
        jsError.stack = UTF8ToString(rawErrorStackTrace);

        let fingerprint = attributes['_dd.error.fingerprint'];
        if (fingerprint) {
            attributes.remove('_dd.error.fingerprint');
            attributes['error.fingerprint'] = fingerprint;
        }

        DD_RUM.addError(jsError, attributes);
    },

    DDRum_AddTiming: function(rawName) {
        let name = UTF8ToString(rawName);
        DD_RUM.addTiming(name);
    },

    DDRum_AddAction: function(rawType, rawName, rawAttributes) {
        let id = crypto.randomUUID();
        let name = UTF8ToString(rawName);
        let type = UTF8ToString(rawType);
        let attributesStr = UTF8ToString(rawAttributes);

        let attributes = JSON.parse(attributesStr);

        let timestampMs = attributes['_dd.timestamp'];
        let eventTime = this.getEventRelativeTime(timestampMs);

        this.addEvent(
            eventTime,
            {
                type: 'action',
                date: eventTime,
                context: attributes,
                action: {
                    id,
                    type,
                    target: {
                        name
                    }
                }
            },
            {},
        );

        //DD_RUM.addAction(name, attributes);
    },

    DDRum_AddFeatureFlagEvaluation: function(rawAttribute) {
        let attributeStr = UTF8ToString(rawAttribute);
        let attribute = JSON.parse(attributeStr);
        DD_RUM.addFeatureFlagEvaluation(attribute.name, attribute.value);
    },

    DDRum_StartView: function(rawViewName, rawAttributes) {
        let viewName = UTF8ToString(rawViewName);
        let attributesStr = UTF8ToString(rawAttributes);
        let jsAttributes = JSON.parse(attributesStr) ?? {};

        DD_RUM.startView({
            name: viewName,
            context: jsAttributes,
        });
    },

    DDRum_StopSession: function() {
        DD_RUM.stopSession();
    }
};

mergeInto(LibraryManager.library, ddRumLib);
