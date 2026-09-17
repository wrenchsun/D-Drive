# 36. デザイナーマニュアル スクリーンショット撮影リスト（2026-09-15）

関連: [40_manual_screenshot_workflow.md](40_manual_screenshot_workflow.md)（**撮り方と取り込み手順はこちら**。本書は撮る対象のリスト） / [DesignerManual/Readme.html](DesignerManual/Readme.html) / [09_editor_tools.md](09_editor_tools.md)（ウィンドウ名・メニューパス） / [28_manual_verification_phase5.md](28_manual_verification_phase5.md) / [23_manual_verification_2026-09-11.md](23_manual_verification_2026-09-11.md)（実際の操作手順） / [35_tutorial_video_scripts.md](35_tutorial_video_scripts.md)（動画台本、収録ルールを本リストの共通設定にも流用） / [32_spec_web.md](32_spec_web.md) §10（発注ツールの画面構成） / [11_tasks.md](11_tasks.md) 6-4

> **2026-09-17 更新**: 撮影結果を §5 に追記した。45 枚を `docs/DesignerManual/images/` に配置して各 HTML に `<figure>` を挿入済み。Inspector のスクリーンショットは**すべて不要**と判断して取りやめ（既存の `se-emitter-inspector.png` も削除）。残り 10 枚は不具合・手順不明のため未撮影（理由は §5）。**どの番号がどうなったかは §5 の表が最新**（§4 の各表は当初の撮影指示のまま残してある）。

> **このドキュメントは撮影リストのみ**（何を・どこに・どう撮るか）。実際のスクリーンショット撮影・`docs/DesignerManual/images/` への配置・各 HTML への `<img>` 挿入は別作業（人、またはエージェントが Unity 画面キャプチャで行う）。本チケットでは各ページの HTML に挿入位置を示す `<!-- screenshot: 36-#N -->` コメントのみを入れてある（表示には影響しない）。

## 0. 集計

| 項目 | 値 |
|---|---|
| 対象ページ数 | 26（`docs/DesignerManual/*.html` 全ページ。うち `glossary.html` は撮影不要、`getting-started.html` は最小限） |
| 既存スクリーンショット | 6 枚（`asset-browser-window` / `audio-editor-3dpad` / `audio-editor-window` / `root-overview` / `se-emitter-inspector` / `vfx-editor-window`）。うち `se-emitter-inspector` は 2026-09-17 に削除 |
| うち撮り直しが必要 | 2 枚（`root-overview.png` / `asset-browser-window.png`。理由は §2 参照） |
| うち既存のままで良い | 3 枚（`audio-editor-window` / `audio-editor-3dpad` / `vfx-editor-window`）。`se-emitter-inspector` は Inspector のため削除（2026-09-17） |
| 新規に撮影するスクリーンショット | 59 枚（上記の撮り直し 2 枚を含む。差分は 57 枚が完全新規） |
| 優先度 A（無いと操作が分からない） | 23 枚 |
| 優先度 B（あると理解が早い） | 28 枚 |
| 優先度 C（補足） | 8 枚 |
| 撮影順グループ数 | 9 グループ（§1 参照） |
| 撮影済み（2026-09-17） | 45 枚（うち 3 枚は Inspector のため不採用、配置したのは 42 枚。§5） |
| 撮影を取りやめ | 7 枚（Inspector 5 枚 + #40 + #59。§5） |
| 未撮影（不具合・手順不明） | 10 枚（§5） |

番号は本ドキュメント内で `#1`〜`#59` の連番。HTML 側のコメントは `<!-- screenshot: 36-#N -->` の形式（例: `<!-- screenshot: 36-#12 -->`）。

## 1. 撮影の進め方（撮影順グループ）

同じシーン・同じウィンドウで続けて撮れるものをまとめた。上から順に進めると準備の手間が最小になる。

| グループ | 内容 | 含む番号 |
|---|---|---|
| ① Asset Browser・関連ウィンドウ（シーン不要） | Asset Browser 本体・新規ダイアログ・使用箇所/依存ツリー/未使用一覧・削除ウィンドウ一式・Validation Run All・仕様書同期(Unity側) | #1, #3〜#15, #56 |
| ② Inspector 単体（シーン不要） | ~~SeData / BgmData / MaterialData / CanvasData の Inspector 項目だけを映すもの~~ **2026-09-17 に撮影取りやめ**（Inspector は撮らない方針） | ~~#4, #16, #17, #35, #41~~ |
| ③ Audio・環境音（既存確認シーン） | 既存 2 枚（`audio-editor-window` / `audio-editor-3dpad`）で足りる。SeEmitter へのドラッグのみ新規 | #18 |
| ④ VFX・Anchor・配置セット（VFX 確認用シーン） | vfx-data の AnchorPoint、VFX Editor、Anchor Editor、Anchor Group Editor | #19〜#25 |
| ⑤ モデル・アニメーション（Anim 確認用シーン） | Model Editor、Anim Editor、Anim2D Editor | #26〜#34 |
| ⑥ マテリアル（現在のシーンに配置） | Material Editor、Material 変換（#35 の MaterialData Common は②に含み、撮影取りやめ） | #36〜#38 |
| ⑦ Prefab（Prefab 確認用シーン） | Prefab Editor | #39, #40 |
| ⑧ Canvas・UI（Canvas 確認用シーン / UI Tween 確認用シーン） | Canvas Editor、Button/Slider Skin、Slider Editor、UI Tween Editor、プリセットギャラリー（#41 の CanvasData Inspector は②に含み、撮影取りやめ） | #42〜#51 |
| ⑨ 演出・カメラ揺れ・振動（Presentation 確認用シーン） + Web 発注ツール（ブラウザ） | Presentation Editor、剣攻撃デモ、Shake/Haptics Editor、発注ツール Web 画面一式 | #52〜#55, #57〜#59 |

`getting-started.html`（#2）はどのグループでもよい単発の撮影（メインツールバーのボタン位置だけ）。

## 2. 既存 6 枚の扱い

| ファイル | 使用ページ | 判定 | 理由 |
|---|---|---|---|
| `root-overview.png` | Readme.html | **撮り直し**（#1） | Asset Browser のツールバーが検索欄・種別のみで切れており、2026-09-14/15 で追加された「未使用...」ボタン・行頭のアイコン列・「更新者・更新日時」列が写っていない。現状の画面と食い違う |
| `asset-browser-window.png` | asset-browser.html | **撮り直し**（#3） | 同上（「未使用...」ボタン・アイコン列・更新者/更新日時列が無い状態で撮影されている） |
| `audio-editor-window.png` | audio-editor.html | 既存のままでよい | 波形・トリミングボタン・再生コントロール・3D パッドの構成は現行の説明と一致 |
| `audio-editor-3dpad.png` | audio-editor.html | 既存のままでよい | 3D サウンド確認パッドの構図は現行の説明と一致 |
| `se-emitter-inspector.png` | scene-sound.html | **削除**（2026-09-17） | Inspector のスクリーンショットは撮らない方針にしたため。scene-sound.html の `<figure>` ごと削除した |
| `vfx-editor-window.png` | vfx-editor.html | 既存のままでよい | ツールバー・基本設定・Anchor・パラメータ・イベント欄の構成は現行の説明と一致 |

## 3. 撮影の共通設定

[35_tutorial_video_scripts.md](35_tutorial_video_scripts.md) §0 の動画収録ルールを静止画にも適用する。

- **テーマ**: 既存 6 枚がすべて Unity の**ダークスキン**で撮られているため、新規分もダークスキンで統一する（Editor の Preferences で切り替えていないか確認）
- **構図**: 既存画像はデスクトップ全体ではなく、**対象ウィンドウ（Inspector / 各専用エディタ / Asset Browser 単体）を切り出した構図**（タスクバー・他アプリは映さない）。新規分もこれに合わせ、対象ウィンドウをフローティングにして最大化し、その領域だけをキャプチャする。SceneView を含めるカット（§1 の④⑤⑦⑧⑨の一部）は「エディタウィンドウ + SceneView/Game ビュー」を並べたレイアウトで、両方が収まる範囲を切り出す
- **ウィンドウ幅の目安**: Inspector 系（1 カラム）は 550〜600px 幅、タイムライン/ノードグラフ系（Anim Editor・Canvas Editor・Presentation Editor・Anchor Group Editor）は 1200〜1400px 幅を目安に、内容が窮屈にならない広さを確保する（既存 `vfx-editor-window.png` が 581px、`root-overview.png` が 1349px 相当なのを参考に）
- **形式**: PNG（既存と統一。透過は不要）
- **ファイルサイズの目安**: Web 版マニュアル（`Tools/SpecWeb`）は画像を data URI で埋め込むため、1 枚が大きすぎると GAS 側の HTML ファイルが肥大化する。既存 6 枚は 13KB〜119KB（平均約 43KB）。**1 枚あたり 150KB を超えないことを目安**にし、超える場合は PNG の圧縮（不要な余白を切る、色数の多い部分を減らす）を検討する。スクリーンショットツールでの範囲指定キャプチャ（フルスクリーン撮影 → 後から切り出しではなく、最初から必要な範囲だけを撮る）でも自然に抑えられる
- **マウスカーソルの見せ方**: 操作対象の直前で止めた位置に置く（動画のルールと同様、早すぎる移動の残像などが写らないようにする）
- **プライバシー・機密情報を映さないこと**（[35] §0.3 と同じ基準。本リストで特に注意が必要な箇所は各表の「撮り方」列に明記した）:
  - Windows のユーザー名（Inspector の「更新者(Author)」欄・タイトルバー・パス表示に出る）。6-3 で追加された版数表示（`v12・名前・日時`）の「名前」は保存した人の Windows ユーザー名がそのまま入るため、**撮影用の共通アカウント（例: `demo`）で保存し直す**か、該当欄をぼかす
  - 発注ツール（Web）の URL・トークン・ログイン中の Google アカウントのメールアドレス
  - 実際の企画・チームの実名・実データ（練習用のダミー名を使う）

## 4. ページごとの撮影リスト

### Readme.html

| # / 優先度 | ファイル名案 | 挿入位置 | 何を映すか | 準備 | 撮り方 |
|---|---|---|---|---|---|
| #1 / A（既存の撮り直し） | `images/root-overview.png` | 既存の `<figure>`（「はじめの一歩」の直後） | Asset Browser 単体。検索欄・種別フィルタ・「新規」「更新」「未使用...」ボタンが 1 行に収まった状態、一覧に 3〜4 件（アイコン列・更新者/更新日時列を含む）、下部にプレビューバー | 練習用の SE を 2〜3 件、BGM を 1 件、いずれかを保存済みにして更新者列に値が入った状態にしておく | Asset Browser ウィンドウをフローティングにして幅 1300px 程度に広げ、ツールバー全体〜一覧数行〜プレビューバーが収まる高さでキャプチャ。更新者名はぼかすか撮影用アカウントで保存する |

### getting-started.html

| # / 優先度 | ファイル名案 | 挿入位置 | 何を映すか | 準備 | 撮り方 |
|---|---|---|---|---|---|
| #2 / B | `images/getting-started-manual-button.png` | 「手順2: 「？ マニュアル」ボタンの場所を確認する」の警告 div の直後 | Unity メインツールバーの再生ボタン（▶）の右隣にある「？ マニュアル」ボタンとその横の▼ | メインツールバーの右クリックで「D-Drive/Manual」を表示済みにしておく | メインツールバー上部だけを幅広く切り出す（Game/Scene ビューは映さなくてよい）。他のウィンドウ配置は問わない |

> 手順3（Asset Browser を開く）・手順4（新規登録ダイアログ）は asset-browser.html の #3・#5 で映るので、このページ専用のスクリーンショットは追加しない（重複を避けるため）。

### asset-browser.html

| # / 優先度 | ファイル名案 | 挿入位置 | 何を映すか | 準備 | 撮り方 |
|---|---|---|---|---|---|
| #3 / A（既存の撮り直し） | `images/asset-browser-window.png` | 既存の `<figure>`（「開き方」の直後） | ツールバー全体（検索欄・種別・新規・更新・未使用...）+ 一覧（アイコン列・更新者/更新日時列を含む数行）+ プレビューバー | #1 と同じ状態で流用可（同じタイミングで撮れる） | #1 と同様。幅 1300px 程度 |
| #4 / B | `images/asset-browser-inspector-header.png` | 「バージョン表示（2026-09-15 追加）」tip の直後 | Inspector 最上部の共通ヘッダ: 「▶ 〜 Editor で開く」ボタン、アイコン行（自動生成/フォルダから選択/シーンから作成/クリア）、版数・更新者・更新日時の行、ChangeNote 欄 | 一度保存済みの SE データを選択。更新者は撮影用アカウント名にする | Inspector 上部 300〜400px 分だけを切り出す |
| #5 / A | `images/asset-browser-new-dialog.png` | 「方法2: 「新規」ボタン（効果音以外はこちら）」の直後 | 「新規アセット作成」ダイアログ全体: 種別（Vfx 等）・表示名（日本語）・カテゴリ・識別子・備考・仕様リンク・生成先プレビュー | 種別に Vfx、表示名「テスト斬撃」、カテゴリ「Player」、識別子「TestSlash」を入力した状態 | ダイアログ単体をキャプチャ |
| #6 / B | `images/asset-browser-spec-picker.png` | 既存コメント `<!-- TODO: スクショ（新規ダイアログの「仕様書から選ぶ」一覧） -->` の位置（差し替え） | 新規ダイアログ上部の「仕様書から選ぶ」一覧。行を1つ選択した「選択中の行: ○○」表示と「解除」ボタンが見える状態 | 発注ツールに未インポートの発注を1件以上用意し、同期済みにしておく | ダイアログ全体（一覧が伸びた状態） |
| #7 / B | `images/asset-browser-usage-list.png` | 「使用箇所を調べる」の手順リストの直後 | 「使用箇所を表示」の結果一覧（アセット・シーンに分かれた行） | 何らかの SE を Prefab とシーンの両方から参照した状態にしておく | 結果ウィンドウ単体 |
| #8 / C | `images/asset-browser-dependency-tree.png` | 「依存ツリーを見る」の段落の直後 | 依存ツリー（木構造）。Prefab→Material→Texture のような連なりが 2〜3 階層見える状態 | マテリアルを持つ Prefab を対象に開く | ツリーウィンドウ単体 |
| #9 / B | `images/asset-browser-unused-list.png` | 「使っていないアセットをまとめて片付ける」の手順リストの直後 | 未使用アセット一覧ウィンドウ（チェックボックス・全選択/選択解除・「選択項目を一括Archive」ボタン） | 未使用の練習用アセットを 2〜3 件用意 | ウィンドウ単体 |
| #10 / A | `images/asset-browser-delete-overview.png` | 既存コメント `<!-- TODO: スクショ（削除の確認ウィンドウ 全体） -->` の位置（差し替え） | 「アセットを削除」ウィンドウの分析画面全体（削除するアセット一覧・参照元・依存先の3ブロックが見える） | 参照元・依存先を両方持つ練習用アセットを対象に「削除...」を開く | ウィンドウ全体 |
| #11 / B | `images/asset-browser-delete-references.png` | 既存コメント `<!-- TODO: スクショ（参照元一覧・依存先一覧） -->` の位置（差し替え） | 「このアセットを使っている場所」と「このアセットが使っているもの」の2ブロックを拡大。依存先のチェックボックス（一緒に削除）が見える | #10 と同じ対象で継続 | 該当ブロックにズームして切り出す |
| #12 / B | `images/asset-browser-delete-method.png` | 既存コメント `<!-- TODO: スクショ（削除方法の選択、参照の差し替え先を選ぶ欄） -->` の位置（差し替え） | 削除方法の4択（参照を差し替え/強制削除/アーカイブのみ/キャンセル）と、差し替え先を選ぶプルダウンが開いた状態 | 「参照を差し替えてから削除」を選び、差し替え先の候補一覧を表示させる | ダイアログのその部分を切り出す |
| #13 / C | `images/asset-browser-delete-result.png` | 既存コメント `<!-- TODO: スクショ（結果画面） -->` の位置（差し替え） | 「実行」後の結果画面。コードで使っている ID 定数の警告例、差し替えた参照一覧 | コード側にダミーで ID 定数を参照させた状態（無ければ警告なしの通常結果でも可） | ウィンドウ全体 |
| #14 / A | `images/asset-browser-preview-inline.png` | 「試聴する（プレビューバー）」の手順リストの直後 | 一覧で SE を選択し、プレビューバーの▶が再生中（速度スライダー・ループにチェック）の状態 | SE を 1 件選択して再生ボタンを押した瞬間 | Asset Browser 下部のプレビューバー付近を拡大 |

### validation.html

| # / 優先度 | ファイル名案 | 挿入位置 | 何を映すか | 準備 | 撮り方 |
|---|---|---|---|---|---|
| #15 / A | `images/validation-run-all.png` | 「重要度の見かた」の表の直後 | Run All の結果レポート。Error（赤）・Warning（黄）・Info（青）が最低 1 件ずつ見える状態と、Error の行にある「修正」ボタン | わざと Mixer 未割当の SE、Prefab 未設定の VFX などを 1 件ずつ用意しておく | レポートウィンドウ全体 |

### se-data.html

| # / 優先度 | ファイル名案 | 挿入位置 | 何を映すか | 準備 | 撮り方 |
|---|---|---|---|---|---|
| #16 / A | `images/se-data-inspector.png` | 導入段落（「Audio Editor を開いたまま作業する」tip）の直後 | SeData の Inspector 全体: Clips/Select Mode/Mixer/Volume/Pitch Range、3D（Spatial=Anchor、Min/Max Distance）、制御（Max Concurrent/Cooldown） | Spatial=Anchor、Clips に音声を1つ以上設定した状態 | Inspector 全体をスクロールなしで収める（複数カットに分けず 1 枚に収まる高さでウィンドウを広げる） |

### bgm-data.html

| # / 優先度 | ファイル名案 | 挿入位置 | 何を映すか | 準備 | 撮り方 |
|---|---|---|---|---|---|
| #17 / A | `images/bgm-data-inspector.png` | 導入段落の直後 | BgmData の Inspector: Intro/Loop Body/Loop Start/Loop End、Fade In/Fade Out（モード=曲線、Ease 名が見える状態） | Intro・Loop Body の両方を設定し、Fade In/Out を「曲線」モードにしておく | Inspector 全体 |

### audio-editor.html

新規スクリーンショットなし。既存の `audio-editor-window.png`・`audio-editor-3dpad.png` で「波形・トリミング・再生コントロール・3D パッド」がすべて説明どおりに映っているため十分。

### scene-sound.html

| # / 優先度 | ファイル名案 | 挿入位置 | 何を映すか | 準備 | 撮り方 |
|---|---|---|---|---|---|
| #18 / C | `images/scene-sound-drag-emitter.png` | 「手順」リストの手順2の直後 | Project ウィンドウの `SeEmitter` プレハブを Hierarchy へドラッグしている最中の様子（ドラッグ中のゴースト表示） | `Assets/GameData/Prefabs/Audio/SeEmitter` を Project で表示 | Project と Hierarchy が両方見えるレイアウトで、ドラッグ中の瞬間を撮る（動きの一瞬なので数回試行）。優先度 C のため無ければ省略してよい |

既存の `se-emitter-inspector.png` はそのまま使用。

### vfx-data.html

| # / 優先度 | ファイル名案 | 挿入位置 | 何を映すか | 準備 | 撮り方 |
|---|---|---|---|---|---|
| #19 / B | `images/vfx-data-anchorpoint-sceneview.png` | 「AnchorPoint / AnchorRig（シーンに置く目印）」の手順リストの直後 | SceneView 上のキャラクターに付けた AnchorRig と、その子の AnchorPoint（`Anchor_RightHand` などの名前付きギズモ、色付きの球） | AnchorRig をキャラクター Prefab の子に置き、SceneView で見やすい角度にする | SceneView をズームしてギズモとラベルが読める大きさにする |

### vfx-editor.html

| # / 優先度 | ファイル名案 | 挿入位置 | 何を映すか | 準備 | 撮り方 |
|---|---|---|---|---|---|
| #20 / B | `images/vfx-editor-anchor-handle.png` | Anchor 調整の表、「SceneView 表示」の行の直後 | VFX Editor ウィンドウ + SceneView の両方。SceneView 側に移動ハンドル（矢印）が出ている状態 | 「SceneView 表示」ON、VFX Editor をクリックしてハンドルを表示させる | VFX Editor と SceneView を左右に並べたレイアウトで両方を収める |
| #21 / C | `images/vfx-editor-prefab-mode.png` | 「Prefab の中で再生する」の手順リストの直後 | プレハブモードに入った状態の VFX Editor（上部に青い案内文が見える）+ Hierarchy がプレハブの中身だけになっている様子 | 対象 VFX の「Prefab を開く」を押した直後、再生中 | VFX Editor + Hierarchy + SceneView が収まる構図 |

### anchor-data.html

| # / 優先度 | ファイル名案 | 挿入位置 | 何を映すか | 準備 | 撮り方 |
|---|---|---|---|---|---|
| #22 / A | `images/anchor-editor-window.png` | 「はじめの一歩（5 分でできる練習）」の手順リストの直後 | Anchor Editor ウィンドウ全体: 連鎖表示・スポーン先・基準の表示（✓）・設定項目・試し出しボタン | AnchorRig から一括生成した Anchor を対象にして開く | ウィンドウ単体、幅 600px 程度 |
| #23 / B | `images/anchor-editor-sceneview.png` | 「Anchor Editor の画面」の表、「SceneView」の行の直後 | SceneView: ルートから対象までの点線、ランダム半径の円、移動/回転ハンドル | 「試し出し」で VFX を出した直後、ハンドルが見える状態 | SceneView を対象にズーム |

### anchor-group.html

| # / 優先度 | ファイル名案 | 挿入位置 | 何を映すか | 準備 | 撮り方 |
|---|---|---|---|---|---|
| #24 / A | `images/anchor-group-editor-window.png` | 「はじめの一歩（3×3 の格子に出す）」の手順リストの直後 | Anchor Group Editor ウィンドウ全体: 配置パターン=Grid、点数、番号順ディレイ、全点共通 VFX の設定欄 | Grid 3×3 で設定済みの配置セットを開く | ウィンドウ単体 |
| #25 / B | `images/anchor-group-sceneview-grid.png` | 「画面の説明」の表、「SceneView」の行の直後 | SceneView: 3×3 の番号付き点（水色）、大きい円（原点）、選択中の点（黄） | 1点を選択した状態、「▶ 全点」を押す前 | SceneView をズームし番号が読める大きさに |

### model-editor.html

| # / 優先度 | ファイル名案 | 挿入位置 | 何を映すか | 準備 | 撮り方 |
|---|---|---|---|---|---|
| #26 / A | `images/model-editor-window.png` | 「はじめの一歩（キャラクターを登録して確認する）」の手順リストの直後 | Model Editor ウィンドウ全体: ツールバー・配置/撤去・ターンテーブル・Material スロット一覧・Default Animation | Slot 自動収集済み、Prefab 設定済みのモデルを開く | ウィンドウ単体 |
| #27 / B | `images/model-editor-turntable-sceneview.png` | 「複数モデル並列表示」の行の直後 | SceneView: ターンテーブル回転中のモデル + 隣に並んだ複数モデル（2〜3体） | 「複数モデル並列表示」を使い、2〜3体を配置 | SceneView 全体 |

### anim-editor.html

| # / 優先度 | ファイル名案 | 挿入位置 | 何を映すか | 準備 | 撮り方 |
|---|---|---|---|---|---|
| #28 / A | `images/anim-editor-timeline.png` | 「はじめの一歩（攻撃モーションに斬撃音を付ける）」の手順リストの直後 | Anim Editor ウィンドウ全体: タイムライン（橙のマーカー付き）・SE/VFX イベント一覧・イベントログ・再生コントロール | 攻撃モーションに SE イベントを1つ以上付けた状態 | ウィンドウ単体、幅 1200px 程度 |
| #29 / B | `images/anim-editor-timeline-zoom.png` | 「画面の説明」の表、「タイムライン」の行の直後 | タイムラインを拡大し、橙のマーカー・波形（SE 分）・フレーム番号目盛りが読める状態 | 同じ対象でタイムラインをズームイン | タイムライン部分だけを切り出す |
| #30 / C | `images/anim-editor-blend-tools.png` | 「ブレンドを確認する」の表の直後 | 「ブレンド確認」の折りたたみを開いた状態（遷移シーケンス or レイヤー同時再生のいずれか） | Steps を2つ以上並べた遷移シーケンスを設定 | その折りたたみ部分を切り出す |
| #31 / B | `images/anim-editor-sceneview-playback.png` | 「シーン上で確認する」の手順リストの直後 | SceneView: 攻撃モーション再生中のモデル（Anim Editor と並べる） | 再生中に一瞬止めてキャプチャ | Anim Editor + SceneView を並べた構図 |

### anim2d-editor.html

| # / 優先度 | ファイル名案 | 挿入位置 | 何を映すか | 準備 | 撮り方 |
|---|---|---|---|---|---|
| #32 / A | `images/anim2d-editor-create-mode.png` | 「はじめの一歩（4 コマ横並びのシートから待機アニメを作る）」の手順リストの直後 | 作成モードの入力欄全体: 入力モード=Grid、Columns/Rows/Sprite Count、Texture、Name/State、Frame Rate/Loop | 4コマのスプライトシートを Texture に設定済み | ウィンドウ単体 |
| #33 / B | `images/anim2d-editor-detect-preview.png` | 「作成モード」の表、「検出プレビュー(Automatic)」の行の直後 | 検出プレビューの結果画像。緑の枠と番号がスプライトシートに重なった状態 | 隙間が不揃いなシートで「検出プレビュー」を実行 | プレビュー画像部分を切り出す |
| #34 / B | `images/anim2d-editor-edit-mode.png` | 「編集モード」の表の直前 | 編集モードの全体: 編集対象・プレビュー(SceneView で確認)・Anim Editor で開くボタン・リタイミング・検証 | 生成済みの Anim2DData を読み込んだ状態 | ウィンドウ単体 |

### material-data.html

| # / 優先度 | ファイル名案 | 挿入位置 | 何を映すか | 準備 | 撮り方 |
|---|---|---|---|---|---|
| #35 / B | `images/material-data-common-channels.png` | 「共通チャンネル（Common）」の表の直前 | MaterialData の Inspector、Common 欄（Albedo/Normal/Mask/Emission/Blend が ID で設定済み） | Albedo・Normal・Mask にテクスチャデータを設定した Lit マテリアルを開く | Inspector の Common 部分を切り出す。Windows ユーザー名が Author 欄に映らないよう注意 |

### material-editor.html

| # / 優先度 | ファイル名案 | 挿入位置 | 何を映すか | 準備 | 撮り方 |
|---|---|---|---|---|---|
| #36 / A | `images/material-editor-sphere.png` | 「はじめの一歩（マテリアルを作ってシーンで確かめる）」の手順リストの直後 | Material Editor ウィンドウ全体: 球のサムネイル、Common 欄、Specific 操作行 | DDrive/Lit のマテリアルを開き、Albedo/Metallic/Smoothness を設定 | ウィンドウ単体 |
| #37 / B | `images/material-editor-compare.png` | 「プレビューの設定」の表、「比較対象」の行の直後 | サムネイルの左右比較表示（A: 元のマテリアル / B: 比較対象） | 比較対象に別のマテリアルを設定 | サムネイル部分を切り出す |
| #38 / C | `images/material-editor-conversion.png` | 「Material 変換ウィンドウ（シェーダーの乗り換え）」の表の直前 | Material 変換ウィンドウ: 変換前後のサムネイル比較 + 互換表（〇/×/→ の記号） | DDrive/Lit → DDrive/Unlit の変換を試す | ウィンドウ全体 |

### prefab-data.html

| # / 優先度 | ファイル名案 | 挿入位置 | 何を映すか | 準備 | 撮り方 |
|---|---|---|---|---|---|
| #39 / A | `images/prefab-editor-window.png` | 「はじめの一歩(壊せる箱を登録する)」の手順リストの直後 | Prefab Editor ウィンドウ全体: 対象・確認用シーンに配置/撤去ボタン・設定（Kind/GameplayTags/CollisionLayer/Flags） | Kind=Gimmick、GameplayTags に1件入力済みの PrefabData を開く | ウィンドウ単体 |
| #40 / B | `images/prefab-editor-sceneview.png` | 「画面の説明(Prefab Editor)」の表、「確認用シーンに配置」の行の直後 | SceneView: 配置されたプレハブのプレビュー（Prefab Editor と並べる） | 「確認用シーンに配置」を実行した直後 | Prefab Editor + SceneView を並べた構図 |

### canvas-data.html

| # / 優先度 | ファイル名案 | 挿入位置 | 何を映すか | 準備 | 撮り方 |
|---|---|---|---|---|---|
| #41 / B | `images/canvas-data-inspector-wires.png` | 「Buttons(ButtonWire)— 「この要素のこの操作で、これをする」」の見出しの直前 | CanvasData の Inspector、Buttons/Sliders 配列（ButtonPath/Trigger/Action/Target が入った行が1〜2行） | OpenCanvas に配線したボタン、SetOption に配線したスライダーをそれぞれ1行用意 | Inspector の該当セクションを切り出す |

### canvas-editor.html

| # / 優先度 | ファイル名案 | 挿入位置 | 何を映すか | 準備 | 撮り方 |
|---|---|---|---|---|---|
| #42 / A | `images/canvas-editor-nav-graph.png` | 「Navigation グラフの操作」の見出しの直後 | Navigation グラフ全体: ノード（四角）、色分けされた矢印（水色/オレンジ/黄/紫）、緑枠（FirstSelected） | Selectable を自動収集し、上下左右のリンクを2〜3個張った状態 | グラフ部分を拡大して矢印の色が判別できるようにする |
| #43 / B | `images/canvas-editor-elementfx-panel.png` | 「ElementFx 割当セクションの行の読み方」の見出しの直後 | ElementFx 割当の折りたたみを1つ展開した状態（ドロップダウン・直接指定欄・▶再生ボタン） | 要素を自動収集し、Appear に PopIn を割り当てた行を展開 | その行の枠を切り出す |
| #44 / B | `images/canvas-editor-preview-gameview.png` | 「はじめの一歩(タイトル画面を組む)」の手順6（確認用シーンを開く）の直後 | Game ビュー: 開いた画面にボタンが PopIn で現れている瞬間 + パッド操作シミュレーションのパネル | 「確認用シーンを開く」を実行し、Appear 再生中にキャプチャ | Game ビュー + Canvas Editor のパッド操作シミュレーション部分を並べる |

### ui-skin.html

| # / 優先度 | ファイル名案 | 挿入位置 | 何を映すか | 準備 | 撮り方 |
|---|---|---|---|---|---|
| #45 / A | `images/button-skin-editor-window.png` | 「Button Skin」の「画面の説明」の表の直前 | Button Skin Editor ウィンドウ全体 + 確認用シーンに配置されたボタン（Game ビュー） | 「確認用シーンに配置」を実行済みの Button Skin を開く | Skin Editor + Game ビューを並べる |
| #46 / B | `images/button-skin-state-transition.png` | 「状態遷移（オレンジの線の箱）」の行の直後 | 状態遷移の再生中（「● マウスでクリック: 3/5 Pressed」のような表示）+ プレビューボタンに当たり判定の赤枠が重なった状態 | 「状態遷移」を再生しつつ「当たり判定を表示」も ON | Skin Editor の該当欄 + Game ビューのボタン部分 |
| #47 / A | `images/slider-editor-response-curve.png` | 「Slider Editor（スライダーの動き方を見る）」の「画面の説明」の表の直前 | Slider Editor 全体: (a) 応答曲線グラフ、(b) ノッチ/Step の帯、(c) 実操作ボタン | 「スタミナ」プリセットを適用（4分割ノッチ + 対角線から離れた応答曲線が見やすい） | ウィンドウ単体、縦に長めに |
| #48 / C | `images/slider-skin-editor-window.png` | 「Slider Skin」の「画面の説明」の表の直前 | Slider Skin Editor + 確認用シーンに配置されたスライダー（Track/Fill/Handle が見える） | 「確認用シーンに配置」を実行済み | Skin Editor + Game ビュー |

### ui-tween.html

| # / 優先度 | ファイル名案 | 挿入位置 | 何を映すか | 準備 | 撮り方 |
|---|---|---|---|---|---|
| #49 / A | `images/ui-tween-editor-window.png` | 「はじめの一歩（滑り込みながらフェードイン）」の手順リストの直後 | UI Tween Editor ウィンドウ全体: カーブ一覧（Track 2本）、選択中 Track の設定、確認用シーンの画像が再生中（Game ビュー） | SlideFadeInLeft から生成した Tween を再生中にキャプチャ | Tween Editor + Game ビュー |
| #50 / B | `images/ui-tween-preset-gallery.png` | 「プリセットギャラリー（Preset Gallery）」の「画面の説明」の表の直前 | プリセットギャラリー全体: タブ（出現/常時/消滅/強調/カタログ）、カード一覧、★お気に入り | 「出現」タブを開いた状態 | ウィンドウ単体 |
| #51 / C | `images/ui-tween-pathmove-handles.png` | 「＋点を追加 / −最後の点を削除」の行の直後 | SceneView: PathMove の経路（P0/P1…のハンドル、水色の線） | Property=PathMove の Track を作り、3点以上の経路を描く | SceneView をズーム |

### presentation.html

| # / 優先度 | ファイル名案 | 挿入位置 | 何を映すか | 準備 | 撮り方 |
|---|---|---|---|---|---|
| #52 / A | `images/presentation-editor-timeline.png` | 既存コメント `<!-- TODO: スクショ（Presentation Editor のタイムライン全体） -->` の位置（差し替え） | Presentation Editor 全体: 複数レーン（Anim/Se/Vfx/CameraShake/Signal）にトラックが配置され、タイムラインに再生ヘッドがある状態 | 剣攻撃デモの Presentation データを開く | ウィンドウ単体、幅 1300px 程度 |
| #53 / B | `images/presentation-editor-signal-lane.png` | 「Signal を手動で送る」の段落の直後 | 再生中の「Signal レーン」に表示されるボタン（例: onHit） | Kind=Signal のトラックを含む演出を再生中 | タイムライン下部の Signal レーン部分を切り出す |
| #54 / B | `images/presentation-demo-sceneview.png` | 「剣攻撃デモで確認する」の段落の直後 | Game ビュー: `PresentationSkillSlashPreviewScene` で剣攻撃再生中（VFX+SE、当たった瞬間の画面揺れ） | シーンを Play し、Space キーで「当たった」合図を送った瞬間 | Game ビュー全体 |

### camera-haptics.html

| # / 優先度 | ファイル名案 | 挿入位置 | 何を映すか | 準備 | 撮り方 |
|---|---|---|---|---|---|
| #55 / A | `images/shake-haptics-editor-window.png` | 既存コメント `<!-- TODO: スクショ（Shake / Haptics Editor の波形プレビュー） -->` の位置（差し替え） | Shake/Haptics Editor 全体: 波形プレビュー（Envelope の減衰曲線）、パラメータ欄、プリセットボタン一覧 | Camera Shake データを Explosion プリセットで開く | ウィンドウ単体。**カメラの揺れ・パッドの振動そのものは静止画に向かないため撮影しない**（波形グラフで代替） |

### spec-sync.html

| # / 優先度 | ファイル名案 | 挿入位置 | 何を映すか | 準備 | 撮り方 |
|---|---|---|---|---|---|
| #56 / A | `images/spec-sync-unity-window.png` | 「D-Drive 側の操作: 仕様書同期ウィンドウ」の手順リストの直後 | Unity 側「仕様書と同期」ウィンドウ: 「新規」「変更」の差分プレビュー一覧、チェック済みの行 | 発注ツールに新規・変更行を1件以上用意して「取得」を実行 | ウィンドウ全体。**「Web API URL」「人向け SPA URL」「読み取り/書き込みトークン」の入力欄は必ずダミー値に置き換えるかぼかす**（[35] §0.3 と同じ注意） |
| #57 / A | `images/order-tool-tree-view.png` | 「画面構成」の表、「発注ツリー（既定画面）」の行の直後 | 発注ツール（ブラウザ）の発注ツリー画面: Presentation 発注グループ1件 + 子の発注2〜3件、状態列 | ダミーの発注データ（練習用の表示名）を用意 | ブラウザウィンドウ全体（アドレスバーの URL・ログイン中のメールアドレス表示部分はぼかす） |
| #58 / B | `images/order-tool-detail-panel.png` | 「ファイル形式・ファイル名（2026-09-14 追加）」の段落の直後 | 発注の詳細パネル: 発注者/受注者、納品期限、ファイル形式/納品ファイル名、リファレンス（Markdown 表示） | ダミーの発注を1件開き、ファイル形式・ファイル名・簡単なメモを入力 | 詳細パネル部分を切り出す。実名・実メールは映さない |
| #59 / B | `images/order-tool-member-list.png` | 「ログイン」の見出しの直後 | メンバー画面: 許可リストの一覧（表記付き名前の列）、管理者専用セクション | ダミーのメンバーを2〜3件登録 | メンバー画面全体。**メールアドレス列は必ずぼかす**（[35] §0.3 と同じ注意） |

> #58 は当初「調整値タブ」も候補にしていたが、テーブル型調整値の編集グリッドは他ページから参照されないため優先度 C とし、今回は撮影対象から外した（必要になったら本リストに追記する）。

### glossary.html

撮影不要。用語の説明のみのページで、各用語には該当ページへのリンクがあり、そちらのスクリーンショットで説明が足りる。

## 5. 撮影結果（2026-09-17）

45 枚を受領し、Inspector の 3 枚（#4 / #16 / #35）を除く **42 枚を `docs/DesignerManual/images/` に配置**して、各 HTML の `<!-- screenshot: 36-#N -->` の位置に `<figure>` を挿入した。あわせて既存の `se-emitter-inspector.png` を削除した（§2）。

**この表が各番号の最新の状態**。§4 の各表は当初の撮影指示としてそのまま残してある。

### 5.1 方針の変更

| 決定 | 内容 |
|---|---|
| Inspector は撮らない | **Inspector のスクリーンショットはすべて不要**と判断（2026-09-17）。該当は #4 / #16 / #17 / #35 / #41 の 5 件と、既存の `se-emitter-inspector.png`。受領済みだった #4 / #16 / #35 の 3 枚も配置せず破棄し、HTML 側の `<!-- screenshot: 36-#N -->` コメントも削除した。Inspector の項目は本文の表で説明できているため、画像が無くても読める |
| 画像の最適化 | 受領時の合計は約 5.3MB（最大 494KB）で §3 の「1 枚 150KB まで」を超えるものが 8 枚あったため、**256 色パレット化（ディザ無し）で再エンコード**した。合計 約 1.5MB・最大 157KB（`vfx-editor-prefab-mode.png`）まで縮小。UI のスクリーンショットは元の色数が最大でも約 15,000 色のため、文字のにじみ・階調の破綻は生じていない |
| 機密情報の伏せ字 | #56（仕様書と同期）に GAS のデプロイ URL が写り込んでいたため、§3 のルールに従い **Web API URL / 人向け SPA URL の 2 欄をダミー文字列に差し替えた**（トークン欄は元から `*` 表示）。差し替え前の原本は配置していない |

### 5.2 配置済み（42 枚）

| # | ファイル | 挿入先 |
|---|---|---|
| #1 | `root-overview.png`（撮り直し） | Readme.html（既存 `<figure>` を差し替え） |
| #2 | `getting-started-manual-button.png` | getting-started.html |
| #3 | `asset-browser-window.png`（撮り直し） | asset-browser.html（既存 `<figure>` を差し替え） |
| #5 | `asset-browser-new-dialog.png` | asset-browser.html |
| #7 | `asset-browser-usage-list.png` | asset-browser.html |
| #8 | `asset-browser-dependency-tree.png` | asset-browser.html |
| #9 | `asset-browser-unused-list.png` | asset-browser.html |
| #10 | `asset-browser-delete-overview.png` | asset-browser.html |
| #12 | `asset-browser-delete-method.png` | asset-browser.html |
| #13 | `asset-browser-delete-result.png` | asset-browser.html |
| #18 | `scene-sound-drag-emitter.png` | scene-sound.html |
| #21 | `vfx-editor-prefab-mode.png` | vfx-editor.html |
| #22 | `anchor-editor-window.png` | anchor-data.html |
| #24 | `anchor-group-editor-window.png` | anchor-group.html |
| #25 | `anchor-group-sceneview-grid.png` | anchor-group.html |
| #26 | `model-editor-window.png` | model-editor.html |
| #28 | `anim-editor-timeline.png` | anim-editor.html |
| #29 | `anim-editor-timeline-zoom.png` | anim-editor.html |
| #30 | `anim-editor-blend-tools.png` | anim-editor.html |
| #31 | `anim-editor-sceneview-playback.png` | anim-editor.html |
| #32 | `anim2d-editor-create-mode.png` | anim2d-editor.html |
| #33 | `anim2d-editor-detect-preview.png` | anim2d-editor.html |
| #34 | `anim2d-editor-edit-mode.png` | anim2d-editor.html |
| #36 | `material-editor-sphere.png` | material-editor.html |
| #37 | `material-editor-compare.png` | material-editor.html |
| #38 | `material-editor-conversion.png` | material-editor.html |
| #39 | `prefab-editor-window.png` | prefab-data.html |
| #42 | `canvas-editor-nav-graph.png` | canvas-editor.html |
| #43 | `canvas-editor-elementfx-panel.png` | canvas-editor.html |
| #44 | `canvas-editor-preview-gameview.png` | canvas-editor.html |
| #45 | `button-skin-editor-window.png` | ui-skin.html |
| #46 | `button-skin-state-transition.png` | ui-skin.html |
| #47 | `slider-editor-response-curve.png` | ui-skin.html |
| #48 | `slider-skin-editor-window.png` | ui-skin.html |
| #49 | `ui-tween-editor-window.png` | ui-tween.html |
| #50 | `ui-tween-preset-gallery.png` | ui-tween.html |
| #51 | `ui-tween-pathmove-handles.png` | ui-tween.html |
| #52 | `presentation-editor-timeline.png` | presentation.html |
| #55 | `shake-haptics-editor-window.png` | camera-haptics.html |
| #56 | `spec-sync-unity-window.png` | spec-sync.html（URL 2 欄をダミーに差し替え済み） |
| #57 | `order-tool-tree-view.png` | spec-sync.html（表の中にコメントがあったため、表の直後に置いた） |
| #58 | `order-tool-detail-panel.png` | spec-sync.html |

`<figure>` の挿入位置について: コメントが表（`<table>`）や箇条書き（`<ol>` / `<ul>`）の内側にある番号（#18 / #29 / #33 / #44 / #46 / #51 / #56 / #57）は、そのまま挿入すると `<tr>` の間・`<li>` の間に `<figure>` が入って HTML として壊れるため、**その表・箇条書きを閉じた直後**に置いた。

### 5.3 撮影を取りやめ（7 枚）

| # | ファイル名案 | 理由 |
|---|---|---|
| #4 | `asset-browser-inspector-header.png` | Inspector のため（受領済みだが破棄） |
| #16 | `se-data-inspector.png` | Inspector のため（受領済みだが破棄） |
| #17 | `bgm-data-inspector.png` | Inspector のため。あわせて、撮影しようとした時点で Fade 欄が `No GUI Implementation` と表示される不具合を確認している（→ **2026-09-17 に修正済み。[39](39_usability_fixes_2026-09-17.md) U-14 / [09 §8](09_editor_tools.md)**。撮影の取りやめ自体は変更なし） |
| #35 | `material-data-common-channels.png` | Inspector のため（受領済みだが破棄） |
| #40 | `prefab-editor-sceneview.png` | 不要と判断 |
| #41 | `canvas-data-inspector-wires.png` | Inspector のため |
| #59 | `order-tool-member-list.png` | 不要と判断 |

上記 7 件は HTML 側の `<!-- screenshot: 36-#N -->` コメントも削除した。

### 5.4 未撮影（10 枚）

不具合が理由のものは、直った後に撮影する。HTML 側のコメントは `<!-- screenshot: 36-#N / 未撮影: 理由 -->` の形で残してある。

> **2026-09-17 夕: 10 枚のうち 9 枚は原因が解消し、撮影できる状態になった**（[39](39_usability_fixes_2026-09-17.md) の U-1 / U-12 / U-13 / U-15 / U-24 の修正）。残るのは #53（U-25 が未着手）だけ。
> ただし **Unity 上での動作確認はまだ**なので、撮影のときに「本当に直っているか」の確認も兼ねることになる。

| # | ファイル名案 | 未撮影の理由 |
|---|---|---|
| #6 | `asset-browser-spec-picker.png` | 仕様書 URL を設定しても「仕様書 URL が未設定」と表示される不具合のため → **2026-09-17 に修正済み（[39](39_usability_fixes_2026-09-17.md) U-15）。撮影可能** |
| #11 | `asset-browser-delete-references.png` | 撮り忘れ。#10 / #12 と同じウィンドウなので、次の撮影でまとめて撮る |
| #14 | `asset-browser-preview-inline.png` | Asset Browser 下部のサウンドのプレビューバーの UI が崩れているため → **2026-09-17 に修正済み（[39](39_usability_fixes_2026-09-17.md) U-12）。撮影可能** |
| #15 | `validation-run-all.png` | 個別検証がある種別と無い種別があり、VFX Editor では展開しても何も表示されないため → **2026-09-17 に修正済み（[39](39_usability_fixes_2026-09-17.md) U-13）。撮影可能** |
| #19 | `vfx-data-anchorpoint-sceneview.png` | SceneView に基準（原点）が描かれず、LocalOffset だけでは位置関係が分からないため → **2026-09-17 に修正済み（[39](39_usability_fixes_2026-09-17.md) U-24、[21 §3.10](21_anchor_spec.md)）。撮影可能** |
| #20 | `vfx-editor-anchor-handle.png` | 撮り忘れ + U-24 待ちだった → **2026-09-17 に U-24 を修正済み。撮影可能**（#19 / #23 と同じタイミングで撮れる） |
| #23 | `anchor-editor-sceneview.png` | #19 と同じ（SceneView に基準が描かれない）→ **2026-09-17 に修正済み（U-24）。撮影可能** |
| #27 | `model-editor-turntable-sceneview.png` | 3D オブジェクトのプレビューが透明になり、何も写らないため → **2026-09-17 に修正済み（[39](39_usability_fixes_2026-09-17.md) U-1）。撮影可能**。真因は Material スロットではなく `ModelsManager` が `renderingLayerMask` に 0 を書いていたこと |
| #53 | `presentation-editor-signal-lane.png` | Signal を手動で送る操作のやり方が分からないため（手順の整理・UI の改善が必要）→ **U-25 が未着手。これだけまだ撮れない** |
| #54 | `presentation-demo-sceneview.png` | #27 と同じくモデルが透明になり、何も写らないため → **2026-09-17 に修正済み（U-1）。撮影可能** |

### 5.5 残っている確認事項

- ~~Web 版マニュアルの再生成がまだ~~ → **2026-09-17 に実行済み**。Node は PATH に無かったが、VS Code 同梱の Electron を `ELECTRON_RUN_AS_NODE=1` で Node として使えた（[40](40_manual_screenshot_workflow.md) 手順 9）。`Tools/SpecWeb/html/manual/` は 26 ページ・2.9MB に。drift チェックを含む SpecWeb テスト 501 件が green
- #58 として受領した `order-tool-detail-panel.png` は、§4 で想定していた「詳細パネル（ファイル形式・納品ファイル名・リファレンス）」ではなく**「新規発注」パネル**が写っている。説明文と合わせるか、撮り直すかを決める
- #1 / #3（`root-overview.png` / `asset-browser-window.png`）は下端のプレビューバーが途中で切れており、「更新者」列に Windows ユーザー名（`yamag`）が入ったままになっている。§3 の「Windows ユーザー名を映さない」に照らすと、撮影用アカウントで撮り直すか、当該列をぼかすのが望ましい
- #58 の一覧にも実在の姓（`山口`）が 1 行入っている。§3 のとおりダミー名に置き換えて撮り直すのが望ましい
