# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2019-2020 Datadog, Inc.

import os
import shutil
import subprocess
import sys
import time
from pathlib import Path

schemas_path = ".schemas"
schema_repo = "https://github.com/DataDog/rum-events-format.git"


def schemas_path_exists(path: str | os.PathLike[str] = schemas_path) -> bool:
    """Test whether the schema directory exists."""
    return os.path.isdir(path)


def ensure_schemas(path: str | os.PathLike[str] = schemas_path) -> None:
    """Refresh schemas on local startup, at most once per day after a successful update."""
    if 'CI' in os.environ:
        return

    last_update = Path(path) / '.git' / 'last-schema-update'
    if last_update.exists() and time.time() - last_update.stat().st_mtime < 24 * 60 * 60:
        return

    try:
        update_schemas(path)
    except (OSError, subprocess.SubprocessError) as error:
        if not schemas_path_exists(path):
            raise
        print(f'⚠️ Could not update schemas; using the existing clone: {error}', file=sys.stderr)


def update_schemas(path: str | os.PathLike[str] = schemas_path) -> None:
    """Clone or update the schema repository in the supplied directory."""
    if os.path.exists(path):
        if not os.path.exists(os.path.join(path, '.git')):
            print(f'⚠️ {path} exists but is not a git repo. Deleting and starting over.')
            shutil.rmtree(path)
            _clone_schemas_repo(path)
        else:
            _update_schemas_repo(path)
    else:
        _clone_schemas_repo(path)
    (Path(path) / '.git' / 'last-schema-update').touch()


def _clone_schemas_repo(path: str | os.PathLike[str]) -> None:
    print(f"Running git clone of {schema_repo}")
    subprocess.check_call(['git', 'clone', schema_repo, os.fspath(path)])


def _update_schemas_repo(path: str | os.PathLike[str]) -> None:
    print(f"Running git pull on {path}")
    subprocess.check_call(['git', 'pull', '--ff-only'], cwd=path, timeout=10)
