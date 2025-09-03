// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.
"use strict";

let ddCoreLib = {
    DDCore_SetUserInfo: function(userInfo) {
        let userInfoStr = UTF8ToString(userInfo);
        let jsUserInfo = JSON.parse(userInfoStr);
        if (DD_LOGS) {
            DD_LOGS.setUser(jsUserInfo);
        }
        if (DD_RUM) {
            DD_RUM.setUser(jsUserInfo)
        }
    },

    DDCore_SetTrackingConsent: function(rawTrackingConsent) {
        let trackingConsent = UTF8ToString(rawTrackingConsent);
        if (DD_LOGS) {
            DD_LOGS.setTrackingConsent(trackingConsent);
        }
        if (DD_RUM) {
            DD_RUM.setTrackingConsent(trackingConsent)
        }
    },

    DDCore_SetUserProperties: function(properties) {
        let preopertiesStr = UTF8ToString(properties);
        let jsProperties = JSON.parse(preopertiesStr) ?? {};
        for (var key in jsProperties) {
            if (DD_LOGS) {
                DD_LOGS.setUserProperty(key, jsProperties[key])
            }
            if (DD_RUM) {
                DD_RUM.setUserProperty(key, jsProperties[key])
            }
        }
    }
};

mergeInto(LibraryManager.library, ddCoreLib);
