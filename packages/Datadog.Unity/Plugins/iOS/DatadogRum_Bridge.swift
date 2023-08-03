// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

import Foundation
import Datadog

@_cdecl("DatadogRum_StartView")
func DatadogRum_StartView(key: UnsafeMutablePointer<CChar>?, name: UnsafeMutablePointer<CChar>?, attributes: UnsafeMutablePointer<CChar>?) {
    if let key = decodeCString(cString: key),
       let jsonAttributes = decodeJsonCString(cString: attributes) {
        let decodedAttributes = castJsonAttributesToSwift(jsonAttributes)
        let name = decodeCString(cString: name)

        Global.rum.startView(key: key, name: name, attributes: decodedAttributes)
    }
}

@_cdecl("DatadogRum_StopView")
func DatadogRum_StopView(key: UnsafeMutablePointer<CChar>?, attributes: UnsafeMutablePointer<CChar>?) {
    if let key = decodeCString(cString: key),
       let jsonAttributes = decodeJsonCString(cString: attributes) {
        let decodedAttributes = castJsonAttributesToSwift(jsonAttributes)

        Global.rum.stopView(key: key, attributes: decodedAttributes)
    }
}


