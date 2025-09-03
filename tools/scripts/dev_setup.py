"""
Setup project settings for development, including setting properties common for
integration test development.
"""
import os
import sys
import argparse
from common.ddconfig import DatadogRuntimeConfig, FirstPartyHost, __dd_settings_asset_relpath__
from common.inet_addr import get_reachable_inet_addr

__repo_root__ = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
__default_test_project_root__ = os.path.join(__repo_root__, 'samples', 'Datadog Sample')

def _apply_settings(for_integration_tests: bool, project_path: str):
    mock_host = ""
    mock_server_url = ""
    first_party_hosts = []
    env = "integration-test"

    if for_integration_tests:
        # We need our mock server to bind to an address that is reachable from a simulator
        # or external device on our network
        mock_server_port = 5000
        mock_server_bind_addr = get_reachable_inet_addr()
        if not mock_server_bind_addr:
            raise RuntimeError('Failed to resolve a private IP for mock server')
        mock_host = f'{mock_server_bind_addr}:{mock_server_port}'
        mock_server_url = f'http://{mock_host}'
        first_party_hosts = [
            # 18 == TracingHeaderType.Datadog | TracingHeaderType.TraceContext
            FirstPartyHost(host=mock_host, tracing_header_type=18)
        ]


    config = DatadogRuntimeConfig(
        enabled=True,
        sdk_verbosity=1,
        client_token='fake-client-token',
        env=env,
        service_name='datadog-sample',
        custom_endpoint=mock_server_url,
        batch_size=1,
        upload_frequency=1,
        batch_processing_level=1,
        crash_reporting_enabled=True,
        forward_unity_logs=True,
        remote_log_threshold=3,
        rum_enabled=True,
        rum_application_id='fake-rum-application-id',
        automatic_scene_tracking=True,
        session_sample_rate=100,
        trace_sample_rate=100,
        telemetry_sample_rate=100,
        first_party_hosts=first_party_hosts
    )
    config_path = os.path.join(project_path, __dd_settings_asset_relpath__)
    config.apply_to(config_path)

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='')
    parser.add_argument('--for-integration-tests', '-i', action='store_true', default=False, help="Change properties specifically for development of integration tests.")
    parser.add_argument('--project', '-p', default=__default_test_project_root__, help="Path to the root directory of the Unity project to load; defaults to 'samples/Demo Data' in this repo.")
    args = parser.parse_args()

    _apply_settings(args.for_integration_tests, args.project)
