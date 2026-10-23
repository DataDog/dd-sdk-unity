#!/usr/bin/env bash
# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

# Source with a repository name to request and export its token.
# Token files let after_script revoke them from its separate shell.
release_token_dir="/tmp/dd-unity-release-${CI_JOB_ID:?}"
case "${1:?Expected dd-sdk-unity, unity-package, or revoke}" in
  dd-sdk-unity)
    mkdir -p "$release_token_dir"
    dd-octo-sts --disable-tracing token --scope DataDog/dd-sdk-unity \
      --policy self.release > "$release_token_dir/dd-sdk-unity"
    export SOURCE_GITHUB_TOKEN="$(cat "$release_token_dir/dd-sdk-unity")"
    ;;

  unity-package)
    mkdir -p "$release_token_dir"
    dd-octo-sts --disable-tracing token --scope DataDog/unity-package \
      --policy dd-sdk-unity.release > "$release_token_dir/unity-package"
    export PACKAGE_GITHUB_TOKEN="$(cat "$release_token_dir/unity-package")"
    ;;

  revoke)
    for release_token_file in "$release_token_dir/dd-sdk-unity" "$release_token_dir/unity-package"; do
      if [ -s "$release_token_file" ]; then
        dd-octo-sts --disable-tracing revoke --token "$(cat "$release_token_file")" || true
      fi
    done
    rm -rf "$release_token_dir"
    ;;

  *)
    echo "Unknown release credential target: $1" >&2
    return 1
    ;;
esac
