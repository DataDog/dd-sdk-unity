// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace Datadog.Unity.Tests
{
    internal static class PythonScriptUtils
    {
        internal static string Run(
            string scriptName,
            string arguments,
            int timeoutSeconds = 60,
            IReadOnlyDictionary<string, string> environmentVariables = null,
            Action<string> onOutput = null)
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PythonScriptUtils).Assembly);
            var repositoryPath = Path.GetFullPath(Path.Combine(package.resolvedPath, "../.."));
            var startInfo = new ProcessStartInfo
            {
                FileName = GetCommandPath(scriptName, repositoryPath),
                Arguments = arguments,
                WorkingDirectory = repositoryPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            if (environmentVariables != null)
            {
                foreach (var variable in environmentVariables)
                {
                    startInfo.EnvironmentVariables[variable.Key] = variable.Value;
                }
            }

            using (var process = Process.Start(startInfo))
            {
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                var exited = process.WaitForExit(timeoutSeconds * 1000);
                if (!exited)
                {
                    process.Kill();
                }

                var standardOutput = output.GetAwaiter().GetResult().Trim();
                onOutput?.Invoke(standardOutput);
                if (!exited)
                {
                    throw new TimeoutException($"Timed out running {scriptName}.");
                }

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"Failed to run {scriptName}:\n{output.GetAwaiter().GetResult()}\n{error.GetAwaiter().GetResult()}");
                }

                return standardOutput;
            }
        }

        private static string GetCommandPath(string scriptName, string repositoryPath)
        {
            var isWindows = Application.platform == RuntimePlatform.WindowsEditor;
            var executable = isWindows ? $"{scriptName}.exe" : scriptName;
            var command = Path.Combine(repositoryPath, ".venv", isWindows ? "Scripts" : "bin", executable);
            if (!File.Exists(command))
            {
                throw new InvalidOperationException(
                    $"Install the repository's Python tooling in {repositoryPath}/.venv before running {scriptName}. See CONTRIBUTING.md.");
            }

            return command;
        }
    }
}
