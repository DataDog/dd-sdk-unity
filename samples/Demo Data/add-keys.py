#! /usr/bin/env python

# -----------------------------------------------------------
# Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
# This product includes software developed at Datadog (https://www.datadoghq.com/).
# Copyright 2023-Present Datadog, Inc.
# -----------------------------------------------------------

import fileinput
import os


def main():
    if "DATADOG_CLIENT_TOKEN" not in os.environ or "DATADOG_APPLICATION_ID" not in os.environ:
        print("Could not find either DATADOG_CLIENT_TOKEN or DATADOG_APPLICATION_ID.")
        return

    datadog_client_token = os.environ["DATADOG_CLIENT_TOKEN"]
    datadog_applicaiton_id = os.environ["DATADOG_APPLICATION_ID"]

    print("Modifying `Assets/Resources/DatadogSettings.asset`...")
    settings_path = os.path.join(".", "Assets", "Resources", "DatadogSettings.asset")

    with fileinput.input(settings_path, inplace=True) as f:
        for line in f:
            if line.startswith("  ClientToken:"):
                print(f"  ClientToken: {datadog_client_token}")
            elif line.startswith("  RumApplicationId:"):
                print(f"  RumApplicationId: {datadog_applicaiton_id}")
            else:
                print(line, end='')



if __name__ == "__main__":
    main()
