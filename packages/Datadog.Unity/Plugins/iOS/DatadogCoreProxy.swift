// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

import Foundation
import DatadogInternal
import DatadogCore

/// A `DatadogCoreProtocol` which proxies all calls to the real `DatadogCore` implementation. It intercepts
/// all events written to the actual core and provides APIs to read their values back for tests.
///
/// Usage example:
///
///     ```
///     let core = DatadogCoreProxy(context: .mockWith(service: "foo-bar"))
///     defer { core.flushAndTearDown() }
///     core.register(feature: LoggingFeature.mockAny())
///
///     let logger = Logger.builder.build(in: core)
///     logger.debug("message")
///
///     let events = core.waitAndReturnEvents(of: LoggingFeature.self, ofType: LogEvent.self)
///     XCTAssertEqual(events[0].serviceName, "foo-bar")
///     ```
///
internal class DatadogCoreProxy: DatadogCoreProtocol {
/// The SDK core managed by this proxy.
    private let core: DatadogCoreProtocol

    private var featureScopeInterceptors: [String: FeatureScopeInterceptor] = [:]

    init(core: DatadogCoreProtocol) {
        self.core = core
    }

    func register<T>(feature: T) throws where T: DatadogFeature {
        featureScopeInterceptors[T.name] = FeatureScopeInterceptor()
        try core.register(feature: feature)
    }

    func get<T>(feature type: T.Type) -> T? where T: DatadogFeature {
        return core.get(feature: type)
    }

    func scope(for feature: String) -> FeatureScope? {
        return core.scope(for: feature).map { scope in
            FeatureScopeProxy(proxy: scope, interceptor: featureScopeInterceptors[feature]!)
        }
    }

    func set(baggage: @escaping () -> FeatureBaggage?, forKey key: String) {
        core.set(baggage: baggage, forKey: key)
    }

    func send(message: FeatureMessage, else fallback: @escaping () -> Void) {
        core.send(message: message, else: fallback)
    }
}

private struct FeatureScopeProxy: FeatureScope {
    let proxy: FeatureScope
    let interceptor: FeatureScopeInterceptor

    func eventWriteContext(bypassConsent: Bool, forceNewBatch: Bool, _ block: @escaping (DatadogContext, Writer) -> Void) {
        interceptor.enter()
        proxy.eventWriteContext(bypassConsent: bypassConsent, forceNewBatch: forceNewBatch) { context, writer in
            block(context, interceptor.intercept(writer: writer))
            interceptor.leave()
        }
    }

    func context(_ block: @escaping (DatadogContext) -> Void) {
        interceptor.enter()
        proxy.context { context in
            block(context)
            interceptor.leave()
        }
    }
}

private class FeatureScopeInterceptor {
    struct InterceptingWriter: Writer {
        static let jsonEncoder = JSONEncoder.dd.default()

        let group: DispatchGroup
        let actualWriter: Writer
        unowned var interception: FeatureScopeInterceptor?

        func write<T: Encodable, M: Encodable>(value: T, metadata: M) {
            group.enter()
            defer { group.leave() }

            actualWriter.write(value: value, metadata: metadata)

            let event = value
            let data = try! InterceptingWriter.jsonEncoder.encode(value)
            interception?.events.append((event, data))
        }
    }

    func intercept(writer: Writer) -> Writer {
        return InterceptingWriter(group: group, actualWriter: writer, interception: self)
    }

    // MARK: - Synchronizing and awaiting events:

    @ReadWriteLock
    private var events: [(event: Any, data: Data)] = []

    private let group = DispatchGroup()

    func enter() { group.enter() }
    func leave() { group.leave() }

    func waitAndReturnEvents(timeout: DispatchTime) -> [(event: Any, data: Data)] {
        _ = group.wait(timeout: timeout)
        return events
    }
}

extension DatadogCoreProxy {
    /// Returns all events of given type for certain Feature.
    /// - Parameters:
    ///   - name: The Feature to retrieve events from
    ///   - type: The type of events to filter out
    /// - Returns: A list of events.
    func waitAndReturnEvents<T>(ofFeature name: String, ofType type: T.Type, timeout: DispatchTime = .distantFuture) -> [T] where T: Encodable {
        guard let interceptor = self.featureScopeInterceptors[name] else {
             return []
        }
        return interceptor.waitAndReturnEvents(timeout: timeout).compactMap { $0.event as? T }
    }

    /// Returns serialized events of given Feature.
    ///
    /// - Parameter feature: The Feature to retrieve events from
    /// - Returns: A list of serialized events.
    func waitAndReturnEventsData(ofFeature name: String, timeout: DispatchTime = .distantFuture) -> [String] {
        guard let interceptor = self.featureScopeInterceptors[name] else {
            return []
        }
        return interceptor.waitAndReturnEvents(timeout: timeout).map {
            String(decoding: $0.data, as: UTF8.self)
        }
    }
}
