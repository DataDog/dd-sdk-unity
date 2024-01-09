// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Linq;
using NUnit.Framework;

namespace Datadog.Unity.Rum.Tests
{
    public class TracingUuidTest
    {
        [Test]
        public void TracingDefaultValueOfZeroAsStringIsZero()
        {
            // Given
            var uuid = new TracingUuid(0, 0);

            // When
            var str = uuid.ToString();

            // Then
            Assert.AreEqual("0", str);
        }

        [Test]
        [TestCase(0ul, 224ul, "224")]
        [TestCase(13ul, 115257ul, "239807672958224286265")]
        public void TracingUuidDecRepresentationsAreCorrect(ulong high, ulong low, string expected)
        {
            // Given
            var uuid = new TracingUuid(high, low);

            // When
            var str = uuid.ToString();

            // Then
            Assert.AreEqual(expected, str);
        }

        [Test]
        [TestCase(0ul, 0x2fee4ul, "2FEE4")]
        [TestCase(0xf1aul, 0x14e255ul, "F1A000000000014E255")]
        public void TracingUuidHexRepresentationsAreCorrect(ulong high, ulong low, string expected)
        {
            // Given
            var uuid = new TracingUuid(high, low);

            // When
            var str = uuid.ToString(TraceIdRepresentation.Hex);

            // Then
            Assert.AreEqual(expected, str);
        }

        [Test]
        [TestCase(0ul, 0x2fee4ul, "000000000002FEE4")]
        [TestCase(0xf1aul, 0x14e255ul, "000000000014E255")]
        public void TracingUuidHex16RepresentationsAreCorrect(ulong high, ulong low, string expected)
        {
            // Given
            var uuid = new TracingUuid(high, low);

            // When
            var str = uuid.ToString(TraceIdRepresentation.Hex16Chars);

            // Then
            Assert.AreEqual(expected, str);
        }

        [Test]
        [TestCase(0ul, 0x2fee4ul, "0000000000000000000000000002FEE4")]
        [TestCase(0xf1aul, 0x14e255ul, "0000000000000F1A000000000014E255")]
        public void TracingUuidHex32RepresentationsAreCorrect(ulong high, ulong low, string expected)
        {
            // Given
            var uuid = new TracingUuid(high, low);

            // When
            var str = uuid.ToString(TraceIdRepresentation.Hex32Chars);

            // Then
            Assert.AreEqual(expected, str);
        }

        [Test]
        [Repeat(25)]
        public void CreateTracingUuid63BitSucceeds()
        {
            // This test contains a random component, so it's possible it could flake. If it does
            // appear flaky at any point, it is likely that we need to re-check the logic.

            // When
            var uuid = TracingUuid.Create63Bit();

            // Then
            var str = uuid.ToString(TraceIdRepresentation.Hex);
            Assert.Less(str.Length, 17);
            if (str.Length == 16)
            {
                // Makes sure the top most bit is not set
                Assert.IsTrue(char.IsDigit(str.First()) && int.Parse(str.First().ToString()) <= 7);
            }
        }

        [Test]
        [Repeat(25)]
        public void CreateTracingUuid64BitSucceeds()
        {
            // This test contains a random component, so it's possible it could flake. If it does
            // appear flaky at any point, it is likely that we need to re-check the logic.

            // When
            var uuid = TracingUuid.Create64Bit();

            // Then
            var str = uuid.ToString(TraceIdRepresentation.Hex);
            Assert.Less(str.Length, 17);
        }

        [Test]
        [Repeat(25)]
        public void CreateTracingUuid128BitSucceeds()
        {
            // This test contains a random component, so it's possible it could flake. If it does
            // appear flaky at any point, it is likely that we need to re-check the logic.

            // When
            var uuid = TracingUuid.Create64Bit();

            // Then
            var str = uuid.ToString(TraceIdRepresentation.Hex);
            Assert.Less(str.Length, 63);
            Assert.Greater(str.Length, 0);
        }
    }
}
