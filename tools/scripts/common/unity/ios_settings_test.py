"""
Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import pytest

from .ios_settings import TargetIosSdk, TargetIosSimulatorArchitecture, _modify_project_settings_impl

__project_settings_device__ = '''%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!129 &1
PlayerSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 26
  androidMaxAspectRatio: 2.1
  applicationIdentifier:
    Android: com.datadoghq.unity.example
    Standalone: com.DefaultCompany.Datadog-Sample
    iPhone: com.DefaultCompany.Datadog-Sample
  CreateWallpaper: 0
  APKExpansionFiles: 0
  keepLoadedShadersAlive: 0
  StripUnusedMeshComponents: 0
  strictShaderVariantMatching: 0
  VertexChannelCompressionMask: 4054
  iPhoneSdkVersion: 988
  iOSSimulatorArchitecture: 0
  iOSTargetOSVersionString: 12.0
  tvOSSdkVersion: 0
'''

__project_settings_simulator_x86_64__ = '''%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!129 &1
PlayerSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 26
  androidMaxAspectRatio: 2.1
  applicationIdentifier:
    Android: com.datadoghq.unity.example
    Standalone: com.DefaultCompany.Datadog-Sample
    iPhone: com.DefaultCompany.Datadog-Sample
  CreateWallpaper: 0
  APKExpansionFiles: 0
  keepLoadedShadersAlive: 0
  StripUnusedMeshComponents: 0
  strictShaderVariantMatching: 0
  VertexChannelCompressionMask: 4054
  iPhoneSdkVersion: 989
  iOSSimulatorArchitecture: 0
  iOSTargetOSVersionString: 12.0
  tvOSSdkVersion: 0
'''

__project_settings_simulator_arm64__ = '''%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!129 &1
PlayerSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 26
  androidMaxAspectRatio: 2.1
  applicationIdentifier:
    Android: com.datadoghq.unity.example
    Standalone: com.DefaultCompany.Datadog-Sample
    iPhone: com.DefaultCompany.Datadog-Sample
  CreateWallpaper: 0
  APKExpansionFiles: 0
  keepLoadedShadersAlive: 0
  StripUnusedMeshComponents: 0
  strictShaderVariantMatching: 0
  VertexChannelCompressionMask: 4054
  iPhoneSdkVersion: 989
  iOSSimulatorArchitecture: 1
  iOSTargetOSVersionString: 12.0
  tvOSSdkVersion: 0
'''


def test_modify_project_settings_impl():
    got = _modify_project_settings_impl(__project_settings_device__, TargetIosSdk.DEVICE, architecture=None)
    assert got == __project_settings_device__

    got = _modify_project_settings_impl(__project_settings_device__, TargetIosSdk.SIMULATOR, architecture=TargetIosSimulatorArchitecture.ARM64)
    assert got == __project_settings_simulator_arm64__

    got = _modify_project_settings_impl(__project_settings_simulator_arm64__, TargetIosSdk.SIMULATOR, architecture=TargetIosSimulatorArchitecture.X86_64)
    assert got == __project_settings_simulator_x86_64__

    got = _modify_project_settings_impl(__project_settings_simulator_arm64__, TargetIosSdk.DEVICE, None)
    assert got == __project_settings_device__.replace('iOSSimulatorArchitecture: 0', 'iOSSimulatorArchitecture: 1')

    with pytest.raises(ValueError):
        _modify_project_settings_impl('not-a-valid-asset', TargetIosSdk.SIMULATOR, None)
