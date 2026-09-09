"""
Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import pytest

import io

from .semver import Version
from .edm4u_deps import ExternalDependencyVersions, _write_external_dependency_versions_impl, read_external_dependency_versions


__development_xml__ = '''<dependencies>
  <androidPackages>
    <repositories>
      <repository>https://repo.maven.apache.org/maven2</repository>
      <repository>https://oss.sonatype.org/content/repositories/snapshots</repository>
    </repositories>
    <androidPackage spec="com.datadoghq:dd-sdk-android-rum:2+">
    </androidPackage>
    <androidPackage spec="com.datadoghq:dd-sdk-android-logs:2+">
    </androidPackage>
    <androidPackage spec="com.datadoghq:dd-sdk-android-ndk:2+">
    </androidPackage>
  </androidPackages>
  <iosPods>
    <iosPod name="DatadogCore" bitcodeEnabled="false" minTargetSdk="12.0" version="2.27.0" />
    <iosPod name="DatadogLogs" bitcodeEnabled="false" minTargetSdk="12.0" version="2.27.0" />
    <iosPod name="DatadogRUM" bitcodeEnabled="false" minTargetSdk="12.0" version="2.27.0" />
    <iosPod name="DatadogCrashReporting" bitcodeEnabled="false" minTargetSdk="12.0" version="2.27.0" />
  </iosPods>
</dependencies>'''


__versions__ = ExternalDependencyVersions(
    dd_sdk_android=Version(major=2, minor=22, patch=0),
    dd_sdk_ios=Version(major=2, minor=28, patch=1),
)


__release_xml__ = '''<dependencies>
  <androidPackages>
    <repositories>
      <repository>https://repo.maven.apache.org/maven2</repository>
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


def test_write_external_dependency_versions_impl():
    infile = io.BytesIO(__development_xml__.encode())
    outfile = io.BytesIO()
    _write_external_dependency_versions_impl(infile, outfile, __versions__)
    outfile.seek(0)
    assert outfile.read().decode() == __release_xml__


def test_read_external_dependency_versions():
    got = read_external_dependency_versions(__release_xml__)
    assert got == ExternalDependencyVersions(
        dd_sdk_android=Version.parse('2.22.0'),
        dd_sdk_ios=Version.parse('2.28.1'),
    )


# Matches the current (post-Phase-2) shape of DatadogDependencies.xml: no <iosPods>
# element at all, since the iOS pin now lives in IosDependencyVersion.json.
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


__android_only_versions__ = ExternalDependencyVersions(
    dd_sdk_android=Version(major=3, minor=12, patch=0),
    dd_sdk_ios=None,
)


def test_read_external_dependency_versions_android_only():
    got = read_external_dependency_versions(__android_only_xml__)
    assert got == ExternalDependencyVersions(
        dd_sdk_android=Version.parse('3.10.0'),
        dd_sdk_ios=None,
    )


def test_write_external_dependency_versions_impl_android_only():
    infile = io.BytesIO(__android_only_xml__.encode())
    outfile = io.BytesIO()
    _write_external_dependency_versions_impl(infile, outfile, __android_only_versions__)
    outfile.seek(0)
    written = outfile.read().decode()
    assert 'iosPod' not in written
    assert 'dd-sdk-android-rum:3.12.0' in written
    assert 'dd-sdk-android-logs:3.12.0' in written
    assert 'dd-sdk-android-ndk:3.12.0' in written
