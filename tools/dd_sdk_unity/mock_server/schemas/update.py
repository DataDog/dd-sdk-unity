# -----------------------------------------------------------
# Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
# This product includes software developed at Datadog (https://www.datadoghq.com/).
# Copyright 2019-2020 Datadog, Inc.
# -----------------------------------------------------------

import os
import shutil
import subprocess

schemas_path = ".schemas"
schema_repo = "https://github.com/DataDog/rum-events-format.git"


def schemas_path_exists(path=schemas_path):
    """Test whether the schema directory exists."""
    return os.path.isdir(path)


def update_schemas(path=schemas_path):
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


def _clone_schemas_repo(path):
    print(f"Running git clone of {schema_repo}")
    subprocess.check_call(['git', 'clone', schema_repo, os.fspath(path)])


def _update_schemas_repo(path):
    print(f"Running git pull on {path}")
    subprocess.check_call(['git', 'pull'], cwd=path)
