# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2019-2020 Datadog, Inc.

from abc import ABC, abstractmethod


class Schema(ABC):
    name: str  # displayed in the UI
    pretty_name: str
    is_known: bool
    endpoint_template: str
    request_template: str

    @staticmethod
    @abstractmethod
    def matches(method: str, path: str) -> bool:
        pass
