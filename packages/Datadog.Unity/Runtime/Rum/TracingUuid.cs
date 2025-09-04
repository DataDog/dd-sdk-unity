// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Numerics;

namespace Datadog.Unity.Rum
{
    /// <summary>
    /// Supported formats for representing a TraceId as a string.
    /// </summary>
    public enum TraceIdRepresentation
    {
        // Decimal string representation of the entire TraceId
        Dec,

        // Decimal string representation of the low 64-bits of the TraceId
        LowDec,

        // Hexadecimal string representation of the entire TraceId, with no padding
        Hex,

        // Hexadecimal string representation of the low 64-bits TraceId, padded to 16 characters
        Hex16Chars,

        // Hexadecimal string representation of the high 64-bits TraceId, padded to 16 characters
        HighHex16Chars,

        // Hexadecimal string representation of the entire TraceId, padded to 32 characters
        Hex32Chars,
    }

    /// <summary>
    /// 128-bit UUID used to identify a trace. Stored as two uint64 values representing the high
    /// and low portions of the UUID, to avoid the overhead of BigInteger.
    ///
    /// May be used to store 64-bit values, in which case the significant portion is always held
    /// in the lower half.
    /// </summary>
    public readonly struct TracingUuid
    {
        private static Random _random = new();

        private readonly ulong _high;
        private readonly ulong _low;

        /// <summary>
        /// Initializes a new instance of the <see cref="TracingUuid"/> struct, given the high and
        /// low parts of its full 128-bit value.
        /// </summary>
        public TracingUuid(ulong high, ulong low)
        {
            _high = high;
            _low = low;
        }

        /// <summary>
        /// Formats the trace ID in decimal format.
        /// </summary>
        public override string ToString()
        {
            return ToString(TraceIdRepresentation.Dec);
        }

        /// <summary>
        /// Formats the trace ID in the requested representation.
        /// </summary>
        public string ToString(TraceIdRepresentation representation)
        {
            switch (representation)
            {
                default:
                case TraceIdRepresentation.Dec:
                    var bigInteger = ((BigInteger)_high << 64) | _low;
                    return bigInteger.ToString();

                case TraceIdRepresentation.LowDec:
                    return $"{_low}";

                case TraceIdRepresentation.HighHex16Chars:
                    return $"{_high:x16}";

                case TraceIdRepresentation.Hex:
                case TraceIdRepresentation.Hex16Chars:
                case TraceIdRepresentation.Hex32Chars:
                    string hexString;
                    if (_high > 0 && representation != TraceIdRepresentation.Hex16Chars)
                    {
                        hexString = $"{_high:x}{_low:x16}";
                    }
                    else
                    {
                        hexString = $"{_low:x}";
                    }

                    if (representation == TraceIdRepresentation.Hex16Chars)
                    {
                        // Pad to 16 characters and truncate, just in case high was actually set
                        hexString = hexString.PadLeft(16, '0');
                    }
                    else if (representation == TraceIdRepresentation.Hex32Chars)
                    {
                        hexString = hexString.PadLeft(32, '0');
                    }

                    return hexString;
            }
        }

        /// <summary>
        /// Generates a random ID, with the value stored in the lower 63 bits. The topmost bit of
        /// the lower half is reserved and will always be zero.
        /// </summary>
        public static TracingUuid Create63Bit()
        {
            byte[] bytes = new byte[sizeof(ulong)];
            _random.NextBytes(bytes);

            // Mask out the top bit
            bytes[7] &= 0x7f;
            var low = BitConverter.ToUInt64(bytes);

            return new TracingUuid(0, low);
        }

        /// <summary>
        /// Creates a random ID, with the value stored in the lower 64 bits.
        /// </summary>
        public static TracingUuid Create64Bit()
        {
            byte[] bytes = new byte[sizeof(ulong)];
            _random.NextBytes(bytes);
            var low = BitConverter.ToUInt64(bytes);
            return new TracingUuid(0, low);
        }

        /// <summary>
        /// Creates a random ID with a 128-bit value.
        /// </summary>
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
