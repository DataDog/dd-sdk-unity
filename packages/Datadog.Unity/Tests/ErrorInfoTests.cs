// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Runtime.CompilerServices;
using NUnit.Framework;

namespace Datadog.Unity.Tests
{
    public class ErrorInfoTests
    {
        [Test]
        public void ExceptionIsCoercedToRuntimeError()
        {
            ErrorInfo err = null;
            try
            {
                FunctionThatThrows();
            }
            catch (Exception e)
            {
                err = e;
            }

            Assert.NotNull(err);
            Assert.AreEqual("InvalidCastException", err.Type);
            Assert.AreEqual("very bad cast", err.Message);
            StringAssert.Contains("at Datadog.Unity.Tests.ErrorInfoTests.FunctionThatThrows () [0x", err.StackTrace);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void FunctionThatThrows()
        {
            throw new InvalidCastException("very bad cast");
        }
    }
}
