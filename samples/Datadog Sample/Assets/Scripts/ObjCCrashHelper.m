// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

#import <Foundation/Foundation.h>

void throwObjectiveC()
{
#ifdef __EXCEPTIONS
    NSLog(@"Throwing an Objective-C Exception");
    @throw [NSException exceptionWithName:@"Exception"
                                   reason:@"User Requested Exception"
                                 userInfo:nil];
#else
    NSLog(@"Cannot throw Objective-C Exception: Exceptions are disabled. "
            "Consider enabling GCC_ENABLE_OBJC_EXCEPTIONS");
#endif
}
