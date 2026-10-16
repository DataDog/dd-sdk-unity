# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2019-2020 Datadog, Inc.

import argparse
import datetime
import json
import os
from dataclasses import asdict, dataclass, is_dataclass
from hashlib import sha1
from socketserver import TCPServer
from typing import Any, TypedDict, cast
from urllib.parse import urlparse

import flask
from flask import Flask, Request, redirect, render_template, request, url_for
from flask_cors import CORS
from werkzeug import serving as _serving
from werkzeug.wrappers import Response

from dd_sdk_unity.mock_server.schemas.raw import RAWSchema
from dd_sdk_unity.mock_server.schemas.rum import RUMSchema
from dd_sdk_unity.mock_server.schemas.schema import Schema
from dd_sdk_unity.mock_server.schemas.session_replay import SRSchema
from dd_sdk_unity.mock_server.schemas.update import schemas_path_exists, update_schemas
from dd_sdk_unity.mock_server.server_address import (
    get_best_server_address,
)

app = Flask(__name__)
CORS(app)


@app.route('/_healthcheck')
def healthcheck() -> tuple[str, int]:
    # Keep readiness probes out of the request history inspected by tests.
    return '', 200

configured_responses = {}  # path -> { status, body, content_type }

@dataclass()
class GenericRequest:
    method: str
    path: str
    query_string: str | None
    date: datetime.datetime
    content_type: str | None
    content_length: int | None
    data_as_text: str
    schemas: list[Schema]

    def __init__(self, r: Request) -> None:
        self.method = r.method
        self.path = r.path
        self.query_string = f'?{r.query_string.decode("utf-8")}' if r.query_string else ''
        if 'ddforward' in r.args:
            # The path and query string is actually what's here in ddforward
            parsed_forward = urlparse(r.args['ddforward'])
            self.path = parsed_forward.path
            self.query_string = f'?{parsed_forward.query}' if parsed_forward.query else None

        self.date = datetime.datetime.now()
        self.content_type = r.content_type
        self.content_length = r.content_length
        self.data_as_text = r.get_data(as_text=True)
        self.schemas = schemas_for_request(r)

    def follow_url(self, schema: Schema) -> str:
        return url_for(
            'inspect_request',
            schema_name=schema.name,
            endpoint_hash=self.endpoint_hash(),
            request_hash=self.hash()
        )

    def hash(self) -> str:
        meta = f'{self.method} {self.path} {self.date}'.encode()
        body = self.data_as_text.encode('utf-8')
        return sha1(meta + body).hexdigest()

    def endpoint_hash(self) -> str:
        return sha1(f'{self.method} {self.path}'.encode()).hexdigest()

    def schema_with_name(self, name: str) -> Schema | None:
        return next((s for s in self.schemas if s.name == name), None)


@dataclass()
class GenericEndpoint:
    method: str
    path: str
    requests: list[GenericRequest]  # all requests sent to this endpoint
    schemas: list[Schema]

    def name(self) -> str:
        return f'{self.method} {self.path}'

    def requests_count(self) -> int:
        return len(self.requests)

    def bytes_received(self) -> int:
        return sum(r.content_length or 0 for r in self.requests)

    def follow_url(self, schema: Schema) -> str:
        return url_for('inspect_endpoint', schema_name=schema.name, endpoint_hash=self.hash())

    def hash(self) -> str:
        return sha1(f'{self.method} {self.path}'.encode()).hexdigest()

    def schema_with_name(self, name: str) -> Schema | None:
        return next((s for s in self.schemas if s.name == name), None)


class _EndpointRequests(TypedDict):
    endpoint: str
    requests: list[GenericRequest]


def schemas_for_request(r: Request) -> list[Schema]:
    schemas: list[Schema] = []

    if RAWSchema.matches(r.method, r.path):
        schemas.append(RAWSchema(request=r))

    if RUMSchema.matches(r.method, r.path):
        schemas.append(RUMSchema(request=r))

    if SRSchema.matches(r.method, r.path):
        schemas.append(SRSchema(request=r))

    return schemas

def add_cors_headers(r: flask.Response) -> None:
    r.headers['access-control-allow-origin'] = '*'
    r.headers['access-control-allow-headers'] = '*'
    r.headers['access-control-allow-methods'] = 'GET, POST'

endpoints: list[GenericEndpoint] = []

class DataClassJsonEncoder(json.JSONEncoder):
    def default(self, o: Any) -> Any:
        if is_dataclass(o):
            return asdict(o)
        as_json_method = getattr(o, "as_json", None)
        if callable(as_json_method):
            return o.as_json()
        if isinstance(o, datetime.datetime):
            return str(o)
        return super().default(o)

@app.route('/configure_response', methods=['POST'])
def configure_response() -> flask.Response:
    """
    POST /configure_response

    Store a per-path response override so that subsequent POST requests to
    that path return the configured status/body instead of the default 202.
    """
    data = request.get_json()
    configured_responses[data['path']] = data
    resp = flask.Response('OK', status=200)
    add_cors_headers(resp)
    return resp


@app.route('/', methods=['POST'])
@app.route('/<path:rest>', methods=['POST'])
def generic_post(rest: str = '') -> flask.Response:
    """
    POST /*

    Record generic (any) POST request sent to `/**/*`
    """

    gr = GenericRequest(r=request)

    # Record the request regardless of whether we return a configured response
    if existing := next((e for e in endpoints if e.hash() == gr.endpoint_hash()), None):
        existing.requests.append(gr)
    else:
        endpoints.append(
            GenericEndpoint(
                method=gr.method,
                path=gr.path,
                requests=[gr],
                schemas=gr.schemas
            )
        )

    # Return a configured response if one was registered for this path
    if gr.path in configured_responses:
        cfg = configured_responses[gr.path]
        resp = flask.Response(cfg['body'], status=cfg.get('status', 200))
        resp.headers['Content-Type'] = cfg.get('content_type', 'application/json')
        add_cors_headers(resp)
        return resp

    resp = flask.Response('OK - request recorded\n', status=202)
    add_cors_headers(resp)
    return resp

@app.route('/', methods=['GET'])
@app.route('/<path:rest>', methods=['GET'])
def generic_get(rest: str = '') -> flask.Response:
    """
    GET /*

    Record generic (any) GET request sent to `/**/*`
    """

    gr = GenericRequest(r=request)

    if existing := next((e for e in endpoints if e.hash() == gr.endpoint_hash()), None):
        existing.requests.append(gr)
        response_text = 'OK - request recorded to known endpoint\n'
    else:
        endpoints.append(
            GenericEndpoint(
                method=gr.method,
                path=gr.path,
                requests=[gr],
                schemas=gr.schemas
            )
        )
        response_text = 'OK - request recorded to new endpoint\n'

    resp = flask.Response(response_text, status=202)
    add_cors_headers(resp)
    return resp


@app.route('/inspect_requests/')
def inspect_json() -> flask.Response:
    """
    GET /inspect_requests

    Browse recorded requests serialized as JSON
    """
    endpoint_requests: list[_EndpointRequests] = [ { "endpoint": e.path, "requests": e.requests } for e in endpoints ]
    # Remove non-raw schemas
    for endpoint_request in endpoint_requests:
        for recorded_request in endpoint_request["requests"]:
            recorded_request.schemas = [s for s in recorded_request.schemas if isinstance(s, RAWSchema)]

    resp = flask.Response(json.dumps(endpoint_requests, cls=DataClassJsonEncoder))
    resp.headers['Content-Type'] = 'application/json'
    add_cors_headers(resp)
    return resp

@app.route('/inspect/')
def inspect() -> str:
    """
    GET /inspect

    Browse recorded requests.
    """
    return render_template('endpoints.html', title='Endpoints', endpoints=endpoints)

@app.route('/reset')
def reset() -> flask.Response:
    """
    GET /reset

    Clear currently logged requests on all endpoints
    """
    endpoints.clear()
    configured_responses.clear()
    resp = flask.Response('OK', status=200)
    add_cors_headers(resp)
    return resp


@app.route('/inspect/<schema_name>/<endpoint_hash>')
def inspect_endpoint(schema_name: str, endpoint_hash: str) -> str | Response:

    if endp := next((e for e in endpoints if e.hash() == endpoint_hash), None):
        if schm := endp.schema_with_name(name=schema_name):
            return render_template(
                'endpoint.html',
                back_url=url_for('inspect'),
                endpoint=endp,
                selected_schema=schm
            )
        else:
            print(f'⚠️ Endpoint has no schema named {schema_name}')
            return redirect(url_for('inspect'))
    else:
        print(f'⚠️ Could not find endpoint with hash {endpoint_hash}')
        return redirect(url_for('inspect'))


@app.route('/inspect/<schema_name>/<endpoint_hash>/<request_hash>')
def inspect_request(schema_name: str, endpoint_hash: str, request_hash: str) -> str | Response:

    if endp := next((e for e in endpoints if e.hash() == endpoint_hash), None):
        if req := next((r for r in endp.requests if r.hash() == request_hash), None):
            if schm := req.schema_with_name(name=schema_name):
                return render_template(
                    'request.html',
                    back_url=url_for('inspect'),
                    endpoint=endp,
                    request=req,
                    selected_schema=schm
                )
            else:
                print(f'⚠️ Could not find request with hash {request_hash}')
                return redirect(url_for('inspect'))
        else:
            print(f'⚠️ Endpoint has no schema named {schema_name}')
            return redirect(url_for('inspect'))
    else:
        print(f'⚠️ Could not find endpoint with hash {endpoint_hash}')
        return redirect(url_for('inspect'))

class _MockServer(_serving.ThreadedWSGIServer):
    def server_bind(self) -> None:
        # HTTPServer performs reverse DNS here, which can stall before listen() in CI.
        # Test URLs already use explicit addresses, so no hostname lookup is needed.
        TCPServer.server_bind(self)
        self.server_name, self.server_port = cast(tuple[str, int], self.server_address[:2])


def run(preferred_address: str, port: int) -> None:
    if not preferred_address:
        preferred_address = get_best_server_address().ip

    # Keep Flask's debugger/reloader while bypassing reverse DNS for every startup.
    original_server = _serving.ThreadedWSGIServer
    # Replacing Werkzeug's class symbol with our subclass is intentional.
    _serving.ThreadedWSGIServer = _MockServer  # ty: ignore[invalid-assignment]
    try:
        app.run(debug=True, host=preferred_address, port=port)
    finally:
        _serving.ThreadedWSGIServer = original_server

def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--prefer-localhost", action='store_true')
    parser.add_argument("--update-schemas", action='store_true')
    parser.add_argument("--addr", type=str)
    parser.add_argument("--port", type=int, default=5000)

    args = parser.parse_args()
    os.chdir(os.path.dirname(os.path.abspath(__file__)))
    if args.update_schemas:
        update_schemas()
        return 0

    if not schemas_path_exists():
        update_schemas()

    if args.addr and args.prefer_localhost:
        raise ValueError('--addr and --prefer-localhost are mutually exclusive')

    preferred_address = args.addr or ''
    if args.prefer_localhost:
        preferred_address = '127.0.0.1'

    run(preferred_address, args.port)
    return 0
