# 開発ログ（再開するときはここから）

## 現在の状態（2026-10-18 未明、M1 の途中）
- **M1（1 人で一晩）でできたこと**:
  - 内容は JSON（`Unity/Assets/Resources/Content/`: classes / cards / enemies / Scenarios/grey_shrine）。CLI とテストも同じファイルを読む。
  - `TavernGame`（`Core/Tavern.Core/Game/`）: 1 手 = 判定の計画（強制）→ ゲームが振る →（運命のコインで振り直し）→ 語り（強制）。
    語りの「効果の欄」（ダメージ・回復・持ち物・手がかり・移動・戦闘開始・次の幕・結末）をゲームが検証して反映する。
    戦闘はルールで進む（カード・基本の行動・自由な行動は AI が大きさを決める・敵の行動の型・ボスの大技の予告・かばう・死のサイコロ・逃げる）。
    幕の終わりに成長（能力値 +1 か最大 HP +3）。ボスの登場・結末は 3.8 Flash（山場）。途中保存と続きから（`SaveData`）。
  - Unity: タイトル、キャラ作り（1〜3 人、職業・見た目・名前・生い立ち）、テーブル画面（試し絵どおり）、3D の 20 面ダイス、地図、
    語り部への注文、運命のコイン、成長、結末。毎手番の後に `%USERPROFILE%/AppData/LocalLow/tavern-tales/酒場の語り部/evening.json` へ保存。
  - 仮絵（Juggernaut XL）: `Tools/ArtGen/jobs/m1.json` の 38 題材 × 2 案を生成、選んで `Unity/Assets/Resources/Art/*.jpg` に取り込み。
  - 確認: テスト 29 件、CLI で第 1 幕を通し（Gemini、14 回の呼び出し）、Unity の見本 AI（`-demo`）と本物の Gemini でボットが数手番遊んだ画面。
- 起動の旗: `-demo`（見本の AI）、`-quickstart`（見本の 3 人ですぐ始める）、`-autoplay N`（ボットが N 手番）、`-continue`、`-partyscreen`、
  `-savefile PATH`、`-screenshot PATH -shotat 秒,.. -autoquit 秒`。
- **M1 で残っていること**: 第 2〜3 幕とボス戦・結末までの通しの確認（本物の AI で）、語りの長さ（Flash-Lite は長くなりがち）、
  成長で新しい技のカードを選べるようにする（職業ごとのカードが 3 枚しかないので M3 で 8 枚に増やしてから）。
- 運命のコインは探索の判定・攻撃・かわす・死のサイコロのどれにも使える（失敗したときに振り直すかを聞く）。戦闘では、ルールの結果（誰が何点）もログに出る。

## M2（オンライン）の途中（2026-10-18 未明）
- 設計は `Docs/04_Online.md`。Relay のルームコード（前作の Unity Cloud プロジェクトを流用、`cloudProjectId` 設定済み）、
  名前付きメッセージだけ（gzip した JSON、`ReliableFragmentedSequenced`）。ホストが本物のゲーム、友人は毎手番の状態から鏡のゲームを組み立てて同じ画面を描く。
- できたもの: `NetSession`（Relay・送受信）、`NetProtocol`（Core。メッセージと鏡、テストあり）、ロビー（コード表示・顔ぶれ・開始）、
  タイトルの「部屋を作る / 部屋に入る」、オンラインのキャラ作り（1 人 1 キャラ + プレイヤー名 + コード）、
  テーブルのオンライン版（自分のキャラだけ操作、全員の宣言がそろったらホストが手番を進める、宣言済み表示、
  運命のコインと成長は持ち主の PC で選ぶ、語り部への注文、友人の入り直し、切断の表示）。
- 続きから（オンライン）: ホストが保存した夜を新しい部屋として開き直し、友人は同じ名前で入れば前のキャラクターに戻る（`ResumeLobbyScreen`）。
  遊んでいる途中で友人が落ちても、同じコードと名前で入り直せる（ホストはその人を待たずに進める）。
- 試験: `Tools/OnlineTest.ps1`（同じ PC でホスト + 友人 2 人、ボット、見本の AI。3 台とも同じ 63 行を表示したことを確認）、
  `Tools/ResumeTest.ps1`（途中の切断と入り直し、部屋の開き直し。エラーなし、戦闘の途中から再開できた）。
- 残り: 招待リンク（https の中継ページ + ゲームを起動するリンク。GitHub Pages の設定と、PC へのリンクの登録が要るのでオーナーに確認してから）。旗: `-onlinehost -codefile F -startwhen N`、
  `-onlinejoinfile F`、`-presetchar N`、`-playername S`、`-profile S`。

## M0（2026-10-17）
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
dotnet run --project Core/Tavern.Cli -- --script Core/Tavern.Cli/scripts/evening.txt --seed 11   # Gemini で一晩を通す
Builds/windows/Tavern.exe -demo -quickstart -autoplay 5 -screenshot Logs/shots/demo.png -shotat 7,15,23 -autoquit 30
dotnet build Tools/ArtGen -o Logs/artgen-bin && dotnet Logs/artgen-bin/ArtGen.dll import Logs/art/m1 Unity/Assets/Resources/Art --pick id=2,...
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
