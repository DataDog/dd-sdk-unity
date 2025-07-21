#!/usr/bin/python3

# -----------------------------------------------------------
# Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
# This product includes software developed at Datadog (https://www.datadoghq.com/).
# Copyright 2023-Present Datadog, Inc.
# -----------------------------------------------------------

import argparse
import csv

third_party_csv = "../../LICENSE-3rdparty.csv"
notice_location = "../../packages/Datadog.Unity/NOTICE.txt"

project_name = "Datadog Unity SDK\n"
copyright_notice = "Copyright 2024-Present Datadog, Inc.\n"

class ThirdPartyDependency:
    component: str
    origin: str
    license: str
    copyright: str

    def __init__(self, csv_line: list[str]) -> None:
        self.component = csv_line[0]
        self.origin = csv_line[1]
        self.license = csv_line[2]
        self.copyright = csv_line[3]

def main() -> None:
    dependency_info: list[ThirdPartyDependency] = []
    try:
        with open(third_party_csv, "r") as csv_file:
            csv_reader = csv.reader(csv_file, quotechar='"')
            for row in csv_reader:
                dependency_info.append(ThirdPartyDependency(row))
    except:
        print("Failed to read 3rd party csv. Are you running from the tools/scripts directory properly?")

    with open(notice_location, "w") as notice_file:
        notice_file.write(project_name)
        notice_file.write(copyright_notice)
        notice_file.write("\n")

        # First, write out the AOSP and Kotlin notices, which we'll ignore in the dependency list
        notice_file.write("This product includes software developed as part of:\nThe Android Open Source Project (http://source.android.com).\n\n")
        notice_file.write("This product includes software developed as part of:\nKotlin Language, Copyright 2010-2024 JetBrains s.r.o and respective authors and developers.\n\n")


        for dependency in dependency_info:
            if dependency.origin.startswith("androidx") or dependency.origin.startswith("org.jetbrains.kotlin"):
                # Ignore as they fall under the AOSP and Jetbrains copyright above
                continue

            # Only include things that we actually ship as part of the product, and don't
            # include other Datadog developed SDKS
            if dependency.component == "import" and not dependency.origin.startswith("dd-sdk"):
                notice_file.write(f"This product includes software developed as part of:\n {dependency.origin}, {dependency.copyright}\n\n")

    print(f"Done generating {notice_location}")

if __name__ == "__main__":
    main()
