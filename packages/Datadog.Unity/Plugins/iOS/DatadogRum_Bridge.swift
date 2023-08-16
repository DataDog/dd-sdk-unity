// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

import Foundation
import Datadog

@_cdecl("DatadogRum_StartView")
func DatadogRum_StartView(key: CString?, name: CString?, attributes: CString?) {
    if let key = decodeCString(cString: key) {

        let name = decodeCString(cString: name)
        let decodedAttributes = decodeJsonAttributes(fromCString: attributes)

        Global.rum.startView(key: key, name: name, attributes: decodedAttributes)
    }
}

@_cdecl("DatadogRum_StopView")
func DatadogRum_StopView(key: CString?, attributes: CString?) {
    if let key = decodeCString(cString: key) {

        let decodedAttributes = decodeJsonAttributes(fromCString: attributes)

        Global.rum.stopView(key: key, attributes: decodedAttributes)
    }
}

@_cdecl("DatadogRum_AddTiming")
func DatadogRum_AddTiming(name: CString?) {
    if let name = decodeCString(cString: name) {
        Global.rum.addTiming(name: name)
    }
}

@_cdecl("DatadogRum_AddUserAction")
func DatadogRum_AddUserAction(type: CString?, name: CString?, attributes: CString?) {
    if let type = decodeUserActionType(fromCStirng: type),
       let name = decodeCString(cString: name) {

        let decodedAttributes = decodeJsonAttributes(fromCString: attributes)

        Global.rum.addUserAction(type: type, name: name, attributes: decodedAttributes)
    }
}

@_cdecl("DatadogRum_StartUserAction")
func DatadogRum_StartUserAction(type: CString?, name: CString?, attributes: CString?) {
    if let type = decodeUserActionType(fromCStirng: type),
       let name = decodeCString(cString: name) {

        let decodedAttributes = decodeJsonAttributes(fromCString: attributes)

        Global.rum.startUserAction(type: type, name: name, attributes: decodedAttributes)
    }
}

@_cdecl("DatadogRum_StopUserAction")
func DatadogRum_StopUserAction(type: CString?, name: CString?, attributes: CString?) {
    if let type = decodeUserActionType(fromCStirng: type),
       let name = decodeCString(cString: name) {

        let decodedAttributes = decodeJsonAttributes(fromCString: attributes)

        Global.rum.stopUserAction(type: type, name: name, attributes: decodedAttributes)
    }
}

@_cdecl("DatadogRum_AddError")
func DatadogRum_AddError(message: CString?, source: CString?, type: CString?, stack: CString?, attributes: CString?) {
    if let message = decodeCString(cString: message),
       let source = decodeErrorSource(fromCString: source) {

        let type = decodeCString(cString: type)
        let stack = decodeCString(cString: stack)
        let decodedAttributes = decodeJsonAttributes(fromCString: attributes)

        Global.rum.addError(message: message, type: type, source: source, stack: stack, attributes: decodedAttributes)
    }
}

func decodeUserActionType(fromCStirng cStirng: CString?) -> RUMUserActionType? {
    guard let actionTypeString = decodeCString(cString: cStirng) else {
        return nil
    }

    switch actionTypeString {
    case "Tap": return .tap
    case "Scroll": return .scroll
    case "Swipe": return .swipe
    case "Custom": return .custom
    default:
        return nil
    }
}

func decodeErrorSource(fromCString cString: CString?) -> RUMErrorSource? {
    guard let errorSourceString = decodeCString(cString: cString) else {
        return nil
    }

    switch errorSourceString {
    case "Source": return .source
    case "Network": return .network
    case "WebView": return .webview
    case "Console": return .console
    case "Custom": return .custom
    default:
        return nil
    }
}
