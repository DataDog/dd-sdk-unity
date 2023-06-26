// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.
package datadog.unity.sample

import kotlin.concurrent.thread

object KotlinCrashHelper {
    @JvmStatic
    fun throwException() {
        throw Exception("Exception from Crash Helper")
    }

    @JvmStatic
    fun throwFromThread() {
        thread(start = true) {
            throw Exception("Exception from Threaded Crash Helper")
        }
    }
}
