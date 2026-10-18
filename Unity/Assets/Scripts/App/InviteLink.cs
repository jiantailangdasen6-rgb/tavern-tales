#nullable enable
using System;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;

namespace Tavern.Unity.App
{
    /// <summary>
    /// Invite links: the host copies https://…/join.html#CODE (Discord only links https), the relay page on GitHub Pages
    /// (Web/, Tools/DeployPages.ps1) opens tavern-tales://join/CODE, and Windows starts the game with that as an argument.
    /// Every start of the game registers the tavern-tales:// scheme for the current user (HKCU, no admin) to this exe,
    /// so a game moved to another folder still opens.
    /// </summary>
    public static class InviteLink
    {
        public const string Scheme = "tavern-tales";
        const string Page = "https://jiantailangdasen6-rgb.github.io/tavern-tales/join.html#";

        public static string For(string code) => Page + code.Trim().ToUpperInvariant();

        /// <summary>The room code when the game was opened from an invite link (tavern-tales://join/CODE), else null.</summary>
        public static string? CodeFromArgs()
        {
            foreach (var arg in Environment.GetCommandLineArgs())
            {
                var m = Regex.Match(arg, "^" + Scheme + @"://join/([A-Za-z0-9]{4,12})/?$");
                if (m.Success) return m.Groups[1].Value.ToUpperInvariant();
            }
            return null;
        }

        /// <summary>Points tavern-tales:// at this exe (Windows builds only; skipped by automated runs).</summary>
        public static void Register()
        {
            if (Application.platform != RuntimePlatform.WindowsPlayer || AppMain.Flag("-autoquit") || AppMain.Flag("-noregister")) return;
            string exe;
            try { exe = Process.GetCurrentProcess().MainModule!.FileName; }
            catch (Exception) { return; }
            var key = @"HKCU\Software\Classes\" + Scheme;
            Task.Run(() =>
            {
                Reg($"add \"{key}\" /ve /d \"URL:酒場の語り部\" /f");
                Reg($"add \"{key}\" /v \"URL Protocol\" /d \"\" /f");
                Reg($"add \"{key}\\DefaultIcon\" /ve /d \"\\\"{exe}\\\",0\" /f");
                Reg($"add \"{key}\\shell\\open\\command\" /ve /d \"\\\"{exe}\\\" \\\"%1\\\"\" /f");
            });
        }

        static void Reg(string args)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo("reg.exe", args) { UseShellExecute = false, CreateNoWindow = true });
                p?.WaitForExit(5000);
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning("[InviteLink] " + e.Message); }
        }
    }
}
