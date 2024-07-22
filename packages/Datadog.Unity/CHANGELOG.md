# Change Log

## Unreleased

* Add and option to control when trace context headers are injected into web requests (Trace Context Injection).
* Add support for global log attributes, which adds attributes to logs sent from all loggers.
* Update iOS SDK to 2.14.1
  * Add support for Watchdog Terminations tracking in RUM
  * Use #fileID over #filePath as default argument in errors.
  * Fix compilation error in Xcode 16.
  * Update IPHONEOS_DEPLOYMENT_TARGET to 12.0
  * Add support for Fatal App Hangs tracking in RUM
* Update Android SDK to 2.11.0
  * Optimise `BatchFileOrchestator` performance.
  * Use custom naming for threads created inside SDK.
  * Start sending batches immediately after feature is initialized.
  * Add status code in user-facing message in case of UnknownError during batch upload.

## 1.1.3

* Fix `setVerbosity` on Android.

## 1.1.2

* Fix Datadog Site support in iOS.

## 1.1.1

* Allow configuration of Datadog SDK verbosity.
* Isolate Datadog SDKs to Unity framework to prevent certain build errors.

## 1.1.0

* Remove precompiled frameworks in favor of Cocoapod resolution with EDM4U
* Update iOS SDK to 2.9.0
  * Track App Hangs as RUM errors.
* Update Android SDK to 2.9.0

## 1.0.4

* Fix SDK version reporting to Datadog.
* Update Android SDK to 2.6.2
  * Fix a crash when trying to get the frame rate vitals

## 1.0.3

* Update Android to 2.6.1

## 1.0.2

* Update Android to 2.6.0

## 1.0.1

* Update iOS to 2.7.1

## 1.0.0

* Initial release
* Log support with multiple loggers and log interception
* Manual view tracking with `StartView` / `StopView`
* Automatic scene tracking using `SceneManager`
* Automatic resource tracking using `DatadogTrackedWebRequest` wrapper
