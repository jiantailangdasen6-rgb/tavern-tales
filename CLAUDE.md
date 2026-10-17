# tavern-tales — 酒場の語り部（仮タイトル）

AI がゲームマスターを務める剣と魔法のファンタジー TRPG。最大 3 人のオンライン協力、身近な友人と遊ぶための非商用の企画。
Unity 6000.6.2f1 / C#。対応は Windows の exe のみ（ブラウザ・スマホは保留）。
**現在は企画の相談中**: 決まったことは `Docs/00_Decisions.md`。実装を始める前にそこを読む。

## 役割
- ユーザーは指示だけを出す（スマホからリモートコントロールで見ていることが多い）。実装・検証・Git はすべて Claude。
- Unity エディタを GUI で開かせない。設定・ビルド・確認はバッチモードとスクリーンショットで行う。

## 決まり
- ユーザーに見える文はすべて日本語（途中の一言、通知の後の返事、最後のまとめも）。コード・コメント・コミットは英語。
- 自動の実行は音を出さない。既定の音量は小さく。
- 秘密の値（API キーなど）は絶対にコミットしない。AI のキーはホストの PC の設定ファイルにだけ置く。
- リポジトリは **公開**（オーナー決定 2026-10-17）。アセットストアの素材は公開での再配布が規約違反なので使わない（使うなら相談）。
- 大きなダウンロード（AI のモデルなど）は D ドライブ（`D:\tools\`。Ollama は `D:\tools\ollama`、モデルは `D:\tools\ollama-models`）に置く。

## 参考にする既存のプロジェクト
- `D:\project\roguelike-hackslash`: Netcode for GameObjects + Unity Relay のルームコードでの協力プレイ、バッチモードのテストとビルドの仕組み。
- `D:\project\cardgame`: 画像生成の手順（`Tools/ArtGen`、`docs/art/style-guide.md`、`docs/art/generation-log.md`）。
  最終的には fal.ai の FLUX Kontext Pro（1 枚 約 0.04 ドル）に統一した。手元の ComfyUI（`D:\tools\ComfyUI_windows_portable`、
  FLUX schnell / DreamShaperXL / Juggernaut XL）もある。ComfyUI は GPU を占有するので Unity のビルドと同時に回さない。
