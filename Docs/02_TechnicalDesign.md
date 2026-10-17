# 技術設計（M0 時点）

## 構成

```
Core/                     Unity 非依存の C#（netstandard2.1 / C# 9）。Unity からはローカルパッケージとして参照する
  Tavern.Core/              ルール（サイコロ・判定・死のサイコロ・キャラクター）、GM（AI とのやり取り）
  Tavern.Core.Tests/        NUnit（dotnet test Core）
  Tavern.Cli/               コンソールで GM と話す確認用（AI の語りの質を Unity 抜きで確かめる）
Unity/                    Unity 6000.6.2f1 のプロジェクト。Core を "file:../../Core/Tavern.Core" で参照
Tools/ArtGen/             画像の生成（fal.ai / 手元の ComfyUI）
Docs/                     決定事項・設計・画風の決まり
```

- カードゲーム（`D:\project\cardgame`）と同じ分け方。Core のビルド成果物は `bin~` / `obj~`（Unity が無視するフォルダ名）に出す。
- Core は決定論的に作る: 乱数は `IRandom` を通して渡し、テストではシードを固定する。

## ゲームと AI の役割分担

- **ゲーム（Core）が正**: 能力値・HP・気力・持ち物・サイコロ・敵の行動・ダメージはすべてゲームが決める。
- **AI は語りと提案だけ**: AI は「ツール」（関数呼び出し）を通してゲームに頼む。
  - `request_check`（誰が・どの能力値で・どの難しさで・何のために）→ ゲームがサイコロを振り、結果を AI に返す → AI が結果を語る
  - `suggest_actions`（次の行動案 2〜3 個）
  - 以後の段階で: `start_combat`（絵のある敵の一覧から）、`give_item`、`advance_act`、`reveal_location` など
- AI が自由な文章で「成功した」と書いても、ゲームの状態は変わらない。状態を変えるのはツールだけ。

## AI のつなぎ口

- `ILlmClient`: メッセージの列とツールの定義を渡し、返事（本文 + ツール呼び出し）を受け取る。
  - `OllamaClient`: 手元の Ollama（`http://127.0.0.1:11434/api/chat`、ツール対応、`think: false`）。M0 / M1 はこれ
  - `GeminiClient`: Gemini API の無料枠（キーはホストの環境変数か設定ファイル。M1 以降に足す）
  - 有料の AI も同じ形で足せる
- `GmSession`: 語り部の性格・ルール・シナリオの設定表から指示文を作り、宣言 → AI → ツール → AI … → 語り、の 1 手を回す。

## Ollama（手元の AI）

- 本体は zip 版を `D:\tools\ollama` に置く（インストーラーを使わず、PC の設定を変えない）。
- モデルは `D:\tools\ollama-models`。`ollama serve` を起動するときだけ環境変数 `OLLAMA_MODELS` をその場で渡す
  （`Tools/Ollama.ps1`）。
- モデル: `qwen3:8b`（約 5 GB。日本語とツール呼び出しに強く、VRAM 8 GB に収まる）。
