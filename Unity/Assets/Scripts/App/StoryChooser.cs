#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Tavern.Core.Content;
using Tavern.Unity.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Tavern.Unity.App
{
    /// <summary>What story tonight: the prepared one, one the storyteller makes up (any type, or a chosen type), and a one-line request.</summary>
    public sealed class StoryChoice
    {
        public bool Prepared = true;
        /// <summary>Which prepared story (Prepared true; null: the first).</summary>
        public string? PreparedId;
        /// <summary>Null = the storyteller picks the type.</summary>
        public string? TemplateId;
        public string Request = "";
    }

    public static class StoryChooser
    {
        public static Func<StoryChoice> Build(Transform parent, StoryContent story, IReadOnlyList<ScenarioDef> preparedStories, float x, float y)
        {
            var choice = new StoryChoice { PreparedId = preparedStories[0].Id };
            int templateIndex = 0, preparedIndex = 0;
            Button prepared = null!, auto = null!, typed = null!;
            InputField request = null!;
            UiKit.Label(parent, "今宵の物語", 18, Theme.Dim, x, y + 16, 120, 28);
            prepared = UiKit.Button(parent, "", () =>
            {
                if (choice.Prepared) preparedIndex = (preparedIndex + 1) % preparedStories.Count;   // pressed again: the next story
                choice.Prepared = true; choice.PreparedId = preparedStories[preparedIndex].Id; Refresh();
            }, x + 120, y, 250, 60, 18);
            auto = UiKit.Button(parent, "おまかせ\n<size=14>語り部が作る</size>", () => { choice.Prepared = false; choice.TemplateId = null; Refresh(); }, x + 380, y, 180, 60, 18);
            typed = UiKit.Button(parent, "", () =>
            {
                if (!choice.Prepared && choice.TemplateId != null) templateIndex = (templateIndex + 1) % story.Templates.Count;
                choice.Prepared = false; choice.TemplateId = story.Templates[templateIndex].Id; Refresh();
            }, x + 570, y, 260, 60, 18);
            request = UiKit.Input(parent, "一言の注文（任意）例: 海賊が出る話がいい", x + 840, y + 6, 420, 48, 18);
            request.onValueChanged.AddListener(v => choice.Request = v.Trim());
            if (AppMain.Arg("-story") is string pick)   // tests: -story prepared|prepared:<story id>|auto|<template id>
            {
                choice.Prepared = pick.StartsWith("prepared");
                choice.TemplateId = pick == "auto" || choice.Prepared ? null : pick;
                if (pick.StartsWith("prepared:")) { choice.PreparedId = pick.Substring(9); preparedIndex = Math.Max(0, preparedStories.ToList().FindIndex(s => s.Id == choice.PreparedId)); }
            }

            void Refresh()
            {
                var t = story.Templates[templateIndex];
                prepared.SetText($"用意された物語（次へ）\n<size=14>「{preparedStories[preparedIndex].Title}」</size>");
                typed.SetText($"型を選ぶ（押すたびに次へ）\n<size=14>「{t.Name}」</size>");
                prepared.GetComponent<Image>().color = choice.Prepared ? Theme.Hex("6b4f24") : Theme.Hex("2e241a");
                auto.GetComponent<Image>().color = !choice.Prepared && choice.TemplateId == null ? Theme.Hex("6b4f24") : Theme.Hex("2e241a");
                typed.GetComponent<Image>().color = !choice.Prepared && choice.TemplateId != null ? Theme.Hex("6b4f24") : Theme.Hex("2e241a");
                request.interactable = !choice.Prepared;
            }
            Refresh();
            return () => choice;
        }
    }
}
