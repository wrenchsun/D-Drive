# 19. VFX 使い勝手レビューと設計見直し（2026-09-08）

関連: [04_vfx.md](04_vfx.md) / [09_editor_tools.md](09_editor_tools.md) / [11_tasks.md](11_tasks.md) 2-12 / [14_networking.md](14_networking.md) §12

Phase 2 のエフェクト実装（2-1〜2-11）が一区切りついた時点で「使い勝手が悪い」という評価を受け、**デザイナーが VfxEditor だけで調整を完結できるか**を軸にランタイム・エディタ・ドキュメントを見直した記録。判断の根拠を残し、同種の問題を Phase 3 以降のエディタ（Anim/Material/Presentation）で繰り返さないためのチェックリストを末尾に置く。

---

## 1. 見つかった問題と判断

### A. エディタ（VfxEditorWindow）— 使い勝手の本丸

| # | 問題 | 影響 | 判断・対応 |
|---|---|---|---|
| A-1 | Prefab / LifeMode / Duration / Render / Layer は Inspector でしか編集できず、ウィンドウは Anchor・Params・Events だけ | Inspector とウィンドウを行き来する。ウィンドウで「なぜ出ないか」が分からない | **基本設定 Foldout を追加**（SerializedObject バインド。Undo/Prefab 変更検知は Unity 標準）。RenderLayer は `LayerField`、LightLayerMask は Rendering Layer 名付き `MaskField` |
| A-2 | 再コンパイル・PlayMode 遷移で対象・スポーン先が消える（`_target` が非シリアライズ） | 調整のたびにアセットを入れ直す | `[SerializeField]` 化（対象 / スポーン先 / ロック / リピート / ハンドル ON / 速度）。AudioEditor と同じ方針 |
| A-3 | Project ウィンドウで別の VfxData を選んでも追従しない（開いた時だけ） | 複数 VFX を順に見るときにドラッグし直し | `OnSelectionChange` で追従 + ツールバー「🔒 対象を固定」 |
| A-4 | Anchor を変えても再生中の実体に反映されない。Path をドロップダウンで選んでも Space=World のままだと効かない | 「効いていない」に見える。停止→再生を繰り返す | `VfxManager.ReapplyAnchor` で**即時反映**。Path/Space/スポーン先/Prefab など再スポーンが必要な変更は自動で撮り直し。ドロップダウン選択時は Space を NamedObject に自動切替。**解決状態を常に文字で表示**（✓ / ⚠ 見つからない / ⚠ スポーン先未指定） |
| A-5 | Anchor 位置の編集手段が 2D パッド + 数値だけ。SceneView で見ながら動かせない | 3D 的な位置合わせが試行錯誤になる | **SceneView に移動/回転ハンドル**（`SceneView.duringSceneGui`）。逆変換は `AnchorPose` の式を共有し、AnchorPoint の SpawnOffset・ランダム分を差し引く |
| A-6 | OneShot の VFX は毎回 ▶ を押す | 数十回押すことになる | **リピート**トグル（終了後 0.35 秒で再スポーン。Loop は対象外） |
| A-7 | 速度スライダーが EditMode の手動 Simulate にしか効かない（PlayMode 中は無視） | PlayMode 中の確認で速度が変わらない | PlayMode 中は `Manager.SetSpeed`（simulationSpeed）に反映。EditMode は従来どおり dt 乗算（二重適用しない） |
| A-8 | Undo/Redo 後に UI が古い値のまま | 混乱する | `Undo.undoRedoPerformed` で UI・再生中実体・SceneView を同期 |
| A-9 | スポーン物とプール残骸が Hierarchy 直下に散らばる | 確認用シーンが汚れる | `[D-Drive] VFX Preview` ルート（DontSave）配下にまとめ、シーン切替で台帳をリセット |
| A-10 | Params の定義（追加/削除/TargetProperty）は Inspector 頼み | 「TargetProperty をどこで書くか」が分からない | Params の PropertyField を「定義の追加・削除」Foldout として内包。定義が変わったときだけ即時反映コントロールを作り直す（Default 編集でフォーカスが飛ばない） |
| A-11 | Validation 結果はウィンドウに出ない | Prefab 未設定・URP 非対応シェーダー等に気づくのが遅れる | `VfxDataValidator` をその場で実行して Error/Warning を表示 |
| A-12 | 確認用シーン・Prefab を開くのにメニューを探す | 導線が長い | ツールバーに「確認用シーンを開く / Prefab を開く / Project で表示」。シーンにカメラ・ライトが無ければ警告 |
| A-13 | 535 行の単一ファイル | 変更しづらい | `VfxEditorWindow.cs`（対象/再生/基本設定）+ `.Anchor.cs` + `.Params.cs` の partial に分割 |

### B. ランタイム（VfxManager / VfxData / Vfx）

| # | 問題 | 影響 | 判断・対応 |
|---|---|---|---|
| B-1 | `LightLayerMask` の既定値 0 がそのまま `renderingLayerMask=0` に適用される | Lit 系マテリアルのパーティクルが**一切ライトを受けない**。原因に気づきにくい | 既定値を 1（Default）に変更し、**0 = Prefab の設定を上書きしない**と定義（既存アセットの 0 は安全側に倒れる） |
| B-2 | `FollowRotation=true` のとき `LocalEuler` が完全に無視される | 「向き」スライダーが効かない | 姿勢の式を `AnchorPose` に統一し、回転 = アタッチ先回転 × Euler(LocalEuler) × jitter に。LocalEuler=0 の既存アセットは挙動不変（[04] §2.6） |
| B-3 | 姿勢の計算が Spawn/Tick/エディタで別々に書かれていた | 一箇所直すと他がズレる | `Runtime/Anchoring/AnchorPose.cs` に純粋関数として集約。Manager・エディタ・テストが共有 |
| B-4 | `Vfx` 静的ファサードが Spawn/Stop/Kill/Preload のみ。設計書の `h.Move(...)` が書けない | プログラマーが Manager 実体を掴む羽目になる | `Vfx.Move/Attach/Detach/SetSpeed/SetParam/IsPlaying` + `VfxHandleExtensions`（`h.Move(pos)` 等）。未 Bind は no-op |
| B-5 | Root がシーン破棄で先に消えると Tick が NRE。Pool は破棄済み GO の `OnReturn` を呼ばず台帳が残る | シーン切替時にエディタ/実機で例外 | `ReturnToPool` で `CleanupBookkeeping` を必ず通す。Tick で `Root == null` を検出して掃除 |
| B-6 | `VfxData.Anchor` が `default`（LocalScale=0）で生成される | エディタのスケール欄が 0 で混乱 | 既定値を `AnchorDef.WorldDefault`。0 は 1 扱いを維持（旧アセット互換） |
| B-7 | EditMode の手動 Simulate が PreviewService と SceneVfxPreviewDriver に重複 | 片方だけ直すと差が出る | `EditModeParticleStepper` に共通化 |

### C. ネットワーク（MS2026 移植前提での統一。ユーザー指示 2026-09-08）

| # | 問題 | 判断・対応 |
|---|---|---|
| C-1 | NGO のバージョンが D-Drive 2.2.0 / MS2026 2.13.2 で不一致 | 2.13.2 に統一（同じ Unity 6000.3.13f1 なので互換） |
| C-2 | `NgoNetBridge` の `[ClientRpc]` は NGO 2.x では `SendTo.NotServer` 扱いで**ホスト自身に届かない** → ホストの Manager が Cosmetic を再生できない | 統一 RPC `[Rpc(SendTo.ClientsAndHost)]` に変更 |
| C-3 | Client からの `Broadcast` は警告して破棄（MS2026 の「入力はクライアントが送る」モデルと噛み合わない） | `[Rpc(SendTo.Server)]` で Host に依頼 → Host がレート制限・種別検証して全員へ配る（[14] §9 の方針を実装） |
| C-4 | `NetChannel.Unreliable` が無視され常に Reliable | `RpcDelivery.Unreliable` に対応（1000 bytes 超は Reliable にフォールバック） |
| C-5 | ログ表記が `[DDrive]` | `[Net/Host]` / `[Net/Client]` に統一（MS2026 Networking.md §5） |

詳細な対応表は [14_networking.md](14_networking.md) §12。

---

## 2. 見送った・後回しにしたもの

| 項目 | 理由 | いつ |
|---|---|---|
| VFX Graph 対応 | パッケージ未導入。`VisualEffect` 検出を Instance 生成時に足せば同じ Handle API で扱える設計は維持 | 必要になった時点 |
| `Spawn(VfxId, in PlayContext)` | Presentation 層（Phase 5）の PlayContext 定義待ち | Phase 5 |
| Cosmetic の `AnchorNetId` 追従・`paramOverrides` 同期 | `INetBridge` に Transform→NetId の逆引きが無い。NGO 側で `NetworkObject.NetworkObjectId` を使えば実装できる | Phase 5（Presentation ネット再生）と同時 |
| Params の Curve/Gradient 即時反映 | MaterialPropertyBlock 非対応。ベイク（テクスチャ化）が必要 | 要望があれば |
| SceneView ハンドルでのスケール編集 | 数値入力で足りると判断 | 要望があれば |
| 複数スロットの Anchor 即時反映 | スロットは別 VfxData を並べる用途で、Anchor 編集はメイン対象のみ | — |
| ModelEditor の SceneView 方式移行 | ターンテーブル用途はプレビューシーン方式が適する | 要望があれば |

---

## 3. 検証状況

- コンパイルは Unity 6000.3.13f1 上で成功を確認済み（2026-09-08 11:20、DDrive 由来のエラー・警告なし）。EditMode テストの実行結果は未確認 → 下記 §3.1 の手順で確認する
- 2026-09-08 午後の再確認: Unity MCP はポート 8080 を別プロセス（`Livelist.exe`）が占有していたため接続不可（docs/20 §1 の対処表参照）。MCP 無しで行った静的確認は次の通り: 変更した Runtime 5 ファイルに LINQ / `Instantiate` / `Resources.Load` / `UnityEditor` 参照なし、`Tick` 経路にクロージャ・boxing なし（`OnReturnedToPool` のラムダは Spawn 時 1 回で改修前から存在）、`ProjectSettings/EditorBuildSettings.asset` 等の改行のみ差分 6 件は `git checkout` で戻した。その後ポートを 8081 に変更して MCP 接続を回復し、`run_tests`（EditMode）を実行: **109 件中 109 件 green**（新規テスト含む）。初回実行で `AssetCreationServiceTests.Create_GeneratesConventionalFileNameIdAndCatalogEntry` が Id=0 で失敗したが、原因は今回の改修と無関係の既存不具合（`CreateAsset` 直後の `CreateFolder` による再インポートで Id と dirty が消える。1 回おきに再現）で、`AssetCreationService.Create` を修正（カタログフォルダを先に作成 + `SaveAssetIfDirty`）して解消。同テストを単独 3 回 + 全件で green を確認。docs/12 §3 にチェック項目を追加。`Tools/D-Drive/Validation/Run All` はテスト asmdef 内のダミー `IValidator`（`always fails`）を拾っていたため、`CI.DiscoverValidators` で `DDrive.Tests.*` アセンブリを除外（`AssetIdGenerator.FindDefinitions` はテストがテスト用型の検出を前提にしているため除外しない）。残る Validation エラーは確認用データの内容（`BGM_Title_Test` / `SE_Player_Slash` の Clip 未設定、`VFX_Player_Slash` のマテリアルが Built-in 用シェーダー）で、デザイナー側の修正対象。VFX Editor の手動操作確認（§3.1 手順 2）は人が実施し問題なし（2026-09-08）。追加要望「Prefab 内でも再生確認」は同日実装（`SceneVfxPreviewDriver` のプレハブモード対応。対象 Prefab 自身のステージでは二重表示を避けてその場再生、[04] §5、テスト 116/116 green）。調査中に EditMode の手動 Simulate では `IsAlive` が true のままで OneShot が終わらない（リピートが始まらない）ことが分かり、同日修正。Anchor 仕様改定の要望は [21_anchor_spec.md](21_anchor_spec.md) に提案としてまとめた。デザイナー向けマニュアル（`docs/DesignerManual/`）に `vfx-data.html` / `vfx-editor.html` を追加し、トップと用語集を更新。**同日午後に Anchor 仕様改定（[21]）を実装**: この時点で判明したこととして、`Tests/Runtime` は asmdef が全プラットフォーム対象のため Test Runner では PlayMode テストであり、本書の「EditMode 109/112/116 件 green」は Editor 側のみだった。PlayMode（Runtime 側 258 件）も同日 green を確認し、CLAUDE.md の手順を「EditMode + PlayMode 両方」に改めた
- 追加したテスト: `AnchorPoseTests`（式の往復）/ `VfxFacadeTests`（未 Bind no-op・拡張メソッド委譲）/ `VfxManagerTests`（既定値・ReapplyAnchor・FollowRotation×LocalEuler・LightLayerMask・破棄済み Root・TryGetAnchorTarget）/ `SceneVfxPreviewDriverTests`（プレビュールート・Dispose・ReapplyAnchorToAll）
- `NgoNetBridge` は EditMode テストで検証できない（NetworkManager が必要）。**MPPM または実機 2 台で `NetBridgeSmokeTest` を回して、ホスト側でも Cosmetic VFX が出ることを確認する**こと

### 3.1 検証手順（2026-09-08 改修分）

#### 手順 1: EditMode テスト

いずれか 1 つでよい。

**A. Test Runner（推奨）**
1. Unity のウィンドウをクリックし、Console にコンパイルエラーが無いことを確認する
2. `Window > General > Test Runner` → **EditMode** タブ
3. ツリーで `DDrive.Tests.Editor` と `DDrive.Tests.Runtime` を展開し **Run All**
4. 期待: 全件 green。今回追加分は `AnchorPoseTests`（7 件）/ `VfxFacadeTests`（2 件）/ `VfxManagerTests` の `NewVfxData_HasSafeDefaults`・`ReapplyAnchor_*`・`FollowRotation_*`・`LightLayerMask_*`・`Tick_DestroyedRoot_*`・`TryGetAnchorTarget_*` / `SceneVfxPreviewDriverTests` の `Play_ParentsSpawnedObjectUnderPreviewRoot`・`Dispose_RemovesPreviewRoot`・`ReapplyAnchorToAll_ReflectsEditedOffset`
5. 失敗があればテスト名と Console のスタックトレースを控える

**B. MCP 経由**
1. `Window > MCP for Unity > Toggle MCP Window` → Transport を HTTP (Local) → **Start Server** → **Connect**
2. Claude Code を再起動（`.mcp.json` の許可ダイアログで許可）
3. 「EditMode テストを全部実行して結果を教えて」と依頼する（内部で `run_tests` が走る）

**C. コマンドライン（Unity を閉じてから）**
```bash
"C:\Program Files\Unity\Hub\Editor\6000.3.13f1\Editor\Unity.exe" -batchmode -projectPath C:\Users\yamag\wrench\D-Drive -runTests -testPlatform EditMode -testResults C:\Users\yamag\wrench\D-Drive\TestResults\editmode.xml -logFile C:\Users\yamag\wrench\D-Drive\Logs\editmode-test.log
```
`TestResults/editmode.xml` の `<test-run ... result="Passed"` を確認する。

#### 手順 2: VFX Editor の操作確認

準備: `Tools > D-Drive > Editors > VFX確認用シーンを開く` → Project で `Assets/GameData/Vfx/Player/VFX_Player_Slash.asset` を選択 → `Tools > D-Drive > Editors > VFX`。

| # | 操作 | 期待 |
|---|---|---|
| 1 | ウィンドウを開く | 対象アセットに VFX_Player_Slash が自動で入る。「検証」Foldout に結果が出る（問題なし or Error/Warning） |
| 2 | Project で別の VfxData を選ぶ → 戻す | 対象が追従する。ツールバー「🔒 対象を固定」ON にすると追従しない |
| 3 | ▶ 再生 | SceneView にエフェクトが出る。Hierarchy に `[D-Drive] VFX Preview` が現れ、その下にスポーン物が入る。ステータスが「● 再生中」 |
| 4 | 「リピート」ON のまま再生 | OneShot が終わるたび約 0.35 秒後に自動再スポーン。■ 停止で止まる |
| 5 | 速度スライダーを 0.3 / 2.0 に | 再生速度が変わる |
| 6 | Anchor の「高さオフセット」「向き」スライダー、2D パッドをドラッグ | **再生中の実体がその場で動く**（停止→再生の押し直し不要）。SceneView の緑の円も追従 |
| 7 | 「SceneView で編集」ON → SceneView の移動ハンドルをドラッグ | 高さスライダー・2D パッドの値が追従する。`E`（回転ツール）で回転ハンドルに変わり、向きスライダーが追従する |
| 8 | Ctrl+Z / Ctrl+Y | ウィンドウの値・実体の位置・SceneView が一緒に戻る |
| 9 | `Assets/GameData/Prefabs/Anchors/AnchorRig.prefab` を Hierarchy にドラッグして配置 → ウィンドウの「スポーン先」に指定 | 「一覧から選択」の先頭に `★ Anchor_Main` が出る |
| 10 | `★ Anchor_Main` を選ぶ | Space が NamedObject に切り替わり、解決欄が「✓ 'Anchor_Main'(★AnchorPoint…)」。再生中なら AnchorRig の位置に再スポーン。AnchorRig をシーンで動かすと追従する |
| 11 | Path に存在しない名前を手入力 | 解決欄が「⚠ '…' が見つかりません」 |
| 12 | 基本設定で LifeMode を Loop → 再生 | 停止するまで続く。Duration に戻すと Duration 秒で消える |
| 13 | 基本設定で Prefab を別のパーティクル Prefab に差し替え（再生中） | 自動で再スポーンされ、新しい Prefab が出る。「検証」が更新される |
| 14 | パラメータ →「定義の追加・削除」で + → Label `Tint` / Type `Color` / TargetProperty `_BaseColor` | 即時反映欄に `Tint` の色フィールドが出る。再生中に色を変えると反映（URP Particles/Unlit 系マテリアルの場合）。TargetProperty を `_Nothing` にすると「検証」に Error |
| 15 | 複数同時再生に 2 つ以上入れて ▶ | 同時に出る。各 ■ で個別停止 |
| 16 | 任意の .cs を保存して再コンパイル | ウィンドウの対象・スポーン先・トグルが保持される |

#### 手順 3: プレハブモード内再生 + 本日の修正分（2026-09-08 午後）

準備: 手順 2 と同じ（VFX 確認用シーン + `VFX_Player_Slash` を対象にして VFX ウィンドウを開く）。

| # | 操作 | 期待 |
|---|---|---|
| 1 | ツールバー「Prefab を開く」でプレハブモードに入る | 上部に青い案内「…この VFX の Prefab 自身なので、ステージ内の実体をその場で再生します」。カメラ/ライトの黄色い警告は出ない |
| 2 | ▶ 再生 | **ステージ内の ParticleSystem がそのまま動く。Hierarchy に `[D-Drive] VFX Preview` や 2 つ目のインスタンスは出ない**（二重表示なし） |
| 3 | 再生中に Inspector で ParticleSystem を編集（Start Size、色、Emission など） | 編集した内容が再生中の実体にそのまま出る（保存不要） |
| 4 | 速度スライダー 0.3 / 2.0、■ 停止 | 速度が変わる。停止で粒子が消える |
| 5 | Hierarchy で ParticleSystem を選択し、SceneView 右下の Particle Effect パネルで再生 | ウィンドウ側は進めない（倍速にならない）。選択を外すとウィンドウ側の進行に戻る |
| 6 | 「リピート」ON + LifeMode=OneShot で ▶ | 粒子が尽きると約 0.35 秒後に再スポーン（EditMode でも繰り返す。以前は繰り返さなかった） |
| 7 | プレハブモードを閉じる（Hierarchy 上部の ←） | ステータスが停止に戻る。確認用シーンにスポーン物が残っていない |
| 8 | 確認用シーンで ▶ 再生中に、Project から**別の** Prefab（例: `AnchorRig.prefab`）をダブルクリックしてプレハブモードへ | 確認用シーンのスポーン物が消える。案内は「プレハブモード 'AnchorRig' の中で再生します」。▶ でステージのシーンにスポーンする（この場合は別インスタンス） |
| 9 | 手順 8 の状態で Ctrl+S（プレハブ保存）→ 閉じる | 保存しても `[D-Drive] VFX Preview` はプレハブに入らない。閉じた後の確認用シーンは変更なし（ダーティにならない） |
| 10 | 確認用シーンに戻り LifeMode=OneShot でリピート ON | 手順 6 と同じく繰り返す（Manager 経由のスポーンでも OneShot が終わる） |
| 11 | AssetBrowser（または `Tools > D-Drive`）で SE を**続けて 2 回**新規作成 | 2 回とも Id が 0 でない（Inspector の Id 欄、または AudioCatalog のエントリ）。以前は 1 回おきに 0 になっていた |
| 12 | `Tools > D-Drive > Validation > Run All` | 「always fails」が出ない。残るエラーは確認用データの内容（Clip 未設定・Built-in シェーダー）のみ |
| 13 | 任意の .cs を保存して再コンパイル → 手順 1〜2 | プレハブモード内でも再生状態と対象が復元される |

#### 手順 4: Anchor アセット（[21_anchor_spec.md](21_anchor_spec.md)、2026-09-08 実装分）

準備: VFX 確認用シーンを開く → `Tools > D-Drive > Generate > Anchor プレハブを生成` で出来た `AnchorRig.prefab` を Hierarchy にドラッグして配置（子に `Anchor_Main`）。`VFX_Player_Slash` と `SE_Player_Slash` を使う。自動テストは EditMode 125 / PlayMode 258 で green 済みなので、ここでは**エディタ操作と見た目**を確認する。

| # | 操作 | 期待 |
|---|---|---|
| 1 | `Tools > D-Drive > Editors > Anchor` | Anchor Editor が開く。「対象アセットを選択してください」。ツールバーに 🔒 / 確認用シーンを開く / Project で表示 / SceneView 表示 |
| 2 | Hierarchy で AnchorRig を選択 → `Tools > D-Drive > Generate > 選択した AnchorRig から Anchor を一括生成` | `Assets/GameData/Anchor/AnchorRig/ANC_AnchorRig_Main.asset` が出来て Console に「1 件生成」。Anchor Editor の対象に入る（Project 選択追従）。設定欄: Space=NamedObject / Path=AnchorRig / Local Offset = AnchorPoint のローカル位置 |
| 3 | 「スポーン先」に Hierarchy の AnchorRig を入れる | 基準の表示が「✓ 'AnchorRig'」。「一覧から選択」に ★ Anchor_Main と AnchorRig 配下の名前が並ぶ |
| 4 | SceneView を見る | 黄色い円が Anchor_Main の位置に出る。移動ハンドルでドラッグすると Local Offset が変わり、Ctrl+Z で戻る。回転ツール（E）で回転ハンドルに変わる |
| 5 | 試し出し「確認用 VFX」に VFX_Player_Slash → ▶ | その位置にエフェクトが出る（ステータス「● 再生中」）。■ で消える。AnchorRig をシーンで動かすと追従する |
| 6 | Position Jitter Radius = 0.5 → ▶ を数回 | SceneView に半径 0.5 の円が出て、出る位置が毎回ばらつく。Euler Jitter=(0,180,0) で向きもばらつく |
| 7 | Delay Sec = 1 → ▶ | 試し出しのステータスが「● 生成待ち(Delay)」→ 約 1 秒後にエフェクトが出る。待ち中に ■ を押すと出ない |
| 8 | Spawn Chance = 0.3 → ▶ を 10 回 | 3 回前後しか出ない。0 にすると「検証」に Warning、▶ で何も出ずステータスが「(SpawnChance に外れた…)」 |
| 9 | Asset Browser「新規作成」で種別 Anchor、識別子 `Spark` → Anchor Editor で Parent に ANC_AnchorRig_Main、Local Offset=(0,0,1) | 上部の連鎖が「ANC_AnchorRig_Main → [ANC_…_Spark]」。SceneView で親から点線が伸び、親の前方 1m に円。「親を開く」で親に切り替わる |
| 10 | 子（Spark）で Space=BoneName、Follow Rotation=ON にする | 「検証」に Warning「子 Anchor では Space/Path は無視されます」「FollowRotation/DetachOnStop は無視されます」。位置は変わらない |
| 11 | Spark の Parent を Spark 自身（または親を Spark に）にする | 「検証」に Error「Parent が循環しています」。連鎖表示に「⚠ ルートに辿り着けません」。試し出しは警告付きで到達ノードをルート扱いにして出る（例外で止まらない） |
| 12 | 親 ANC_AnchorRig_Main に Delay 0.5 / Chance 0.5、子 Spark に Delay 0.5 / Chance 0.5 → 子を対象に ▶ | 基準表示の末尾に「生成: 1s 後・確率 25%」。実際に約 1 秒後・4 回に 1 回程度 |
| 13 | VFX Editor で VFX_Player_Slash を開き、Anchor 欄「Anchor アセット」に ANC_AnchorRig_Main を選ぶ | 埋め込み欄（Space/Path/スライダー/パッド）が畳まれ「Anchor アセット '…' を使用中」。「AnchorEditor で開く」が押せて Anchor Editor に切り替わる。「検証」に Warning は出ない（埋め込みが既定値のとき） |
| 14 | その状態で ▶ 再生 → Anchor Editor 側で Local Offset を変える | VFX Editor で再生中の実体が**その場で**動く（再スポーン不要）。SceneView のハンドルは Anchor Editor 側だけに出る |
| 15 | 埋め込み Anchor に高さ 1 を入れたまま Anchor アセットを設定 | 「検証」に Warning「AnchorId が設定されているため、埋め込みの Anchor は無視されます」 |
| 16 | Anchor アセットを None に戻し、埋め込みで高さ 0.5・向き 45 → 「埋め込みをアセット化」 | `ANC_Player_VFXPlayerSlashAnchor.asset`（カテゴリ = VFX の Category）が出来て Anchor アセット欄に入る。Anchor Editor で開くと Local Offset y=0.5 / Local Euler y=45 |
| 17 | SE_Player_Slash の Inspector: Spatial=Anchor、Anchor Id に ANC_AnchorRig_Main | Inspector 下部に「Anchor アセットを使用中」「AnchorEditor で開く」。Anchor Editor の試し出し「確認用 SE」に入れて ▶ で鳴る（Clip 未設定なら無音 + Validation Error は既存の内容） |
| 18 | Hierarchy で AnchorRig の子に空オブジェクト `FxPoint` を作り、localPosition=(0,1,0) → 選択して Anchor Editor「選択した Transform から作成」 | `ANC_AnchorRig_FxPoint.asset`。Space=NamedObject / Path=AnchorRig / Local Offset=(0,1,0)。FxPoint を消しても Anchor は残る |
| 19 | `Tools > D-Drive > Validation > Run All` | 追加した Anchor 由来の Error が 0（手順 11 の循環を直してから）。Anchor 以外の既存エラー（Clip 未設定等）は変わらず |
| 20 | Anchor Editor と VFX Editor を両方開き、交互にクリック | 最後にクリックした方だけ SceneView にハンドル・連鎖が出て、もう片方は薄い小さな円だけ。各ウィンドウの案内が「このウィンドウが描画中 / '…' が描画中」に切り替わる。「SceneView 表示」OFF でそのウィンドウ分が消える |
| 21 | 任意の .cs を保存して再コンパイル | Anchor Editor の対象・スポーン先・確認用 VFX/SE・SceneView 表示が保持される |
| 22 | `Tools > D-Drive > Generate > Regenerate Asset IDs` | `Assets/Generated/AssetIds.g.cs` に `ANCHORID` クラスと作成した Anchor の定数（`ANCHORID.AnchorRig_Main` 等）が出る。コンパイルエラーなし |

#### 手順 5: 配置セット（AnchorGroup、[22_anchor_group.md](22_anchor_group.md)）

準備: 確認用シーン。`VFX_Player_Slash`（必要なら URP 用マテリアルに差し替え済みのもの）。

| # | 操作 | 期待 |
|---|---|---|
| 1 | Asset Browser「新規作成」→ 種別 AnchorGroup、識別子 `HealField` | `Assets/GameData/AnchorGroup/<カテゴリ>/ANCG_<カテゴリ>_HealField.asset`。`Tools > D-Drive > Editors > Anchor Group` に対象として入る |
| 2 | 設定: Layout=Grid、X=3・Z=3、Spacing=(1,1,1) | SceneView に 0〜8 の番号付きの点が 3×3 に並ぶ（原点 = 中央の大きい円）。状態表示「点: 9」。Grid 以外の欄（Circle 等）は隠れる |
| 3 | 「全点共通 VFX」に VFX_Player_Slash → ▶ 全点 | 9 か所に出る。■ で全部消える |
| 4 | SceneView で 8 番をクリック → ハンドルで外側へドラッグ | Grid Spacing が大きくなり、全点が広がる（Ctrl+Z で戻る） |
| 5 | 「番号順ディレイ(秒/点)」= 0.1 → ▶ 全点 | 0 番から順に 0.1 秒ずつ遅れて出る |
| 6 | 4 番をクリック →「選択点を Overrides に追加」→ Overrides の Vfx に別の VFX、0 番も追加して Skip ON | SceneView で 4 番が橙、0 番が灰色 ×。▶ 全点で 4 番だけ別の VFX、0 番は出ない。「▶ 選択点のみ」で 1 点だけ出る |
| 7 | Layout=Circle、Count=8、Radius=2、外向き ON | 8 点が円周に並び、番号ラベルの向きが放射状（回転を持つ VFX なら外向きに出る）。0 番をドラッグで半径が変わる |
| 8 | Layout=Line、Count=3、Length=2、Direction=(1,0,0) | 一列。端の点をドラッグで長さが変わる |
| 9 | Layout=Random、Count=10、Radius=1、Seed=0 → ▶ を数回 | 表示は固定だが再生ごとに配置が変わる。Seed=7 にすると表示・再生とも同じ配置 |
| 10 | 「手置きの点」を 1 つ追加（Local Offset=(0,2,0)） | 原点から点線でつながった点が増える（番号は末尾）。SceneView でドラッグで動く |
| 11 | 円形の配置セットをもう 1 つ作り、格子側の「入れ子」に登録（At Index=-1） | ▶ 全点で格子の各点に円形の粒が出る（子の原点は無視され各点が中心）。入れ子を自分自身にすると「検証」に Error |
| 12 | 「各点の生成確率」= 0.5 → ▶ 全点を数回 | 毎回半分程度の点だけ出る |
| 13 | 原点 Anchor アセットに手順 4 で作った `ANC_AnchorRig_Main` を選び、スポーン先に AnchorRig | 状態表示「原点: ✓ 'AnchorRig'」。全点が AnchorRig の位置を中心に並ぶ |
| 14 | Anchor Editor と Anchor Group Editor を同時に開き交互にクリック | 最後にクリックした側だけハンドル・番号が出て、他方は薄い円のみ |
| 15 | `Validation > Run All` | 配置セット由来の Error 0（Children の自己参照を直してから） |

#### 手順 6: アニメーション（3-1〜3-4、[05] B）

準備: Animator（Controller 付きが望ましい）を持つモデルの ModelData。無ければ Animator だけの Prefab でも「Controller なし」経路で確認できる。

| # | 操作 | 期待 |
|---|---|---|
| 1 | Asset Browser「新規作成」→ 種別 Anim、Clip を設定 | `Assets/GameData/Anim/<カテゴリ>/ANIM_…asset`。`Tools > D-Drive > Editors > Animation (3D)` に対象として入る |
| 2 | 「確認用モデル」に ModelData →「確認用シーンを開く」 | 確認用シーンが開き、モデルが原点に配置されて「シーン上の Animator(対象)」に自動で入る。モデル情報は 1 行の要約（Controller / BlendShape 数）、詳細は折りたたみ |
| 3 | ▶ 再生 | SceneView でモデルが動く（Controller ありなら CrossFade、無しなら Clip のサンプリング）。ステータス「● 再生中 xx%」。タイムラインの再生ヘッドが進む |
| 4 | タイムラインをクリック | その位置にシーク（ポーズが変わる）。イベントログに発火は出ない |
| 5 | Events に Frame=15 / Action=PlayAsset / Target=SE を追加 → ▶ | 0.5 秒で SE が鳴り、イベントログに「Frame 15: PlayAsset Se …」。マーカーが橙で出る |
| 6 | マーカーをドラッグ | Time が変わり（Frame 単位）、Ctrl+Z で戻る。Clip 長を超えると赤 + 検証に Error |
| 7 | Target を VFX にして ▶ | ビューポート内のモデル位置に VFX が出る（Anchor が BoneName ならそのボーン） |
| 8 | Loop ON + Events に OnLoop → ▶ | 周回ごとにログに OnLoop。ステータスの周回数が増える |
| 9 | ブレンド確認: B に別の Anim、CrossFade 0.3 →「A → B を再生」 | A の半分で B に切り替わる（ログに「→ '…' へ CrossFade」） |
| 10 | StateName を存在しない名前に | 検証に Error「StateName '…' が確認用モデルの Controller にありません」。再生は時間追跡だけ続く（警告 1 回） |
| 11 | BlendShapes に存在しない名前 | 検証に Warning。存在する名前 + カーブなら再生中に表情が変わる |
| 12 | 速度 0.3 / 2.0、ループ試聴 ON | 速度が変わる。終わると自動でもう一度 |
| 13 | Hierarchy に `[D-Drive] Anim Preview` | 保存対象外（DontSave）。■ 停止でモデルは残り、Frame イベントの SE / VFX はシーン内に出る |
| 14 | ツールバー「モデル Prefab を開く」→ ▶ | プレハブモードに入ると「シーン上の Animator(対象)」に Prefab ルートの Animator が自動で入り、その場で動く。■ 停止で再生前のポーズに戻り、Prefab は dirty にならない（Ctrl+S しても再生中ポーズが保存されない） |
| 15 | シーン再生中にプレハブモードを閉じる / 別シーンを開く | エラーなし。配置物が消え、対象が空になる（確認用モデルがあれば次の ▶ で再配置） |
| 16 | Hierarchy で別のモデルを選んで「選択から取得」→ ▶ | そのモデルが動き、停止で元のポーズに戻る |

#### 手順 8: 起動配線と Addressables（[02] §14 / §5、レビュー P0-1 / P0-2）

| # | 操作 | 期待 |
|---|---|---|
| 1 | `Tools > D-Drive > Generate > Addressables 登録を同期(カタログ → グループ)` | Addressables Groups に `DDrive_GameData`（全 Data、address = ファイル名）と `DDrive_Catalogs`（カタログ、ラベル `DDriveCatalog`）が出来る。ログに件数 |
| 2 | `Validation > Run All` | Addressables 由来の Error 0。エントリを 1 つ消して Run All → 「Addressables 未登録」Error、FixAction で復帰 |
| 3 | `Generate > 起動オブジェクト(DDriveRuntimeBootstrap)をシーンに配置` | `[D-Drive] Runtime` が出来て Catalogs に GameData/Catalogs の全カタログが入る。GameLoopDriver が同居 |
| 4 | Play Mode | Inspector に「● Ready(カタログ N 件)」。`Anim.Play(ANIMID.…, animator)` / `Models.Spawn` がコードから動く（未 Bind の no-op にならない）。Frame イベントの SE / VFX が実行時にも出る |
| 5 | Asset Browser で新規作成 | 作成直後に Addressables のエントリがある（Groups ウィンドウで address を確認） |

#### 手順 7: Inspector の「エディターで開く」（[09] §8）

| # | 操作 | 期待 |
|---|---|---|
| 1 | Project で VfxData / SeData / BgmData / ModelData / AnimData / AnchorData / AnchorGroupData を選ぶ | Inspector の一番上に「▶ … Editor で開く」ボタン。押すとそのアセットを対象に専用エディタが開く |
| 2 | SeData | ヘッダーのボタンに加え、従来のトリミング GUI がそのまま下に出る（末尾の AudioEditor ボタンはヘッダーへ統合） |
| 3 | Test Runner（EditMode）`DataEditorRegistryTests` | 4 件 green（新しい Data 種別を作って属性を付け忘れると `EveryConcreteDataType_HasEditor` が落ちる） |


| 17 | Play Mode に入る → 抜ける | Console に例外が出ない |
| 18 | ウィンドウを閉じる | `[D-Drive] VFX Preview` が Hierarchy から消える。シーンに未保存マーク（*）が付かない |

#### 手順 3: NgoNetBridge の 2 クライアント確認（MPPM）

1. 新規シーン `Assets/Scenes/NetSmokeScene.unity` を作る（SampleScene を複製でも可）
2. 空 GameObject `NetworkManager` を作り `NetworkManager` コンポーネントを追加 → Inspector の Network Transport で **UnityTransport** を選択
3. 空 GameObject `NetBridge` を作り `NetworkObject` / `NgoNetBridge` / `NetBridgeSmokeTest` を追加（bridge 欄は Awake で自動取得）
4. `Window > Multiplayer > Multiplayer Play Mode` → **Player 2** にチェック → Virtual Player が起動するのを待つ
5. メインエディタで Play → Hierarchy の `NetworkManager` を選択 → Inspector の **Start Host**
6. Player 2 のウィンドウで `NetworkManager` を選択 → **Start Client**（初回は Windows Firewall の許可ダイアログが出る → 許可）
7. 期待ログ（2 秒ごと）
   - ホスト Console: `[NetBridgeSmokeTest] Host broadcasting Ping #1` と **`ClientId=0 received Ping #1`**（← ホスト自身が受信する。旧 `[ClientRpc]` ではこの行が出なかった）
   - Player 2 Console: `ClientId=1 received Ping #1`
8. `NetBridgeSmokeTest` の **Broadcast From Client** を ON にして 5〜6 をやり直す
   - Player 2: `Client broadcasting Ping #1` → ホスト・Player 2 の両方に `received Ping #1`（Client→Host 中継経路）。ホストに `[Net/Host] … レート制限` の警告が出ないこと
9. 余裕があれば実機 2 台 + 実 LAN で同じ確認（`UnityTransport` の Address をホストの IP にする）

> VfxManager と NgoNetBridge を組み合わせた起動コード（composition root）はまだ無いため、VFX の Cosmetic 配送そのものは `CosmeticDeliveryTests`（Loopback）と上記ブリッジ疎通で分けて確認する。

#### 手順 4: コミット前

1. `git status` で `.cs` に対応する `.meta` が揃っていることを確認（Unity が自動生成済み）
2. `ProjectSettings/EditorBuildSettings.asset` と `ShaderGraphSettings.asset` は改行コードのみの差分（`git diff --ignore-all-space` で空）。`git checkout -- ProjectSettings/EditorBuildSettings.asset ProjectSettings/ShaderGraphSettings.asset` で戻してよい
3. `Packages/packages-lock.json` は unity-mcp / NGO 2.13.2 の解決結果なので含める
4. `Tools > D-Drive > Validation > Run All` で Error 0 を確認
5. コミット例: `feature/P2 VFX設計見直し(2-12) + MCP導入 + NGO 2.13.2統一`

---

## 4. 次のエディタ実装で最初から満たすチェックリスト（[12_review.md](12_review.md) §3「Editor / ツール」への追加提案）

- [ ] そのウィンドウだけで Data の全項目を編集できる（Inspector との往復を前提にしない）
- [ ] 編集対象・作業状態が `[SerializeField]` でドメインリロードを跨ぐ
- [ ] Project の選択に追従する + ロックできる
- [ ] 値の変更が再生中の実体に即時反映される。再スポーンが必要な変更は自動で撮り直す
- [ ] 「今の設定で何が起きるか」（解決先・無効理由）を常に文字で表示する
- [ ] 空間的な値は SceneView ハンドルでも編集できる（数値と 2D パッドだけにしない）
- [ ] ワンショットの繰り返し確認（リピート）がある
- [ ] Undo/Redo 後に UI・実体・SceneView が同期する
- [ ] スポーン物を専用ルートにまとめ、シーン切替・ウィンドウ閉鎖で確実に片付ける
- [ ] Validator の結果をウィンドウ内に出す
- [ ] 300〜400 行を超えたら partial / 責務で分割する
