// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

import Foundation
import Datadog

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
func DatadogLogging_Log(logId: UnsafeMutablePointer<CChar>?, logLevel: Int, message: UnsafeMutablePointer<CChar>?, attributes: UnsafePointer<CChar>?) {
    guard let logId = logId, let message = message else {
        return
    }

    if let idString = String(cString: logId, encoding: .utf8),
       let logger = LogRegistry.shared.logs[idString],
       let swiftMessage = String(cString: message, encoding: .utf8) {

        var decodedAttributes: [String: Encodable]?
        if let attributes = attributes,
           let attributeString = String(cString: attributes, encoding: .utf8),
           let attributesData = attributeString.data(using: .utf8),
           let jsonAttributes = try? JSONSerialization.jsonObject(with: attributesData) as? [String: Any] {
            decodedAttributes = castJsonAttributesToSwift(jsonAttributes)
        }

        let logLevel = LogLevel(rawValue: logLevel) ?? .info
        logger.log(level: logLevel, message: swiftMessage, error: nil, attributes: decodedAttributes)
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

@_cdecl("DatadogLogging_RemoveTag")
func DatadogLogging_RemoveTag(logId: UnsafeMutablePointer<CChar>?, tag: UnsafeMutablePointer<CChar>?) {
    guard let logId = logId, let tag = tag else {
        return
    }

    if let idString = String(cString: logId, encoding: .utf8),
       let logger = LogRegistry.shared.logs[idString],
       let swiftTag = String(cString: tag, encoding: .utf8) {
        logger.remove(tag: swiftTag)
    }
}

@_cdecl("DatadogLogging_RemoveTagWithKey")
func DatadogLogging_RemoveTagWithKey(logId: UnsafeMutablePointer<CChar>?, tag: UnsafeMutablePointer<CChar>?) {
    guard let logId = logId, let tag = tag else {
        return
    }

    if let idString = String(cString: logId, encoding: .utf8),
       let logger = LogRegistry.shared.logs[idString],
       let swiftTag = String(cString: tag, encoding: .utf8) {
        logger.removeTag(withKey: swiftTag)
    }
}

@_cdecl("DatadogLogging_AddAttribute")
func DatadogLogging_AddAttribute(logId: UnsafeMutablePointer<CChar>?, attributeJson: UnsafeMutablePointer<CChar>?) {
    guard let logId = logId, let attributeJson = attributeJson else {
        return
    }

    if let idString = String(cString: logId, encoding: .utf8),
       let logger = LogRegistry.shared.logs[idString],
       let attributeString = String(cString: attributeJson, encoding: .utf8),
       let attributeData = attributeString.data(using: .utf8),
       let jsonAttribute = try? JSONSerialization.jsonObject(with: attributeData) as? [String: Any] {
        jsonAttribute.forEach { (key, value) in
            logger.addAttribute(forKey: key, value: castAnyToEncodable(value))
        }
    }
}


@_cdecl("DatadogLogging_RemoveAttribute")
func DatadogLogging_RemoveAttribute(logId: UnsafeMutablePointer<CChar>?, key: UnsafeMutablePointer<CChar>?) {
    guard let logId = logId, let key = key else {
        return
    }

    if let idString = String(cString: logId, encoding: .utf8),
       let logger = LogRegistry.shared.logs[idString],
       let swiftKey = String(cString: key, encoding: .utf8) {
        logger.removeAttribute(forKey: swiftKey)
    }
}

internal func castJsonAttributesToSwift(_ jsonObject: [String: Any?]) -> [String: Encodable] {
    var casted: [String: Encodable] = [:]

    jsonObject.forEach { key, value in
        if let value = value {
            casted[key] = castAnyToEncodable(value)
        }
    }

    return casted
}

internal func castAnyToEncodable(_ jsonAny: Any) -> Encodable {
    switch jsonAny {
    case let number as NSNumber:
        if CFGetTypeID(number) == CFBooleanGetTypeID() {
            return CFBooleanGetValue(number)
        } else {
            switch CFNumberGetType(number) {
            case .charType:
                return number.uint8Value
            case .sInt8Type:
                return number.int8Value
            case .sInt16Type:
                return number.int16Value
            case .sInt32Type:
                return number.int32Value
            case .sInt64Type:
                return number.int64Value
            case .shortType:
                return number.uint16Value
            case .longType:
                return number.uint32Value
            case .longLongType:
                return number.uint64Value
            case .intType, .nsIntegerType, .cfIndexType:
                return number.intValue
            case .floatType, .float32Type:
                return number.floatValue
            case .doubleType, .float64Type, .cgFloatType:
                return number.doubleValue
            @unknown default:
                return JsonEncodable(jsonAny)
            }
        }
    case let string as String:
        return string
    default:
        return JsonEncodable(jsonAny)
    }
}

// This is similar to AnyEncodable, but for simplicity, it only looks for types
// that are JSON serializable
internal class JsonEncodable: Encodable {
    public let value: Any

    init(_ value: Any) {
        self.value = value
    }

    public func encode(to encoder: Encoder) throws {
        var container = encoder.singleValueContainer()

        switch value {
        case let number as NSNumber:
            try encodeNSNumber(number, into: &container)
        case is NSNull, is Void:
            try container.encodeNil()
        case let string as String:
            try container.encode(string)
        case let array as [Any]:
            try container.encode(array.map { JsonEncodable($0) })
        case let dictionary as [String: Any]:
            try container.encode(dictionary.mapValues { JsonEncodable($0) })
        default:
            let context = EncodingError.Context(
                codingPath: container.codingPath,
                // swiftlint:disable:next line_length
                debugDescription: "Value \(value) cannot be encoded - \(type(of: value)) is not supported by `JsonEncodable`."
            )
            throw EncodingError.invalidValue(value, context)
        }
    }
}

private func encodeNSNumber(_ number: NSNumber, into container: inout SingleValueEncodingContainer) throws {
    if CFGetTypeID(number) == CFBooleanGetTypeID() {
        try container.encode(CFBooleanGetValue(number))
    } else {
        switch CFNumberGetType(number) {
        case .charType:
            try container.encode(number.uint8Value)
        case .sInt8Type:
            try container.encode(number.int8Value)
        case .sInt16Type:
            try container.encode(number.int16Value)
        case .sInt32Type:
            try container.encode(number.int32Value)
        case .sInt64Type:
            try container.encode(number.int64Value)
        case .shortType:
            try container.encode(number.uint16Value)
        case .longType:
            try container.encode(number.uint32Value)
        case .longLongType:
            try container.encode(number.uint64Value)
        case .intType, .nsIntegerType, .cfIndexType:
            try container.encode(number.intValue)
        case .floatType, .float32Type:
            try container.encode(number.floatValue)
        case .doubleType, .float64Type, .cgFloatType:
            try container.encode(number.doubleValue)
        @unknown default:
            return
        }
    }
}
