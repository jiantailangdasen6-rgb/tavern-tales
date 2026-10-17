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
- **オーナーが AI Studio で確認: gemini-3.8-flash の無料枠は 1 日 20 回**（一晩は 150〜200 回の見込みなので足りない）。
- 軽量版も試した（`--thinking minimal`）:
  - gemini-3.5-flash-lite: 語りは十分良い（毒矢・碑文・燭台を盗む）、1 手 2〜3 秒、行動案も出す。エリスの判定を頼まなかった。毎回「さて〜かね」で締める。
  - gemini-3.1-flash-lite: 道具の呼び出しを本文に文字で書いてしまう（`suggest_actions{...}`）→ 使えない。
- 有料にした場合の目安（ネットの情報。2026 年末までの導入価格 入力 0.75 / 出力 3.75 ドル毎 100 万トークン、2027 年から倍）:
  一晩 150 回 × 入力 1 万トークンなら 1〜3 ドル程度（履歴を要約して短くすればもっと安い）。公式の価格表で要確認。
- **gemini-3.5-flash-lite の無料枠は 1 日 500 回**（オーナーが確認）→ 普段は Lite、山場は 3.8 Flash、最後に手元の AI、に決定。
  `GmSession(..., highlightLlm)` と `PlayAsync(input, highlight)`。CLI はスクリプトの行頭 `!` が山場。
  混ぜて試した結果（`Logs/gm-shrine-mixed.txt`）: 手ごとに AI が替わっても問題なく続く。Lite の手は行動案を出さず、
  判定も 1 人分しか頼まないことがある → M1 で指示文と「足りない道具を促す」仕組みで直す。
- gemini-3-flash-preview の無料枠（1 日 20 回）は試しで使い切った。
- **1 つの手の順番をゲームが固定した**（AI の気分に任せない）:
  1. `plan_checks` を強制（Gemini の `toolConfig` mode ANY）: 宣言した全員を 1 人ずつ並べ、`needs_check` を答えさせる → ゲームが全部振る
  2. `narrate` を強制: 語り（`text`）と行動案（`suggestions`）を必ず一緒に返す
  物語の始まりは 2 だけ（`OpenAsync`）。1 手 = 2 回の呼び出し（一晩 100 手でも 200 回 < 500 回）。
  手元の AI は強制できないので、その道具だけを渡す（文章で返したらそのまま語りとして使う）。
- 結果（`Logs/gm-shrine-lite-forced2.txt`、Flash-Lite だけ）: 行動案が毎回出る。扉を蹴る・罠・盗みに判定が付く
  （見張る・碑文を読む・一礼は判定なし）。行動案に未登場の敵を書かない決まりも効いた。1 手 3〜4 秒。
  残り: 「ガレスが盾を突き出し」のように宣言にない動きを足すことがある。
- 直したこと: 行動案だけの返事のあとに次の宣言を送ると Gemini が空の返事をした → 行動案の呼び出しは履歴に残さない。
  判定を頼むときは先に語らない、と指示に足した。503 は少し待って送り直す。`FallbackLlm`（Gemini → 手元の AI）を用意した。

## 次（M1: 1 人で一晩）
- シナリオの型 1 本と設定表、ダメージ・回復・持ち物・場面の移動・戦闘開始のツール、戦闘の流れ、試し絵どおりの画面、仮絵、途中保存、簡単な 3D のサイコロ。
