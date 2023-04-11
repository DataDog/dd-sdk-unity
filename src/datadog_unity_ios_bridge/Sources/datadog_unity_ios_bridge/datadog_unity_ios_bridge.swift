import Datadog

@_cdecl("DatadogBridge_Init")
public func datadogBridge_Init() {
    let ddConfigBuilder = Datadog.Configuration.builderUsing(
        rumApplicationID: "",
        clientToken: "",
        environment: "prod"
    )

    Datadog.initialize(
        appContext: Datadog.AppContext(),
        trackingConsent: .granted,
        configuration: ddConfigBuilder.build()
    )
}
