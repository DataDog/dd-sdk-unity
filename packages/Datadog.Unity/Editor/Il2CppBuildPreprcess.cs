// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Datadog.Unity.Editor
{
    public class Il2CppBuildPreprcess : IPreprocessBuildWithReport
    {
        const string Il2CppEmitSourceMappingArg = "--emit-source-mapping";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (PlayerSettings.GetScriptingBackend(report.summary.platformGroup) != ScriptingImplementation.IL2CPP)
            {
                return;
            }

            var options = DatadogConfigurationOptionsExtensions.GetOrCreate();
            var il2CppArgs = PlayerSettings.GetAdditionalIl2CppArgs();
            if (options.Enabled && options.OutputSymbols)
            {
                if (!il2CppArgs.Contains(Il2CppEmitSourceMappingArg))
                {
                    il2CppArgs += $" {Il2CppEmitSourceMappingArg}";
                    PlayerSettings.SetAdditionalIl2CppArgs(il2CppArgs);
                }
            }
            else
            {
                if (il2CppArgs.Contains(Il2CppEmitSourceMappingArg))
                {
                    il2CppArgs = il2CppArgs.Replace(Il2CppEmitSourceMappingArg, string.Empty);
                    PlayerSettings.SetAdditionalIl2CppArgs(il2CppArgs);
                }
            }
        }
    }
}
