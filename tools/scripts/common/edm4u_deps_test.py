import pytest

import io

from .types import ExternalDependencyVersions
from .edm4u_deps import _write_external_dependency_versions_impl


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
    dd_sdk_android='2.22.0',
    dd_sdk_ios='2.28.1',
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
