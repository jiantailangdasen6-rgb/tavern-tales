#nullable enable
using System;
using System.Net.Http;

namespace Tavern.Core.Gm
{
    /// <summary>
    /// Plain-language notices for the host when the free AI quota runs out: which model takes over, and when the
    /// daily quota comes back (Gemini resets it at midnight Pacific time, 16:00 or 17:00 in Japan).
    /// </summary>
    public static class AiNotices
    {
        static readonly Lazy<TimeZoneInfo?> Pacific = new Lazy<TimeZoneInfo?>(() =>
        {
            foreach (var id in new[] { "Pacific Standard Time", "America/Los_Angeles" })
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }
            return null;
        });

        /// <summary>The next midnight in Pacific time after now (UTC-8 all year if the zone is unknown).</summary>
        public static DateTimeOffset NextReset(DateTimeOffset now)
        {
            var tz = Pacific.Value;
            if (tz == null)
            {
                var pst = now.ToOffset(TimeSpan.FromHours(-8));
                return new DateTimeOffset(pst.Date.AddDays(1), pst.Offset);
            }
            var local = TimeZoneInfo.ConvertTime(now, tz);
            var midnight = local.Date.AddDays(1);
            return new DateTimeOffset(midnight, tz.GetUtcOffset(midnight));
        }

        /// <summary>"16:00" in the given offset (the player's clock).</summary>
        public static string ResetClock(DateTimeOffset now, TimeSpan localOffset) => NextReset(now).ToOffset(localOffset).ToString("HH:mm");

        /// <summary>"Gemini 3.8 Flash", "Gemini 3.5 Flash-Lite", "手元の AI" from a backend's name.</summary>
        public static string Model(ILlmClient c)
        {
            var name = c.Name;
            if (name.StartsWith("ollama", StringComparison.Ordinal)) return "手元の AI";
            if (!name.StartsWith("gemini:", StringComparison.Ordinal)) return name;
            var parts = name.Substring("gemini:gemini-".Length).Split('-');
            var words = new System.Collections.Generic.List<string> { "Gemini" };
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == "flash" && i + 1 < parts.Length && parts[i + 1] == "lite") { words.Add("Flash-Lite"); i++; }
                else words.Add(parts[i].Length > 0 && char.IsLetter(parts[i][0]) ? char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1) : parts[i]);
            }
            return string.Join(" ", words);
        }

        /// <summary>The notice when a FallbackLlm moves on to its next backend.</summary>
        public static string Switched(ILlmClient from, ILlmClient to, Exception why, DateTimeOffset now, TimeSpan localOffset)
        {
            var daily = why is LlmRateLimitException r && r.Daily;
            var reason = daily ? $"今日の {Model(from)} の無料枠を使い切った"
                : why is LlmUnavailableException ? $"{Model(from)} が混み合っている"
                : $"{Model(from)} の 1 分あたりの枠がいっぱい";
            var next = Model(to) == "手元の AI" ? "手元の AI で語ります（語りの質は少し落ちます）" : $"ここからは {Model(to)} が語ります";
            var back = daily ? $"枠は {ResetClock(now, localOffset)} ごろに戻ります。" : "しばらくしたら元に戻します。";
            return $"{reason}ので、{next}。{back}";
        }

        /// <summary>
        /// The notice when no AI could answer a turn (null: not about the quota or the connection).
        /// localOnly: every Gemini model is out for today and the local AI was the last resort.
        /// </summary>
        public static string? Failed(Exception ex, bool localOnly, DateTimeOffset now, TimeSpan localOffset)
        {
            var saved = "ここまでは保存されているので、「続きから」でいつでも再開できます。";
            if (ex is LlmRateLimitException || localOnly && ex is HttpRequestException)
                return $"今日の AI の無料枠を使い切りました（{ResetClock(now, localOffset)} ごろに戻ります）。" +
                       "手元の AI（Ollama）を起動すれば、今すぐ続けられます。" + saved;
            if (ex is HttpRequestException)
                return "AI につながりません。インターネットの接続を確かめて、もう一度宣言してください。" + saved;
            if (ex is LlmUnavailableException)
                return "AI が混み合っていて答えられませんでした。少し待って、もう一度宣言してください。";
            return null;
        }
    }
}
