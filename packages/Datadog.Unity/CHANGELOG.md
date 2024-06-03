# Change Log

## Unreleased

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
