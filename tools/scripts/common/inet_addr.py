"""
Utility code for finding available network interface IPs.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import socket
from typing import cast, Optional


def get_reachable_inet_addr() -> Optional[str]:
    # Test common subnets reserved for private IPs
    subnets = [
        '10.255.255.255',
        '192.168.255.255',
        '172.31.255.255',
    ]
    for subnet in subnets:
        ip = get_ip_on_subnet(subnet)
        if ip:
            return ip
    return None


def get_ip_on_subnet(subnet: str) -> Optional[str]:
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        s.connect((subnet, 1))
        return cast(str, s.getsockname()[0])
    except:
        return None
    finally:
        s.close()
