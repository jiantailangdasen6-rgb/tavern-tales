#nullable enable
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Tavern.Unity.Editor
{
    /// <summary>
    /// Batch builds:
    ///   -executeMethod Tavern.Unity.Editor.BuildScript.BuildWindows [-out PATH]
    /// Default output: Builds/windows/Tavern.exe at the repository root (gitignored).
    /// </summary>
    public static class BuildScript
    {
        static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

        static string? ArgValue(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        public static void BuildWindows()
        {
            ProjectSetup.Apply();
            var output = ArgValue("-out") ?? Path.Combine(RepoRoot, "Builds", "windows", "Tavern.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ProjectSetup.ScenePath },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            var s = report.summary;
            Debug.Log($"[BuildScript] {s.result} {output} {s.totalSize / 1024 / 1024} MB, {s.totalErrors} errors");
            if (s.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
