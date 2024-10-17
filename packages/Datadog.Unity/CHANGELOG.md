# Change Log

## Unreleased

* Added an option for detecting non-fatal ANRs on Android.
* Added an option for detecting non-fatal app hangs within a given threshold on iOS.
* Added documentation tooltips to the Datadog Options window (on Labels).
* Added support for file / line mappings in C# exceptions through C# Native Stack Mapping
* Make DatadogWorker's message pools thread safe.
* Stop Resources in DatadogTrackedWebRequest if the underlying UnityWebRequest is Disposed.
* Updated iOS SDK to 2.17.0
  * Memory warnings are now tracked as RUM errors
  * Fix refresh rate vital for variable refresh rate displays
* Updated Android SDK to 2.14.0
  * Increase retry delay on DNS error.
  * Stop upload worker on upload failure
  * Ensure `UploadWorker` uses the SDK instance name.

## 1.2.0

* Add and option to control when trace context headers are injected into web requests (Trace Context Injection).
* Add support for global log attributes, which adds attributes to logs sent from all loggers.
* Add the ability to customize your Service Name from the Datadog Options dialog.
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
