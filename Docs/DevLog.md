# 開発ログ（再開するときはここから）

## 現在の状態（2026-10-17）
- 企画の相談は完了。決定事項は `Docs/00_Decisions.md`、技術設計は `Docs/02_TechnicalDesign.md`、画風は `Docs/art/style-guide.md`。
- **M0 完了**:
  - Core（`Core/Tavern.Core`）: 20 面の判定（大成功・大失敗、手伝い +2、運命のコインの振り直し）、HP と倒れる、死のサイコロ、
    GM との会話（`GmSession`: ツール `request_check` / `suggest_actions`、判定はゲームが振る）、Ollama のつなぎ口。テスト 20 件。
  - `Core/Tavern.Cli`: Unity 抜きで GM と話す（`dotnet run --project Core/Tavern.Cli -- --script Core/Tavern.Cli/scripts/shrine.txt`）。
  - Unity 6000.6.2f1 のプロジェクト（URP、Core をローカルパッケージで参照）、仮の起動画面、バッチの設定とビルド、スクリーンショット。
  - Ollama（`D:\tools\ollama`、`qwen3:8b` を `D:\tools\ollama-models`）。
- リポジトリ: https://github.com/jiantailangdasen6-rgb/tavern-tales （公開）

## コマンド
```
dotnet test Core/Tavern.slnx
powershell -ExecutionPolicy Bypass -File Tools/Ollama.ps1            # 手元の AI を起動（-Stop で停止）
dotnet run --project Core/Tavern.Cli -- --script Core/Tavern.Cli/scripts/shrine.txt --seed 7
powershell -ExecutionPolicy Bypass -File Tools/RunUnity.ps1 -Method Tavern.Unity.Editor.BuildScript.BuildWindows
Builds/windows/Tavern.exe -screenshot Logs/shots/x.png -shotat 3 -autoquit 4
```

## 手元の AI（qwen3:8b）を試して分かったこと（2026-10-17、`Logs/gm-shrine-1.txt`）
- 良い: ツールでの判定の依頼はできる（罠を外す・碑文を読む・扉を蹴る）。判定の結果どおりに語る。1 手 7〜13 秒（最初だけモデルの読み込みで 74 秒）。
- 問題:
  1. 最初の場面で秘密（碑文の中身、騎士の名の手がかり）をいきなり明かした。
  2. 冒険者の行動と台詞を勝手に決めた（ロッコが剣を拾う、「これ、騎士の遺物だな」）。
  3. 語りが長い（2〜4 文を守らない）。毎回「さて、若き勇者たちが何をしようとおもう？」で締める。
  4. `suggest_actions` を一度も呼ばなかった。
  5. 日本語が時々おかしい（「呼びよぶ」「背負いしめる」）。
  6. 「毒矢が刺さって倒れる」と語ったが、ゲームの HP は変わらない → ダメージや状態を変えるツールが要る（M1）。
- 対策の候補: 指示文の調整（禁止事項を短く強く、良い例・悪い例を添える）、語りの後に足りないツールを促す、
  Gemini の無料枠（日本語と指示の守り方がずっと良い見込み）、もう一回り大きい手元のモデル（VRAM 8 GB を超えるので遅くなる）。

## 次（M1: 1 人で一晩）
- シナリオの型 1 本と設定表、ダメージ・回復・持ち物・場面の移動・戦闘開始のツール、戦闘の流れ、試し絵どおりの画面、仮絵、途中保存、簡単な 3D のサイコロ。
