# 23. 実装確認手順書（2026-09-10〜11 自律作業分）

> 2026-09-10〜11 に Claude Code が自律実装した Phase 3 後半（3-5〜3-13）・Phase 4（4-1〜4-12 / 4-14〜4-18）と、レビュー対応（VFX / Anim 10 件、Codex 指摘）の **人による確認項目**。
> 自動検証（isuzu MCP: コンパイル 0 エラー / EditMode / PlayMode テスト）は各コミット時に通しているが、**見た目・音・操作感・実機**は未確認。上から順にやれば全機能を一巡できる。
>
> 共通の前提: Unity 6000.3.13f1 で `Assets/GameData/Scenes` の確認用シーンを開くか、各エディタの「確認用シーンを開く / 配置」を使う。ウィンドウ内描画は廃止しており、確認はすべて SceneView / Game ビューで行う（2026-09-10 決定、[09] §2）。
> 不具合を見つけたら、該当チケット行（[11_tasks.md](11_tasks.md)）と該当設計書の「実装メモ」を参照して修正する。



## レビュー対応(コミット 6359d29)
- docs/19 §3.1 手順 6(Anim、4〜22)/ 手順 7(Inspector / アイコン)/ 手順 7b(Model Editor)
- Anim Editor: ⏸ → ▶ / ⏸ → 別 Anim を Play / ⏸ → 対象解除 で Tick が止まり続けないこと
- Anim Editor: 借用 Animator で再生 → 停止(ポーズ残る) → Ctrl+S でシーン保存 → ポーズが元に戻り、シーンにポーズが書かれないこと
- Anim Editor: タイムラインのマーカードラッグ → Ctrl+Z で 1 回で戻ること
- VFX Editor: FadeOutSec > 0 の VFX を Stop → 一時停止 → 再開 で放出が再開しないこと
- 無効 Handle 警告がコンソールに出続けないこと(Anim / VFX Editor で再生終了後に放置)

## 3-5 Material(コミット a9bb1d8)
- Tools > D-Drive > Editors > Material: MaterialData を作成(Create > D-Drive > Material > Material Data)し、Common を編集 → 「シーンにプレビュー球を配置」で URP Lit の見た目が反映されること(Albedo / Tint / Normal / Emission / Transparent / DoubleSided)
- MaterialAnim(OffsetU + Rate + Loop)で UV スクロールが SceneView で動くこと
- TextureData を作成 → Texture を入れて MaterialCommon.Albedo に ID で参照 → 球に貼れること
- ModelData.Slots に Material ID を入れて Model Editor で配置 → 差し替わること
- Validation > Run All に MaterialDataValidator の項目が出ること

## 3-6 シェーダー変換(コミット 予定)
- Tools > D-Drive > Editors > Material 変換: 変換元 MaterialData + 変換先 Shader(URP Unlit 等)で差分が出る / 「新規 MaterialData として作成」で同じカテゴリに作成される / 「この Data を変換」→ Ctrl+Z で戻る
- ShaderConversionTable アセット(Create > D-Drive > Material > Shader Conversion Table)にルールを追加して「変換テーブルを再読み込み」→ 対応(→)表示になること

## 3-8 Texture Importer 規約(コミット 669783a)
- Assets/SourceAssets 配下に `Xxx_N.png` / `Xxx_M.png` / `Xxx_UI.png` を置いてインポート → Texture Type / sRGB / Mipmap が規約どおりになること(TexturePostprocessor)
- Material Editor で TextureData を選択 → Importer 要約 + 規約名 + 「命名規約を適用して再インポート」ボタンの表示(差分があるときだけ)
- Validation > Run All に TextureDataValidator の項目(Normal/Mask/Sprite/9-slice/POT/MaxSize)が出て FixAction が効くこと
- 必要なら Assets/GameData に TextureImportProfile を作成して規約をカスタマイズ(無ければ組み込み既定)

## 3-7 Maya FBX 自動生成(コミット 予定)
- 実 FBX(Maya から Stingray PBS / aiStandardSurface で書き出し)を Assets/SourceAssets/<Category>/ に入れ、インポート後のログ「[DDrive] Maya インポート」で MaterialData / TextureData が Assets/GameData/Material/<Category>/ に作られること
- Unity が生成した Material のテクスチャプロパティ名が既定(_BaseMap/_BumpMap/_MetallicGlossMap/_EmissionMap)以外なら、MayaImportProfile(Create > D-Drive > Material > Maya Import Profile)の PropertyChannels に追加する
- 生成された MaterialData の Specific / RenderQueueOffset を編集 → FBX を再インポート → Common だけ更新され固有調整が残ること(ログに「更新: ...(Common: ...)」)
- Tools > D-Drive > Generate > 「選択したモデルから MaterialData を生成」が FBX 選択時だけ有効になること

## 3-9 Material プレビュー拡張(コミット 予定→済)
- Material Editor: 形状(球 / 板=Quad / Cube / ModelData)を切り替えて配置、ターンテーブルの回転、ライト回転スライダー(Directional Light の Y 回転、撤去で元に戻る)を SceneView で確認
- 「比較対象」に別の MaterialData を入れて「並べて比較」→ 右 1.5m に 2 体目が出ること
- Material 変換 → 「新規 MaterialData として作成」→ 「Material Editor で比較」で変換前後が並ぶこと
- 板が Quad(片面)で意図どおりか(Plane が良ければ変更)

## 3-12 Anim2D ランタイム(コミット 1c16af4)
- 方向付き Anim2DData を BlendTree(x, y)付き Controller で `Anim2D.Play(id, animator, dir)` → 方向が切り替わること(ランタイム / 実機での確認)

## 3-11 Anim2D エディタ(コミット 予定)
- Tools > D-Drive > Editors > Animation (2D): 作成モードで小さなスプライトシート(例 4x1)を Grid 入力 → Name/State → 生成 → Assets/GameData/Anim2D/Clips/Anim_<Name>_<State>.anim と ANIM2D_… .asset ができること
- 方向セット=Eight + 8 テクスチャ + AnimatorController で BlendTree(FreeformDirectional2D、x/y)が作られること
- 編集モード: Anim2DData を読み込み → 「確認用シーンを開く」→ SpriteRenderer 付きプレビュー物が置かれ ▶ で SceneView 上でコマが進むこと(ウィンドウ内描画は廃止)。リタイミング/Uniform → 適用 で Clip のキー時刻が変わり Retiming が保存されること
- com.unity.2d.sprite 未導入のため、スライスは TextureImporter.spritesheet 経由。Sprite Editor パッケージを入れる場合は挙動確認

## 3-13 Anim2DEditor(コミット 済)
- Anim2DData の Inspector に「Anim2D Editor で開く」「Anim Editor で開く」の両方が出ること
- Anim2D Editor(編集)→「Anim Editor で開く」→ 確認用シーンを開く → SpriteRenderer 付きプレビュー物で ▶ 再生され、Frame イベントで SE/VFX が出ること
- 方向付き Anim2DData で BlendTree に x/y が無いとき Validation に Error + 「修正」でパラメータが追加されること
- タイムラインへの SE/VFX アセットの直接 D&D は未実装(既存の Anim Editor と同じく「追加」+ ドロップダウン)。必要なら AnimEditor 側の新規スコープ

## 4-4 Prefab(コミット 8e3ea2f / 4cb3e4b)
- PrefabData を作成 → Inspector「Prefab Editor で開く」→「確認用シーンに配置 / 撤去」で SceneView に出る・消えること
- Flags.Pool=None(既定)の Prefab は Despawn で破棄、Pooled は再利用されること(ランタイム確認)
- Validation > Run All に PrefabDataValidator(GameplayTags のタイポ検出 "Destructable")が出ること

## ModelData 種別修正(コミット 6a1046b)
- Assets/GameData/Model/Player/MODEL_Player_Model.asset(旧 PREFAB_Player_Model)を参照していたシーン / Model Editor の確認用モデルが正しく解決されること(参照は GUID なので影響しない想定)
- Validation > Run All で ModelDataValidator の項目が出るようになったこと(以前は種別不一致で走っていなかった)

## 4-1 Canvas(コミット 201285b)
- CanvasData を作成(Prefab に Canvas + Selectable を持つ UI プレハブ)→ Canvas Editor「Selectable を自動収集」で Navigation が埋まる / Ctrl+Z で戻る
- 「確認用シーンで開く」→ シーン上の [D-Drive] UI Root 配下に開き、Fade 遷移(Duration>0)が Game/Scene ビューで見えること →「閉じる」で消える
- ランタイム: Ui.Open / Popup(モーダルの入力遮断)/ CloseTop(戻る)/ PauseGameWhileOpen の動作(実機 or Play モードで確認)
- Validation > Run All に CanvasDataValidator(パス不整合 / 到達不能 Selectable)が出ること

## 4-6 / 4-2 UiButton + 配線(コミット a9600d3)
- ButtonSkinData を作成 → Inspector「Skin Editor で開く」→「確認用シーンに配置」で UiButton サンプルが出て、Hover / Pressed / Disabled / Locked の Tint / Scale と SE が見える・聞こえること
- CanvasData.Buttons(ButtonWire: Click → SendSignal / OpenCanvas / CloseSelf)を設定し、Play で UiButton を押すと実行されること。LongPress / Repeat / DoubleClick のタイミング
- パッドの十字キーで Selectable / UiButton 間のフォーカス移動(NavNode 指定あり = 明示、なし = Unity 自動)

## Codex 最終レビュー(a9600d3)の扱い
- P1(Repeat が Disabled/Locked 後も発火)→ 4-8 完了後に修正予定(UiButton.Advance の Repeat 分岐を Interactable/Locked ガードに通し、Disable/Lock で hold 状態を解除)
- P2(同フレーム多重発火防止が全コントロール横断)→ 設計書 docs/15 A-2 の明示仕様「全UiButton横断」のため据え置き。docs に「意図的」と明記する

## 4-8 UiTween(PR #1 マージ済み)
- Tools > D-Drive > Editors > UI Tween: UiTweenData を作成 → 「プリセットから Tracks を生成」(FadeIn / SlideInLeft / PopIn / Shake / Pulse など)→「確認用シーンに配置」→ ▶ 再生 で Game ビューの見た目(イージング / オーバーシュート / 画面外からのスライド)
- ButtonSkinData の Pressed.EnterPreset = PunchScale を Play で確認
- 近似実装のプリセット(FlipInX/Y, FlipOutX/Y, RotateIn/Out, TypeFillIn, RainbowTint, WobbleLoop, ColorFlash, Jelly, Tada, RubberBand, AttentionJump)は見た目が仮

## 4-9 / 4-7 ElementFx(PR #2 マージ済み)
- Canvas Editor: 「要素を自動収集」→「一括適用: 全ボタンに <preset>」→「確認用シーンで開く」で出現(スタッガー)/ 常時 / 消滅(閉じる時に完了待ち)の見た目
- UiLayerSettings アセットを作成して DDriveRuntimeBootstrap.LayerSettings に割当 → SkinId 未設定の UiButton にレイヤー既定 Skin / SE が効くこと
- パッドのフォーカス移動(Selected)で HoverSe が鳴り、マウス Hover と二重に鳴らないこと

## 4-14〜4-16 / 4-18 UiSlider(PR #3 マージ済み)
- Tools > D-Drive > Editors > Slider Skin: 「確認用シーンに配置」→ Game ビューでドラッグ / トラッククリック / ホイール / パッド左右、Notch / Limit の SE、プリセット 5 種(音量 / 感度 / HP バー / スタミナ / キャラメイク)
- Canvas の SliderWire(SetOption: MasterVolume)を設定した設定画面で Play → 音量が AudioListener.volume に反映され PlayerPrefs に保存・復元されること(Bgm / Se / Voice のバス音量は Phase 5 で接続)
- パッドの左右キーはスライダー操作に消費され、EscapeOnLimit=true のとき端で隣へ抜けること

## 4-10 / 4-11(PR #4 マージ済み)
- UI Tween Editor: 新プリセット(FlipInX/Y, RotateIn/Out, TypeFillIn, RainbowTint, WobbleLoop, ColorFlash, Jelly, Tada, RubberBand, AttentionJump)を生成 → 確認用シーンで再生して見た目(向き / 速さは実装者の判断値)
- PathMove トラックで「確認用シーンに配置」→ SceneView の制御点ハンドルをドラッグ → Undo が効くこと
- UiPresetCatalog を作成して UiTweenData を登録 → UI Tween Editor / Canvas Editor のドロップダウンに「[Catalog] 名前」が並ぶこと
- Canvas Editor の ElementFx 割当(ポップアップ / ObjectField / 他要素へコピー)

## 4-3 Canvas ノードグラフ(PR #5 マージ済み)
- Canvas Editor「Navigation グラフ」: ポート(▲▼◀▶)をドラッグして別ノードへ → リンクが張られ Undo できること、右クリック「FirstSelected にする / リンク削除」、ダブルクリックで Hierarchy に Ping
- 要素が多い Canvas でパン(中ドラッグ)/ ズーム(ホイール)の見やすさ
- 「確認用シーンで開く」→ パッド ▲▼◀▶ / 決定 でフォーカスが Game ビューと一致すること(EventSystem が無い EditMode ではウィンドウ内のラベルだけ)

## 4-17 SliderEditor / 4-12 プリセットギャラリー(2026-09-11。EditMode 237 / PlayMode 466 green)
- Tools > D-Drive > Editors > Slider: 「確認用シーンにサンプルを配置」→ 応答曲線グラフ(Response=InQuad で下側が細かい)、ノッチ可視化、◀▶ / ドラッグ模擬で Game ビューのスライダーが動き Notch / Limit の SE が鳴ること、「2 体目を配置して FollowMotion を比較」、「全状態を並べる」で 6 状態の Skin
- SliderSkinData の Inspector に「Slider Editor で開く」が出ること
- Tools > D-Drive > Editors > UI Tween · Preset Gallery: タブ / 検索 / お気に入り、カードの「この要素に適用」「Canvas 内一括適用」「選択中のシーン要素で再生」「独自プリセットとして登録」

## 4-13 Simulated Spawn(PR #7)/ Phase 4 レビュー対応(2026-09-11)
- Play で NetMode=Simulated の PrefabData を `Prefabs.Spawn` → LocalLoopbackBridge(単機)では生成されること。NGO 接続時の NetworkObject 複製は Phase 6 で接続予定
- レビュー対応の手動確認: プリセットギャラリーの「選択中のシーン要素で再生」→ 停止で位置 / スケール / alpha が元に戻り、追加された CanvasGroup が Undo で消えること(P1-6)。Canvas を Scale 遷移で閉じて再度開いたとき表示されること(P1-1)。設定画面で音量を変えて Play 終了 → 再度 Play で値が復元されること(P1-4)
- docs/24 の整理項目 1〜6 は未対応(後続)
