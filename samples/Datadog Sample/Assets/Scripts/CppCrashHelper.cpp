// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

#include <exception>
#include <string>

extern "C" {

void perform_cpp_throw()
{
    try {
        // throws std::length_error
        std::string("1").substr(2);
    } catch (const std::exception &e) {
        throw;
    }
}

} // extern "C"
