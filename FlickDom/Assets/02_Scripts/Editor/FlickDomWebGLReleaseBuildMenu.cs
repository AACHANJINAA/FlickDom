using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FlickDom.EditorTools
{
    public static class FlickDomWebGLReleaseBuildMenu
    {
        private const string BuildMenuPath = "FlickDom/Build/WebGL Release";
        private const string BuildAndRunMenuPath = "FlickDom/Build/WebGL Release And Run";
        private const string OutputPath = "Web_Test_Release";

        [MenuItem(BuildMenuPath)]
        private static void BuildRelease()
        {
            BuildReleaseInternal(BuildOptions.None);
        }

        [MenuItem(BuildAndRunMenuPath)]
        private static void BuildReleaseAndRun()
        {
            BuildReleaseInternal(BuildOptions.AutoRunPlayer);
        }

        private static void BuildReleaseInternal(BuildOptions runOption)
        {
            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                throw new InvalidOperationException("No enabled scenes are configured in Build Settings.");
            }

            ApplyReleaseSettings();

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
            {
                EditorUserBuildSettings.SwitchActiveBuildTarget(NamedBuildTarget.WebGL, BuildTarget.WebGL);
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = OutputPath,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = runOption
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"WebGL release build failed: {report.summary.result}");
            }

            WriteLocalHostingHeaders(OutputPath);
            Debug.Log(
                $"WebGL release build complete: {report.summary.outputPath} ({report.summary.totalSize / (1024f * 1024f):F1} MB)");
        }

        private static void ApplyReleaseSettings()
        {
            EditorUserBuildSettings.development = false;
            EditorUserBuildSettings.allowDebugging = false;
            EditorUserBuildSettings.connectProfiler = false;
            EditorUserBuildSettings.buildWithDeepProfilingSupport = false;

            PlayerSettings.stripEngineCode = true;
            PlayerSettings.SetManagedStrippingLevel(
                NamedBuildTarget.WebGL,
                ManagedStrippingLevel.Medium);
            PlayerSettings.SetIl2CppCompilerConfiguration(
                NamedBuildTarget.WebGL,
                Il2CppCompilerConfiguration.Release);

            PlayerSettings.WebGL.debugSymbolMode = WebGLDebugSymbolMode.Off;
            PlayerSettings.WebGL.exceptionSupport =
                WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.showDiagnostics = false;
            PlayerSettings.WebGL.initialMemorySize = 128;
        }

        private static void WriteLocalHostingHeaders(string outputPath)
        {
            Directory.CreateDirectory(outputPath);
            File.WriteAllText(
                Path.Combine(outputPath, "_headers"),
                @"/*
  X-Content-Type-Options: nosniff
  Cache-Control: no-store

/Build/*.wasm
  Content-Type: application/wasm

/Build/*.js
  Content-Type: application/javascript

/Build/*.data
  Content-Type: application/octet-stream
");
        }
    }
}
