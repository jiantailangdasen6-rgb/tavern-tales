# クレジット（音・画面の部品・文字の素材）

ゲームのタイトル画面の「クレジット」と、配布用 zip の「はじめに.txt」にも同じ内容を載せる。
表記が必須なのは CC BY 系（Tabletop Audio・Kevin MacLeod）だけ。CC0 と PeriTune は任意だが、感謝を込めて載せる。
ダウンロードした元のファイルは `Logs/audio-src/`（git に入れない）。ゲームに使う分だけを `Unity/Assets/Resources/Music`・`Sfx` に取り込む
（今使っているものはゲーム内の `Resources/Music/credits.txt` に載せる）。

## 表記が必要
- **Tabletop Audio**（https://tabletopaudio.com/）— CC BY-NC-ND 4.0（https://creativecommons.org/licenses/by-nc-nd/4.0/）
  非営利でのみ使う。曲を切る・つなぐなどの加工はしない（そのまま再生する）。
  - Tavern Celebration / The Hearth Inn / Halfling Lodge / Sun Dappled Trail / Through the Woods / The Verdant Dark /
    Tomb Guardians / Whispering Caverns / Necropolis / Fog of War / Village Raid / Druid Hilltop / Summoning
- **Kevin MacLeod**（https://incompetech.com/）— CC BY 4.0（https://creativecommons.org/licenses/by/4.0/）
  - Satiate（弦のみ、Satiate Strings）: 緊迫の曲（オーナー 2026-10-25）

## 表記は任意
- **PeriTune**（https://peritune.com/）— Celtic and Medieval、Battle Tracks（itch.io。商用可・改変可・非営利の再配布可、クレジット任意）
- **RandomMind**（OpenGameArt, CC0）— Medieval: The Bard's Tale / Minstrel Dance / Harvest Season / Rejoicing / Market Day /
  Exploration / Battle / Victory Theme / Defeat Theme
- **Wolfgang_**（OpenGameArt, CC0）— Battle Theme 0
- **Juhani Junkala**（OpenGameArt, CC0）— Epic Boss Battle
- **Pro Sensory**（OpenGameArt, CC0）— Boss Battle (loop)
- **BlackCortex**（OpenGameArt, CC0）— Determined Pursuit (Epic Orchestra Loop)
- **Kenney**（https://kenney.nl/, CC0）— Casino Audio、RPG Audio、Interface Sounds、Impact Sounds、Music Jingles、UI Audio（効果音の選び直し、オーナー 2026-10-25）
- **rubberduck**（OpenGameArt, CC0）— 80 CC0 RPG SFX
- **JaggedStone**（OpenGameArt, CC0）— Magic Spell SFX
- **PagDev**（OpenGameArt, CC0）— Fireplace Sound loop
- **AntumDeluge**（OpenGameArt, CC0）— Fire Crackling

## 見送ったもの
- Freesound のサイコロの音（CC0）: ダウンロードにアカウントが要るため。
- 魔王魂・OpenTracks（旧 DOVA-SYNDROME）・効果音ラボ: 具体的な曲をまだ選んでいない。
  魔王魂は「音楽：魔王魂」の表記が必要、OpenTracks と効果音ラボは素材のファイルを公開の場（公開リポジトリ）に置けない。

## 文字（ブラウザ版）
- Noto Sans JP・Noto Serif JP（https://github.com/notofonts/noto-cjk の SubsetOTF/JP）。SIL Open Font License 1.1。
  `Unity/Assets/Resources/Fonts/`（規約の全文は同じ場所の `OFL.txt`）。ブラウザは PC の游明朝・游ゴシックを使えないので、ブラウザ版だけがこれを使う。

## 文字（見出しと数字、全版）
- Zen Antique（Yoshimichi Ohira）・Cinzel（Natanael Gama）。どちらも SIL Open Font License 1.1。
  `Unity/Assets/Resources/Fonts/`（規約の全文は `OFL-ZenAntique.txt`・`OFL-Cinzel.txt`）。本文は今まで通り游明朝（ブラウザ版は Noto Serif JP）。

## 画面の部品（`Unity/Assets/Resources/UI/`）
- **Kenney**「Fantasy UI Borders」（https://kenney.nl/, CC0）— `frame_double.png`・`frame_single.png`（白い枠を金色に染めて 9 分割で伸ばす）。
- **game-icons.net**（https://game-icons.net/）の Lorc・Delapouite・Carl Olsen — CC BY 3.0（https://creativecommons.org/licenses/by/3.0/）。
  `ic_*.png`（白い SVG を 128 px の PNG にした）。表記が必要。
- 羊皮紙（`parchment.png`）と革（`leather.jpg`）は FLUX.1 Kontext Pro で描いた。
