#!/usr/bin/python3

# -----------------------------------------------------------
# Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
# This product includes software developed at Datadog (https://www.datadoghq.com/).
# Copyright 2019-2020 Datadog, Inc.
# -----------------------------------------------------------

import glob
import os
import shutil
from tempfile import TemporaryDirectory

schemas_path = ".schemas"
schema_repo = "git@github.com:DataDog/rum-events-format.git"

def schemas_path_exists():
    """
    Test to see if schemas are available
    """
    return os.path.exists(schemas_path) and os.path.isdir(schemas_path):

def update_schemas():
    """
    Update RUM schemas to current master of the schema repo
    """

    with TemporaryDirectory() as temp_dir:
        print(f"Running git clone on {schema_repo} to {temp_dir}")
        os.system(f'git clone {schema_repo} {temp_dir}')

        if not os.path.exists(schemas_path):
          os.mkdir(schemas_path)
        schemas = glob.glob(f"{temp_dir}/schemas/**", recursive=True)

        for file in schemas:
            base_file = file.replace(f'{temp_dir}/schemas/', '')
            if len(base_file) == 0: continue
            target_file = os.path.join(schemas_path, base_file)

            if os.path.isdir(file):
                if not os.path.exists(target_file):
                    os.mkdir(target_file)
                continue

            shutil.copy2(file, target_file)
            print(f'Copied {temp_dir} to {schemas_path}/{base_file}')
