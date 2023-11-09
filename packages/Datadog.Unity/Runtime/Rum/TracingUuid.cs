// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Numerics;

namespace Datadog.Unity.Rum
{
    internal enum TraceIdRepresentation
    {
        dec,
        hex,
        hex16Chars,
        hex32Chars,
    }

    // A Uint128 value for a trace ID. This is held as two UInt64s,
    // a low portion and a high portion to avoid the overhead of BigInteger.
    internal readonly struct TracingUuid
    {
        private static Random _random = new Random();

        private readonly ulong _high;
        private readonly ulong _low;

        public TracingUuid(ulong high, ulong low)
        {
            _high = high;
            _low = low;
        }

        public override string ToString()
        {
            return ToString(TraceIdRepresentation.dec);
        }

        public string ToString(TraceIdRepresentation representation)
        {
            switch (representation)
            {
                default:
                case TraceIdRepresentation.dec:
                    var bigInteger = (BigInteger)_high << 64 | _low;
                    return bigInteger.ToString();

                case TraceIdRepresentation.hex:
                case TraceIdRepresentation.hex16Chars:
                case TraceIdRepresentation.hex32Chars:
                    string hexString;
                    if (_high > 0 && representation != TraceIdRepresentation.hex16Chars)
                    {
                        hexString = $"{_high:X}{_low:X16}";
                    }
                    else
                    {
                        hexString = $"{_low:X}";
                    }

                    if (representation == TraceIdRepresentation.hex16Chars)
                    {
                        // Pad to 16 characters and truncate, just in case high was actually set
                        hexString = hexString.PadLeft(16, '0');
                    }
                    else if (representation == TraceIdRepresentation.hex32Chars)
                    {
                        hexString = hexString.PadLeft(32, '0');
                    }

                    return hexString;
            }
        }

        public static TracingUuid Create63Bit()
        {
            byte[] bytes = new byte[sizeof(ulong)];
            _random.NextBytes(bytes);
            // Mask out the top bit
            bytes[7] &= 0x7f;
            var low = BitConverter.ToUInt64(bytes);

            return new TracingUuid(0, low);
        }

        public static TracingUuid Create64Bit()
        {
            byte[] bytes = new byte[sizeof(ulong)];
            _random.NextBytes(bytes);
            var low = BitConverter.ToUInt64(bytes);
            return new TracingUuid(0, low);
        }

        public static TracingUuid Create128Bit()
        {
            byte[] bytes = new byte[sizeof(ulong)];
            _random.NextBytes(bytes);
            var low = BitConverter.ToUInt64(bytes);
            _random.NextBytes(bytes);
            var high = BitConverter.ToUInt64(bytes);
            return new TracingUuid(high, low);
        }
    }
}
