#!/usr/bin/python3

# -----------------------------------------------------------
# Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
# This product includes software developed at Datadog (https://www.datadoghq.com/).
# Copyright 2019-2020 Datadog, Inc.
# -----------------------------------------------------------

import zlib
import gzip
from typing import Optional
from flask import Request
from schemas.schema import Schema
from templates.components.card import Card, CardTab


class RAWSchema(Schema):
    name = 'raw'
    pretty_name = 'Raw'
    is_known = False
    endpoint_template = 'raw/endpoint.html'
    request_template = 'raw/request.html'

    # RAW-specific:
    headers: [str]  # ['field1: value1', 'field2: value2', ...]
    data_as_text: str
    decompressed_data: Optional[str]  # `None` if data was not compressed

    def __init__(self, request: Request):
        self.headers = list(map(lambda h: f'{h[0]}: {h[1]}', request.headers))
        self.data_as_text = request.get_data(as_text=True)
        encoding = request.headers.get('Content-Encoding', None)
        if encoding == 'deflate':
            self.decompressed_data = zlib.decompress(request.get_data()).decode('utf-8')
        elif encoding == 'gzip':
            self.decompressed_data = gzip.decompress(request.get_data()).decode('utf-8')
        else:
            self.decompressed_data = None

    def headers_card(self) -> Card:
        return Card(
            title='Headers',
            tabs=[CardTab(title='', template='raw/headers.html', object=self.headers)]
        )

    def body_views_card(self) -> Card:
        tabs = []

        if self.decompressed_data:
            tabs.append(
                CardTab(title='RAW (decompressed)', template='raw/text_body_view.html', object=self.decompressed_data)
            )

        tabs.append(CardTab(title='RAW (original)', template='raw/text_body_view.html', object=self.data_as_text))

        return Card(title='View as:' if len(tabs) > 1 else 'Body', tabs=tabs)

    def as_json(self) -> dict:
        return {
            "headers": self.headers,
            "data": self.data_as_text,
            "decompressed_data": self.decompressed_data
        }

    @staticmethod
    def matches(method: str, path: str):
        return True
