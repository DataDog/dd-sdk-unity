// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Datadog.Unity.Editor
{
    public class Il2CppBuildPreprocess : IPreprocessBuildWithReport
    {
        private const string EmitSourceMappingArg = "--emit-source-mapping";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (PlayerSettings.GetScriptingBackend(report.summary.platformGroup) != ScriptingImplementation.IL2CPP)
            {
                return;
            }

            var args = PlayerSettings.GetAdditionalIl2CppArgs();
            if (!args.Contains(EmitSourceMappingArg))
            {
                args += $" {EmitSourceMappingArg}";
            }

            PlayerSettings.SetAdditionalIl2CppArgs(args);
        }
    }
}
