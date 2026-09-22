// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Datadog.Unity.Editor.Android;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

namespace Datadog.Unity.Editor
{
    /// <summary>
    /// Modifies a Unity project's build.gradle file to ensure compatibility with certain transitive dependencies
    /// included by dd-sdk-android, and to write Datadog's own Android dependency declarations.
    /// </summary>
    public class DatadogGradlePostProcessor : IPostGenerateGradleAndroidProject
    {
        // These comments mark the start and end of the section in the `dependencies` block that this class itself
        // writes, containing the dd-sdk-android implementation declarations.
        private const string DatadogHeaderText = "// Datadog Dependencies Start";
        private const string DatadogFooterText = "// Datadog Dependencies End";

        // The anchor line every generated unityLibrary/build.gradle contains; our dependency declarations are
        // spliced in immediately after it.
        private const string DependenciesAnchorFragment = "implementation fileTree(dir: 'libs'";

        // Must run before SymbolAssemblyBuildProcess (callbackOrder int.MaxValue), and late enough that Unity has
        // finished generating the Gradle project.
        public int callbackOrder => 999;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            // Read the pin once; a missing/malformed pin is a hard build misconfiguration, so let it propagate.
            AndroidDependencyPinData pin = AndroidDependencyVersion.Load();

            // Modify the unityLibrary build.gradle; abort silently if it doesn't exist
            string gradlePath = Path.Combine(path, "build.gradle");
            if (File.Exists(gradlePath))
            {
                string[] lines = File.ReadAllLines(gradlePath);
                lines = ApplyDatadogDependencies(lines, pin.version, pin.artifacts);
                if (Array.IndexOf(lines, DatadogHeaderText) == -1)
                {
                    throw new BuildFailedException(
                        $"Datadog: no '{DependenciesAnchorFragment}' anchor was found in {gradlePath}, so " +
                        "Datadog's Android dependencies could not be declared. Please report this issue.");
                }

                if (RequiresAndroidxMetricsCompatibilityFix())
                {
                    lines = ApplyAndroidxMetricsCompatibilityFix(lines);
                }

                File.WriteAllLines(gradlePath, lines);
            }

            // Modify the root build.gradle to force AGP 7-compatible androidx.core versions
            string rootGradlePath = Path.Combine(path, "..", "build.gradle");
            if (RequiresAndroidxMetricsCompatibilityFix() && File.Exists(rootGradlePath))
            {
                string[] lines = File.ReadAllLines(rootGradlePath);
                lines = ApplyAndroidxCoreCompatibilityFix(lines);
                File.WriteAllLines(rootGradlePath, lines);
            }
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
        /// Writes Datadog's own `implementation` dependency declarations into a generated build.gradle file,
        /// immediately after the `implementation fileTree(dir: 'libs', ...)` anchor line.
        /// </summary>
        /// <param name="lines">The complete set of lines parsed from a build.gradle file.</param>
        /// <param name="version">The dd-sdk-android version to declare for each artifact.</param>
        /// <param name="artifacts">The dd-sdk-android artifact IDs to declare, in order.</param>
        /// <returns>The same set of lines with Datadog's dependency declarations spliced in, or unchanged if
        /// already present or if no anchor line was found.</returns>
        public static string[] ApplyDatadogDependencies(string[] lines, string version, string[] artifacts)
        {
            // Idempotent: if our markers are already present, assume a previous invocation already wrote them.
            if (Array.IndexOf(lines, DatadogHeaderText) != -1)
            {
                return lines;
            }

            // Locate the anchor line; abort silently if it's not present.
            int anchorIndex = Array.FindIndex(lines, l => l.Contains(DependenciesAnchorFragment));
            if (anchorIndex == -1)
            {
                return lines;
            }

            // Derive the indent from the anchor line's leading whitespace.
            string anchorLine = lines[anchorIndex];
            string indent = anchorLine.Substring(0, anchorLine.Length - anchorLine.TrimStart().Length);

            var block = new System.Collections.Generic.List<string> { DatadogHeaderText };
            foreach (var artifact in artifacts)
            {
                block.Add(indent + "implementation '" + AndroidDependencyVersion.MavenGroupId + ":" + artifact + ":" + version + "'");
            }

            block.Add(DatadogFooterText);

            return lines.Take(anchorIndex + 1).Concat(block).Concat(lines.Skip(anchorIndex + 1)).ToArray();
        }

        /// <summary>
        /// Modifies the contents of a build.gradle file to apply the compatibility fix for
        /// androidx.metrics:metrics-performance, downgrading it from 1.0.0-beta02 to 1.0.0-beta01.
        /// </summary>
        /// <param name="lines">The complete set of lines parsed from a build.gradle file.</param>
        /// <returns>The same set of lines with androidx.metrics:metrics-performance downgraded to beta01.</returns>
        public static string[] ApplyAndroidxMetricsCompatibilityFix(string[] lines)
        {
            // Find the start and end of the Datadog-written dependencies, and abort silently if there's no such section
            int datadogHeaderIndex = Array.IndexOf(lines, DatadogHeaderText);
            int datadogFooterIndex = Array.IndexOf(lines, DatadogFooterText, datadogHeaderIndex + 1);
            if (datadogHeaderIndex == -1 || datadogFooterIndex == -1)
            {
                return lines;
            }

            // Find the first `implementation` directive that declares dd-sdk-android-rum as a dependency, using a regex
            // that will capture the relevant details of that declaration, and ensuring that it's located within the
            // Datadog-managed section of the file
            var regex = new Regex(@"^(\s+)implementation([ \(])(['""]com\.datadoghq:dd-sdk-android-rum:.*['""])(\)\s*{)?(?:\s*(\/\/.*))?");
            var found = lines
                .Select((line, index) => (Match: regex.Match(line), Index: index))
                .FirstOrDefault(t => t.Match.Success);
            if (found.Match == null || !found.Match.Success || found.Index <= datadogHeaderIndex ||
                found.Index >= datadogFooterIndex)
            {
                return lines;
            }

            // Parse the dependency declaration so we can examine whether it's a single-line statement as written by
            // the Datadog dependency writer, e.g.:
            //   implementation 'com.datadoghq:dd-sdk-android-rum:2.20.0'
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

        /// <summary>
        /// Appends a subprojects block to the root build.gradle that forces AGP 7-compatible versions of
        /// androidx.core. androidx.core 1.15.0+ ships Java 21 bytecode that D8 in AGP 7.x cannot process.
        /// </summary>
        /// <param name="lines">Lines from the root build.gradle.</param>
        /// <returns>The same lines with the force block appended, or unchanged if already applied.</returns>
        public static string[] ApplyAndroidxCoreCompatibilityFix(string[] lines)
        {
            if (Array.Exists(lines, l => l.Contains("androidx.core:core:1.13.1")))
            {
                return lines;
            }

            string[] block =
            {
                "",
                "// DatadogGradlePostProcessor: Force AGP 7-compatible androidx.core versions",
                "// androidx.core 1.15.0+ ships Java 21 bytecode that D8 in AGP 7.x cannot process",
                "subprojects {",
                "    configurations.all {",
                "        resolutionStrategy {",
                "            force 'androidx.core:core:1.13.1'",
                "            force 'androidx.core:core-ktx:1.13.1'",
                "        }",
                "    }",
                "}",
            };
            return lines.Concat(block).ToArray();
        }
    }
}
