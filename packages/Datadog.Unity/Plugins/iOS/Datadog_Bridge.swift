// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

import Foundation
import DatadogCore
import DatadogInternal

// Hold the core, allowing it to be replacable
public class DatadogUnityCore {
    public static var shared: DatadogCoreProtocol?
}

@_cdecl("Datadog_SetTrackingConsent")
func Datadog_SetTrackingConsent(trackingConsentInt: Int) {
    guard let core = DatadogUnityCore.shared else {
        return
    }

    let trackingConsent: TrackingConsent?
    switch trackingConsentInt {
    case 0: trackingConsent = .granted
    case 1: trackingConsent = .notGranted
    case 2: trackingConsent = .pending
    default: trackingConsent = nil
    }

    if let trackingConsent = trackingConsent {
        Datadog.set(trackingConsent: trackingConsent, in: core)
    }
}

@_cdecl("Datadog_SetUserInfo")
func Datadog_SetUserInfo(
    id: CString?,
    name: CString?,
    email: CString?,
    extraInfo: CString?
) {
    guard let core = DatadogUnityCore.shared else {
        return
    }

    let idString = decodeCString(cString: id)
    let nameString = decodeCString(cString: name)
    let emailString = decodeCString(cString: email)
    let decodedExtraInfo = decodeJsonAttributes(fromCString: extraInfo)

    Datadog.setUserInfo(id: idString, name: nameString, email: emailString, extraInfo: decodedExtraInfo, in: core)
}

@_cdecl("Datadog_AddUserExtraInfo")
func Datadog_AddUserExtraInfo(extraInfo: CString) {
    guard let core = DatadogUnityCore.shared else {
        return
    }

    let decodedExtraInfo = decodeJsonAttributes(fromCString: extraInfo)

    Datadog.addUserExtraInfo(decodedExtraInfo, in: core)
}

@_cdecl("Datadog_SendDebugTelemetry")
func Datadog_SendDebugTelemetry(message: UnsafeMutablePointer<CChar>?) {
    guard let message = message else {
        return
    }

    if let messageString = String(cString: message, encoding: .utf8) {
        Datadog._internal.telemetry.debug(id: "datadog_unity:\(messageString)", message: messageString)
    }
}

@_cdecl("Datadog_SendErrorTelemetry")
func Datadog_SendErrorTelemetry(
    message: UnsafeMutablePointer<CChar>?,
    stack: UnsafeMutablePointer<CChar>?,
    kind: UnsafeMutablePointer<CChar>?
) {
    guard let message = message else {
        return
    }

    if let messageString = String(cString: message, encoding: .utf8) {
        var errorStack: String?
        var errorKind: String?

        if let stack = stack {
            errorStack = String(cString: stack, encoding: .utf8)
        }
        if let kind = kind {
            errorKind = String(cString: kind, encoding: .utf8)
        }

        Datadog._internal.telemetry.error(id: "datadog_unity:\(messageString)", message: messageString, kind: errorKind, stack: errorStack)
    }
}

@_cdecl("Datadog_ClearAllData")
func Datadog_ClearAllData() {
    guard let core = DatadogUnityCore.shared else {
        return
    }

    Datadog.clearAllData(in: core)
}

// MARK: - Functionss for integration testing

// Function used in testing to install a proxy in front of the main core
public func proxyDatadogCore() -> DatadogCoreProtocol? {
    guard let core = DatadogUnityCore.shared else {
        return nil
    }

    let proxyCore = DatadogCoreProxy(core: core)
    DatadogUnityCore.shared = proxyCore;

    return proxyCore
}

@_cdecl("Datadog_GetAllEvents")
func Datadog_GetAllEvents(feature: CString) -> UnsafeMutablePointer<UInt8>? {
    guard let coreProxy = DatadogUnityCore.shared as? DatadogCoreProxy,
          let feature = String(cString: feature, encoding: .utf8) else {
        return nil
    }

    do {
        let events = coreProxy.waitAndReturnEventsData(ofFeature: feature)
        let data = try JSONSerialization.data(withJSONObject: events)
        let retPtr = UnsafeMutablePointer<UInt8>.allocate(capacity: data.count)
        data.copyBytes(to: retPtr, count: data.count)

        return retPtr
    } catch {
        consolePrint("\(error)", .error)
    }
    return nil
}

@_cdecl("Datadog_FreePointer")
func Datadog_GetAllEvents(ptr: UnsafeMutablePointer<UInt8>?) {
    ptr?.deallocate()
}
