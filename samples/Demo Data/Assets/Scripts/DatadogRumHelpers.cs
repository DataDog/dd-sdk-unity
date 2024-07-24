// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System.Collections;
using Datadog.Unity;
using Datadog.Unity.Rum;
using UnityEngine;

namespace Datadog.Demo.Unity
{
    public static class RumHelpers
    {
        public static IEnumerator FakeScroll(ScrollDirection direction, float scrollTime)
        {
            var directionString = direction switch
            {
                ScrollDirection.Up => "Scroll Up",
                ScrollDirection.Down => "Scroll Down",
                _ => "Scroll Unknown"
            };
            DatadogSdk.Instance.Rum.StartAction(RumUserActionType.Scroll, directionString);
            yield return new WaitForSeconds(scrollTime);
            DatadogSdk.Instance.Rum.StopAction(RumUserActionType.Scroll, directionString);
        }
    }

    public enum ScrollDirection
    {
        Up,
        Down,
    }
}
