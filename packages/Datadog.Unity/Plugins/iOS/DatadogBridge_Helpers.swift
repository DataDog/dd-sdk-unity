// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

typealias CString = UnsafeMutablePointer<CChar>

func decodeCString(cString: UnsafeMutablePointer<CChar>?) -> String? {
    guard let cString = cString else {
        return nil
    }

    return String(cString: cString, encoding: .utf8)
}

func decodeJsonCString(cString: UnsafeMutablePointer<CChar>?) -> [String: Any]? {
    guard let string = decodeCString(cString: cString),
          let data = string.data(using: .utf8) else {
        return nil
    }

    return try? JSONSerialization.jsonObject(with: data) as? [String: Any]
}

func decodeJsonAttributes(fromCString attributeString: CString?) -> [String: Encodable] {
    var decodedAttributes: [String: Encodable] = [:]

    if let jsonAttributes = decodeJsonCString(cString: attributeString) {
        decodedAttributes = castJsonAttributesToSwift(jsonAttributes)
    }

    return decodedAttributes
}
