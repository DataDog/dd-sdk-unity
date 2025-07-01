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
    custom_endpoint: Optional[str]
    client_token: str
    rum_application_id: str


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
        ('ClientToken', config.client_token),
        ('RumApplicationId', config.rum_application_id),
    ]
    if config.custom_endpoint is not None:
        to_modify.append(('CustomEndpoint', config.custom_endpoint))

    for key, value in to_modify:
        prefix = f'  {key}:'
        i = next((i for i, s in enumerate(lines) if s.startswith(prefix)), -1)
        if i < 0:
            raise ValueError(f"Invalid {__dd_settings_asset_filename__} file: no existing line begins with {prefix}")
        lines[i] = f'{prefix} {value}'
    return '\n'.join(lines) + '\n'
