"""
Utility code for modifying the DatadogSettings.asset file for any given Unity project.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import os
from dataclasses import dataclass
from contextlib import contextmanager
from typing import Optional

__dd_settings_asset_filename__ = 'DatadogSettings.asset'
__dd_settings_asset_relpath__ = os.path.join('Assets', 'Resources', __dd_settings_asset_filename__)


@dataclass
class DatadogRuntimeConfig:
    """
    Subset of DatadogSettings modified at build-time to configure how the Datadog SDK
    will behave at runtime in the packaged build.
    """
    enabled: Optional[bool] = None
    sdk_verbosity: Optional[int] = None
    client_token: Optional[str] = None
    env: Optional[str] = None
    service_name: Optional[str] = None
    custom_endpoint: Optional[str] = None
    batch_size: Optional[int] = None
    upload_frequency: Optional[int] = None
    batch_processing_level: Optional[int] = None
    crash_reporting_enabled: Optional[bool] = None
    forward_unity_logs: Optional[bool] = None
    remote_log_threshold: Optional[int] = None
    rum_enabled: Optional[bool] = None
    rum_application_id: Optional[str] = None
    automatic_scene_tracking: Optional[bool] = None
    session_sample_rate: Optional[int] = None
    trace_sample_rate: Optional[int] = None
    telemetry_sample_rate: Optional[int] = None


@contextmanager
def modified_datadog_settings(project_root: str, config: DatadogRuntimeConfig):
    # Read the original contents of DatadogSettings.asset
    path = os.path.join(project_root, __dd_settings_asset_relpath__)
    with open(path) as fp:
        old_text = fp.read()

    # Modify the file to contain our desired settings
    new_text = _modify_datadog_settings_impl(old_text, config)
    with open(path, 'w') as fp:
        fp.write(new_text)

    # Yield, then ensure that we revert to the original file contents
    try:
        yield
    finally:
        with open(path, 'w') as fp:
            fp.write(old_text)


def _modify_datadog_settings_impl(text: str, config: DatadogRuntimeConfig) -> str:
    lines = text.splitlines()
    to_modify = [
        ('Enabled', config.enabled),
        ('SdkVerbosity', config.sdk_verbosity),
        ('ClientToken', config.client_token),
        ('Env', config.env),
        ('ServiceName', config.service_name),
        ('CustomEndpoint', config.custom_endpoint),
        ('BatchSize', config.batch_size),
        ('UploadFrequency', config.upload_frequency),
        ('BatchProcessingLevel', config.batch_processing_level),
        ('CrashReportingEnabled', config.crash_reporting_enabled),
        ('ForwardUnityLogs', config.forward_unity_logs),
        ('RemoteLogThreshold', config.remote_log_threshold),
        ('RumEnabled', config.rum_enabled),
        ('RumApplicationId', config.rum_application_id),
        ('AutomaticSceneTracking', config.automatic_scene_tracking),
        ('SessionSampleRate', config.session_sample_rate),
        ('TraceSampleRate', config.trace_sample_rate),
        ('TelemetrySampleRate', config.telemetry_sample_rate),
    ]
    for key, value_or_none in to_modify:
        if value_or_none is None:
            continue
        value = value_or_none
        if isinstance(value, bool):
            value = int(value)
        prefix = f'  {key}:'
        i = next((i for i, s in enumerate(lines) if s.startswith(prefix)), -1)
        if i < 0:
            raise ValueError(f"Invalid {__dd_settings_asset_filename__} file: no existing line begins with {prefix}")
        lines[i] = f'{prefix} {value}'
    return '\n'.join(lines) + '\n'
