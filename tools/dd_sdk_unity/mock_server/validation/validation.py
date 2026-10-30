# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2019-2020 Datadog, Inc.

import json
import os
from collections.abc import Mapping
from typing import Any

from jsonschema import Draft7Validator, RefResolver, ValidationError


class JSONSchemaValidationResult:
    schema_name: str
    all_ok: bool
    error: str | None  # `None` if `all_ok`

    def __init__(self, schema_path: str, all_ok: bool, error: str | None) -> None:
        self.schema_name = os.path.basename(schema_path)
        self.all_ok = all_ok
        self.error = error


def patch_ajv_uri(uri: str) -> Any:
    """
    For patching $ref and $ids after AJV-required
    change introduced in https://github.com/DataDog/rum-events-format/pull/88
    """
    patches = {
        # patching RUM schema:
        '/rum/rum/': '/rum/',
        '/telemetry/telemetry/': '/telemetry/',
        # patching SR mobile schema:
        '/session-replay/mobile/session-replay/mobile/': '/session-replay/mobile/',
        '/session-replay/mobile/session-replay/common/': '/session-replay/common/',
        # patching SR browser schema:
        '/session-replay/browser/session-replay/browser/': '/session-replay/browser/',
        '/session-replay/browser/session-replay/common/': '/session-replay/common/',
        # patching SR mobile & browser:
        '/session-replay/common/session-replay/common/': '/session-replay/common/',
    }

    file_url: str = uri[7:]

    hit = True
    while hit:
        hit = False
        for pattern, fix in patches.items():
            if pattern in file_url:
                file_url = file_url.replace(pattern, fix)
                hit = True

    with open(file_url, 'r') as schema_file:
        return json.load(schema_file)


def validate_event(event: Mapping[str, object], schema_path: str) -> JSONSchemaValidationResult:
    try:
        with open(schema_path, 'r') as schema_file:
            schema = json.load(schema_file)
        base_path = os.path.abspath(os.path.dirname(schema_path))
        resolver = RefResolver(base_uri='file://' + base_path + '/', referrer=schema, handlers={'file': patch_ajv_uri})
        Draft7Validator(schema, resolver=resolver).validate(event)
        return JSONSchemaValidationResult(schema_path=schema_path, all_ok=True, error=None)
    except ValidationError as error:
        return JSONSchemaValidationResult(schema_path=schema_path, all_ok=False, error=pretty_error_message(error))
    except Exception as error:  # noqa: BLE001 -- Display all schema loading/resolution failures in the UI.
        return JSONSchemaValidationResult(schema_path=schema_path, all_ok=False, error=f'{error}')


def pretty_error_message(error: ValidationError) -> str:
    return f'{error.message} ({" → ".join(str(part) for part in error.schema_path)})'
