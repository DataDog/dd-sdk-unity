"""
Utility code for querying available Unity releases from the archive (normally
accessible at https://unity.com/releases/editor/archive), which provides the full set
of Unity releases.

Unity Hub normally only allows you to install the latest published patch version of any
major Unity release, and that set of officially-supported versions can be inconsistent
between different environments and/or builds of Unity Hub, making it difficult to
control the versions of Unity that we build against.

Luckily, Unity Hub _does_ allow you to install older versions, but it requires an
accompanying `changeset` value that must be fetched from the Unity download archive
page. This code hits a public graphql endpoint to retrieve the same data rendered on
that page.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
from dataclasses import dataclass
from typing import List, Optional

import requests

from .install import UnityVersion


__graphql_endpoint__ = 'https://services.unity.com/graphql'
__graphql_operation__ = 'GetRelease'
__graphql_query__ = '''query GetRelease($limit: Int, $skip: Int, $version: String!, $stream: [UnityReleaseStream!]) {
  getUnityReleases(
    limit: $limit
    skip: $skip
    stream: $stream
    version: $version
    entitlements: [XLTS]
  ) {
    totalCount
    edges {
      node {
        version
        entitlements
        releaseDate
        unityHubDeepLink
        stream
        __typename
      }
      __typename
    }
    __typename
  }
}'''


@dataclass
class UnityArchiveRelease:
    version: UnityVersion
    changeset: str
    stream: str


def _fetch_archive_releases(major_version: int) -> List[UnityArchiveRelease]:
    r = requests.post(__graphql_endpoint__, json={
        'operationName': __graphql_operation__,
        'query': __graphql_query__,
        'variables': {
            'version': str(major_version),
            'limit': 300,
        }
    })
    if not r.ok:
        raise RuntimeError(f'POST {__graphql_endpoint__} failed with status code {r.status_code}: {r.text}')
    try:
        data = r.json()['data']
    except (requests.exceptions.JSONDecodeError, TypeError, KeyError):
        raise RuntimeError(f"Unexpected result from POST {__graphql_endpoint__}: not a JSON object with a 'data' property")

    releases: List[UnityArchiveRelease] = []
    for edge in data['getUnityReleases']['edges']:
        node = edge['node']
        version = UnityVersion.parse(node['version'])
        hub_uri = node['unityHubDeepLink']
        changeset = hub_uri.rsplit('/')[-1]
        stream = node['stream']
        releases.append(UnityArchiveRelease(version, changeset, stream))
    return releases


def find_archive_release(version: UnityVersion) -> Optional[UnityArchiveRelease]:
    releases = _fetch_archive_releases(version.major)
    return next((x for x in releases if x.version == version), None)
