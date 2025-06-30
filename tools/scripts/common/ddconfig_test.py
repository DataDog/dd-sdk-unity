import pytest

from .ddconfig import DatadogRuntimeConfig, _modify_datadog_settings_impl

__old_settings__ = '''%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 0}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: 31528199819cf497cbec19aabbb133a1, type: 3}
  m_Name: DatadogSettings
  m_EditorClassIdentifier: 
  Enabled: 1
  SdkVerbosity: 0
  OutputSymbols: 1
  ClientToken: 
  Site: 0
  Env: 
  CustomEndpoint: 
  BatchSize: 0
  UploadFrequency: 0
  BatchProcessingLevel: 1
  CrashReportingEnabled: 1
  ForwardUnityLogs: 1
  RemoteLogThreshold: 3
  RumEnabled: 1
  RumApplicationId: 
  AutomaticSceneTracking: 1
  VitalsUpdateFrequency: 2
  SessionSampleRate: 100
  TraceSampleRate: 100
  TraceContextInjection: 0
  FirstPartyHosts:
  - Host: shopist.io
    TracingHeaderType: 18
  TelemetrySampleRate: 20
'''

__config__ = DatadogRuntimeConfig(
    client_token='my-very-special-client-token',
    rum_application_id='my-cool-rum-application',
)

__new_settings__ = '''%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 0}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: 31528199819cf497cbec19aabbb133a1, type: 3}
  m_Name: DatadogSettings
  m_EditorClassIdentifier: 
  Enabled: 1
  SdkVerbosity: 0
  OutputSymbols: 1
  ClientToken: my-very-special-client-token
  Site: 0
  Env: 
  CustomEndpoint: 
  BatchSize: 0
  UploadFrequency: 0
  BatchProcessingLevel: 1
  CrashReportingEnabled: 1
  ForwardUnityLogs: 1
  RemoteLogThreshold: 3
  RumEnabled: 1
  RumApplicationId: my-cool-rum-application
  AutomaticSceneTracking: 1
  VitalsUpdateFrequency: 2
  SessionSampleRate: 100
  TraceSampleRate: 100
  TraceContextInjection: 0
  FirstPartyHosts:
  - Host: shopist.io
    TracingHeaderType: 18
  TelemetrySampleRate: 20
'''


def test_modify_datadog_settings_impl():
    got = _modify_datadog_settings_impl(__old_settings__, __config__)
    assert got == __new_settings__

    with pytest.raises(ValueError):
        _modify_datadog_settings_impl('not-a-valid-asset', __config__)
