# Changelog

D-Drive（`com.ddrive.core`）の変更履歴。[Keep a Changelog](https://keepachangelog.com/ja/1.0.0/) 形式に準拠し、[Semantic Versioning](https://semver.org/lang/ja/) を採用する。

> **運用ルール（[docs/42_distribution.md](docs/42_distribution.md) §4.1・§5.11-10）**:
> - 各バージョン見出しには **`### 互換性` 節を必ず書く**。「破壊なし / 追加のみ / マイグレーションあり（自動・手動）/ 破壊あり（移行ガイドへリンク。開発リポジトリでは `docs/migrations/`、パッケージでは `Documentation~/migrations/`〔2026-09-20 同梱、[47_review_p_tickets_2026-09-20.md](47_review_p_tickets_2026-09-20.md) P2-9〕）」のいずれかを明記する（空欄は CI の CHANGELOG ガードで fail にする）。この CHANGELOG.md 自体は開発リポジトリ直下とパッケージ直下（`Packages/com.ddrive.core/CHANGELOG.md`）に同じ内容が同梱されるため、本文中の相対リンクは開発リポジトリ側でのみ有効(パッケージ側から見るときは `Documentation~/` 配下の対応するファイルを直接開く)。
> - どの桁を上げるかは人の裁量ではなく [docs/42_distribution.md](docs/42_distribution.md) §5 の互換面ごとの区分で機械的に決まる（§4.1 の対応表）。
> - **本ファイルは P-2（互換性ポリシーの確定）の成果物として、P チケット完了（P-13 発効）前に用意した雛形**。互換性ポリシー自体は P-13 が発効するまで参考情報であり、`[Unreleased]` は現時点では通常の変更ログとして運用する。

## [Unreleased]

### 互換性

- 追加のみ（MINOR）: **N-1（2026-09-22、[docs/14_networking.md](docs/14_networking.md) §14・[docs/11_tasks.md](docs/11_tasks.md) N チケット）** — 開発用の手動ネット接続 API。`NetLaunchRole` に `Manual`（末尾追加）、`DDriveRuntimeBootstrap` に `NetStartMode`(新規 enum)・`DefaultNetStart`(新規フィールド、既定 `Auto`)・`public bool IsNetworkStarted`・`public bool StartHost(ushort)`・`public bool StartClient(string,ushort)`・`public void StopNetworking()` を追加。`NgoBridgeCreateResult`（`DDrive.Runtime.Net`）に `IsListening`/`ManualStartHost`/`ManualStartClient`/`ManualStop` の delegate フィールドを追加。既存の Auto 起動（既定 `DefaultNetBridge=Loopback`/`DefaultNetStart=Auto`）の挙動・既定値は無改修
- 追加のみ（MINOR）: **N-2（2026-09-22、[docs/14_networking.md](docs/14_networking.md) §15・[docs/11_tasks.md](docs/11_tasks.md) N チケット）** — 開発用の手動接続 UI。`DDrive.Runtime.Net` に `public static class NetManualConnectInput`（`TryParsePort`/`TryParse`）を追加。`DDrive.Runtime.Ngo`（互換性スナップショット対象外）に `NetManualConnectOverlay`（新規コンポーネント）を追加。既存の公開 API・挙動・既定値は無改修（`NgoBridgeFactoryInstaller.Create()` の内部実装のみ変更）
- 追加のみ（MINOR）: **N-3（2026-09-22、[docs/14_networking.md](docs/14_networking.md) §16・[docs/11_tasks.md](docs/11_tasks.md) N チケット）** — Host 1 + Client 3 対応。`DDrive.Runtime`（互換性スナップショット対象）に `NetLaunchOptions.ExpectedClientCount`（フィールド追加）・`NetLaunchArgs.ExpectClientsFlag`（定数追加）・`NetCheckCounters.ExpectedClientCount`/`MaxConnectedClientsObserved`（フィールド追加）を追加。`DDrive.Runtime.Ngo`（互換性スナップショット対象外）に `NgoNetBridge.ConnectedClientCount` を追加。`Samples~/NetCheck/`（`NetBridgeSmokeTest`/`NetCheckRunner`）をパッケージ本体 `Runtime/Ngo/NetCheck/` へ移動し、`package.json` の `samples` から削除（サンプルではなくパッケージ本体の一部に区分変更。GUID 不変のため既存の `NetCheckScene.unity` の参照は壊れない）
- 追加のみ（MINOR）: **N-4（2026-09-22、[docs/14_networking.md](docs/14_networking.md) §17・[docs/11_tasks.md](docs/11_tasks.md) N チケット）** — `DDrive.Runtime.Presentation` に `enum PresentationEffectScope { Everyone = 0, ParticipantsOnly = 1 }` を追加。`PresentationTrack` に `public PresentationEffectScope Scope`（末尾追加フィールド、既定 0=`Everyone`）を追加。`PresentationManager` に `public static bool IsParticipant(INetBridge, ulong, ulong)` を追加。シリアライズ形式は末尾追加のみ（既存 `.asset` は再インポート不要）。既存の HitStop/CameraShake/Haptic の挙動・既定値（`Scope=Everyone`）は無改修

### 追加

- N-1（2026-09-22）: 開発用の手動ネット接続 API（[docs/14_networking.md](docs/14_networking.md) §14）
  - 背景: MS2026（4 人対戦）へ持ち込む前提の開発用テストプレイで「LAN 外の特定 IP を入力 → 接続 → テストプレイ」をしたいが、既存の `DDriveRuntimeBootstrap` は起動時に自動で `StartHost`/`StartClient` を呼ぶため、実行中に IP を選ぶ余地が無かった
  - `NetLaunchRole.Manual`（末尾追加）+ `-ddrive-net manual`。役割解決を純関数 `NetLaunchArgs.ResolveEffectiveRole(cliRole, defaultBridgeIsNgo, defaultStartIsManual)` に切り出し（EditMode テスト）
  - `DDriveRuntimeBootstrap.NetStartMode`(`Auto`/`Manual`)・`DefaultNetStart`(既定 `Auto`)。Manual のときは `ResolveNetBridge()` が NGO ブリッジの解決・`NetworkManager`/`NgoNetBridge` の検索までは行い、Transport 設定と `StartHost`/`StartClient` は新 API 呼び出し時まで遅延する
  - `public bool StartHost(ushort port)` / `public bool StartClient(string address, ushort port)` / `public void StopNetworking()` / `public bool IsNetworkStarted` を追加。`NetBridgeMode.Loopback`、または NGO 未導入/シーンに `NetworkManager`+`NgoNetBridge` が無いときは警告して no-op（例外で止めない）。既に接続中なら警告して `false`
  - **手動 Host は `"0.0.0.0"` で listen する**（レビュー指摘、2026-09-22 追記。Auto の Host は従来どおり `DefaultHostAddress`/`-ddrive-host` に bind し、挙動を変えていない）。`NgoTransportConfigurator.TryConfigure`（`DDrive.Runtime.Ngo`、互換性スナップショット対象外）に省略可能引数 `listenAddress`（既定 `null` = 挙動不変）を追加し、`UnityTransport.SetConnectionData(host, port, listenAddress)` の `ServerListenAddress` を明示制御できるようにした。省略時は `ServerListenAddress = host` になり、手動 Host が別 LAN・LAN 外からのテストプレイで listen に失敗する不具合を修正
  - 現在の役割（Host/Client）は重複を避けるため新規プロパティを設けず、既存の `NetBridge.IsServer`/`NetBridge.IsClient`（`NetDebugOverlay` と同じ判定）をそのまま使う
  - テスト: `Tests/Editor/NetLaunchArgsTests.cs` に `-ddrive-net manual` のパース + `ResolveEffectiveRole` の 4 パターンを追加。`NgoNetBridge`/`NgoBridgeFactoryInstaller` は `NetworkBehaviour`/`NetworkManager` 依存のため EditMode 化できず、実機/PlayMode での確認は N-1 では未実施（要フォローアップ）
  - `docs/11_tasks.md` に N-1〜N-4 のチケット枠を追加（N-2: 開発用接続 UI、N-3: NetCheckScene の N クライアント対応、N-4: 1v1 前提の当事者判定の 4 人対応。N-2〜N-4 は未着手）

- N-2（2026-09-22）: 開発用の手動接続 UI（[docs/14_networking.md](docs/14_networking.md) §15）
  - 背景: N-1 で追加した `StartHost`/`StartClient`/`StopNetworking`/`IsNetworkStarted` はコードから呼ぶ API のみで、実行中に IP を入力する導線が無かった。実機（Unity の無いビルド済み exe）向けなので EditorWindow ではなくランタイム UI にした
  - `Runtime/Net/NetManualConnectInput.cs`（新規、Unity API 非依存の純関数）: `TryParsePort(string, out ushort, out string)`・`TryParse(string ip, string port, out string address, out ushort portValue, out string error)`。IP は IPv4 のドット表記のみ許可（ホスト名不可、`"localhost"` だけ `"127.0.0.1"` に読み替え）
  - `Runtime/Ngo/NetManualConnectOverlay.cs`（新規、`#if DDRIVE_NGO`）: `NetDebugOverlay` と同じ `OnGUI` 方式。画面左下に IP/Port 入力欄 + 「Host で開始」「Client で接続」「切断」ボタン + 状態 1 行（未接続/Host listening/Client 接続中/切断）を表示。接続中は入力欄を編集不可にする。最後に接続した IP/Port を `PlayerPrefs`（`DDrive.Net.Manual.LastAddress`/`LastPort`）に保存し次回の初期値にする
  - `NgoBridgeFactoryInstaller.cs`（`NgoBridgeFactory.Create`）: `role==NetLaunchRole.Manual` かつ `Debug.isDebugBuild || Application.isEditor` のときだけ `NetManualConnectOverlay` を生成する（リリースビルドで Manual が指定された場合は生成せず警告を 1 回だけ出す）。`DDriveRuntimeBootstrap` に新規 Inspector フィールドは追加していない
  - テスト: `Tests/Editor/NetManualConnectInputTests.cs`（EditMode 新規 25 件）。互換性スナップショット `public-api-DDrive.Runtime.txt` を更新（`NetManualConnectInput` の追加のみ）。EditMode 1148/1148・PlayMode（`DDrive.Tests.Runtime`）754/754 green（Unity MCP 経由で確認済み）
  - **未実施**: `NetCheckBuilder` の実ビルドを 2 プロセス起動しての Host/Client 接続・切断・再接続の実機確認（実装完了時点で空きメモリが約 1.2GB、ビルドの目安閾値 1.3GB 未満だったため見送り）

- N-3（2026-09-22）: `NetCheckScene`/`NetCheckRunner` の Host 1 + Client 3 対応（[docs/14_networking.md](docs/14_networking.md) §16）
  - 背景: MS2026（4 人対戦: Host 1 + Client 3）向けに NGO 経路を「Host 1 + Client 3」で検証したいが、6-7 の自動確認（`run-netcheck.cmd`）は Host 1 + Client 1 の 2 プロセス前提だった。さらに P-5（2026-09-20）で `NetCheckRunner`/`NetBridgeSmokeTest` が `Samples~/NetCheck/`（Unity が import しない領域）へ移されており、開発リポジトリでは未コンパイルの状態（`NetCheckScene.unity` が Runner を missing script として参照）になっていた
  - 復旧: `Samples~/NetCheck/NetCheckRunner.cs`/`NetBridgeSmokeTest.cs`（+ `.meta`）を `git mv` で `Runtime/Ngo/NetCheck/`（既存の `DDrive.Runtime.Ngo` アセンブリ）へ移設（GUID 不変）。名前空間を `DDrive.Samples` から `DDrive.Runtime.Net` に統一。`Samples~/NetCheck/DDrive.Samples.NetCheck.asmdef` を削除し、`package.json` の `samples` から NetCheck エントリを削除
  - `NgoNetBridge.ConnectedClientCount`（`public int`、Server のときだけ「Host 自身を除いたリモート Client の数」〔`NetworkManager.ConnectedClientsIds.Count` から Host 自身の 1 人分を引く。専用サーバーは引かない〕、Client では 0）を追加。**2026-09-22 レビュー指摘で修正**: 当初 `ConnectedClientsIds.Count` をそのまま返す実装だったため、Host 1 + Client 3 全員接続時に `4` になり、Client が 2 人しか繋がっていなくても `-ddrive-expect-clients 3` の判定を誤って満たしてしまう実バグがあった
  - `NetLaunchArgs`/`NetLaunchOptions` に `-ddrive-expect-clients <n>`（`int?`）を追加。`NetCheckCounters` に `ExpectedClientCount`/`MaxConnectedClientsObserved` を追加し、`NetCheckJudge.Evaluate` は Host 役で `ExpectedClientCount>0` のとき `MaxConnectedClientsObserved >= ExpectedClientCount` を PASS 条件に加える（Client 役・未指定時は従来どおりスキップ）
  - `NetCheckRunner` の `Update()` に役割の遅延評価を追加（Manual モードで `_role` が `off`/`unknown` の間は毎フレーム `RoleOf()` を再評価し、host/client/server に変わった時点で `ready` ログを出す。既存の Auto 経路は無改修）。Host 役での `client_left=<clientId>` ログ（`NgoNetBridge.ClientDisconnected` から）、Heartbeat への `clients=<n>` 追加、`NetDebugOverlay` への「Clients: n」行追加（Host のみ、変化検知パターンで文字列を作る）
  - `ForbiddenApiScanner`（[00_requirements.md](docs/00_requirements.md) §5 の禁止 API 静的走査）の除外パスに `/Runtime/Ngo/NetCheck/` を追加（`Samples~` 配下から通常配置へ移ったことで新規に対象へ入り、`Time.deltaTime`/`Time.time` 直接参照の違反が発生していたため。確認用のヘッドレス自動テストコードという性質は変わらない）
  - `Tools/CI/Run-NetCheck.ps1` に `quad0`/`quad_latejoin`/`quad_leave`/`quad_hostquit`（Host 1 + Client 3。既存 4 シナリオ `pair0`/`pair200`/`latejoin`/`disconnect` は無改修）を追加。ポートは 7841/7851/7861/7871（既存と重複なし）
  - テスト: `Tests/Editor/NetLaunchArgsTests.cs`・`Tests/Editor/NetCheckJudgeTests.cs`・`Tests/Editor/ForbiddenApiScannerTests.cs` に EditMode テストを追加。互換性スナップショット `public-api-DDrive.Runtime.txt` を手動更新（`NetLaunchOptions.ExpectedClientCount`・`NetLaunchArgs.ExpectClientsFlag`・`NetCheckCounters.ExpectedClientCount`/`MaxConnectedClientsObserved` の追加のみ）
  - **PR レビュー対応（2026-09-22）**: `NgoNetBridge.ConnectedClientCount` が Host 自身を含んだ値を返しており、`-ddrive-expect-clients` 判定が Client 1 人不足でも誤って PASS してしまう実バグを修正（Host を除いたリモート Client 数を返すよう変更）
  - **軽量検証（2026-09-22）**: 空きメモリ制約下で instance ファイル経由の直接 JSON-RPC 接続により、**compile_request → error 0**（`Samples~/NetCheck` → `Runtime/Ngo/NetCheck` の namespace 変更に起因する `Presentation.Play` の namespace 衝突〔CS0234〕を発見・修正。using エイリアスを namespace ブロック内側に置いて解決）・**EditMode を絞って実行（`Compat|NetCheckJudge|NetLaunchArgs|ForbiddenApiScanner`）→ 99/99 green**（`PublicApiSnapshotTests.Runtime_MatchesGolden` を含み、手動更新したスナップショットが実際の公開 API と一致することを確認）を実施
  - **main（N-4 マージ済み、6ae597b）取り込み後のフル検証（2026-09-22）**: `git merge main` → `compile_status → error 0` → **EditMode 全件 1164/1164 green**・**PlayMode 全件（`DDrive.Tests.Runtime`）775/775 green** を実施。テストが残した `Assets/Tests/`・`ProjectSettings/DDriveProjectSettings.asset` の差分は削除・復元済み（Addressables/`Assets/GameData` に差分なし）
  - **`NetCheckBuilder.Build()` + `run-netcheck.cmd`（8 シナリオ）の実行結果と修正（2026-09-22）**: ビルドは成功。初回の 8 シナリオ全部 FAIL の原因を切り分け、2 件とも修正した:
    - **GameData 修正**: `ANC_Player_VFXPlayerSlashAnchor`/`SE_test_NewSound` の `Flags.Load` が `LazyLoad` のままで、同期解決経路（`AnchorChain.Resolve`/`AssetEventDispatcher`）では Placeholder に落ちる仕様どおりの挙動だった（データ側の実バグ）。Unity Editor 経由で `Flags.Load` を `Preload` に修正し、`AssetCreationService.RegisterExisting` でカタログ（`AnchorCatalog`/`AudioCatalog`）のスナップショットも再同期した
    - **`Tools/CI/Run-NetCheck.ps1` 修正**: `Test-SignalPhase` が Client の退出時刻を分母（Host の `signal_fire`）から除外していなかったため、quad_latejoin/quad_leave の途中退出・遅延参加 Client の位相差判定が `signal_relay_ratio_low` で誤って FAIL していた。`Get-ClientLastNetworkTime`（観測した networkTime の最大値ベース。当初「最後の行」ベースで実装し disconnect シナリオに回帰を起こしたため再修正）を追加して分母を Client の生存時間窓に限定した
    - **`NetCheckRunner` に音声ミュート追加**: `run-netcheck.cmd` 実行中に SE が鳴り続ける実害（ユーザー報告）を受け、`-ddrive-autotest` 実行時だけ `AudioListener.volume=0f` にする対応を追加（手動実行・実機確認では従来どおり鳴る）
    - **`forged_cancel_mismatch` の修正**: quad 4 シナリオの全 Client が `forged_cancel_mismatch sent=N discarded=M`（M が N の約 3 倍）で FAIL していた原因は、`NetCheckRunner` の破棄カウントが 1v1 前提のままで、Broadcast が全ピアに届く quad 構成では他 Client 分の破棄ログも自分のものとしてカウントしていたため（実プロダクトの発行者検証・破棄自体は正常）。`SendForgedCancel` で送った鍵を `HashSet<uint>` に保持し、破棄ログにその鍵が含まれるときだけカウントするよう `NetCheckRunner` を修正（1 回目、`NetCheckJudge` は無改修）
    - **`forged_cancel_mismatch` の追加修正（2 回目、最終）**: 1 回目の鍵一致だけでは、偽造キーの選定が決定的（アクティブハンドルの先頭を選ぶ）なため複数 Client が同一の実キーを偽造対象に選んだ場合に他 Client 分の破棄まで数えてしまい、quad の一部 Client で discarded が sent を上回ったまま残った。`PresentationManager` の発行者不一致による破棄ログには `NgoNetBridge.RequestBroadcastRpc` が伝える真の発行者 `ClientId(N)` が既に含まれているため、`OnLogMessageReceived` を「鍵一致に加え、ログに `ClientId(` があるなら自分の `LocalClientId` のものだけ」数えるよう修正（未知キー側の破棄ログは送信元が無いため鍵一致のみ判定。`PresentationManager` の文言変更は不要）
    - **最終結果**: **8 シナリオ全て PASS**。quad 4 シナリオも全 Client で `sent==discarded` が厳密に一致した。詳細・ログ抜粋は [docs/29_network_device_test.md](docs/29_network_device_test.md) §24
- N-4（2026-09-22）: HitStop/CameraShake/Haptic の当事者限定（Scope）（[docs/14_networking.md](docs/14_networking.md) §17）
  - 背景: [docs/14_networking.md] §5 の 5-8 実装メモ「HitStop は全員が実行する（観戦者を区別しない、既定）」は MS2026 が 1v1 前提だった頃の要判断。MS2026 が 4 人対戦（Host 1 + Client 3）になったため、当事者ではない Client にも HitStop/CameraShake/Haptic が誤って波及する
  - `Runtime/Presentation/PresentationTrack.cs` に `enum PresentationEffectScope { Everyone = 0, ParticipantsOnly = 1 }` と `PresentationTrack.Scope`（末尾追加、既定 `Everyone`）を追加。HitStop/CameraShake/Haptic のみ意味を持つ
  - `PresentationManager` に `public static bool IsParticipant(INetBridge bridge, ulong selfNetId, ulong targetNetId)`（0 alloc 純関数）を追加。SelfNetId/TargetNetId のどちらかが `ResolveNetObject`→`IsLocalPlayerObject` で自分の所有物なら true。両方未解決（0）なら安全側で true（従来どおり全員実行）
  - 既存の `FireHaptic`（`HapticsData.LocalPlayerOnly`、6-0 で追加済み）を `IsParticipant` と同じ解決経路（`IsLocalParticipant`、private）を共有する形に書き換えた（重複コード排除。`PresentationInstance` に `SelfNetId`/`TargetNetId` を追加してネット受信 Instance に限り保持する）
  - `FireCameraShake`/`FireHaptic`/`FireHitStop` は `PlayedViaNetworkReceive && Scope==ParticipantsOnly && !IsParticipant(...)` のとき発火をスキップする。予測再生（`PredictLocal`）した行為者自身（`PlayedViaNetworkReceive=false`）は Scope に関わらず常に発火する
  - `PresentationDataValidator` に Info 検査を追加: `Scope=ParticipantsOnly` なのに `Flags.Net=Local`（ネット再生されず無意味）
  - `Editor/Presentation/PresentationEditorWindow.Tracks.cs`: トラック編集 Inspector に `Scope` フィールドを追加（`Kind` が CameraShake/Haptic/HitStop のときのみ表示）
  - テスト: `Tests/Editor/PresentationIsParticipantTests.cs`（新規、EditMode、`IsParticipant` の純関数テスト）・`Tests/Runtime/PresentationParticipantScopeTests.cs`（新規、PlayMode、Everyone 回帰・Self/Target 当事者・第三者非発火・未解決時の安全側発火・予測再生の無条件発火）・`PresentationDataValidatorTests` に Info 検査 2 件。`Tests/Runtime/FakeNetBridge.cs` に `ResolveNetObject` の順引き（`netId → Transform`）辞書を追加（`SetNetId` の逆引きと対にした。既存呼び出しの挙動は不変）
  - **未検証**: このセッションでは Unity MCP（isuzu-unity/CoplayDev）のどちらにも接続できず、コンパイル・EditMode/PlayMode テスト・互換性スナップショットの再生成（`Tools > D-Drive > Compat > スナップショットを更新`）を一度も実行できなかった。次回 Unity Editor 上で必ず確認すること

## [1.1.0] - 2026-09-20

### 互換性

- 追加のみ（MINOR）: **P-14（2026-09-20、[docs/42_distribution.md](docs/42_distribution.md) §4.2・§6 P-14）** — 更新ウィンドウ（`Tools > D-Drive > Update > 更新ウィンドウ`）の最上段に「更新チェック」（`git ls-remote --tags` で最新版を取得し、現在の参照と比較して manifest の `#ref` を更新する）を追加した。変更は `DDrive.Editor` のみ（新設: `Editor/Update/{GitPackageUrl.cs, GitTagListParser.cs, IGitTagLister.cs, GitCliTagLister.cs, UpdateCheckLogic.cs}`）で、公開 API（`DDrive.Foundation`/`DDrive.Runtime`）・シリアライズ形式・生成コード・ネットメッセージには触れていない。`DDriveProjectSettings` に `PreviousPackageRef`（string）フィールドを追加（`ScriptableSingleton`、`ProjectSettings/DDriveProjectSettings.asset` 配下、フィールド追加のみ）

### 追加

- P-14（2026-09-20、[docs/42_distribution.md](docs/42_distribution.md) §4.2・§6 P-14）: **更新ウィンドウの更新チェック / 版上げ**（v1.0.0 の後の最初の MINOR = v1.1.0）
  - `Tools > D-Drive > Update > 更新ウィンドウ` の最上段に「1. 更新チェック」を新設（既存の節は 1 つずつ繰り下げ）。「最新の版を確認」ボタンが `git ls-remote --tags` でタグを取得し、現在の参照（manifest の `#ref`。コミットハッシュ指定のときは `package.json` の版）と比較して「最新です」/MINOR/MAJOR を表示する（MAJOR は赤字で移行ガイドの確認を促す）
  - `Editor/Update/GitPackageUrl.cs`（新規）: `Packages/manifest.json` の `com.ddrive.core` の値を URL・`?path=`・`#ref` に分解する純関数。`WithRef` は `#ref` だけを差し替える（URL・`?path=`・`git+https`/`git+ssh` の形式は保持）。git URL でない値（レジストリ配布・`file:`）は対象外として no-op にする
  - `Editor/Update/{IGitTagLister.cs, GitCliTagLister.cs}`（新規）: `git ls-remote --tags` の実プロセス起動をインターフェースに分離（タイムアウト 30 秒、`git` が無い/失敗時は警告表示のみで例外を投げない）。`Editor/Update/GitTagListParser.cs`（新規、純関数）: 標準出力から `refs/tags/vX.Y.Z` を抽出し、peeled 行（`^{}`）と非 SemVer タグを除外して降順に整列する
  - `Editor/Update/UpdateCheckLogic.cs`（新規、純関数）: 現在の参照 vs 取得した最新タグを比較し `UpToDate`/`Patch`/`Minor`/`Major`/`Unknown` を判定する
  - 「manifest を選んだ版に更新する」ボタン（確認ダイアログ付き）で `#ref` を書き換えて保存 → `AssetDatabase.Refresh()` → `Client.Resolve()`。差し替え前の値は `DDriveProjectSettings.PreviousPackageRef`（新規フィールド）に退避し、「前の参照に戻す」ボタンで入れ替えて戻せる（2 回押すと元に戻せる簡易 1 段 undo）。「起動時に確認」トグルは作らない（手動のみ）
  - テスト: `Tests/Editor/Update/{GitPackageUrlTests, GitTagListParserTests, UpdateCheckLogicTests}`（新規 30 件）+ `DDriveProjectSettingsTests` に `PreviousPackageRef` の往復テストを追加
  - docs: `docs/42_distribution.md` §4.2・`docs/09_editor_tools.md` §14・`docs/11_tasks.md`（P-14 行）・パッケージ `README.md`「更新する」/「ロールバック」・`Documentation~/skills/ddrive-consumer/{SKILL.md, references/update-checklist.md}`・`docs/DesignerManual/package-setup.html`・`docs/ProgrammerManual/getting-started.html` を更新
  - **未検証**: Unity が使えない環境（メモリ制約で別プロジェクトのバッチ起動中）で実装したため、コンパイル・EditMode/PlayMode 実行は未検証

## [1.0.0] - 2026-09-20

`Assets/DDrive/` を `Packages/com.ddrive.core/` へパッケージ化し、UPM（git URL 参照）での配布を開始する最初の版。[docs/42_distribution.md](docs/42_distribution.md) を参照。

### 互換性

- 破壊なし（初回リリース。**1.0.0 から開始**する理由は [docs/42_distribution.md](docs/42_distribution.md) §7 A-3 のとおり: 0.x は SemVer 上「壊してよい期間」を意味し、ユーザー要望「以降は互換性を持たせる」と矛盾するため）
- **発効前の一度きりの整理**（[docs/42_distribution.md](docs/42_distribution.md) §5.13）: `AssetIdGenerator.KnownPrefixes` に `MODEL`/`ANC`/`ANCG`/`SKIN` を追加し、生成定数名の接頭辞重複（例: `MODELID.MODELPlayerModel` → `MODELID.PlayerModel`）を解消済み（2026-09-18、コミット `ead2149`）。ID(ulong) 値は不変
- P-3（2026-09-20）: `ValidationResult` に `Code`（string、既定引数）を追加。既存の `Error/Warning/Info` 呼び出しはすべて変更不要（省略可能引数のため既定は空文字）。追加のみなので互換性への影響なし
- P-3（2026-09-20）: `AddressablesRegistrationValidator` の 5 種のメッセージに `Code`（`DD-ADDR-CATALOG-MISSING` / `DD-ADDR-NO-SETTINGS` / `DD-ADDR-MISSING` / `DD-ADDR-MISMATCH` / `DD-ADDR-PRELOAD-REQUIRED`）を付与。メッセージ文言・Severity（いずれも Error）は変更なし
- P-3（2026-09-20）: `AssetIdGenerator.Regenerate` / `CI.LoadAllAssetDataAssets` が `Tests/Editor/Compat/Fixtures/` 配下のアセットを常に除外するようにした（互換性スナップショットの旧版フィクスチャが実生成物・実 Validation に混入するのを防ぐ）。実 GameData の挙動に影響なし
- P-4（2026-09-20）: `CI` に public static メソッド `ResolveForbiddenApiScanRoot` を追加(追加のみ)。`Tests/Editor/Compat/Snapshots/editor-contract.txt` を更新済み(`Tools > D-Drive > Compat > スナップショットを更新`)
- P-5（2026-09-20）: `DDriveVersion.Value` を `"1.0.0-dev"` → `"1.0.0"` に変更(パッケージ化発効に伴う正式表記。`PackageVersionConsistencyTests` で `package.json` の `version` と一致することを確認済み)
- P-5（2026-09-20）: `AssetSearch.Roots` の既定値を `{"Assets"}` から `{"Assets", <D-Drive 自身のパッケージ asset パス>}` に拡張(`PackageInfo` で解決。他パッケージは対象外のまま)。D-Drive 自身が `Packages/com.ddrive.core/` に移った後も `Tests/` 配下の一時フィクスチャ等を検索できるようにするための必須修正(追加のみ、`DDrive.Editor` は互換面 §5.4 の対象外)
- P-5（2026-09-20）: `CameraExecutionOrderValidator.IsDDrivePath` が `PackageInfo` 経由でパッケージの実 asset パスも D-Drive 自身のスクリプトと判定するようにした(`"Assets/DDrive/"` 前方一致は後方互換のため維持)。`ScanRiskyPatternFiles` の走査対象を `DDriveCodeScanRoots`(Assets 全体 + D-Drive 自身のパッケージパス)に拡張
- P-6（2026-09-20）: `DDriveProjectSettings` に `IsDevelopmentRepo`（bool）・`EmitGeneratedAsmdef`（bool、既定 true）を追加(追加のみ)。`AssetCreationService` に `EnsureCatalogFile`/`AllCatalogNames`（`public static`、追加のみ）を追加
- P-6（2026-09-20）: `DDriveMenu` に `Setup`（`"Tools/D-Drive/Setup/"`）を追加(追加のみ)。`Tests/Editor/Compat/Snapshots/editor-contract.txt` を更新済み(`Tools > D-Drive > Compat > スナップショットを更新`)
- P-7（2026-09-20）: `AssetDataBase` に `SchemaVersion`（`int`、`[HideInInspector]`、既定 0）を追加(追加のみ)。`Foundation.Data.DDriveSchema`(`public const int Current = 1`)を新設。`VersionStampProcessor` が保存の都度(新規作成時も)`SchemaVersion = DDriveSchema.Current` を書き込む(`Version`〔保存回数〕とは別カウンタ)。`DDriveProjectSettings` に `LastAppliedVersion`（string）・`AppliedMigrationIds`（string[]）・`HasAppliedMigration`/`MarkMigrationApplied`（追加のみ）を追加。`DDriveMenu` に `Update`（`"Tools/D-Drive/Update/"`）を追加。`CI` に `MigrateCheck`（追加のみ）を追加。`Tests/Editor/Compat/Snapshots/{public-api-DDrive.Foundation.txt,editor-contract.txt}` を更新済み(いずれも追加のみ。`serialized-layout.txt` は `[HideInInspector]` フィールドが `SerializedProperty.NextVisible` の走査対象外のため差分なし)。`Tools/CI/run-ci.cmd` に `[1/7] CI.MigrateCheck` を追加し、以降のステップ番号を `[2/7]`〜`[7/7]` に繰り下げ
- **P-8（2026-09-20、[docs/42_distribution.md](docs/42_distribution.md) §4.2 手順 5・§5.6・§6 P-8）: 更新ツール + 版の照合**
  - `Runtime/Net/CatalogContentHashMessages.cs`: `CatalogContentHashMsg` に `PackageVersion`（string）・`ProtocolVersion`（int）を**フィールド追加**（`JsonUtility` は未知/欠落フィールドに寛容なため旧版と混在しても落ちない）。**ネットメッセージ形式の変更のため `Tests/Editor/Compat/Snapshots/{net-messages.txt,public-api-DDrive.Runtime.txt}` を更新**（`Tools > D-Drive > Compat > スナップショットを更新` 相当。いずれも追加のみ）
  - `Foundation/Net/DDriveProtocol.cs`（新設）: `public const int Current = 1`。`Tests/Editor/Compat/Snapshots/public-api-DDrive.Foundation.txt` を更新済み(追加のみ)
  - `CatalogContentHashGate`: `ProcessHostSide` で ContentHash の照合より**先に** `ProtocolVersion` を照合するようにした(不一致は旧版 Client〔フィールド無し→既定値 0〕も含めて `CatalogContentHashPolicy.Decide` と同じ方針〔開発は警告継続・リリースは切断〕で扱う)。一致すれば従来どおり ContentHash の照合に進む。`LocalPackageVersion`/`LastKnownRemotePackageVersion`（追加のみ、表示専用）を追加
  - `NetDebugOverlay`（`#if DDRIVE_NGO`）に自分の版・相手の版(分かる範囲)を表示する行を追加
  - `Editor/Update/`（新設）: `Tools > D-Drive > Update > 更新ウィンドウ`（`UpdateWindow`）。現在の版/前回適用した版(`DDriveProjectSettings.LastAppliedVersion`)/その間の CHANGELOG 該当節(`ChangelogRangeReader`/`ChangelogLocator`)を表示し、「互換性」節に「破壊あり」があれば警告(`ChangelogCompatibilityAnalyzer`)。「更新を適用」はウィンドウ非依存の `UpdateActions.Apply`(純関数、フェイクの段でテスト可能)に委譲し、`UpdateStepsFactory` が実処理(マイグレーション → ID/Tuning 再生成 → Addressables 同期 → Validation → `LastAppliedVersion` 更新)を配線する。途中の段が失敗したら以降を実行しない。「テストを有効化」「エージェント向けスキルを更新」は P-6 の `ProjectSetupActions`(`SetTestablesEnabled`/`CopyConsumerSkillIfBundled`)を再利用(重複実装なし)
  - `ProjectSetupValidator` に `LastAppliedVersion` が現在版より古い(または未適用)ことを検出する Warning(`DD-SETUP-UPDATE-PENDING`)を追加(§5.8 の 2 段階ルールに従い Warning。開発リポジトリ〔`IsDevelopmentRepo=true`〕は対象外)
  - §2.3 #10(`CatalogContentHashMsg` に版情報が無い)に対応

- 破壊あり（互換性ポリシーは未発効のため 1.0.0 発効前の例外として実施。[docs/42_distribution.md](docs/42_distribution.md) §5 は P-13 で発効する草案段階）: **P-10.5 レビュー対応（2026-09-20、[docs/47_review_p_tickets_2026-09-20.md](docs/47_review_p_tickets_2026-09-20.md) P1-1）** — NGO を任意依存にする実現方式を asmdef 分離まで修正した。`DDriveRuntimeBootstrap` の public フィールド `NetworkManagerRef`/`NgoBridgeRef` を削除し（`DDrive.Runtime.Ngo` アセンブリの `DDriveNgoBootstrapHook` へ移設）、`NgoNetBridge`/`NgoTransportConfigurator`/`NetDebugOverlay` を `DDrive.Runtime` から新設アセンブリ `DDrive.Runtime.Ngo` へ移動した（namespace は `DDrive.Runtime.Net` のまま不変、GUID も不変）。互換性スナップショット `public-api-DDrive.Runtime.txt` を更新（該当箇所は削除+新設 API `INgoBridgeFactory`/`NetBridgeFactoryRegistry`/`NgoBridgeCreateArgs`/`NgoBridgeCreateResult` の追加）。既存シーン（`NetCheckScene.unity`）は Unity Editor 経由で `DDriveNgoBootstrapHook` を追加し直し、参照を復元済み
- P-10.5 レビュー対応（2026-09-20）の残りの修正（P1-2〜P1-7、P2-1〜P2-9）は、Editor 専用 API のシグネチャ変更（`ChangelogLocator.ResolvePath` に `preferDevRepoRoot` 引数を追加 等）・挙動修正（`ForbiddenApiScanner`/`ManualPages`/`DDriveMigrationRunner`/`ValidatorRegistry` 等）・PowerShell/バッチスクリプトの修正で、いずれも `DDrive.Editor` は互換性スナップショットの対象外（ゲームコードは `DDrive.Editor` を参照禁止のため）。公開 API（`DDrive.Foundation`/`DDrive.Runtime`）への影響は上記の NGO 分離のみ
- P-9（2026-09-20）: リリース手順を道具化しただけで、公開 API・シリアライズ形式・生成コード等の互換面には触れていない
- P-10（2026-09-20）: 消費側ドキュメント・スキル・CI テンプレの追加のみで、C# の変更は無い（公開 API・シリアライズ形式・生成コード等の互換面には触れていない）
- P-11 フォローアップ（2026-09-20、[docs/48_p11_install_test_2026-09-20.md](docs/48_p11_install_test_2026-09-20.md) §12）: テスト専用コード（`Tests/Editor`・`Tests/Runtime`）の修正・追加とエディタ専用の `ProjectSetupActions`/`ProjectSetupWizardWindow`（`DDrive.Editor`）の変更のみで、公開 API（`DDrive.Foundation`/`DDrive.Runtime`）・シリアライズ形式・生成コード等の互換面には触れていない
- P-12（2026-09-20、[docs/49_p12_ms2026_install_2026-09-20.md](docs/49_p12_ms2026_install_2026-09-20.md)）: MS2026 への実移植確認のみで、D-Drive（`Packages/com.ddrive.core`）のコードは一切変更していない（変更したのは移植先 MS2026 側のみ）。互換面には触れていない。実移植で発見した D-Drive 側の不具合（`DDriveSpecSettings.DefaultPath` のハードコード、置き場所変更×git URL 参照を想定していない同梱テスト 9 件、`-nographics` バッチモードでの `CutsceneTimelineTracksTests` 1 件の Fail）は [docs/42_distribution.md](docs/42_distribution.md) §2.3 #11・#12 に記録済み → **2026-09-20 フォローアップで修正済み（下記「修正」参照）**
- P-12 フォローアップ（2026-09-20、[docs/49_p12_ms2026_install_2026-09-20.md](docs/49_p12_ms2026_install_2026-09-20.md) §16）: `DDrive.Editor` とテストのみの変更（`DDriveSpecSettings.DefaultPath`/`DefaultTuningTablePath` を `const` → static プロパティに変更したが、既定値〔GameDataRoot 未変更、または旧パスに既存アセットがある場合〕では従来と同じパスを返すため挙動は変わらない）。公開 API（`DDrive.Foundation`/`DDrive.Runtime`）・シリアライズ形式・生成コード等の互換面には触れていない

### 修正

- P-12 フォローアップ（2026-09-20、[docs/49_p12_ms2026_install_2026-09-20.md](docs/49_p12_ms2026_install_2026-09-20.md) §16、[docs/42_distribution.md](docs/42_distribution.md) §2.3 #11・#12）: **P-12（MS2026 への実移植）で発見した D-Drive 側の不具合 4 件 + 起動時に新規発見した 1 件、計 5 件を修正**
  1. `DDriveSpecSettings.DefaultPath`/`DefaultTuningTablePath`（`Editor/Spec/DDriveSpecSettings.cs`）が `const` で `Assets/GameData/Settings/` に固定され、`DDriveProjectSettings.GameDataRoot`（置き場所プリセット）を無視していたのを、これを尊重する static プロパティに変更した。既に既定パスにアセットが存在する場合はそれを優先して使う（移動しない。開発リポジトリの既存アセットが迷子にならない）
  2. 置き場所変更×git URL 参照（`PackageCache` のハッシュ付きパス）で落ちる EditMode テスト 9 件（`DDriveProjectSettingsTests`/`SourceDataCreationTests`/`SpecSnapshotWriterTests`/`CIJUnitXmlTests`）を、実際の `DDriveProjectSettings`/`PackageInfo` の値から期待値を動的に組み立てる形に修正した
  3. `-nographics` バッチモードで Fail していた `CutsceneTimelineTracksTests.Applier_DetectsOverwrite_WhenLaterScriptWritesCameraInLateUpdate`（検出2が実際の SRP カメラ描画コールバックに依存するため `TestFrameWait` だけでは解消できなかった）に `[Category("RequiresGraphics")]` を追加し、新設 `Tests/Runtime/RequiresGraphicsGuard.cs` でガードした
  4. Addressables の既定アセット名（`Default Local Group`・`Packed Assets`）のスペースを解消する経路（`ProjectSetupActions.RenameDefaultAddressablesAssetsToAvoidSpaces`、新規初期化直後の自動実行・ウィザードのボタン・`ProjectSetupValidator` の Warning `DD-SETUP-ADDR-NAME-SPACE`〔修正アクション付き〕）を追加した。開発リポジトリ自身の `Assets/AddressableAssetsData` も Unity Editor 経由でリネームした
  5. （今回のセッション中に新規発見）Unity 起動直後の全量再インポートで `DependencyGraphPostprocessor`→`DependencyGraphService`（`RebuildAll`/`UpdatePaths`）が読み取り専用パッケージ内のシーンを開こうとして「Opening scene in read-only package!」のモーダルが連続表示される不具合を修正した。走査対象を Assets 配下 + 埋め込み/ローカルパッケージに限定する `DependencyGraphService.IsScannablePath` を追加した
- P-11 フォローアップ（2026-09-20、[docs/48_p11_install_test_2026-09-20.md](docs/48_p11_install_test_2026-09-20.md) §12）: **持ち込み先で testables を ON にしたときの Fail 24 件の解消**
  - 開発リポジトリの状態を暗黙の前提にしていたテスト 6 件（`CIJUnitXmlTests`/`ManualPagesTests`/`DDriveMigrationRunnerTests`/`ProjectSetupInspectorTests`/`ProjectSetupValidatorTests`）を、原則はテスト内でセットアップ/モックする自己完結な形に直した（4 件）。実プロジェクトのグローバル設定（URP/Input System/manifest.json/Addressables 初期化）を書き換えないと再現できない 2 件だけ `[Category("DevRepoOnly")]` にした
  - `-batchmode -nographics` で `EditorWindow.GetWindow<T>()`/`RenderTexture.Create` が失敗する 14 件に `[Category("RequiresGraphics")]` を追加し、新設 `Tests/Editor/RequiresGraphicsGuard.cs`（グラフィックデバイスが無ければ `Assume` で Inconclusive）でガードした
  - `new WaitForEndOfFrame()` がバッチモードで失敗する `CutsceneTimelineTracksTests` の 4 件を、新設 `Tests/Runtime/TestFrameWait.cs`（`Application.isBatchMode` なら `yield return null` に切り替える共通ヘルパー）で対処した
  - 開発リポジトリ自身の `Tools/CI/run-ci.cmd` 相当のバッチ実行（`git worktree` で再現）でも同じ 24 件が Fail することを実測で確認した上で対処した
  - `ProjectSetupActions.EnsureDefaultFoldersAndSettings`（セットアップウィザード「4. 既定フォルダ・設定の生成」）が空カタログを作成した直後に `AddressablesSync.SyncAll` を自動実行するようにし、セットアップウィザードの「5. Addressables 同期」に**「全カタログ・Data を今すぐ同期する」ボタン**を追加した（`Tools > D-Drive > Update` の「Addressables 登録を同期」と同じ処理を再利用）。`README.md` の関連する既知の注意を解消済みに更新した

### 追加

- `CHANGELOG.md`（本ファイル）・`docs/migrations/README.md`・`docs/migrations/TEMPLATE.md` を新規作成（P-2）
- [docs/12_review.md](docs/12_review.md) §3 に「互換性」チェック節の草案を追加（P-2）
- P-3（2026-09-20）: 互換性スナップショットテスト群（`Tests/Editor/Compat/`）。[docs/42_distribution.md](docs/42_distribution.md) §5.11 の 1〜10 に対応する EditMode テストとゴールデン（`Tests/Editor/Compat/Snapshots/*`）、旧版フィクスチャ（`Tests/Editor/Compat/Fixtures/v1_0_0/*.asset`、19 種別）、更新メニュー `Tools > D-Drive > Compat > スナップショットを更新`（`CompatSnapshotMenu`）、環境変数 `DDRIVE_UPDATE_COMPAT_SNAPSHOTS=1` による一時フィクスチャ依存ゴールデンの更新経路。`Runtime/DDriveVersion.cs`(`DDriveVersion.Value`)を新設し、CHANGELOG 最新見出しとの一致を検査する `PackageVersionConsistencyTests` を追加
- P-4（2026-09-20、[docs/42_distribution.md](docs/42_distribution.md) §2.3・§5.13）: **境界違反の解消**
  - システム用 shader(`DDrive_Lit`/`DDrive_Unlit`/`AiStandardSurface` 一式)を `Assets/SourceAssets/Shaders/` から `Assets/DDrive/Runtime/Shaders/` へ移設(Unity Editor 経由、GUID 不変。P-5 でさらに `Packages/com.ddrive.core/Runtime/Shaders/` へ移設)
  - `CI.ValidateAll` の `ForbiddenApiScanner` 走査ルートを `PackageInfo.FindForAssembly` から解決するようにし(パッケージ化後も追従)、走査対象フォルダが無い/`.cs` が 0 件のときに Error を返すようにした(禁止 API チェックの恒久的な無効化を防ぐ)
  - `ManualPages.GetManualFolder` がパッケージ化後の `Documentation~/...Manual` を先に探すようにした(P-5 でパッケージ化済みだが `Documentation~/...Manual` 自体の同梱は P-9 待ちのため、現状は既存の `docs/...Manual` にフォールバックし挙動は変わらない)
  - `ControlSkinPreviewSection.DefaultScrollMaterialPath` を GUID 参照(`AssetDatabase.GUIDToAssetPath`)による解決に変更(パッケージ化でパスが変わっても追従。const → static プロパティ)
  - `CodeReferenceScan`/`SpecWebSender` の走査範囲を `Assets/DDrive`・`Assets/Generated` 限定から `Assets` 全体(+ D-Drive 自身のパッケージパス、`DDriveCodeScanRoots` 新設)へ拡張し、持ち込み先のゲームコードも「安全な削除」チェックの対象にした
  - NGO(`com.unity.netcode.gameobjects`)を `versionDefines`(`DDRIVE_NGO`)で必須依存から切り離した。`NgoNetBridge`/`NetDebugOverlay`/`NgoTransportConfigurator`/`Samples/NetCheckRunner`/`Samples/NetBridgeSmokeTest`/`DDriveRuntimeBootstrap` の NGO 分岐/`PrefabDataValidator` の `NetworkObject` 検査を `#if DDRIVE_NGO` で囲んだ(`DDrive.Runtime`/`DDrive.Samples`/`DDrive.Tests.Runtime` の 3 asmdef に versionDefines を追加)。NGO ありの現状(開発リポジトリ)の挙動は変わらない(EditMode/PlayMode green で確認済み)。NGO 無し状態でのコンパイル確認は P-11(空プロジェクト)で行う
  - `com.cysharp.unitask` の manifest 参照をタグ固定(`#2.5.11`)。旧 `packages-lock.json` の hash(`ceac8d69...`)は 2.5.11 より新しい未リリースコミットだったため一致するタグが無く、最新リリースタグ 2.5.11(`2e993ff1...`)へ更新した(実質的な UniTask の更新を伴う。再解決後 EditMode/PlayMode green を確認済み)
  - `DDriveSpecSettings` の旧フィールド `SpreadsheetUrl`/`AssetSheetName`/`TuningSheetName`(および未使用になった `DefaultAssetSheetName`/`DefaultTuningSheetName` 定数)を削除。実 `.asset` に値が入っていないことを P-1 で確認済み
  - `Assets/DDrive/Editor/Settings/DDriveProjectSettings.cs` を新設(`GameDataRoot`/`GeneratedRoot`/`SourceAssetsRoot`/`SpecsRoot`。既定値は現状のまま。実際の参照差し替えとウィザード UI は P-5/P-6)
  - `Tests/Editor/{ImportRuleServiceTests,CutsceneImportServiceTests}.cs` の UnityChan FBX 依存テスト(計 9 件)に `[Category("DevRepoOnly")]` を付与し、`DDRIVE_DEV_REPO`(開発リポジトリの `ProjectSettings` の Scripting Define Symbols にのみ追加)が無ければ Inconclusive にする `DevRepoOnlyGuard` を新設
- P-5（2026-09-20、[docs/42_distribution.md](docs/42_distribution.md) §6 P-5）: **パッケージ化**
  - `Assets/DDrive/{Foundation,Runtime,Editor,Tests}` を `Packages/com.ddrive.core/{Foundation,Runtime,Editor,Tests}` へ Unity Editor 経由(`AssetDatabase.MoveAsset`)で移設(.meta ごと、GUID 不変)
  - `Assets/DDrive/Samples`(`NetCheckRunner`/`NetBridgeSmokeTest`/`PresentationSkillSlashDemo`)を `Packages/com.ddrive.core/Samples~/Demo/` へ移設(スクリプトのみ。対応する確認用シーン・GameData の同梱は見送り)
  - `package.json` を新設(`name: "com.ddrive.core"`、`version: "1.0.0"`、`unity: "6000.3"`、`dependencies`(`com.unity.addressables`/`com.unity.inputsystem`/`com.unity.nuget.newtonsoft-json`/`com.unity.render-pipelines.universal`/`com.unity.timeline`/`com.unity.ugui`)、`samples`)
  - `Packages/com.ddrive.core/README.md`・`Documentation~/README.md`(雛形。P-9/P-10 で完成)を新設
  - 開発 `Packages/manifest.json` に `"testables": ["com.ddrive.core"]` を追加(Test Runner での実行に必要)
  - `AssetCreationService`/`ImportRuleService`/`AssetIdGenerator`/`TuningCodegen`/`AssetIconService`/`ScenePreloadGenerator`/`SpecSnapshotWriter`/`DDriveSpecSettings`/`AssetReorganizer`/`SourceDataCreation`/`CutsceneImportService` の `"Assets/GameData"`/`"Assets/Generated"`/`"Assets/SourceAssets"`/`"Specs"` 決め打ちを `DDriveProjectSettings.{GameDataRoot,GeneratedRoot,SourceAssetsRoot,SpecsRoot}` 経由の解決に置き換え(既定値は現状のままなので挙動は不変。実際にウィザードで変更できるようにするのは P-6)
  - `AiStandardSurface` shader の `#include` 絶対パス・`AiStandardSurfacePreprocessor.ShaderPath` を `Packages/com.ddrive.core/Runtime/Shaders/...` へ更新
  - `Tests/Editor/Compat/*`(`CompatSnapshotPaths`・`LegacyAssetFixtureTests`・`CodegenGoldenTests`・`ConstantNameGoldenTests`・`ValidatorSeverityRegistryTests`)と、その他 `Assets/DDrive/Tests/Editor/Temp*` を自前のスクラッチフォルダにしていたテスト約 50 件のパスを `Packages/com.ddrive.core/Tests/Editor/...` へ更新
- P-6（2026-09-20、[docs/42_distribution.md](docs/42_distribution.md) §3.6・§6 P-6）: **セットアップウィザード + `ProjectSetupValidator`**
  - `Tools > D-Drive > Setup > セットアップウィザード`（`ProjectSetupWizardWindow`）を新設。依存パッケージ・ProjectSettings・置き場所・既定フォルダ/設定の生成・Addressables 初期化・起動オブジェクト・テスト有効化・エージェント向けスキル・完了チェックの 9 段（各段は独立して再検査できる）
  - `ProjectSetupInspector`（検査/計算の純関数）・`ProjectSetupActions`（副作用のある適用）・`ManifestJson`（`Packages/manifest.json` の `dependencies`/`scopedRegistries`/`testables` を Newtonsoft.Json で読み書き）を新設
  - `ProjectSetupValidator`（`IUniversalValidator`）を新設し、ウィザードの検査 1・2・4・5 + A-9（改造の可能性）と同じ判定を `Validation > Run All` にも追加。新設 Code（すべて Warning）: `DD-SETUP-DEP-UNITASK` / `DD-SETUP-DEP-R3` / `DD-SETUP-DEP-R3-NUGET-REGISTRY` / `DD-SETUP-DEP-R3-NUGET` / `DD-SETUP-URP` / `DD-SETUP-INPUT` / `DD-SETUP-API-LEVEL` / `DD-SETUP-ADDRESSABLES` / `DD-SETUP-GAMEDATA-ROOT` / `DD-SETUP-UI-LAYER-SETTINGS` / `DD-SETUP-SPEC-SETTINGS` / `DD-SETUP-EMBEDDED-MODIFIED`
  - `GeneratedAsmdefWriter` を新設し、`AssetIdGenerator.Regenerate()` の既定呼び出し（出力先未指定）から `DDrive.Generated.asmdef` を同時出力できるようにした（`DDriveProjectSettings.EmitGeneratedAsmdef` で ON/OFF、A-8）。このリポジトリ自身は `DevRepoSettingsSync` が初回検出時に `false` にする（既存の `Assets/Generated/` = `Assembly-CSharp` 構成を変えないため）
  - `AssetCreationService.EnsureCatalogFile`/`AllCatalogNames` を新設（既存の `RegisterToCatalog` からカタログ確保ロジックを切り出して共用化。挙動は変えていない）
  - `DevRepoSettingsSync`（`[InitializeOnLoad]`）を新設し、`DDRIVE_DEV_REPO` 定義時に `DDriveProjectSettings.IsDevelopmentRepo` を自動で `true` にする（人手で `ProjectSettings/*.asset` を編集しない）
  - `UnityEditor.PackageManager.Client.AddScopedRegistry` が public API に無いことを確認（[42] §7 C-1 解決）。scoped registry の追加は `ManifestJson.AddScopedRegistry` による manifest.json の直接編集で行う

- P-10（2026-09-20、[docs/42_distribution.md](docs/42_distribution.md) §6 P-10）: **消費側ドキュメント**
  - `Packages/com.ddrive.core/README.md` を全面改訂: 導入 5 ステップ（manifest への git URL 追加〔`git+https`/`git+ssh` 両形式〕→ Unity を開く → セットアップウィザード → SE を登録・試聴 → `Audio.PlaySe`）、依存表、既知の制約、更新手順、ロールバック、問い合わせ先。Unity 操作の手順は「持ち込み先の MCP 構成に従う」の 1 行のみで、D-Drive 独自の MCP 手順は書かない
  - `Packages/com.ddrive.core/Documentation~/AGENTS_CONSUMER.md`（新規）: 持ち込み先の AI エージェント向け禁止事項・ID 経由の利用・Validation・更新手順の要約
  - `Packages/com.ddrive.core/Documentation~/skills/ddrive-consumer/{SKILL.md, references/{common-warnings.md, update-checklist.md}}`（新規）: Claude Code 向け消費側スキル。開発リポジトリ専用の節（新種別追加・ワークツリー・SpecWeb のテスト・MCP セットアップ）は含めない。P-6 の `ProjectSetupActions.CopyConsumerSkillIfBundled`（既存）がこの同梱を検出して `.claude/skills/ddrive-consumer/` へコピーできる
  - `Packages/com.ddrive.core/Tools~/CI/{run-ddrive-ci.cmd, ddrive-ci.yml, README.md}`（新規）: 持ち込み先向け CI テンプレート。`ddrive-ci.yml` は MS2026 の既存 self-hosted runner 運用に合わせた GitHub Actions 雛形
  - `Tools/SpecWeb/README.md` に「16. 持ち込み先で使う」節を新規追加（別デプロイの手順・`HANDOVER.md` への導線）
  - `docs/34_onboarding.md` に「10. 持ち込み先での始め方」節、`docs/DesignerManual/package-setup.html` を完成版に更新、`docs/ProgrammerManual/getting-started.html` に「1-4. 持ち込み先」節を追加
  - C# の変更は無い

- P-9（2026-09-20、[docs/42_distribution.md](docs/42_distribution.md) §4.1・§6 P-9）: **リリース手順の道具化**
  - `Tools/Release/{ReleaseChecks.ps1（共通関数）, bump-version.ps1, check-release.ps1, list-obsolete.ps1}` を新設（PowerShell 7/5.1 両対応・BOM 付き UTF-8）。`bump-version.ps1 -Version x.y.z|-Part major|minor|patch [-DryRun] [-Tag] [-SkipChecks]` が事前チェック→`package.json`/`DDriveVersion.cs`/`CHANGELOG.md` の更新→同梱物の同期（`docs/DesignerManual`・`docs/ProgrammerManual` → `Documentation~/`、`CHANGELOG.md` → `Packages/com.ddrive.core/CHANGELOG.md`）→`-Tag` 時の `git tag -a`（push はしない）を行う。`check-release.ps1`（`-GuardOnly` で CHANGELOG ガードだけに絞れる）はファイルを書き換えずに同じ事前チェック + CHANGELOG ガード（§5.11-10）を検査する
  - `docs/12_review.md` に「7. リリース手順」節を新設
  - `docs/migrations/next-major.md`（`[Obsolete]` 棚卸しの自動生成物。`list-obsolete.ps1` が更新する。2026-09-20 時点で該当 0 件）を新規作成
  - `Tools/CI/run-ci.cmd` に `[1/8] CHANGELOG ガード (check-release.ps1 -GuardOnly)` を追加し、既存の `[1/7]`〜`[7/7]` を `[2/8]`〜`[8/8]` に繰り下げ
  - **P-8 が残した docs の指摘 2 件を修正**: `docs/42_distribution.md` §8 の変更履歴に欠落していた P-7 の記述を追記。`docs/ProgrammerManual/net-api.html` の「既知の制約」が偽造 `CatalogContentHashResultMsg` を未検証としたままだった記述を、2026-09-18 の修正（`docs/14_networking.md` §7 実装メモ）に合わせて更新し、`Tools/SpecWeb/tools/build-manual.js` で再生成した
