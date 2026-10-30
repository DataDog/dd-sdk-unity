// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.Scripting;

namespace Datadog.Unity.Rum.Tests
{
    [Preserve]
    internal static class DeterministicSamplingTestCases
    {
        // Shared dd-trace-go fixtures used by the native and Flutter SDK sampler tests.
        [Preserve]
        public static IEnumerable<TestCaseData> SeedCases
        {
            get
            {
                yield return new TestCaseData(5577006791947779410UL, 94.0509f, true);
                yield return new TestCaseData(15352856648520921629UL, 43.7714f, true);
                yield return new TestCaseData(3916589616287113937UL, 68.6823f, true);
                yield return new TestCaseData(894385949183117216UL, 30.0912f, true);
                yield return new TestCaseData(12156940908066221323UL, 46.889f, true);
                yield return new TestCaseData(9828766684487745566UL, 15.6519f, false);
                yield return new TestCaseData(4751997750760398084UL, 81.364f, false);
                yield return new TestCaseData(11199607447739267382UL, 38.0657f, false);
                yield return new TestCaseData(6263450610539110790UL, 21.8553f, false);
                yield return new TestCaseData(1874068156324778273UL, 36.0871f, false);
            }
        }

        [Preserve]
        public static IEnumerable<TestCaseData> UuidCases
        {
            get
            {
                yield return new TestCaseData("11111111-2222-3333-4444-822107fcfd52", 94.050909f, true);
                yield return new TestCaseData("11111111-2222-3333-4444-4dc76695721d", 43.771419f, true);
                yield return new TestCaseData("11111111-2222-3333-4444-858149c6e2d1", 68.682307f, true);
                yield return new TestCaseData("11111111-2222-3333-4444-cb397916001e", 15.651925f, false);
                yield return new TestCaseData("11111111-2222-3333-4444-7f48392907a0", 30.091186f, true);
                yield return new TestCaseData("11111111-2222-3333-4444-7cc6f3875d04", 81.363996f, true);
                yield return new TestCaseData("11111111-2222-3333-4444-ffa2ba517936", 38.065719f, true);
                yield return new TestCaseData("11111111-2222-3333-4444-21587cb3ad0b", 46.888984f, false);
                yield return new TestCaseData("11111111-2222-3333-4444-768b7c4e0b68", 29.310186f, false);
                yield return new TestCaseData("11111111-2222-3333-4444-3f2525632186", 21.855305f, false);
            }
        }
    }
}
