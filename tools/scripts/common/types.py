from dataclasses import dataclass


@dataclass
class ExternalDependencyVersions:
    dd_sdk_android: str
    dd_sdk_ios: str
