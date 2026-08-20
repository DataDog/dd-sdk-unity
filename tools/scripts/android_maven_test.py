"""
Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.
"""
from android_maven import (
    check_transitive_drift,
    parse_direct_dependencies,
)


__namespaced_pom__ = '''<?xml version="1.0" encoding="UTF-8"?>
<project xmlns="http://maven.apache.org/POM/4.0.0">
  <modelVersion>4.0.0</modelVersion>
  <groupId>com.datadoghq</groupId>
  <artifactId>dd-sdk-android-rum</artifactId>
  <version>3.10.0</version>
  <dependencies>
    <dependency>
      <groupId>org.jetbrains.kotlin</groupId>
      <artifactId>kotlin-stdlib</artifactId>
      <version>2.0.21</version>
      <scope>runtime</scope>
    </dependency>
    <dependency>
      <groupId>com.squareup.okhttp3</groupId>
      <artifactId>okhttp</artifactId>
      <version>4.12.0</version>
      <scope>runtime</scope>
    </dependency>
    <dependency>
      <groupId>com.datadoghq</groupId>
      <artifactId>dd-sdk-android-core</artifactId>
    </dependency>
  </dependencies>
</project>
'''


__non_namespaced_pom__ = '''<?xml version="1.0" encoding="UTF-8"?>
<project>
  <modelVersion>4.0.0</modelVersion>
  <groupId>com.datadoghq</groupId>
  <artifactId>dd-sdk-android-logs</artifactId>
  <version>3.10.0</version>
  <dependencies>
    <dependency>
      <groupId>org.jetbrains.kotlin</groupId>
      <artifactId>kotlin-stdlib</artifactId>
      <version>2.0.21</version>
    </dependency>
  </dependencies>
</project>
'''


def test_parse_direct_dependencies_namespaced():
    got = parse_direct_dependencies(__namespaced_pom__)
    assert got == {
        'org.jetbrains.kotlin:kotlin-stdlib': '2.0.21',
        'com.squareup.okhttp3:okhttp': '4.12.0',
    }


def test_parse_direct_dependencies_non_namespaced():
    got = parse_direct_dependencies(__non_namespaced_pom__)
    assert got == {
        'org.jetbrains.kotlin:kotlin-stdlib': '2.0.21',
    }


def test_parse_direct_dependencies_omits_versionless():
    got = parse_direct_dependencies(__namespaced_pom__)
    assert 'com.datadoghq:dd-sdk-android-core' not in got


def test_check_transitive_drift_no_drift():
    observed = {
        'org.jetbrains.kotlin:kotlin-stdlib': '2.0.21',
        'com.squareup.okhttp3:okhttp': '4.12.0',
    }
    assert check_transitive_drift(observed) == []


def test_check_transitive_drift_reports_mismatch():
    observed = {
        'org.jetbrains.kotlin:kotlin-stdlib': '2.1.0',
        'com.squareup.okhttp3:okhttp': '4.12.0',
    }
    drift = check_transitive_drift(observed)
    assert len(drift) == 1
    assert 'org.jetbrains.kotlin:kotlin-stdlib' in drift[0]
    assert '2.0.21' in drift[0]
    assert '2.1.0' in drift[0]


def test_check_transitive_drift_ignores_unexpected_coordinates():
    observed = {
        'org.jetbrains.kotlin:kotlin-stdlib': '2.0.21',
        'com.squareup.okhttp3:okhttp': '4.12.0',
        'com.google.code.gson:gson': '2.10.1',
    }
    assert check_transitive_drift(observed) == []


def test_check_transitive_drift_reports_missing_coordinate():
    observed = {
        'org.jetbrains.kotlin:kotlin-stdlib': '2.0.21',
    }
    drift = check_transitive_drift(observed)
    assert len(drift) == 1
    assert 'com.squareup.okhttp3:okhttp' in drift[0]
    assert 'missing' in drift[0]
