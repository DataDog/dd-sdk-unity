// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

import Foundation
import DatadogCore
import DatadogLogs
import DatadogInternal

private class LogRegistry {
    public static let shared = LogRegistry()

    var logs: [String: LoggerProtocol] = [:]

    func createLogger(options: Logger.Configuration) -> String {
        let logger = Logger.create(with: options)

        let id = UUID()
        let idString = id.uuidString
        logs[idString] = logger
        return idString
    }
}

extension Logs.Configuration: Decodable {
    public init(from decoder: Decoder) throws {
        self.init()

        let values = try decoder.container(keyedBy: CodingKeys.self)
        customEndpoint = try values.decode(URL.self, forKey: CodingKeys.customEndpoint)
    }

    enum CodingKeys: String, CodingKey {
        case customEndpoint = "CustomEndpoint"
    }
}

extension Logger.Configuration: Decodable {
    public init(from decoder:Decoder) throws {
        self.init()

        let values = try decoder.container(keyedBy: CodingKeys.self)
        service = try values.decode(String?.self, forKey: CodingKeys.service)
        name = try values.decode(String?.self, forKey: CodingKeys.name)
        networkInfoEnabled = try values.decode(Bool.self, forKey: CodingKeys.networkInfoEnabled)
        bundleWithRumEnabled = try values.decode(Bool.self, forKey: CodingKeys.bundleWithRumEnabled)

        // Always true for Unity:
        remoteSampleRate = 100
        remoteLogThreshold = .debug
    }

    enum CodingKeys: String, CodingKey {
        case service = "Service"
        case name = "Name"
        case networkInfoEnabled = "NetworkInfoEnabled"
        case bundleWithRumEnabled = "BundleWithRumEnabled"
    }
}

@_cdecl("DatadogLogginer_Enable")
func DatadogLogging_Enable(jsonLoggingOptions: UnsafeMutablePointer<CChar>?) {
    if let stringLoggingOptions = decodeCString(cString: jsonLoggingOptions),
       let data = stringLoggingOptions.data(using: .utf8),
       let options = try? JSONDecoder().decode(Logs.Configuration.self, from: data) {
        Logs.enable(with: options)
    }
}

/// Create a logger for use in Unity, returns the UUID of the logger
@_cdecl("DatadogLogging_CreateLogger")
func DatadogLogging_CreateLogger(jsonLoggingOptions: UnsafeMutablePointer<CChar>?) -> UnsafeMutablePointer<CChar>? {
    if let stringLoggingOptions = decodeCString(cString: jsonLoggingOptions),
       let data = stringLoggingOptions.data(using: .utf8),
       let options = try? JSONDecoder().decode(Logger.Configuration.self, from: data) {

        let loggerId = LogRegistry.shared.createLogger(options: options)
        return strdup(loggerId)
    }

    return nil
}

@_cdecl("DatadogLogging_Log")
func DatadogLogging_Log(
    logId: UnsafeMutablePointer<CChar>?,
    logLevel: Int,
    message: UnsafeMutablePointer<CChar>?,
    attributes: UnsafeMutablePointer<CChar>?,
    error: UnsafeMutablePointer<CChar>?) {

        guard let logId = logId, let message = message else {
            return
        }

        if let idString = String(cString: logId, encoding: .utf8),
           let logger = LogRegistry.shared.logs[idString],
           let swiftMessage = String(cString: message, encoding: .utf8) {

            var decodedAttributes: [String: Encodable]?
            if let jsonAttributes = decodeJsonCString(cString: attributes) {
                decodedAttributes = castJsonAttributesToSwift(jsonAttributes)
            }

            var errorKind: String?
            var errorMessage: String?
            var stackTrace: String?
            if let jsonError = decodeJsonCString(cString: error) {
                errorKind = jsonError["type"] as? String
                errorMessage = jsonError["message"] as? String
                stackTrace = jsonError["stackTrace"] as? String
            }

            let logLevel = LogLevel(rawValue: logLevel) ?? .info
            logger._internal.log(
                level: logLevel,
                message: swiftMessage,
                errorKind: errorKind,
                errorMessage: errorMessage,
                stackTrace: stackTrace,
                attributes: decodedAttributes
            )
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
       let jsonAttribute = decodeJsonCString(cString: attributeJson) {
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
            // CFNumberGetType can lose the signedness of NSNumber values.
            // Matches native iOS: https://github.com/DataDog/dd-sdk-ios/blob/4dc1b9de87f6cabf3663e9c963463b7056f02188/DatadogInternal/Sources/Codable/AnyEncodable.swift#L133-L165
            switch Character(Unicode.Scalar(UInt8(number.objCType.pointee))) {
            case "c":
                return number.boolValue
            case "s":
                return number.int16Value
            case "i", "l":
                return number.int32Value
            case "q":
                return number.int64Value
            case "C":
                return number.uint8Value
            case "S":
                return number.uint16Value
            case "I", "L":
                return number.uint32Value
            case "Q":
                return number.uint64Value
            case "f":
                return number.floatValue
            case "d":
                return number.doubleValue
            default:
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
        switch Character(Unicode.Scalar(UInt8(number.objCType.pointee))) {
        case "c":
            try container.encode(number.boolValue)
        case "s":
            try container.encode(number.int16Value)
        case "i", "l":
            try container.encode(number.int32Value)
        case "q":
            try container.encode(number.int64Value)
        case "C":
            try container.encode(number.uint8Value)
        case "S":
            try container.encode(number.uint16Value)
        case "I", "L":
            try container.encode(number.uint32Value)
        case "Q":
            try container.encode(number.uint64Value)
        case "f":
            try container.encode(number.floatValue)
        case "d":
            try container.encode(number.doubleValue)
        default:
            let context = EncodingError.Context(
                codingPath: container.codingPath,
                debugDescription: "NSNumber cannot be encoded because its type is not handled"
            )
            throw EncodingError.invalidValue(number, context)
        }
    }
}
