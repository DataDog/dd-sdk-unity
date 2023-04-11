// swift-tools-version: 5.7
// The swift-tools-version declares the minimum version of Swift required to build this package.

import PackageDescription

let package = Package(
    name: "datadog_unity_ios_bridge",
    products: [
        // Products define the executables and libraries a package produces, and make them visible to other packages.
        .library(
            name: "datadog_unity_ios_bridge",
            targets: ["datadog_unity_ios_bridge"]),
    ],
    dependencies: [
        // Dependencies declare other packages that this package depends on.
        .package(path: "../../modules/dd-sdk-ios"),
    ],
    targets: [
        .target(
            name: "datadog_unity_ios_bridge",
            dependencies: [
                .product(name: "Datadog", package: "dd-sdk-ios")
            ]),
        .testTarget(
            name: "datadog_unity_ios_bridgeTests",
            dependencies: ["datadog_unity_ios_bridge"]),
    ]
)
