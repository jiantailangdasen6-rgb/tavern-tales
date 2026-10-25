#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Tavern.Core.Play;

namespace Tavern.Core.Story
{
    /// <summary>
    /// Reads a story sheet. Unknown enum names and malformed conditions throw FormatException with the path,
    /// so the checker (StoryCheck) and the tests point at the line to fix.
    /// </summary>
    public static class StoryLoader
    {
        public static StoryDef Load(string json)
        {
            var o = JObject.Parse(json);
            var s = new StoryDef
            {
                Id = Str(o, "id"), Title = Str(o, "title"), Kind = Str(o, "kind"), Long = Str(o, "length") == "long",
                Region = Str(o, "region"), Darkness = Str(o, "darkness"), Intro = Str(o, "intro"), Advance = (int?)o["advance"] ?? 0,
                Start = Str(o, "start"), Truth = Str(o, "truth"), Choice = Str(o, "choice"),
            };
            if (o["goal"] is JObject goal) { s.GoalText = Str(goal, "text"); s.Goal = ReadWhen(goal["when"], "goal"); }
            foreach (var l in Arr(o, "losses"))
                s.Losses.Add(new Loss { Id = Str(l, "id"), Text = Str(l, "text"), Heavy = (bool?)l["heavy"] ?? false, When = ReadWhen(l["when"], "loss " + Str(l, "id")) });
            foreach (var c in Arr(o, "clocks")) s.Clocks.Add(ReadClock(c));
            foreach (var c in Arr(o, "conclusions"))
            {
                var con = new Conclusion { Id = Str(c, "id"), Text = Str(c, "text"), RewardClock = Str(c, "rewardClock") };
                con.Clues.AddRange(Strs(c, "clues"));
                s.Conclusions.Add(con);
            }
            foreach (var c in Arr(o, "clues")) s.Clues.Add(ReadClue(c));
            s.Fragments.AddRange(Strs(o, "fragments"));
            foreach (var g in Arr(o, "personalGoals"))
                s.PersonalGoals.Add(new PersonalGoal { Id = Str(g, "id"), Text = Str(g, "text"), When = ReadWhen(g["when"], "goal " + Str(g, "id")) });
            foreach (var p in Arr(o, "places")) s.Places.Add(ReadPlace(p));
            foreach (var m in Arr(o, "mechanisms")) s.Mechanisms.Add(ReadMechanism(m));
            foreach (var i in Arr(o, "items"))
                s.Items.Add(new StoryItem { Item = ClassBook.ReadItem(i), At = Str(i, "at"), Note = Str(i, "note") });
            foreach (var p in Arr(o, "people")) s.People.Add(ReadPerson(p));
            foreach (var a in Arr(o, "acts"))
            {
                var act = new Act { N = (int)a["n"]!, Title = Str(a, "title"), Goal = Str(a, "goal"), Advance = ReadWhen(a["advance"], $"act {a["n"]}") };
                act.OnStart.AddRange(ReadActions(a["onStart"], $"act {a["n"]}"));
                s.Acts.Add(act);
            }
            if (o["climax"] is JObject cl)
            {
                s.Climax.Text = Str(cl, "text");
                s.Climax.Starts = ReadWhen(cl["starts"], "climax");
                s.Climax.OnStart.AddRange(ReadActions(cl["onStart"], "climax"));
                foreach (var so in Arr(cl, "solutions"))
                {
                    var sol = new Solution { Id = Str(so, "id"), Text = Str(so, "text"), When = ReadWhen(so["when"], "solution " + Str(so, "id")) };
                    sol.Then.AddRange(ReadActions(so["then"], "solution " + Str(so, "id")));
                    s.Climax.Solutions.Add(sol);
                }
            }
            foreach (var h in Arr(o, "hooks")) s.Hooks.Add(new Hook { Place = Str(h, "place"), Role = Str(h, "role") });
            if (o["endings"] is JObject e) { s.EndingSuccess = Str(e, "success"); s.EndingBitter = Str(e, "bitter"); s.EndingFailure = Str(e, "failure"); }
            return s;
        }

        static StoryClock ReadClock(JToken c)
        {
            var id = Str(c, "id");
            var def = new ClockDef
            {
                Id = id, Name = Str(c, "name"), SecretName = Str(c, "secret"), Segments = (int?)c["segments"] ?? 6,
                Every = (int?)c["every"] ?? 0, Good = (bool?)c["good"] ?? false, FullText = Str(c, "fullText"),
                Speed = Str(c, "speed") switch
                {
                    "every" => ClockSpeed.EveryTurns, "events" => ClockSpeed.EventsOnly, "combat" => ClockSpeed.CombatRounds,
                    var x => throw new FormatException($"clock {id}: unknown speed '{x}' (every / events / combat)"),
                },
            };
            def.Acts.AddRange(Ints(c, "acts"));
            foreach (var f in Strs(c, "onFullKinds")) def.OnFull.Add(Enum<ClockFull>(f, $"clock {id}"));
            var sc = new StoryClock { Def = def, Visible = ReadWhen(c["visible"], $"clock {id} visible") };
            foreach (var r in Arr(c, "rewinds"))
            {
                var rid = Str(r, "id");
                def.Rewinds.Add(new ClockRewind { Id = rid, Text = Str(r, "text"), Amount = (int?)r["amount"] ?? 2 });
                sc.RewindWhen[rid] = ReadWhen(r["when"], $"clock {id} rewind {rid}");
            }
            sc.OnFull.AddRange(ReadActions(c["onFull"], $"clock {id}"));
            return sc;
        }

        static Clue ReadClue(JToken c)
        {
            var id = Str(c, "id");
            var clue = new Clue
            {
                Id = id, Text = Str(c, "text"), Conclusion = Str(c, "conclusion"), Place = Str(c, "place"), Person = Str(c, "person"),
                How = Str(c, "how"), Important = (bool?)c["important"] ?? true, Requires = ReadWhen(c["requires"], $"clue {id}"),
            };
            clue.Acts.AddRange(Ints(c, "acts"));
            var get = c["get"] as JObject;
            var access = get == null ? "free" : Str(get, "type");
            switch (access)
            {
                case "free": clue.Access = ClueAccess.Free; break;
                case "check":
                    clue.Access = ClueAccess.Check;
                    if (!AbilityNames.TryParse(Str(get!, "ability"), out var a)) throw new FormatException($"clue {id}: ability");
                    if (!AbilityNames.TryParseTier(Str(get!, "tier"), out var t)) throw new FormatException($"clue {id}: tier");
                    clue.Ability = a; clue.Tier = t;
                    break;
                case "request":
                    clue.Access = ClueAccess.Request;
                    clue.Request = Enum<RequestSize>(Str(get!, "size"), $"clue {id}");
                    break;
                default: throw new FormatException($"clue {id}: get.type '{access}' (free / check / request)");
            }
            return clue;
        }

        static Place ReadPlace(JToken p)
        {
            var id = Str(p, "id");
            var place = new Place
            {
                Id = id, Name = Str(p, "name"), Art = Str(p, "art"), Look = Str(p, "look"), Secret = Str(p, "secret"),
                Tense = (bool?)p["tense"] ?? false, Shop = (bool?)p["shop"] ?? false,
            };
            place.Acts.AddRange(Ints(p, "acts"));
            foreach (var l in Arr(p, "links"))
            {
                if (l.Type == JTokenType.String) { place.Links.Add(new Link { To = (string)l! }); continue; }
                place.Links.Add(new Link { To = Str(l, "to"), Open = ReadWhen(l["open"], $"place {id} link"), Text = Str(l, "text") });
            }
            foreach (var r in Arr(p, "rules"))
                place.Rules.Add(new PlaceRule { Text = Str(r, "text"), Clock = Str(r, "clock"), Amount = (int?)r["amount"] ?? 1 });
            place.Enemies.AddRange(Strs(p, "enemies"));
            foreach (var t in Arr(p, "traps"))
            {
                var trap = new Trap { Text = Str(t, "text"), Damage = (int?)t["damage"] ?? 3 };
                if (t["ability"] != null) { AbilityNames.TryParse(Str(t, "ability"), out var a); trap.Ability = a; }
                if (t["tier"] != null) { AbilityNames.TryParseTier(Str(t, "tier"), out var tt); trap.Tier = tt; }
                if (t["status"] != null) trap.Status = Enum<StatusKind>(Str(t, "status"), $"place {id} trap");
                place.Traps.Add(trap);
            }
            return place;
        }

        static MechanismDef ReadMechanism(JToken m)
        {
            var id = Str(m, "id");
            var d = new MechanismDef
            {
                Id = id, Name = Str(m, "name"), Place = Str(m, "place"), Event = Str(m, "event"), NoCheck = (bool?)m["noCheck"] ?? false,
                CombatEffect = Str(m, "combatEffect"), CombatAmount = (int?)m["combatAmount"] ?? 0, Uses = (int?)m["uses"] ?? 1,
            };
            if (!d.NoCheck)
            {
                if (!AbilityNames.TryParse(Str(m, "ability"), out var a)) throw new FormatException($"mechanism {id}: ability");
                if (!AbilityNames.TryParseTier(Str(m, "tier"), out var t)) throw new FormatException($"mechanism {id}: tier");
                d.Ability = a; d.Tier = t;
            }
            return d;
        }

        static Person ReadPerson(JToken p)
        {
            var id = Str(p, "id");
            var person = new Person
            {
                Id = id, Name = Str(p, "name"), Portrait = Str(p, "portrait"), At = Str(p, "at"), Role = Str(p, "role"),
                Voice = Str(p, "voice"), Wants = Str(p, "wants"), Fears = Str(p, "fears"), Secret = Str(p, "secret"), Lie = Str(p, "lie"),
                Attitude = Enum<Attitude>(Str(p, "attitude", "Neutral"), $"person {id}"),
                Joins = Enum<JoinWillingness>(Str(p, "joins", "Never"), $"person {id}"),
                JoinWhen = ReadWhen(p["joinWhen"], $"person {id} join"), JoinNote = Str(p, "joinNote"),
                Escort = (bool?)p["escort"] ?? false,
                Opening = (bool?)p["opening"] ?? false,
                Build = Enum<Build>(Str(p, "build", "Weak"), $"person {id}"),
                CombatRole = Enum<CompanionRole>(Str(p, "combatRole", "Attack"), $"person {id}"),
                Enemy = Str(p, "enemy"), Mastermind = (bool?)p["mastermind"] ?? false, OnDeath = Str(p, "onDeath"),
            };
            person.Clues.AddRange(Strs(p, "clues"));
            if (p["betrays"] != null) { person.CanBetray = true; person.Betrays = ReadWhen(p["betrays"], $"person {id} betrayal"); }
            return person;
        }

        // ---- conditions and actions ----

        /// <summary>[[c, c], [c]] (any group), [c, c] (one group) or {c} (one condition).</summary>
        public static When ReadWhen(JToken? t, string where)
        {
            var w = new When();
            if (t == null || t.Type == JTokenType.Null) return w;
            if (t is JObject single) { w.Groups.Add(new List<Condition> { ReadCondition(single, where) }); return w; }
            var arr = (JArray)t;
            if (arr.Count == 0) return w;
            if (arr.All(x => x is JArray))
                foreach (JArray g in arr) w.Groups.Add(g.Select(c => ReadCondition((JObject)c, where)).ToList());
            else
                w.Groups.Add(arr.Select(c => ReadCondition((JObject)c, where)).ToList());
            return w;
        }

        /// <summary>
        /// {"person": id, "is": state} / {"person": id, "attitude": "Friendly"} / {"person": id, "at": place}
        /// {"item": id, "at": place} / {"item": id, "heldBy": "party"|"self"|id} / {"item": id, "gone": true} / {"itemKind": kind, "count": n, "heldBy": ...}
        /// {"place": id} / {"place": id, "who": "any"|"self"|id} / {"event": id} / {"clock": id, "is": "full"|"not_full"} / {"clock": id, "atLeast": n}
        /// {"conviction": id}; any of them with "not": true.
        /// </summary>
        public static Condition ReadCondition(JObject o, string where)
        {
            var c = new Condition { Not = (bool?)o["not"] ?? false };
            if (o["person"] != null)
            {
                c.Type = ConditionType.Person; c.Id = Str(o, "person");
                if (o["attitude"] != null) { c.Is = "attitude"; c.Value = Enum<Attitude>(Str(o, "attitude"), where).ToString(); }
                else if (o["at"] != null) { c.Is = "at"; c.Value = Str(o, "at"); }
                else c.Is = Str(o, "is", "alive");
                if (!new[] { "alive", "down", "asleep", "retreated", "dead", "with_party", "at", "attitude" }.Contains(c.Is))
                    throw new FormatException($"{where}: person state '{c.Is}'");
            }
            else if (o["itemKind"] != null)
            {
                c.Type = ConditionType.Item; c.Kind = Enum<ItemKind>(Str(o, "itemKind"), where); c.Count = (int?)o["count"] ?? 1; c.Value = Str(o, "heldBy", "party");
            }
            else if (o["item"] != null)
            {
                c.Type = ConditionType.Item; c.Id = Str(o, "item");
                if (o["at"] != null) { c.Is = "at"; c.Value = Str(o, "at"); }
                else if (o["heldBy"] != null) { c.Is = "held_by"; c.Value = Str(o, "heldBy"); }
                else if ((bool?)o["gone"] == true) c.Is = "gone";
                else throw new FormatException($"{where}: item condition needs at / heldBy / gone");
            }
            else if (o["place"] != null)
            {
                c.Type = ConditionType.Place; c.Id = Str(o, "place");
                var who = Str(o, "who");
                if (who == "any") c.Is = "any"; else c.Value = who;
            }
            else if (o["event"] != null) { c.Type = ConditionType.Event; c.Id = Str(o, "event"); }
            else if (o["clock"] != null)
            {
                c.Type = ConditionType.Clock; c.Id = Str(o, "clock");
                if (o["atLeast"] != null) { c.Is = "at_least"; c.Count = (int)o["atLeast"]!; }
                else c.Is = Str(o, "is", "full");
            }
            else if (o["conviction"] != null) { c.Type = ConditionType.Conviction; c.Id = Str(o, "conviction"); }
            else throw new FormatException($"{where}: unknown condition {o.ToString(Newtonsoft.Json.Formatting.None)}");
            return c;
        }

        /// <summary>
        /// {"event": id} / {"person": id, "state": "Dead"} / {"person": id, "moveTo": place} / {"item": id, "moveTo": place}
        /// {"clock": id, "push": n} / {"clock": id, "reveal": true} / {"enemies": [ids], "at": place|"party"} / {"climax": true} / {"text": "..."}.
        /// "text" may sit beside any of them (what the AI is told happened).
        /// </summary>
        public static List<StoryAction> ReadActions(JToken? t, string where)
        {
            var list = new List<StoryAction>();
            if (t == null || t.Type == JTokenType.Null) return list;
            foreach (JObject o in t is JArray a ? a : new JArray(t))
            {
                var act = new StoryAction { Text = Str(o, "text") };
                if (o["event"] != null) { act.Kind = "event"; act.Id = Str(o, "event"); }
                else if (o["person"] != null && o["state"] != null) { act.Kind = "person_state"; act.Id = Str(o, "person"); act.Value = Enum<PersonState>(Str(o, "state"), where).ToString(); }
                else if (o["person"] != null && o["moveTo"] != null) { act.Kind = "person_move"; act.Id = Str(o, "person"); act.Value = Str(o, "moveTo"); }
                else if (o["item"] != null && o["moveTo"] != null) { act.Kind = "item_move"; act.Id = Str(o, "item"); act.Value = Str(o, "moveTo"); }
                else if (o["clock"] != null && o["push"] != null) { act.Kind = "clock_push"; act.Id = Str(o, "clock"); act.Amount = (int)o["push"]!; }
                else if (o["clock"] != null && o["reveal"] != null) { act.Kind = "clock_reveal"; act.Id = Str(o, "clock"); }
                else if (o["enemies"] != null) { act.Kind = "enemies"; act.Enemies.AddRange(Strs(o, "enemies")); act.Value = Str(o, "at", "party"); }
                else if (o["climax"] != null) act.Kind = "climax";
                else if (o["text"] != null) act.Kind = "text";
                else throw new FormatException($"{where}: unknown action {o.ToString(Newtonsoft.Json.Formatting.None)}");
                list.Add(act);
            }
            return list;
        }

        // ---- small helpers ----

        static string Str(JToken t, string name, string fallback = "") => (string?)t[name] ?? fallback;
        static IEnumerable<JToken> Arr(JToken t, string name) => (t[name] as JArray) ?? new JArray();
        static IEnumerable<string> Strs(JToken t, string name) => Arr(t, name).Select(x => (string)x!);
        static IEnumerable<int> Ints(JToken t, string name) => Arr(t, name).Select(x => (int)x);

        static T Enum<T>(string text, string where) where T : struct =>
            System.Enum.TryParse<T>(text, true, out var v) ? v : throw new FormatException($"{where}: '{text}' is not a {typeof(T).Name}");
    }
}
