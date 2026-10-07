# 1001. 未完了事項の一覧（未確認・未実装・既知の不具合・判断待ち）

> **目的**: docs が増えて「何がまだ終わっていないか」が 11・verification・reviews・archive に散らばったため、**未完了のものだけ**を 1 枚に集めた（2026-10-07、v1.4.1 リリース時に作成）。完了したものはここから消し、出典の文書には触らない。
> **運用**: 新しい未完了事項（確認の NG・レビューの見送り・保留チケット）が出たら、出典の文書に書いたうえで**この文書にも 1 行足す**。終わったら行を消す（履歴は git にある）。チケットの正本は [11_tasks.md](11_tasks.md)、確認手順の正本は [verification/](verification/README.md)、レビューの正本は [reviews/](reviews/README.md) のまま。ここは索引であって正本ではない。
> **凡例**: 「(状態不明)」= 出典に結果の記載が無く、終わったかどうか本文から判断できないもの。確認して消すか残すか決める。

関連: v1.5.0 の実装内容（D-Drive MCP）は [1002_ddrive_mcp.md](1002_ddrive_mcp.md)。

---

## 0. 先に直す「文書が古くなっている箇所」

| 箇所 | 内容 | 対応 |
|---|---|---|
| [CLAUDE.md](../CLAUDE.md) §1 進捗行 | 「v1.4.0 のタグは未実施」のまま（タグは 2026-10-06 に打って push 済み。[CHANGELOG](../CHANGELOG.md) `[1.4.0]`） | 2026-10-07 に修正 |
| [51](51_tdrive_integration.md) §0・§9 | 同じく「タグは未実施」 | 2026-10-07 に修正 |
| [11](11_tasks.md) 「v1.4.1 候補」表 | 「確認ダイアログのキャンセル文言」は [63](reviews/63_review_pr126_pr129_2026-10-06.md) GE-R-17 で対応済みの可能性（状態不明） | 次に 11 を触るときに確認して消す |
| [verification/37](verification/37_manual_verification_phase6.md) 「6-5 未着手」・[archive/38](archive/38_acceptance_demo.md) §3 「6-7 未実装・v5 未実施」 | 6-5・6-7 は実装済み、v5 の結果は [29](verification/29_network_device_test.md) §16 にある | 古い記述。archive は更新しない方針なので、この行で読み替える |

---

## 1. 人による確認が残っているもの

### 1-A. Timeline（前提: UnityChan の FBX が届いてから。依頼票 = [archive/46](archive/46_cutscene_fbx_request_unitychan.md)）

| 内容 | 出典 |
|---|---|
| Maya FBX の取り込み（fps 検査・FrameRate 不一致・Avatar 未設定・画角カーブ） | [43](verification/43_manual_verification_2026-09-17.md) §7.2 |
| Inspector の導線・確認用シーン・ブレンド・StepFps・スクラブ・上書き警告 | [43](verification/43_manual_verification_2026-09-17.md) §7.3 |
| 再取り込みで足したトラックが残るか、キャラ FBX を消したときのミュート（自動ミュートは未実装） | [43](verification/43_manual_verification_2026-09-17.md) §7.4 |
| Edit Mode プレビューを Timeline ウィンドウで操作した感触（SE/VFX/UI/AnchorGroup/Event/Signal・Camera・Shake/Haptic・Play Mode 専用分・導線の文言） | [43](verification/43_manual_verification_2026-09-17.md) §10.1〜10.4 |
| 要検証 6 項目（画角アニメ・FocusDistance/FStop・アニメ付きユーザープロパティ・Humanoid と原点の合成・Volume DoF の追従・endCameraRendering 時の姿勢） | [26](26_timeline.md) §7.3 |
| FC-3: カメラクリップ再生中の `ViewCamera.TryGetCurrent` | [52](verification/52_manual_verification_fc.md) 3-1 |
| FC-5: インポートルールの再実行（素材が必要） | [52](verification/52_manual_verification_fc.md) 5-1・5-2 |
| FC-7: Timeline 内の SE の使用箇所・未使用・削除・差し替え | [52](verification/52_manual_verification_fc.md) 7-1〜7-5 |
| FC-15: 知らないシェーダーを含む FBX の自動取り込み | [52](verification/52_manual_verification_fc.md) §15.5 |

### 1-B. T-Drive 導入後に確認するもの

| 内容 | 出典 |
|---|---|
| FC-1・FC-2/12・FC-3（実行順の検査の除外を含む）・FC-4・FC-5・FC-6・FC-7・FC-10・FC-14・FC-15・FC-19、修正ラウンド 1/4/5 の分 | [52](verification/52_manual_verification_fc.md) §22 |
| Toon の輪郭線を `SRPDefaultUnlit` で止める | [52](verification/52_manual_verification_fc.md) 11.3-1・11.3-2 |
| Q-4 実行順の検査の除外 | [43](verification/43_manual_verification_2026-09-17.md) 15-30 |
| T-Drive の toon と facial（同じリポジトリの 2 パッケージ。toon パッケージがまだ無い） | [43](verification/43_manual_verification_2026-09-17.md) 15-20 |
| 「D-Drive 1.4.0 以降で警告が消える」（1.4.0 はリリース済みなので今なら確認できる） | [43](verification/43_manual_verification_2026-09-17.md) 15-13 |

### 1-C. D-Drive を git URL で入れたプロジェクト（MS2026 等）で確認するもの

| 内容 | 出典 |
|---|---|
| 更新先を選んで manifest を更新するダイアログ | [43](verification/43_manual_verification_2026-09-17.md) 15-2 |
| D-Drive 自身の URL を追加しようとしたときの文言（Q-1 で変更後） | [43](verification/43_manual_verification_2026-09-17.md) 15-14 |
| MS2026 を v1.4.0 以降に上げた後、禁止 API 29 件を仕分ける | [43](verification/43_manual_verification_2026-09-17.md) 17-9 |
| MS2026 の Player ビルドでカットシーンのトラックが読めるか（M-6） | [52](verification/52_manual_verification_fc.md) 4-6 |
| 次の実機確認では NetCheck のビルドを作り直す（`8ce3ce1` の古いビルドは使わない。2026-10-07 の v1.4.1 リリースで作り直し済み） | [29](verification/29_network_device_test.md) §27、[64](reviews/64_review_m6_2026-10-06.md) GF-R-18 |

### 1-D. P-15 更新ウィンドウ（[43](verification/43_manual_verification_2026-09-17.md) §15）

15-5 / 15-27（同じ URL をもう一度追加したときの文言 Q-1 の再確認）・15-6（タグが 1 つも無いリポジトリ）・15-10 / 15-25（日本語を含む package.json の事前警告、BUG-1 の再確認）・15-17a（資格情報切れ・確認中の再コンパイル）・15-19（プレリリースのタグだけ）・15-21（`BAD-DECLARATION`）・15-22（LFS）・15-23（不正な `?path=`）・15-24（導入中に版上げ）・15-26（読めない package.json）・15-28（依存表示の出し分け Q-2）・15-29（依存の警告にアセットのパスが付かない Q-3）。

### 1-E. Canvas の埋め込み・禁止 API（[43](verification/43_manual_verification_2026-09-17.md) §16・§17）

| 内容 | 出典 |
|---|---|
| 16-18: Hud の検出欄に `OptionRoot/Inner` が出ない（未確認） | §16 |
| 16-34: SceneView で実際にドラッグ → Ctrl+Z | §16 |
| 16-43: 非表示のままプレハブモードを閉じて開き直す手順の追記と確認 (状態不明) | §16、[65](reviews/65_review_pr133_embedded_active_2026-10-06.md) GG-R-16 (2) |
| 17-1/3/6: バッチの `CI.ValidateAll`（終了コード・JUnit）。2026-10-06 以降の run-ci 全段 green で代わりになっている可能性 (状態不明) | §17 |
| 17-5: Editor を開き直しての確認 | §17 |
| 17-7: 行のボタンで外部エディタが開くか | §17 |

### 1-F. FC の確認で一部残っているもの（[52](verification/52_manual_verification_fc.md)）

1-4（`Target=Target` のときの表示）・1-5（Source Track Name のプルダウン候補）・2-1（Model エディタの「配置／撤去」経由）・11-9（Material Editor の「再生成」経由）・19-3（ツールチップ）・23-1〜23-3（新規 Data の Error・BGM のループ 0/0・Shake の尺 0）。

### 1-G. 古い手順書でチェック欄が空のもの（すべて状態不明。確認して消すか、不要なら「不要」と書いて消す）

| 手順書 | 空のまま残っている項目 |
|---|---|
| [43](verification/43_manual_verification_2026-09-17.md) | §0.5 トークンの入れ直し / §1.1〜1.3 の 1〜18（12 は合格） / §2 `clasp push` / §3 Web の確認 19〜26 / §5 スクリーンショット 12 枚 + #58・#1/#3 / §8・§9・§11・§12（Presentation の Anchor・AnchorGroup） / §13 Tuning ウィンドウ / §14 ElementFx プレビュー |
| [28](verification/28_manual_verification_phase5.md) §0 | ② Web 発注ツール一式（デプロイ・同期・O-12〜O-16・追補 1〜3・O-4 廃止） / ③ 5-2c・5-1・5-2・5-2b・5-4 / ④ 5-7 / ⑤ 5-8/5-9（実機 2 台は済み） |
| [37](verification/37_manual_verification_phase6.md) §0 | 6-3・A2・6-9 / 6-4・6-9b・マニュアル / 6-2・6-7 / 6-0 の結果を読む |
| [23](verification/23_manual_verification_2026-09-11.md) | 3-5〜4-18 の見た目・音・操作感（結果欄が無い） |
| [29](verification/29_network_device_test.md) §25 | `NetDebugOverlay` の目視、`migration_failed=1` の失敗経路 |
| [14](14_networking.md) §14 | N-1 の LAN 外接続と再起動経路の実機確認（§25 の 4 台テストで代わりになっている可能性） |

### 1-H. 受け入れ・配布

| 内容 | 出典 |
|---|---|
| 6-8 受け入れデモ: 判定の記入欄が空で、リード承認待ち | [archive/38](archive/38_acceptance_demo.md) §0、[11](11_tasks.md) 6-8 |
| SpecWeb の push とデプロイ（v1.4.0 のリリース手順 8。v1.4.1 ではマニュアル変更なしのため不要） (状態不明) | [archive/60](archive/60_release_1_4_0_prep.md) §1、[12](12_review.md) §7 |
| O-15/O-16 を実際にデプロイした画面で確認 | [11](11_tasks.md) O 節、[28](verification/28_manual_verification_phase5.md) |

---

## 2. 未実装・保留のチケット

| ID | 内容 | 状態 | 出典 | 着手の条件 |
|---|---|---|---|---|
| **MCP-1〜**（v1.5.0） | D-Drive MCP（AI 向けの Editor 操作ツール群） | 仕様書あり・未着手 | [1002](1002_ddrive_mcp.md) | 2026-10-07 起票 |
| FC-8 | カットシーンのキャラ FBX でブレンドシェイプのカーブを通す | 保留 | [51](51_tdrive_integration.md) §4.9 | 表情アニメの運用が決まったら |
| FC-9 | 7-3/7-4 から外部コンポーネントの値を触る口 | 保留 | [51](51_tdrive_integration.md) §4.10 | 7-3/7-4 に着手するとき |
| FC-13 | インスタンスごとのマテリアル値（MPB） | 保留 | [51](51_tdrive_integration.md) §4.14 | MS2026 で表情パラメータを使い始め、方式が決まったら |
| FC-16 / FC-17 / FC-18 | 名前付きスロットセット / ModelData の外部データ参照欄 / プロジェクト設定の検証の拡張点（当面は `IUniversalValidator` で代用） | 後回し | [51](51_tdrive_integration.md) §4.17〜4.19 | 要望が出たら |
| 7-1〜7-7 | Phase 7 の A 群（発注リスト・未実装タブ・デバッグオーバーレイ・Live Tuning・予算・サムネイル・バリアント）。7-8 は FC に置き換え済み | 未着手 | [11](11_tasks.md) Phase 7、[13](13_extensions.md) A-1〜A-6 | 7-3/7-4 は FC-9 を考慮して設計 |
| M-5 | ゲーム向けの時間源の公開 API（`GameLoop` 登録なしでヒットストップ・ポーズ込みの dt） | 案 | [11](11_tasks.md) M-4 節 | ユーザー判断 |
| M-2b | TuningTable をカテゴリ別アセットに分ける | 要判断 | [11](11_tasks.md) M-2 | MS2026 が「分けたい」と判断したら |
| M-3e | 日本語 TMP フォント（DD-9） | 記録のみ | [11](11_tasks.md) M-3、[14](14_networking.md) §20 | ユーザー判断（ライセンス・サイズ） |
| W-13/14/15/20/21/22(一部)/24/25/26 | 仕様書 Web の v2/v3（プリセット・カーブ・ベクトル・横断検索・CSV・書き出し・ライブ調整・派生値・通知）。W-16〜19 は不要・縮小、W-23 は作らない、O-11 は見送り | 未着手 | [11](11_tasks.md) P5 追補 | 未定 |
| 6-1 | CI の本稼働（ランナー登録・ブランチ保護） | P7 末に延期 | [11](11_tasks.md) 6-1、[33](33_ci_setup.md) | ユーザー作業 |
| 6-4 | チュートリアル動画の収録・マニュアルのスクリーンショット | 人の作業 | [11](11_tasks.md) 6-4、[verification/37](verification/37_manual_verification_phase6.md) | 未定 |
| GC-R-01 | `BgmDataValidator` にループ位置の Warning（例 `DD-BGM-LOOP-OUT-OF-CLIP`）を足す。既存の Error の条件・重さは変えない | v1.4.x | [61](reviews/61_review_round7_release_tools_2026-10-06.md) GC-R-01、[11](11_tasks.md) v1.4.1 候補 | 次の版（v1.4.1 には入れていない） |
| GC-R-07 | 尺 0 の Warning 2 件（`DD-SHAKE-ENVELOPE-ZERO-DURATION` / `DD-HAPTICS-ZERO-DURATION`）をゴールデンに固定 | v1.4.x | [61](reviews/61_review_round7_release_tools_2026-10-06.md) GC-R-07 | 次の版 |
| (未起票) | EditMode 初回だけ Undo 系 6 件が失敗する（`AudioEditorWindow.DrawListenerPad` の例外、再現せず） | 候補 | [11](11_tasks.md) v1.4.1 候補 | 再現したら起票 |
| (候補) | `CANVAS_Can_Vas` の編集用配置（`NavigationNodeLayout` 等）の残り | 候補 | [11](11_tasks.md) 確認用データの整理 #8 | — |
| (候補) | Canvas Editor のツールバーが幅 500px で重なる | 候補 | [43](verification/43_manual_verification_2026-09-17.md) 16-40 | — |
| P-11 フォロー | `Tools/CI/run-consumer-smoke.cmd` の自動化（まだ存在しない） | 未着手 | [11](11_tasks.md) P-11、[42](42_distribution.md) §5.11-11 | 未定 |
| U-4 / U-5 / U-6 | 確認用シーンへの配置・配置ボタンの強化・Presentation を確認用シーンで開く | 一覧上は「着手」(状態不明) | [archive/39](archive/39_usability_fixes_2026-09-17.md) §0 | — |
| (後続) | Anim / Anim2D / Presentation の Validation 欄を共通化 | 後続 | [09](09_editor_tools.md)「移行は後続で行う」 | — |
| 6-3 残り | AssetBrowser の列で並べ替え (状態不明) | 未実装 | [11](11_tasks.md) 6-3、[09](09_editor_tools.md) §4.1 | — |
| Tuning | カテゴリをまたいだ検索 | 見送り | [09](09_editor_tools.md) Tuning 節 | 要望が出たら |
| 6-10a | Self/Target バインドのトラックオフセット / Cutscene 受信の未知キー保留の防波堤 | TODO / 条件付き | [11](11_tasks.md) 6-10a | 実キャラ FBX / 実機で順序崩れが出たら |
| 6-10b | 複数 Cutscene がカメラを取り合うときの合成、実時間どおりのブレンドアウト | TODO | [11](11_tasks.md) 6-10b | — |
| 6-10c | イベント用ロケーター → Signal、`dd_focusDistance` / `dd_fStop`、ノード消失時の自動ミュート | 未実装 | [11](11_tasks.md) 6-10c、[26](26_timeline.md) §7.3 | 実 FBX が来たら |
| 2-x | VFX Graph 対応、anchorNetId でのリモート追従・Param 同期、Skybox プレビュー | 見送り | [11](11_tasks.md) Phase 2 注 | 必要になったら |
| 3-x | IK ターゲットの配置 UI | 未実装 | [11](11_tasks.md) Phase 3 | — |
| 13 B 群 | B-1〜B-8 の任意拡張（B-9 Cutscene は実装済み） | 候補 | [13](13_extensions.md) | 各行の「導入判断の目安」 |
| 42 §7 B-11 | Cinemachine アダプタ | 以降の MINOR | [42](42_distribution.md) §7 | 確認が済んだら |
| 42 §7 C-2〜C-7 | 確認事項（Samples の GUID・Documentation~ のパス解決・FindAssets の範囲・ForbiddenApi の範囲・Addressables の Schema・UniTask のタグ） | (状態不明) | [42](42_distribution.md) §7 C | — |
| 43 §6 | テスト実行が実データの Version/UpdatedAt を書き換える / P2-6 の本筋（全体 Validation の結果を Asset と別経路に） / Toolbar の折り返し | (状態不明) | [43](verification/43_manual_verification_2026-09-17.md) §6、[44](reviews/44_review_2026-09-19.md) | — |
| 47 テストの穴 | 2（SerializedLayout の並び順）・3（validator-severity に P-6〜8 のコード）・6（DevRepoOnly の件数）・7（ManifestJson の往復） | (状態不明) | [47](reviews/47_review_p_tickets_2026-09-20.md) | — |

---

## 3. 既知の不具合（未修正）

| 内容 | 出典 | 重さ・扱い |
|---|---|---|
| Idle を流している間に Undo/Redo すると、控えの値で Undo が黙って取り消される | [62](reviews/62_review_verification_fixes_u29_n8_2026-10-06.md) GD-R-10、[09](09_editor_tools.md) | P3・v1.4.x |
| `UiTweenManager.Tick` / `UiManager.Tick` の添字走査に、`WaitAsync` の続きが走査中に走ると崩れる（`PresentationManager` は修正済み） | [56](reviews/56_review_fix_round3_2026-10-04.md) FY-R-03、[11](11_tasks.md) v1.4.1 候補 | v1.3.1 から |
| `GameLoop` の走査中に `Unregister` すると直後の Manager が 1 フレーム飛ばされる（D-Drive 自身の Manager は影響なし） | [59](reviews/59_review_round6_valuedef_2026-10-06.md) GB-R-04 | 直すなら MINOR 以降 |
| `BgmDataValidator` がループ位置の 2 パターンを見逃す（→ §2 GC-R-01） | [61](reviews/61_review_round7_release_tools_2026-10-06.md) GC-R-01 | P2・v1.4.x |
| スクリプトから `OpenTimelineWindow` を呼ぶと `inspectedDirector` が null のまま（人の操作では再現せず） | [52](verification/52_manual_verification_fc.md) §24 | スクリプト経由のみ |
| Shake の揺れのオフセットが姿勢に混ざる（Writer の控え・クリップの外へ出たとき） | [66](reviews/66_review_pr135_editmode_camera_save_2026-10-06.md) GH-R-09、[64](reviews/64_review_m6_2026-10-06.md) GF-R-25 | P3 |
| GH-R-08 の回帰テストが本体の経路を通らない / Timeline で開いていない Director の Shake が同じ Tick で止まる | [64](reviews/64_review_m6_2026-10-06.md) GF-R-26・GF-R-27 | P3 |
| 埋め込み Canvas の端の挙動（配線候補の深さ・循環、RootPath 付け替えの範囲、FirstSelected の条件、内側の `Deactivating` が下りない、テストの抜け） | [65](reviews/65_review_pr133_embedded_active_2026-10-06.md) GG-R-13・14・15・17 | P3・対応の記録なし |
| CHANGELOG に `DD-CANVAS-SLIDER-EMBED-ACTION` の記載が無い | [65](reviews/65_review_pr133_embedded_active_2026-10-06.md) GG-R-16 (1) | 文書（次の版で追記） |
| 照合が終わる前に Late Join の同期が走り、不一致の Client で演出が約 0.2 秒出てから切断される（現状は案 A） | [29](verification/29_network_device_test.md) §22.1 | ユーザー判断待ち（§5） |
| Client 側の `content_hash` が「検証中...」のまま / heartbeat に不一致の文字列が毎行出る | [29](verification/29_network_device_test.md) §22.2-1、[43](verification/43_manual_verification_2026-09-17.md) §0.2-5 | 軽微 (状態不明) |
| Pooled な Simulated Prefab を Reset した後も Pool の「貸出中」数が残る | [14](14_networking.md) N-5 節 | 修正見送り |
| `FadeTo` の一時 Material に `from` 側のパス無効化が残る / `TryGetView` 中の `Unregister` で次のプロバイダを飛ばす | [53](reviews/53_review_fc_2026-10-03.md) FC-R-10・FC-R-16(a) | 見た目のみ / 例外にはならない |
| 予測再生キーが上限に達すると全部捨てる | [57](reviews/57_review_round4_m4_2026-10-05.md) FZ-R-11 | 軽い |
| Spawn/Play 系は厳密な 0 alloc ではない（Instance を new する） | [verification/37](verification/37_manual_verification_phase6.md) 6-2 | 既知・記録のみ |
| `list-obsolete.ps1` が引数なしの `[Obsolete]` を拾えない / `ReleaseChecks` がプレリリース版で例外 | [47](reviews/47_review_p_tickets_2026-09-20.md) | (状態不明) |
| D-Drive で削除したアセットの `ddriveState.created` が true のまま | [32](32_spec_web.md) §10.7-10 | 要判断（§5） |

---

## 4. レビューで見送った項目（v1.4.x / 次の MINOR で可とされたもの）

| レビュー | 見送った ID と内容 |
|---|---|
| [53](reviews/53_review_fc_2026-10-03.md) | FC-R-10（FadeTo のパス無効化）・FC-R-11（一時 Material の生成と Refresh 頻度）・FC-R-12（使用箇所の行ごとに全 CutsceneData 走査）・FC-R-13（サブトラックの走査は推定）・FC-R-16(a)・FC-R-17（外部ハンドラの Target と DataType の整合）・FC-R-19（スナップショットの粒度）・FC-R-20（`*ImportProfile` をスナップショット対象にするか）・FC-R-21 の一部・FC-R-23（参照元トラックが無いときの扱い） |
| [54](reviews/54_review_p15_canvas_2026-10-03.md) | PC-R-12（「選択に追従」の範囲）・PC-R-13 後半（同じリポジトリのタグ揃え）・PC-R-14〜17 の一部（導入の確認・前の参照に戻す・Validator の static・テスト）・PC-R-20（プリセットギャラリーとプレハブモードの食い違い） |
| [55](reviews/55_review_fix_rounds_2026-10-03.md) | FX-R-13 の `CreateFromSelection` のテスト |
| [56](reviews/56_review_fix_round3_2026-10-04.md) | FY-R-04 KillTree（記述を実態に合わせただけ） |
| [57](reviews/57_review_round4_m4_2026-10-05.md) | FZ-R-09 の残り（それらしい形への Warning・束ね・未使用の Info）・FZ-R-11 |
| [58](reviews/58_review_round5_p15fix_2026-10-06.md) | GA-R-04（Q-1 の文面。ユーザー確認待ち → §5） |
| [59](reviews/59_review_round6_valuedef_2026-10-06.md) | GB-R-03（`ValueDefColor` の Alpha）・GB-R-05 細部（Constant のときの Warning）・GB-R-06（`SpecDiffValidator` の Data ごとの指摘のテスト）・GB-R-08（E-9b の順序依存） |
| [61](reviews/61_review_round7_release_tools_2026-10-06.md) | GC-R-01・GC-R-07（→ §2） |
| [62](reviews/62_review_verification_fixes_u29_n8_2026-10-06.md) | GD-R-10（→ §3）・GD-R-11（現状維持）・GD-R-05（DesignerManual に 1 行 (状態不明)） |
| [63](reviews/63_review_pr126_pr129_2026-10-06.md) | GE-R-23（DesignerManual「ボタンの配線」の節。持ち越し。対応済みの可能性あり (状態不明)） |
| [64](reviews/64_review_m6_2026-10-06.md) | GF-R-07 の残り（保存・再インポートの固定）・GF-R-08（NetCheck の記録・Camera クリップの警告）・GF-R-09 の残り・GF-R-15(4)（2 回走査）・GF-R-24 の見送り分・GF-R-25〜28 |
| [65](reviews/65_review_pr133_embedded_active_2026-10-06.md) | GG-R-08(2)（同じ親に A と A/B を登録 = 保証しない）・GG-R-12(2)・GG-R-12(3)（DesignerManual、別担当・未）・GG-R-13〜17 |
| [66](reviews/66_review_pr135_editmode_camera_save_2026-10-06.md) | GH-R-09（→ §3）・GH-R-13（記録のみ） |
| [47](reviews/47_review_p_tickets_2026-09-20.md) | テストの穴 2・3・6・7 |

---

## 5. ユーザーの判断待ち

| 問い | 出典 |
|---|---|
| **D-Drive MCP（v1.5.0）の決め事 Q-1（CoplayDev を外すか）・Q-4（書き込みツールの既定）**。他は 2026-10-07 に確定 | [1002](1002_ddrive_mcp.md) §9.1〜9.3 |
| M-5: 時間源の公開 API を作るか | [11](11_tasks.md) M-5 |
| M-2b: TuningTable を分割するか（MS2026 の判断） | [11](11_tasks.md) M-2b |
| M-3e: 日本語フォントをどう持つか | [11](11_tasks.md) M-3e |
| GA-R-04: Q-1 の文面でよいか（動作は変えていない） | [58](reviews/58_review_round5_p15fix_2026-10-06.md) |
| U-14: FC-13 で `SetMaterial` したときに MPB を維持するかクリアするか | [51](51_tdrive_integration.md) §8 |
| ContentHash の照合前に Late Join を送る件: 案 A（現状）か B か | [29](verification/29_network_device_test.md) §22.1 |
| CoplayDev の MCP を外すか（「3 セッション問題なければ外す」の基準のまま。v1.5.0 の D-Drive MCP で isuzu 版に一本化する提案 → [1002](1002_ddrive_mcp.md) §9 Q-1） | [20](20_mcp_setup.md)「切り替えの判断基準」、[CLAUDE.md](../CLAUDE.md) §4 |
| 6-8 のリードへの確認 1〜4（CI をローカル実行で代える・位相の基準・⑤の時期・実施者と日時） | [archive/38](archive/38_acceptance_demo.md) §4 |
| 42 §7 B の暫定のまま: B-1 URP 以外・B-2 Unity の版・B-3 SpecWeb の展開・B-5 CI の提供形・B-8 2 段階ルール・B-9 サンプルの中身 | [42](42_distribution.md) §7 B |
| Phase 5 の「使ってみて決める」B1〜B14、「後回しでよい」C の残り（C14 BGM の Seek API・C16 常駐 VFX/BGM の Late Join・C18 NetworkManager の動的生成・C32 Bgm/Canvas/UiTween のプレビュー・C10 正式なロード画面・C34/C35 削除の Undo とカスケード。C11・C13・C17 は実装済み） | [archive/31](archive/31_phase5_decisions.md) B・C |
| 削除したアセットの `created` フラグをどうするか | [32](32_spec_web.md) §10.7-10 |
| Host が Client のハッシュを信じて比べるだけ、というセキュリティ上の限界を受け入れるか | [14](14_networking.md) ContentHash 節 |

---

## 6. 次の MAJOR 候補 / Obsolete

| 内容 | 出典 |
|---|---|
| `[Obsolete]` の付いた公開 API は 0 件（2026-09-20 生成。`Tools/Release/list-obsolete.ps1` で再生成） | [migrations/next-major.md](migrations/next-major.md) |
| MAJOR は MS2026 の開発中は 0 回、年 1 回まで | [42](42_distribution.md) §5.12・§7 A-5、[CLAUDE.md](../CLAUDE.md) §0-10 |
| 次の MINOR で Error に上げる予定: FC-1 の SameAsTrack の Warning（U-2）、§5.8 の 2 段階ルールで v1.4.0 に Warning として入れた検査 | [51](51_tdrive_integration.md) §8、[42](42_distribution.md) §5.8 |
| `GameLoop` の走査方式の変更（Foundation の挙動変更 = MINOR 以降） | [59](reviews/59_review_round6_valuedef_2026-10-06.md) GB-R-04 |
| FC-R-19 のスナップショット表記の変更（全行の表記が変わる） | [53](reviews/53_review_fc_2026-10-03.md) |
| 役目を終えた記述: [27](27_spec_sheet.md) §7 の未決事項（スプレッドシート方式は廃止）、[13](13_extensions.md) B-9（Cutscene として実装済み） | — |

---

## 更新履歴

- 2026-10-07（同日）: 文書番号を 67 → 1001 に変更（docs の番号は 10xx に統一）
- 2026-10-07: 作成（v1.4.1 リリース時。docs/11・verification・reviews・archive・51・26・42・13・09・14・20・32 から未完了分を収集）
