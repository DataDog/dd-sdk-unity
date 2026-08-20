"""
Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import pytest

from .semver import Version
from .edm4u_deps import ExternalDependencyVersions, read_external_dependency_versions


__development_xml__ = '''<dependencies>
  <androidPackages>
    <repositories>
      <repository>https://repo.maven.apache.org/maven2</repository>
      <repository>https://oss.sonatype.org/content/repositories/snapshots</repository>
    </repositories>
    <androidPackage spec="com.datadoghq:dd-sdk-android-rum:2.22.0">
    </androidPackage>
    <androidPackage spec="com.datadoghq:dd-sdk-android-logs:2.22.0">
    </androidPackage>
    <androidPackage spec="com.datadoghq:dd-sdk-android-ndk:2.22.0">
    </androidPackage>
  </androidPackages>
  <iosPods>
    <iosPod name="DatadogCore" bitcodeEnabled="false" minTargetSdk="12.0" version="2.28.1" />
    <iosPod name="DatadogLogs" bitcodeEnabled="false" minTargetSdk="12.0" version="2.28.1" />
    <iosPod name="DatadogRUM" bitcodeEnabled="false" minTargetSdk="12.0" version="2.28.1" />
    <iosPod name="DatadogCrashReporting" bitcodeEnabled="false" minTargetSdk="12.0" version="2.28.1" />
  </iosPods>
</dependencies>'''


def test_read_external_dependency_versions():
    got = read_external_dependency_versions(__development_xml__)
    assert got == ExternalDependencyVersions(
        dd_sdk_android=Version.parse('2.22.0'),
        dd_sdk_ios=Version.parse('2.28.1'),
    )


# DatadogDependencies.xml with no <iosPods> element at all, since the iOS pin now
# lives in IosDependencyVersion.json.
__android_only_xml__ = '''<dependencies>
  <androidPackages>
    <androidPackage spec="com.datadoghq:dd-sdk-android-rum:3.10.0">
    </androidPackage>
    <androidPackage spec="com.datadoghq:dd-sdk-android-logs:3.10.0">
    </androidPackage>
    <androidPackage spec="com.datadoghq:dd-sdk-android-ndk:3.10.0">
    </androidPackage>
  </androidPackages>
</dependencies>'''


def test_read_external_dependency_versions_android_only():
    got = read_external_dependency_versions(__android_only_xml__)
    assert got == ExternalDependencyVersions(
        dd_sdk_android=Version.parse('3.10.0'),
        dd_sdk_ios=None,
    )


# Matches a manifest that declares neither Android nor iOS Datadog dependencies (e.g. a
# hypothetical manifest with nothing left to configure).
__empty_xml__ = '<dependencies></dependencies>'


def test_read_external_dependency_versions_empty_manifest():
    got = read_external_dependency_versions(__empty_xml__)
    assert got == ExternalDependencyVersions(
        dd_sdk_android=None,
        dd_sdk_ios=None,
    )


# A corrupt manifest declaring two Datadog <androidPackage> specs with mismatched
# versions must still fail loudly rather than silently picking one.
__mismatched_android_versions_xml__ = '''<dependencies>
  <androidPackages>
    <androidPackage spec="com.datadoghq:dd-sdk-android-rum:3.10.0">
    </androidPackage>
    <androidPackage spec="com.datadoghq:dd-sdk-android-logs:3.11.0">
    </androidPackage>
  </androidPackages>
</dependencies>'''


def test_read_external_dependency_versions_mismatched_android_versions_raises():
    with pytest.raises(RuntimeError):
        read_external_dependency_versions(__mismatched_android_versions_xml__)
