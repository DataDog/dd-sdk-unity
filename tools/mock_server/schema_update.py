#!/usr/bin/python3

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

def schemas_path_exists():
    """
    Test to see if schemas are available
    """
    return os.path.exists(schemas_path) and os.path.isdir(schemas_path)

def update_schemas():
    """
    Update RUM schemas to current master of the schema repo
    """

    if os.path.exists(schemas_path):
        if not os.path.exists(os.path.join(schemas_path, '.git')):
            print(f'⚠️ {schemas_path} exists but is not a git repo. Deleting and starting over.')
            shutil.rmtree(schemas_path)
            _clone_schemas_repo()
        else:
            _update_schemas_repo()
    else:
        _clone_schemas_repo()

def _clone_schemas_repo():
    print(f"Running git clone of {schema_repo}")
    args = ['git', 'clone', schema_repo, schemas_path]
    subprocess.check_call(args)

def _update_schemas_repo():
    print(f"Running git pull on {schemas_path}")
    args = ['git', 'pull']
    subprocess.check_call(args, cwd=schemas_path)
