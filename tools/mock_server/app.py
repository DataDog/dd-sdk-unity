#!/usr/bin/python3

# -----------------------------------------------------------
# Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
# This product includes software developed at Datadog (https://www.datadoghq.com/).
# Copyright 2019-2020 Datadog, Inc.
# -----------------------------------------------------------

import argparse
import os
import json

import datetime
from hashlib import sha1
import sys
from typing import Optional
from dataclasses import dataclass, is_dataclass, asdict
from flask import Flask, request, Request, render_template, url_for, redirect
import flask
from schema_update import schemas_path_exists, update_schemas
from schemas.schema import Schema
from schemas.raw import RAWSchema
from schemas.rum import RUMSchema
from schemas.session_replay import SRSchema
from server_address import get_best_server_address, get_localhost
from templates.components.card import Card, CardTab
from urllib.parse import urlparse

app = Flask(__name__)

@dataclass()
class GenericRequest:
    method: str
    path: str
    query_string: str
    date: datetime
    content_type: str
    content_length: Optional[int]
    data_as_text: str
    schemas: [Schema]

    def __init__(self, r: Request):
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

    def follow_url(self, schema: Schema):
        return url_for(
            'inspect_request',
            schema_name=schema.name,
            endpoint_hash=self.endpoint_hash(),
            request_hash=self.hash()
        )

    def hash(self) -> str:
        meta = f'{self.method} {self.path} {self.date}'.encode('utf-8')
        body = self.data_as_text.encode('utf-8')
        return sha1(meta + body).hexdigest()

    def endpoint_hash(self) -> str:
        return sha1(f'{self.method} {self.path}'.encode('utf-8')).hexdigest()

    def schema_with_name(self, name: str):
        return next((s for s in self.schemas if s.name == name), None)


@dataclass()
class GenericEndpoint:
    method: str
    path: str
    requests: [GenericRequest]  # all requests sent to this endpoint
    schemas: [Schema]

    def name(self):
        return f'{self.method} {self.path}'

    def requests_count(self):
        return len(self.requests)

    def bytes_received(self):
        return sum(map(lambda r: r.content_length or 0, self.requests))

    def follow_url(self, schema: Schema):
        return url_for('inspect_endpoint', schema_name=schema.name, endpoint_hash=self.hash())

    def hash(self) -> str:
        return sha1(f'{self.method} {self.path}'.encode('utf-8')).hexdigest()

    def schema_with_name(self, name: str):
        return next((s for s in self.schemas if s.name == name), None)


def schemas_for_request(r: Request) -> [Schema]:
    schemas: [Schema] = []

    if RAWSchema.matches(r.method, r.path):
        schemas.append(RAWSchema(request=r))

    if RUMSchema.matches(r.method, r.path):
        schemas.append(RUMSchema(request=r))

    if SRSchema.matches(r.method, r.path):
        schemas.append(SRSchema(request=r))

    return schemas

def add_cors_headers(r: flask.Response):
    r.headers['access-control-allow-origin'] = '*'
    r.headers['access-control-allow-headers'] = '*'
    r.headers['access-control-allow-methods'] = 'GET, POST'

endpoints: [GenericEndpoint] = []

class DataClassJsonEncoder(json.JSONEncoder):
    def default(self, obj):
        if is_dataclass(obj):
            return asdict(obj)
        as_json_method = getattr(obj, "as_json", None)
        if callable(as_json_method):
            return obj.as_json()
        if isinstance(obj, datetime.datetime):
            return str(obj)
        return super().default(obj)

def write_to_file(endpoint: GenericEndpoint):
    no = len(endpoint.requests)
    if 'rum' in endpoint.path:
        pass
        with open(f'fixtures/rum/{no}', 'wb') as f:
            f.write(request.get_data())
    elif 'replay' in endpoint.path:
        with open(f'fixtures/replay/{no}', 'wb') as f:
            f.write(request.get_data())

@app.route('/', methods=['POST'])
@app.route('/<path:rest>', methods=['POST'])
def generic_post(rest = ''):
    """
    POST /*

    Record generic (any) POST request sent to `/**/*`
    """
    global endpoints

    gr = GenericRequest(r=request)

    response_text = ''
    if existing := next((e for e in endpoints if e.hash() == gr.endpoint_hash()), None):
        existing.requests.append(gr)
        # write_to_file(endpoint=existing)
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
def inspect_json():
    """
    GET /inspect_requests

    Browse recorded requests serialized as JSON
    """
    global endpoints
    endpoint_requests = [ { "endpoint": e.path, "requests": e.requests } for e in endpoints ]
    # Remove non-raw schemas
    for endpoint_request in endpoint_requests:
        for request in endpoint_request["requests"]:
            request.schemas = [s for s in request.schemas if isinstance(s, RAWSchema)]

    resp = flask.Response(json.dumps(endpoint_requests, cls=DataClassJsonEncoder))
    resp.headers['Content-Type'] = 'application/json'
    add_cors_headers(resp)
    return resp

@app.route('/inspect/')
def inspect():
    """
    GET /inspect

    Browse recorded requests.
    """
    global endpoints
    return render_template('endpoints.html', title='Endpoints', endpoints=endpoints)

@app.route('/reset')
def reset():
    """
    GET /reset

    Clear currently logged requests on all endpoints
    """
    global endpoints
    endpoints.clear()
    resp = flask.Response('OK', status=200)
    add_cors_headers(resp)
    return resp


@app.route('/inspect/<schema_name>/<endpoint_hash>')
def inspect_endpoint(schema_name, endpoint_hash):
    global endpoints

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
def inspect_request(schema_name, endpoint_hash, request_hash):
    global endpoints

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

def run(preferred_address: str, port: int):
    if not preferred_address:
        preferred_address = get_best_server_address().ip

    app.run(debug=True, host=preferred_address, port=port)

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument("--prefer-localhost", action='store_true')
    parser.add_argument("--update-schemas", action='store_true')
    parser.add_argument("--addr", type=str)
    parser.add_argument("--port", type=int, default=5000)

    args = parser.parse_args()
    if args.update_schemas:
        update_schemas()
        exit()

    if not schemas_path_exists():
        print('Missing .schemas. Please run app.py --update-schemas')
        exit()

    if args.addr and args.prefer_localhost:
        raise ValueError('--addr and --prefer-localhost are mutually exclusive')
    
    preferred_address = args.addr or ''
    if args.prefer_localhost:
        preferred_address = '127.0.0.1'

    run(preferred_address, args.port)
