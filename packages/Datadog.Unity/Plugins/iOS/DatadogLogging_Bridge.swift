// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

import Foundation
import Datadog

private class LogRegistry {
    public static let shared = LogRegistry()

    var logs: [String: DDLogger] = [:]

    func createLog() -> String {
        let logger = Logger.builder
            .sendNetworkInfo(true)
            .printLogsToConsole(true)
            .set(datadogReportingThreshold: .info)
            .build()
        let id = UUID()
        let idString = id.uuidString
        logs[idString] = logger
        return idString
    }
}

/// Create a logger for use in Unity, returns the UUID of the logger
@_cdecl("DatadogLogging_CreateLog")
func DatadogLogging_CreateLog() -> UnsafeMutablePointer<CChar>? {
    let loggerId = LogRegistry.shared.createLog()
    return strdup(loggerId)
}

@_cdecl("DatadogLogging_Log")
func DatadogLogging_Log(logId: UnsafeMutablePointer<CChar>?, message: UnsafeMutablePointer<CChar>?) {
    guard let logId = logId, let message = message else {
        return
    }

    if let idString = String(cString: logId, encoding: .utf8),
       let logger = LogRegistry.shared.logs[idString],
       let swiftMessage = String(cString: message, encoding: .utf8) {
       logger.log(level: .info, message: swiftMessage, error: nil, attributes: nil)
    }
}
