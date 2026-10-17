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

## Gemini を試して分かったこと（2026-10-17、`Logs/gm-shrine-*.txt`）
- キーはホストのユーザー環境変数 `GEMINI_API_KEY`（オーナーが設定済み。表示しない）。`GeminiClient`、`--engine gemini`。
- 使えるモデル（このキー）: gemini-3-flash-preview / 3.5-flash / 3.6〜3.8-flash / 3.1-pro-preview など。2.5-flash は新規利用者には使えない（404）。
- 祠の場面（4 手）の比較:
  | モデル | 語りの質 | 判定の依頼 | 行動案 | 1 手 | 呼び出し |
  |---|---|---|---|---|---|
  | 手元 qwen3:8b | 秘密を漏らす・キャラを操る・日本語が崩れる | できる | 出さない | 7〜13 秒 | — |
  | gemini-3-flash-preview | 良い | 全部頼む | 毎回出す | 8〜18 秒 | 503（混雑）で止まった |
  | gemini-3.5-flash | 良いが長め | 扉を蹴るのを頼まなかった | 途中から出さない | 4〜6 秒（1 回 92 秒待ち） | 10 回 |
  | **gemini-3.8-flash** | **一番良い**（短めの段落、ボスの登場、牢の若者） | 扉を蹴るのを頼まなかった | 毎回出す | 6〜9 秒 | 6 回 |
  → 既定は **gemini-3.8-flash**。
- 無料枠: gemini-3-flash-preview は **1 分 5 回** で 429 が出た（`retryDelay` を待って送り直すようにした）。1 日の上限は未確認
  （ネットの情報では 3.5〜3.8 Flash は 1 日 20 回ほどという話もある → AI Studio の rate limit の画面で確かめる）。
- 直したこと: 行動案だけの返事のあとに次の宣言を送ると Gemini が空の返事をした → 行動案の呼び出しは履歴に残さない。
  判定を頼むときは先に語らない、と指示に足した。503 は少し待って送り直す。`FallbackLlm`（Gemini → 手元の AI）を用意した。

## 次（M1: 1 人で一晩）
- シナリオの型 1 本と設定表、ダメージ・回復・持ち物・場面の移動・戦闘開始のツール、戦闘の流れ、試し絵どおりの画面、仮絵、途中保存、簡単な 3D のサイコロ。
