// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor.Android;
using UnityEngine;

namespace Datadog.Unity.Editor
{
    /// <summary>
    /// Modifies a Unity project's build.gradle file to ensure compatibility with certain transitive dependencies
    /// included by dd-sdk-android.
    /// </summary>
    public class DatadogGradlePostProcessor : IPostGenerateGradleAndroidProject
    {
        // These comments mark the start and end of the section in the `dependencies` block where EDM4U writes gradle
        // dependencies
        private const string EdmHeaderText = "// Android Resolver Dependencies Start";
        private const string EdmFooterText = "// Android Resolver Dependencies End";

        public int callbackOrder => 999; // Run after EDM4U

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            // Early-out if we're building in an environment that requires no fixes
            if (!RequiresAndroidxMetricsCompatibilityFix())
            {
                return;
            }

            // Modify the generated build.gradle file; abort silently if it doesn't exist
            string gradlePath = Path.Combine(path, "build.gradle");
            if (!File.Exists(gradlePath))
            {
                return;
            }

            // Read the existing contents of build.gradle, normalizing line endings
            string[] lines = File.ReadAllLines(gradlePath);

            // Ensure that our dependency on dd-sdk-android-rum is declared in such a way that all transitive
            // dependencies are resolved to versions that are compatible with this version of Unity
            lines = ApplyAndroidxMetricsCompatibilityFix(lines);

            // Write our modifications to the file
            File.WriteAllLines(gradlePath, lines);
        }

        /// <summary>
        /// Evaluates whether we need to apply a compatibility fix to build.gradle in order to prevent build errors due
        /// to the inclusion of `androidx.metrics:metrics-performance:1.0.0-beta02`, which requires AGP 8.6.0+. Unity
        /// 2022 and 2021 use AGP 7.x, so they require the fix; versions of Unity 6 prior to 6000.0.45 use AGP 8.3.0,
        /// so they also need the fix.
        /// </summary>
        /// <returns>True if the detected Unity version predates Unity 6000.0.45.</returns>
        private bool RequiresAndroidxMetricsCompatibilityFix()
        {
            // Parse the Unity version string
            string version = Application.unityVersion;
            string[] parts = version.Split('.');
            if (parts.Length < 3)
            {
                // Silently proceed without applying the fix if unable to parse
                return false;
            }
            string majorVersionStr = parts[0];
            string minorVersionStr = parts[1];
            string patchVersionStr = parts[2];

            // The last token may have a suffix like 'f1' or 'b2'; strip it off so we can identify the
            // patch version alone
            int patchVersionStrLen = 0;
            while (patchVersionStrLen < patchVersionStr.Length)
            {
                if (!Char.IsDigit(patchVersionStr[patchVersionStrLen]))
                {
                    break;
                }
                patchVersionStrLen++;
            }
            patchVersionStr = patchVersionStr.Substring(0, patchVersionStrLen);

            // Parse our plain ol' int values so we can do arithmetic
            int majorVersion;
            int minorVersion;
            int patchVersion;
            if (!int.TryParse(majorVersionStr, out majorVersion)
             || !int.TryParse(minorVersionStr, out minorVersion)
             || !int.TryParse(patchVersionStr, out patchVersion))
            {
                return false;
            }

            // For Unity 6, we only need the fix if we're on 6000.0.44 or older
            if (majorVersion == 6000)
            {
                return minorVersion == 0 && patchVersion < 45;
            }

            // For other major Unity releases: apply the fix if our version predates Unity 6
            return majorVersion < 6000;
        }

        /// <summary>
        /// Modifies the contents of a build.gradle file to apply the compatibility fix for
        /// androidx.metrics:metrics-performance, downgrading it from 1.0.0-beta02 to 1.0.0-beta01.
        /// </summary>
        /// <param name="lines">The complete set of lines parsed from a build.gradle file.</param>
        /// <returns>The same set of lines with androidx.metrics:metrics-performance downgraded to beta01.</returns>
        public static string[] ApplyAndroidxMetricsCompatibilityFix(string[] lines)
        {
            // Find the start and end of the EDM4U dependencies, and abort silently if there's no such section
            int edmHeaderIndex = Array.IndexOf(lines, EdmHeaderText);
            int edmFooterIndex = Array.IndexOf(lines, EdmFooterText, edmHeaderIndex + 1);
            if (edmHeaderIndex == -1 || edmFooterIndex == -1)
            {
                return lines;
            }

            // Find the first `implementation` directive that declares dd-sdk-android-rum as a dependency, using a regex
            // that will capture the relevant details of that declaration, and ensuring that it's located within the
            // EDM4U-managed section of the file
            var regex = new Regex(@"^(\s+)implementation([ \(])(['""]com\.datadoghq:dd-sdk-android-rum:.*['""])(\)\s*{)?(?:\s*(\/\/.*))?");
            var found = lines
                .Select((line, index) => (Match: regex.Match(line), Index: index))
                .FirstOrDefault(t => t.Match.Success);
            if (found.Match == null || !found.Match.Success || found.Index <= edmHeaderIndex ||
                found.Index >= edmFooterIndex)
            {
                return lines;
            }

            // Parse the dependency declaration so we can examine whether it's a single-line statement as written by
            // EDM4U, e.g.:
            //   implementation 'com.datadoghq:dd-sdk-android-rum:2.20.0' // DatadogDependencies.xml:12
            // ...or else a multi-line declaration that we've already modified, e.g.:
            //   implementation('com.datadoghq:dd-sdk-android-rum:2.20.0') { // DatadogDependencies.xml:12
            string indentStr = found.Match.Groups[1].Value; // Whitespace chars for a single-level indent
            string openStr = found.Match.Groups[2].Value; // Either space or '('
            string packageSpecLiteral = found.Match.Groups[3].Value; // Full package specifier, as quoted string literal
            string closeStr = found.Match.Groups[4].Value; // Either nothing or ') {'
            string comment = found.Match.Groups[5].Value; // Any comment appearing at the end of the line, incl. '//'

            // If the declaration already has a body, we'll assume that the necessary edit has already been made by a
            // previous invocation of our callback
            if (openStr.Contains("(") || closeStr.Contains(") {"))
            {
                return lines;
            }

            // Otherwise, the line at found.Index is a single-line 'implementation' declaration: replace it with an
            // expanded dependency specifier that explicitly excludes the problematic version of
            // `androidx.metrics:metrics-performance`, then follow it with another declaration that pulls in the version
            // of that library that's compatible with AGP 7.x
            string[] newDeclarationLines =
            {
                indentStr + "implementation(" + packageSpecLiteral + ") {" + (comment.Length > 0 ? $" {comment}" : string.Empty),
                indentStr + indentStr + "// DatadogGradlePostProcessor: exclude the dependency on androidx.metrics:metrics-performance:1.0.0-beta02",
                indentStr + indentStr + "// Version beta02 requires Android Gradle plugin 8.6.0+, which is not supported on Unity 2022 and older",
                indentStr + indentStr + "exclude group: 'androidx.metrics', module: 'metrics-performance'",
                indentStr + "}",
                indentStr + "// DatadogGradlePostProcessor: Explicitly require version beta01 of the same dependency, as it works with AGP 7",
                indentStr + "implementation 'androidx.metrics:metrics-performance:1.0.0-beta01'",
            };
            return lines.Take(found.Index).Concat(newDeclarationLines).Concat(lines.Skip(found.Index + 1)).ToArray();
        }
    }
}
