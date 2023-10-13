// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

import Foundation
import Datadog
import DatadogObjc

@_cdecl("Datadog_SetTrackingConsent")
func Datadog_SetTrackingConsent(trackingConsentInt: Int) {
    let trackingConsent: TrackingConsent?
    switch trackingConsentInt {
    case 0: trackingConsent = .granted
    case 1: trackingConsent = .notGranted
    case 2: trackingConsent = .pending
    default: trackingConsent = nil
    }

    if let trackingConsent = trackingConsent {
        Datadog.set(trackingConsent: trackingConsent)
    }
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
    kind: UnsafeMutablePointer<CChar>?) {
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
