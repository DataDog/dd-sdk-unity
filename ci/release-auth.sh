#!/usr/bin/env bash
# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

# Source from the job so exported tokens stay in its environment. Files exist only
# for unconditional revocation in after_script, which runs in a separate shell.
release_auth_dir="/tmp/dd-unity-release-${CI_JOB_ID:?}"
case "${1:?Expected prepare, publish, read, or revoke}" in
  prepare|publish|read)
    umask 077
    mkdir -p "$release_auth_dir"
    if [ "$1" = read ]; then
      release_source_policy=self.release-read
    else
      release_source_policy=self.release
    fi
    dd-octo-sts --disable-tracing token --scope DataDog/dd-sdk-unity \
      --policy "$release_source_policy" > "$release_auth_dir/source"
    export SOURCE_GITHUB_TOKEN="$(cat "$release_auth_dir/source")"
    if [ "$1" = publish ]; then
      dd-octo-sts --disable-tracing token --scope DataDog/unity-package \
        --policy dd-sdk-unity.release > "$release_auth_dir/package"
      export PACKAGE_GITHUB_TOKEN="$(cat "$release_auth_dir/package")"
    fi
    ;;
  revoke)
    for release_token_file in "$release_auth_dir/source" "$release_auth_dir/package"; do
      if [ -s "$release_token_file" ]; then
        dd-octo-sts --disable-tracing revoke --token "$(cat "$release_token_file")" || true
      fi
    done
    rm -rf "$release_auth_dir"
    ;;
  *)
    echo "Unknown release credential action: $1" >&2
    return 1
    ;;
esac
