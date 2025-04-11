// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.
"use strict";

let ddCoreLib = {
    DDCore_SetUserInfo: function(userInfo) {
        let userInfoStr = UTF8ToString(userInfo);
        let jsUserInfo = JSON.parse(userInfoStr);
        // TODO: Check if logs / rum need to be set separately
        DD_LOGS.setUser(jsUserInfo);
    },

    DDCore_SetUserProperties: function(properties) {
        let preopertiesStr = UTF8ToString(properties);
        let jsProperties = JSON.parse(preopertiesStr) ?? {};
        for (var key in jsProperties) {
            DD_LOGS.setUserProperty(key, jsProperties[key])
        }
    }
};

mergeInto(LibraryManager.library, ddCoreLib);
