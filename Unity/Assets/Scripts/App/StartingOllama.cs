#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Tavern.Core.Gm;

namespace Tavern.Unity.App
{
    /// <summary>
    /// The local AI (Ollama), started by the game the first time it is needed and not running: when every free Gemini
    /// quota is used up, the evening goes on without the host opening anything. Looks for the zip install of
    /// Tools/Ollama.ps1 (D:\tools\ollama, models in D:\tools\ollama-models) and the regular installer's copy.
    /// A server the game started is stopped when the game quits.
    /// </summary>
    public sealed class StartingOllama : ILlmClient
    {
        readonly OllamaClient _inner;
        readonly ConcurrentQueue<string> _notices;
        Task<bool>? _starting;
        static Process? _started;

        static readonly HttpClient Probe = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

        public StartingOllama(OllamaClient inner, ConcurrentQueue<string> notices) { _inner = inner; _notices = notices; }

        public string Name => _inner.Name;

        public async Task<LlmReply> ChatAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDef> tools, string? forceTool = null, CancellationToken ct = default)
        {
            try { return await _inner.ChatAsync(messages, tools, forceTool, ct).ConfigureAwait(false); }
            catch (HttpRequestException)
            {
                // not running: start it once and try again (the first reply also loads the model, about a minute)
                _starting ??= Task.Run(Start);
                if (!await _starting.ConfigureAwait(false)) throw;
            }
            return await _inner.ChatAsync(messages, tools, forceTool, ct).ConfigureAwait(false);
        }

        static string? FindExe() => new[]
        {
            @"D:\tools\ollama\ollama.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe"),
        }.FirstOrDefault(File.Exists);

        bool Start()
        {
            var exe = FindExe();
            if (exe == null) return false;
            _notices.Enqueue("手元の AI（Ollama）を起動しています。最初の語りまで数分かかることがあります。");
            var psi = new ProcessStartInfo(exe, "serve") { UseShellExecute = false, CreateNoWindow = true };
            var models = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(exe)!)!, "ollama-models");
            if (Directory.Exists(models)) psi.EnvironmentVariables["OLLAMA_MODELS"] = models;
            try { _started = Process.Start(psi); }
            catch (Exception e) { UnityEngine.Debug.LogWarning("[LocalAi] " + e.Message); return false; }
            for (int i = 0; i < 30; i++)
            {
                Thread.Sleep(1000);
                if (Up()) return true;
            }
            return false;
        }

        static bool Up()
        {
            try { return Probe.GetAsync("http://127.0.0.1:11434/api/version").Result.IsSuccessStatusCode; }
            catch (Exception) { return false; }
        }

        /// <summary>Stops the server the game started (AppMain calls this when quitting).</summary>
        public static void StopStarted()
        {
            try { if (_started != null && !_started.HasExited) _started.Kill(); }
            catch (Exception) { }
        }
    }
}
