// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.
"use strict";

let ddLogsLib = {
    DDInitLogs: function (jsonConfiguration) {
        let configStr = UTF8ToString(jsonConfiguration);
        let config = JSON.parse(configStr);
        this._activeLoggers = {}
        DD_LOGS.init(config)
    },

    DDCreateLogger: function (loggerId, configuration) {
        let loggerIdStr = UTF8ToString(loggerId)
        let configStr = UTF8ToString(configuration);
        let jsConfig = JSON.parse(configStr);

        let logger = DD_LOGS.createLogger(
            jsConfig.name ?? 'default',
            {}
        );
        logger.setHandler(['http', 'console']);
        this._activeLoggers[loggerIdStr] = logger;
    },

    DDLog: function (loggerId, message, level, errorMessage, errorKind, errorStackTrace, attributes) {
        let loggerIdStr = UTF8ToString(loggerId)
        let logger = this._activeLoggers[loggerIdStr];
        if (!logger) {
            return;
        }

        let attributesStr = UTF8ToString(attributes);
        let jsAttributes = JSON.parse(attributesStr) ?? {};
        let jsError = null;
        if (errorMessage && errorKind && errorStackTrace) {
            jsError = {
                message: UTF8ToString(errorMessage),
                kind: UTF8ToString(errorKind),
                stack: UTF8ToString(errorStackTrace)
            };

            let fingerprint = jsAttributes['_dd.error.fingerprint'];
            if (fingerprint) {
                jsAttributes.remove('_dd.error.fingerprint');
                jsAttributes['error.fingerprint'] = fingerprint;
            }
        }

        logger.log(
            UTF8ToString(message), jsAttributes, UTF8ToString(level), jsError);
    },
};

// autoAddDeps(ddLogsLib, '$activeLoggers');
mergeInto(LibraryManager.library, ddLogsLib);
