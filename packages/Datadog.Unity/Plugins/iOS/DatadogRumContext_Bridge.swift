// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

import Foundation
import DatadogCore
import DatadogInternal

final class DatadogUnityRumContextFeature: DatadogFeature, FeatureMessageReceiver {
    static let name = "unity_rum_context"
    private static let shared = DatadogUnityRumContextFeature()

    var messageReceiver: FeatureMessageReceiver { self }

    @ReadWriteLock
    private var sessionID: String?

    static func register() {
        let core = CoreRegistry.default
        guard core.feature(named: name, type: DatadogUnityRumContextFeature.self) == nil else {
            return
        }
        do {
            try core.register(feature: shared)
        } catch {
            shared.sessionID = nil
        }
    }

    func receive(message: FeatureMessage, from core: DatadogCoreProtocol) -> Bool {
        guard case .context(let context) = message else { return false }
        sessionID = context.additionalContext(ofType: RUMCoreContext.self)?.sessionID
        return true
    }

    static func copySessionID(to buffer: UnsafeMutablePointer<CChar>?, capacity: Int32) -> Int32 {
        guard let buffer = buffer, let id = shared.sessionID else { return 0 }
        let count = id.utf8.count
        guard count + 1 <= Int(capacity) else { return 0 }
        id.withCString { source in
            buffer.update(from: source, count: count + 1)
        }
        return Int32(count)
    }
}

@_cdecl("DatadogRum_GetSessionId")
func DatadogRum_GetSessionId(buffer: UnsafeMutablePointer<CChar>?, capacity: Int32) -> Int32 {
    DatadogUnityRumContextFeature.copySessionID(to: buffer, capacity: capacity)
}
