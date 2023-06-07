// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

import Foundation
import Datadog

@_cdecl("Datadog_SetTrackingConsent")
func Datadog_SetTrackingConsent(trackingConsentInt: Int) {
    let trackingConsent: TrackingConsent?
    switch(trackingConsentInt) {
    case 0: trackingConsent = .granted
    case 1: trackingConsent = .notGranted
    case 2: trackingConsent = .pending
    default: trackingConsent = nil
    }

    if let trackingConsent = trackingConsent {
        Datadog.set(trackingConsent: trackingConsent)
    }
}

private class LogRegistry {
    public static let shared = LogRegistry()

    var logs: [String: DDLogger] = [:]

    func createLogger(options: LoggingOptions) -> String {
        var logBuilder = Logger.builder
            .sendNetworkInfo(options.sendNetworkInfo)
            .sendLogsToDatadog(options.sendToDatadog)
            .printLogsToConsole(true)
            .set(datadogReportingThreshold: options.datadogReportingThreshold)

        if let loggerName = options.loggerName, !loggerName.isEmpty {
            logBuilder = logBuilder.set(loggerName: loggerName)
        }
        if let serviceName = options.serviceName, !serviceName.isEmpty {
            logBuilder = logBuilder.set(serviceName: serviceName)
        }

        let logger = logBuilder.build()

        let id = UUID()
        let idString = id.uuidString
        logs[idString] = logger
        return idString
    }
}

struct LoggingOptions: Decodable {
    let serviceName: String?
    let loggerName: String?
    let sendNetworkInfo: Bool
    let sendToDatadog: Bool
    let datadogReportingThreshold: LogLevel

    enum CodingKeys: String, CodingKey {
        case serviceName = "ServiceName"
        case loggerName = "LoggerName"
        case sendNetworkInfo = "SendNetworkInfo"
        case sendToDatadog = "SendToDatadog"
        case datadogReportingThreshold = "DatadogReportingThreshold"
    }
}

/// Create a logger for use in Unity, returns the UUID of the logger
@_cdecl("DatadogLogging_CreateLogger")
func DatadogLogging_CreateLogger(jsonLoggingOptions: UnsafeMutablePointer<CChar>?) -> UnsafeMutablePointer<CChar>? {
    if let jsonLoggingOptions = jsonLoggingOptions,
       let stringLoggingOptions = String(cString: jsonLoggingOptions, encoding: .utf8),
       let data = stringLoggingOptions.data(using: .utf8),
       let options = try? JSONDecoder().decode(LoggingOptions.self, from: data) {


        let loggerId = LogRegistry.shared.createLogger(options: options)
        return strdup(loggerId)
    }

    return nil
}

@_cdecl("DatadogLogging_Log")
func DatadogLogging_Log(logId: UnsafeMutablePointer<CChar>?, logLevel: Int, message: UnsafeMutablePointer<CChar>?) {
    guard let logId = logId, let message = message else {
        return
    }

    if let idString = String(cString: logId, encoding: .utf8),
       let logger = LogRegistry.shared.logs[idString],
       let swiftMessage = String(cString: message, encoding: .utf8) {
        let logLevel = LogLevel(rawValue: logLevel) ?? .info
        logger.log(level: logLevel, message: swiftMessage, error: nil, attributes: nil)
    }
}

@_cdecl("DatadogLogging_AddTag")
func DatadogLogging_AddTag(logId: UnsafeMutablePointer<CChar>?, tag: UnsafeMutablePointer<CChar>?, value: UnsafeMutablePointer<CChar>?) {
    guard let logId = logId, let tag = tag else {
        return
    }

    if let idString = String(cString: logId, encoding: .utf8),
       let logger = LogRegistry.shared.logs[idString],
       let swiftTag = String(cString: tag, encoding: .utf8) {
        if let value = value, let swiftValue = String(cString: value, encoding: .utf8) {
            logger.addTag(withKey: swiftTag, value: swiftValue)
        } else {
            logger.add(tag: swiftTag)
        }
    }
}
