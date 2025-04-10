// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.
"use strict";

let ddLogsLib = {
    DDLogs_InitLogs: function (jsonConfiguration) {
        let configStr = UTF8ToString(jsonConfiguration);
        let config = JSON.parse(configStr);
        this._activeLoggers = {}
        DD_LOGS.init(config)
    },

    DDLogs_CreateLogger: function (loggerId, configuration) {
        let loggerIdStr = UTF8ToString(loggerId)
        let configStr = UTF8ToString(configuration);
        let jsConfig = JSON.parse(configStr);

        let logger = DD_LOGS.createLogger(
            jsConfig.name,
            {}
        );
        logger.setHandler(['http', 'console']);
        this._activeLoggers[loggerIdStr] = logger;
    },

    DDLogs_AddGlobalAttributes: function(attributes) {
        let attributesStr = UTF8ToString(attributes);
        let jsAttributes = JSON.parse(attributesStr) ?? {};
        for (var key in jsAttributes) {
            DD_LOGS.setGlobalContextProperty(key, jsAttributes[key])
        }
    },

    DDLogs_RemoveGlobalAttribute: function(key) {
        let keyStr = UTF8ToString(key);
        DD_LOGS.removeGlobalContextProperty(keyStr)
    },

    DDLogs_Log: function (loggerId, message, level, errorKind, errorMessage, errorStackTrace, attributes) {
        let loggerIdStr = UTF8ToString(loggerId)
        let logger = this._activeLoggers[loggerIdStr];
        if (!logger) {
            return;
        }

        let attributesStr = UTF8ToString(attributes);
        let jsAttributes = JSON.parse(attributesStr) ?? {};
        let jsError = null;
        if (errorMessage && errorKind && errorStackTrace) {
            jsError = new Error(UTF8ToString(errorMessage));
            jsError.name = UTF8ToString(errorKind);
            jsError.stack = UTF8ToString(errorStackTrace);

            let fingerprint = jsAttributes['_dd.error.fingerprint'];
            if (fingerprint) {
                jsAttributes.remove('_dd.error.fingerprint');
                jsAttributes['error.fingerprint'] = fingerprint;
            }
        }

        logger.log(
            UTF8ToString(message), jsAttributes, UTF8ToString(level), jsError);
    },

    DDLogs_AddAttribute: function (loggerId, jsonAttribute) {
        let loggerIdStr = UTF8ToString(loggerId)
        let logger = this._activeLoggers[loggerIdStr];
        if (!logger) {
            return;
        }

        let attributesStr = UTF8ToString(jsonAttribute)
        let attributes = JSON.parse(attributesStr)
        for (var key in attributes) {
            logger.setContextProperty(key, attributes[key])
        }
    },

    DDLogs_RemoveAttribute: function(loggerId, key) {
        let loggerIdStr = UTF8ToString(loggerId)
        let logger = this._activeLoggers[loggerIdStr];
        if (!logger) {
            return;
        }

        let attributeKey = UTF8ToString(key)
        logger.removeContextProperty(attributeKey)
    }
};

mergeInto(LibraryManager.library, ddLogsLib);
