# 29. ネットワーク実機確認環境（2 台 PC・モバイルホットスポット）

> 2026-09-14 ユーザー指示: 「このPCからモバイルホットスポットで接続しているPCに確認環境を用意し、この会話（Claude Code）から見れるようにしたい」。
> NGO 統合チケット（[11_tasks.md](11_tasks.md) Phase 6 に追加予定の「NGO 統合 + 実機 2 台確認」）の一部。ネットワークの規約は [14_networking.md](14_networking.md) §12 と MS2026 `Docs/Networking.md`（NGO 2.13.2 / Host+Client 1v1 / Host 権威）。

## 1. 構成

| | PC-A（開発機・この PC） | PC-B（確認用） |
|---|---|---|
| 役割 | Unity Editor・ビルド・**Host** | ビルド済みプレイヤーで **Client** |
| ネット | モバイルホットスポットの親（`192.168.137.1`、有線 `192.168.0.x` でインターネット） | ホットスポットに接続（**`192.168.137.74`**、2026-09-14 時点。DHCP なので変わりうる） |
| ビルド受け取り先 | — | **`C:\DDriveTest`**（PC-B の Claude のカレントディレクトリは別の場所なので、指示は必ず絶対パスで） |
| 確認済み（2026-09-14） | ARP に PC-B が見える。PC-A → PC-B の ping は PC-B 側のファイアウォールで応答なし（正常） | ホスト名 `wrench_2nd`、Windows 11 Home 25H2（26200）、Claude デスクトップアプリ Code タブ（Claude Code 2.1.266）で Remote Control 接続済み、PC-B → PC-A の ping は通る |
| Claude Code | この会話（Remote Control 接続） | Claude Code を起動し **Remote Control に接続**（同じアカウント）。PC-A の会話から SendMessage で指示・結果を返す |
| Unity | 6000.3.13f1 | **不要**（Windows 開発ビルドを受け取って実行） |

- PC-A ⇔ PC-B の Claude 同士のやり取りは Anthropic のサーバー経由（PC-B もホットスポット経由でインターネットに出られる必要がある）
- ゲームの通信は LAN 内（UnityTransport、既定 UDP 7777）で直接

## 2. ユーザーが一度だけ行う準備

### PC-B
1. PC-A のモバイルホットスポットに接続する
2. Claude Code（**v2.1.234 以降、ネイティブ Windows**）をインストールし、PC-A と**同じ Anthropic アカウント**でサインイン
3. 作業フォルダ（例 `C:\DDriveTest`）で Claude Code を起動し、**Remote Control に接続**する（claude.ai/code またはアプリから）。セッション名は分かりやすく **`D-Drive 実機B`** にする
4. PC-B の Claude に許可が必要な操作（ファイル取得・プレイヤー起動・ログ読み取り・スクリーンショット）は、PC-B 側の許可プロンプトで承認する（PC-A から代わりに承認はできない）
5. 初回のみ: ビルドのダウンロード先フォルダと、Windows Defender SmartScreen の「実行」確認

### PC-A
1. モバイルホットスポットを ON（`192.168.137.1` になっていること）
2. **Windows ファイアウォール**: Host のプレイヤー（または Unity Editor）が UDP 7777 を受信できるよう、初回起動時の「アクセスを許可する」ダイアログで**プライベート ネットワーク**を許可する（セキュリティ設定のため Claude は変更しない。ユーザーが操作する）
3. ビルドの受け渡し用に一時 HTTP サーバー（Python、`192.168.137.1` のみで待ち受け・ビルドフォルダのみ公開）を Claude が起動する。初回はファイアウォールの許可ダイアログが出るので**プライベート ネットワーク**だけ許可する。確認が終わったら Claude が停止する

### 受け渡し経路の事前確認（2026-09-14 実施・成功）

- PC-A で一時 HTTP サーバー（`scratchpad/serve_build.py`、`192.168.137.1:8765` のみで待ち受け、`Builds/` のみ公開・ディレクトリ一覧無効）を起動し、`Builds/ping.txt` を置いた
- PC-B の Claude が `Invoke-WebRequest -UseBasicParsing -Uri http://192.168.137.1:8765/ping.txt -OutFile C:\DDriveTest\ping.txt` で取得 → **成功（108 bytes、内容一致）**。`Test-NetConnection 192.168.137.1 -Port 8765` → `TcpTestSucceeded: True`（送信元 192.168.137.74、Wi-Fi）
- PC-A の Python は Windows ファイアウォールのプライベート ネットワークで受信許可済み。確認後サーバーは停止
- ~~未確認: Unity プレイヤー（`DDriveNetCheck.exe`）の UDP 7777 受信許可~~ → 2026-09-14 実機確認時点で PC-A に `ddrivenetcheck.exe` の受信許可ルール（Inbound / Allow / **Public** プロファイル）が作成済み。python の受信許可も Public プロファイルのみで、ホットスポット経由の取得が成功しているため、ホットスポット側インターフェイス（`ローカル エリア接続* 10`）は Public 扱いと判断（PC-A の有線 LAN も Public）。Host 起動時 `192.168.137.1:7777/UDP` で待ち受けを確認

## 3. コマンドライン引数（6-0 実装）

開発ビルド（`DDriveNetCheck.exe`）・Unity Editor（Play Mode）の両方が同じ引数を受け付ける（`DDrive.Runtime.Net.NetLaunchArgs` がパースし、`DDriveRuntimeBootstrap.ResolveNetBridge()` が適用する。[14_networking.md] §12）。

| 引数 | 既定値 | 意味 |
|---|---|---|
| `-ddrive-net host\|client\|off` | Inspector の `DefaultNetBridge`（NetCheckScene では `Ngo`=Host 相当） | Host/Client/シングルプレイ（Loopback）を選ぶ |
| `-ddrive-host <ip>` | `192.168.137.1`（PC-A） | 接続先 IP（Client のとき）/ Listen IP（Host のとき、UnityTransport の実装依存） |
| `-ddrive-port <n>` | `7777` | UDP ポート |
| `-ddrive-sim-latency <ms>` | 未指定 = 0 | 遅延(ms)。**2026-09-14 修正(課題1)**: `UnityTransport.SetDebugSimulatorParameters` は導入済みバージョンで `[Obsolete("... is no longer supported and has no effect.")]` であり実際には何もしない(§6 参照)ため、`NgoNetBridge` のアプリ層送受信キューで遅延を代替する(開発ビルドのみ有効) |
| `-ddrive-sim-loss <%>` | 未指定 = 0 | パケットロス率(0-100) |
| `-ddrive-autotest <name>` | 未指定 = 常駐 | `NetCheckRunner` が一定時間チェックを回してから自動終了する(ヘッドレス確認用。`name` はログに残すだけの識別ラベル) |

例: PC-B（Client）側の起動コマンド

```
DDriveNetCheck.exe -ddrive-net client -ddrive-host 192.168.137.1 -ddrive-port 7777 -logFile C:\DDriveTest\Player.log
```

PC-A（Host。Editor の Play Mode でも開発ビルドでも同じ引数）:

```
DDriveNetCheck.exe -ddrive-net host -ddrive-port 7777 -logFile C:\DDriveTest\PlayerHost.log
```

200ms 遅延を再現する場合は Host/Client どちらか（両方でも可）に `-ddrive-sim-latency 200` を追加する。

## 4. ログの見方（判定基準）

`NetCheckRunner`（`Assets/DDrive/Samples/NetCheckRunner.cs`）が `Player.log` に出す行:

- `[DDriveNetCheck] ready=1 role=host|client|server|off` — 起動直後 1 回
- `[DDriveNetCheck] heartbeat=1 role=... clientId=... networkTime=... activeCount=N connected=0|1 rtt_app_ms=... vfx_active=M` — 1 秒おき(または activeCount/vfx_active のいずれかが変化した時)。**Late Join の判定**: 新規接続したクライアントの `activeCount` が `0` → `1` に変わる行が出れば復元成功。**2026-09-14 修正で `connected`/`rtt_app_ms` を追加**(6-0 修正1/5)。`connected` は Client が Host との接続を保っているか(Host は常に 1)。`rtt_app_ms` は `NgoNetBridge` が Ping/Pong で計測したアプリ層の往復時間(ms。Loopback や計測前は `n/a`)。**トランスポートの RTT(`NetDebugOverlay` の `RTT:`)は `-ddrive-sim-latency` を反映しない**(課題1、下記参照)ため、遅延シミュレーターが効いているかどうかは `rtt_app_ms` で判定する。**2026-09-14 追加修正(6-0 修正7)**: `vfx_active`(`VfxManager.ActiveCount` = 生存中の VFX インスタンス数)を追加。「切断後も VFX が消えずに描画し続ける」実バグ(§8)をスクリーンショットの白画素カウントに頼らず判定するためのログ。切断直前に `vfx_active>0` → `disconnected=1` の直後に `vfx_active=0` になれば、ネット経由で開始した演出の強制終了(`PresentationManager.CancelAllNetworked()`)が機能している(§11 参照。**すでに完了済みの演出から Spawn した VFX は対象外**なので、切断より十分前に完了していた分は `vfx_active` に残り続ける。これは既知のスコープの限界であり実バグの再発ではない)
- `[DDriveNetCheck] disconnected=1 role=... reason=...` — **2026-09-14 修正(6-0 修正5)で追加**。Client が Host との接続を失ったときに 1 回だけ出る(`NetworkManager.OnClientDisconnectCallback`/`DisconnectReason` を中継)。**2026-09-14 追加修正(6-0 修正6、切断確認で発見)**: 切断後は `heartbeat` の `rtt_app_ms` が最後の値を表示し続けず `n/a` に戻る(`NgoNetBridge.AppRoundTripMs` を切断時にリセットする)。デバッグ表示(`NetDebugOverlay`)にも `State: 接続中/切断` の行を追加した
- `[DDriveNetCheck] play=<回数> startNetTime=...` — Host が剣攻撃デモ(`PRES_Demo_SkillSlash`)を Play したとき(Host 側のみ)
- `[DDriveNetCheck] signal_fire=hit key=<HandleNetKey> networkTime=...` — 行為者(Host)が `handle.Signal("hit")` を呼んだ(意図表明した)とき(Play から `signalDelaySeconds`(既定 0.5s)後)。**2026-09-14 修正前は `signal=hit`(key/networkTime 無し)で、「全ピア」という記述が誤りだった**(実際は Host が呼んだ直後にしか出ず、Client 側は一切出さなかった。→ 6-0 修正2)
- `[DDriveNetCheck] signal_recv=hit key=<HandleNetKey> networkTime=...` — **2026-09-14 修正(6-0 修正2)で追加**。各ピア(Host 自身の予測 Instance も含む)で実際に `OnSignal` トラックがネット経由で発火したときに出る(`PresentationManager.OnTrackFired`/`OnNetworkReceivedPlay` を使う)。`signal_fire` と `signal_recv` の `networkTime` を突き合わせることで位相差を判定できる
- `[DDriveNetCheck] forged_cancel_sent=<key>` — Client が偽造 Cancel を送信したとき(既定 5 秒おき、Client のみ)。**偽造メッセージ破棄の判定**: この行の直後(同じフレーム〜数フレーム以内)に **Host または他クライアントの `Player.log` に `[Net/Host]` または `[Net/Client]` の警告(「送信元 ClientId(...) が発行者と一致しないため破棄しました」)が出て、`heartbeat` の `activeCount` が変化しない**ことを確認する。**2026-09-14 修正(6-0 修正6)**: `sendForgedCancelPeriodically` は切断中(`connected=0`)は送らない(以前は切断後も 5 秒おきに `NgoNetBridge.Broadcast` の「未接続のため送信できません」警告が出続けていた)
- `[DDriveNetCheck] track_fired=1 kind=<Kind> time=<Time> key=<HandleNetKey> late_ms=<ms> networkTime=...` — **2026-09-14 修正(6-0 修正6)で追加**。各ピアで `TrackTrigger.AtTime` のトラック(Vfx/Se/Anim 等)が実際に発火した瞬間に出る(`PresentationManager.OnAtTimeTrackFired`、Manager 全体の event。Handle 単位の `OnTrackFired(handle)` だと Play() 内の同期発火に購読が追いつかないため専用の event にした)。`late_ms`(= `(elapsed - Time) * 1000`)が判定の主眼: 通常再生・予測再生では 0 に近く、遅延受信では猶予(既定 0.5 秒 = 500ms)以内の正の値になる。**遅延がある環境で VFX/SE が実際に描画/再生されたかをスクリーンショットに頼らず判定できる**(修正前に見つかった実バグ: 遅延 200ms で `late_ms` に相当する猶予が無かったため、開始直後のワンショット演出がリモートで一切発火しなかった)
- `[DDriveNetCheck] track_skipped=1 kind=<Kind> time=<Time> key=<HandleNetKey> late_ms=<ms>` — **2026-09-14 修正(6-0 修正6)で追加、開発ビルドのみ**。`late_ms` が猶予(既定 500ms)を超えていてワンショットの発火をスキップしたとき(Late Join で大幅に古い演出を復元しようとした場合など)に出る
- `[Net/Host]` / `[Net/Client]` — `NgoNetBridge`/`PresentationManager` のログ全般(接続・レート制限・発行者検証の破棄など)

**位相差の判定**: Host/Client 双方の `heartbeat` 行を `play` の直後(数秒間)で突き合わせ、`networkTime` の差が概ね**数ティック以内(目安 100ms 以内**、`-ddrive-sim-latency` を上げた場合はその分)であれば OK(`NetDebugOverlay` の RTT 表示も併用)。**2026-09-15 改訂([31] A8)**: 旧基準は「RTT/2 以内」だったが、NGO の Client 側 `ServerTime` はティック単位のバッファで遅れて推定されるため、実機確認 v2(§8)では RTT/2(≒3ms)を大幅に超える約 80ms の差が実測された。RTT を基準にすると正常なケースを誤診断するため、判定基準を「数ティック以内(目安 100ms 以内)」に改めた。

## 5. 確認の流れ（準備後は Claude が自律で回す）

1. PC-A の Claude: `Tools > D-Drive > Build > 実機確認用 Windows 開発ビルド`(`NetCheckBuilder.Build()`)で `Builds/DDriveNetCheck/DDriveNetCheck.exe` をビルドし、`Builds/DDriveNetCheck.zip` を作る
2. PC-A の Claude: 一時 HTTP サーバー(`scratchpad/serve_build.py`、既定 `192.168.137.1:8765`、`Builds/` のみ公開)を起動する(オーケストレーターが実施)
3. PC-A の Claude → PC-B の Claude(SendMessage): 「zip を取得して展開し、Client で起動、`Player.log` の `[Net/Client]`/`[DDriveNetCheck]` 行とスクリーンショットを返して」。PC-B での取得・展開・起動コマンド:
   ```powershell
   Invoke-WebRequest -UseBasicParsing -Uri http://192.168.137.1:8765/DDriveNetCheck.zip -OutFile C:\DDriveTest\DDriveNetCheck.zip
   Expand-Archive -Force C:\DDriveTest\DDriveNetCheck.zip -DestinationPath C:\DDriveTest\DDriveNetCheck
   C:\DDriveTest\DDriveNetCheck\DDriveNetCheck.exe -ddrive-net client -ddrive-host 192.168.137.1 -ddrive-port 7777 -logFile C:\DDriveTest\Player.log
   ```
4. PC-A: Editor(Play Mode、NetCheckScene を開いて再生。`DefaultNetBridge=Ngo` のため既定で Host 起動)または Host ビルド(`-ddrive-net host`)で起動 → `[Net/Host]`/`[DDriveNetCheck]` 行を確認
5. 両者のログ・スクショを §4 の判定基準で突き合わせる。追加で確認する項目:
   - 2 台での接続(`heartbeat` が両方に出る)
   - Presentation の位相(§4)
   - Signal 中継(`signal=hit` が両方に出る)
   - Late Join(Client を後から起動 → `activeCount` 0→1)
   - 遅延 200ms(`-ddrive-sim-latency 200`)
   - 偽造メッセージ破棄(§4)
   - ファイアウォールの許可ダイアログ(初回起動時。出た場合は PC-B 側で「アクセスを許可する(プライベート ネットワーク)」を選ぶ。Claude は操作できないため人/PC-B の Claude が承認する)
6. 結果は [28](28_manual_verification_phase5.md) と `docs/30_phase5_review_2026-09-14.md` の第 2 弾節に記録する

## 6. 未決・注意
- PC-B の Claude Code がオフライン（スリープ・閉じた）だとメッセージは溜まるだけで実行されない。確認中は PC-B をスリープさせない（2026-09-14 確認: 電源プラン「バランス」、AC 接続時はスリープなし・バッテリー時 45 分 → **確認中は AC 電源につないでおく**）
- ビルドの受け渡しを GitHub Releases 等の外部に上げる方式は採らない（外部公開になるため）
- Unity プレイヤー(`DDriveNetCheck.exe`)の UDP 7777 受信は初回起動時にファイアウォールの許可ダイアログが出る想定(§2 参照)。ダイアログはこの会話からは操作できないため、出た旨をこのドキュメントと最終報告に明記する運用にする
- `-ddrive-sim-latency`/`-ddrive-sim-loss` は `UnityTransport.SetDebugSimulatorParameters` をリフレクション経由で呼ぶ実装(`NgoTransportConfigurator`、asmdef 変更を避けたため。[14_networking.md] §12)。UnityTransport 以外の Transport に差し替えた場合は警告 1 回で無視される
- 6-0 時点でこの PC 上のループバック(127.0.0.1)2 プロセスでの結合確認は実施済み(下記§7)。PC-B での実機確認はオーケストレーターが実施予定

## 8. 実機 2 台での確認結果（2026-09-14、PC-A Host + PC-B Client）

- ビルド受け渡し: PC-B が `http://192.168.137.1:8765/DDriveNetCheck.zip`（104 MB）を取得（serve_build のログ `192.168.137.74 - "GET /DDriveNetCheck.zip HTTP/1.1" 200`）
- PC-A Host: `DDriveNetCheck.exe -ddrive-net host -ddrive-host 192.168.137.1 -ddrive-port 7777`（ウィンドウ表示）→ `[Net/Host] DDriveRuntimeBootstrap: Host として起動しました(port=7777)`、`192.168.137.1:7777/UDP` で待ち受け、剣攻撃デモを約 3 秒ごとに Play / Signal
- **接続: 成功**。PC-B の Client（ClientId 1）が接続し、PC-B が 5 秒おきに送る偽造 Cancel を Host が `[Net/Host] Presentation: PresentationCancelMsg(HandleNetKey=…) の送信元 ClientId(1) が発行者と一致しないため破棄しました。` として**毎回破棄**（`activeCount` は変化せず）→ ホットスポット越しの Client→Host 依頼経路と、P5 レビュー第 2 弾 P1（発行者検証）の修正が実機で機能
- PC-B 側（Client、遅延なし）: zip 109,164,993 bytes を約 30 秒で取得・展開、13:12:14 起動。**SmartScreen・ファイアウォールのダイアログは出なかった**。`[Net/Client] DDriveRuntimeBootstrap: Client として起動しました(host=192.168.137.1:7777)`、`clientId=1`。**Exception / Error / Disconnect / timeout 0 件**。偽造 Cancel `forged_cancel_sent` 13 件に対し Client 側でも「破棄しました」13 件（全件破棄）
- **Late Join 復元: 成功**。Client の接続直後の heartbeat が `networkTime=159.51 activeCount=5`（Host で再生中の 5 件を受信）→ `activeCount=0` → 以降 1〜6 で推移。接続直後に 5→0 になるのは、シーク後に残り尺の無いワンショット演出が即完了したためと推測（要確認: 次回、復元直後の Presentation ごとの経過時刻をログに出すと判定しやすい）
- デバッグ表示（PC-B、起動 55 秒後）: `Role: Client (ClientId=1) / NetworkTime: 209.81 / RTT: 6 ms / Received: 74 (2.0/s)`。画面中央に VFX（白い粒子の球）、スクリーンショットは PC-B の `C:\DDriveTest\netcheck_client_0ms.png`
- PC-B 側（Client、遅延 200ms、13:15:30 起動、ClientId 2）: `[Net] NgoTransportConfigurator: シミュレータ設定を適用しました(latency=200ms, loss=0%)。` は出るが、**デバッグ表示の RTT は 6 ms のまま**（受信レートは 2.0/s → 1.0/s）。接続・Late Join（`activeCount` 5 → 0 → 1…5）・偽造 Cancel 破棄・Exception/Error/Disconnect 0 件は 0ms と同じ

### 修正版 v2（PR #30）での再確認（2026-09-14、PC-A Host v2 + PC-B Client v2）

- **遅延なし（`Player_v2.log`、13:59:14 起動、ClientId 1）**: ダイアログ無し、Exception / Error / Disconnect 0 件
  - **Late Join 直後の Placeholder: 解消**（`Unregistered AssetId` / `Placeholder` 0 件、接続直後の `activeCount` は 5 → 0 に落ちず 5〜6 を維持）
  - **Signal 中継: 確認**。Client に `signal_recv=hit` が届く（例 key=0x0058D6E6: Host `signal_fire` networkTime=172.95 / Client `signal_recv` networkTime=172.87）。同じ key の `signal_recv` が 4 行ずつ出るのは、剣攻撃デモの onHit に OnSignal トラックが 4 本（HitStop / SE / CameraShake / Haptic）あり、トラックごとに 1 行出すため（Host 側も同じく 4 行 = 仕様どおり）
  - **偽造 Cancel: 送信 14 件 = 破棄 14 件**（キーの並びも一致）。最初の 1 件だけ同じ key に 2 回送られた（狙うキーの更新前の重複、実害なし）
  - アプリ層 RTT `rtt_app_ms`: 60 サンプルで最小 1 / 最大 34 / 平均 6.4 ms。デバッグ表示 `RTT: 4 ms / App RTT: 4 ms / Received: 177 (5.0/s)`（PC-B `netcheck_v2_0ms.png`）
  - 気になる点: ①**【決定 2026-09-15、[31] A8】** Host の `signal_fire` と Client の `signal_recv` の networkTime 差が約 80 ms（RTT/2 ≒ 3 ms より大きい）。NGO の Client 側 ServerTime はティック単位のバッファで遅れて推定されるためと推測（未検証）。§4 の位相差の判定基準を「RTT/2 以内」から「数ティック以内（目安 100ms 以内）」に改めた ②起動直後（`role=off`）の heartbeat が `connected=1` と出る（未接続の表示が紛らわしい、軽微、要判断のまま） ③ログを見やすくするなら `signal_recv` にトラックの Kind を足す（軽微、要判断のまま）
- **遅延 200ms（`Player_v2_200ms.log`、14:01:28 起動、ClientId 2）**: `[Net/Client] NgoNetBridge: UnityTransport のシミュレーターは無効化されている(...)ため、アプリ層の送受信キューで遅延(200ms)を代替します。` → **`rtt_app_ms` 74 サンプルすべて 200 以上（最小 205 / 最大 228 / 平均 208）**。デバッグ表示 `RTT: 6 ms / App RTT: 208 ms`（トランスポート RTT は遅延を含まない）。Placeholder 0 件、`activeCount` 4〜6、偽造 Cancel 送信 11 = 破棄 11、Exception / Error / Disconnect 0 件
  - **⚠ 新しい実バグ: 遅延 200ms では剣攻撃デモのエフェクト（VFX）が Client の画面に一切描画されない**（PC-B のスクリーンショット 5 枚すべてで中央の粒子が 0 画素。v2 0ms と v1 200ms では描画されていた）。ログ上は `activeCount` 5〜6・`signal_recv` も届いている
  - **原因（オーケストレーターがコードで確認）**: `PresentationManager.OnReceivePlayMsg` が `elapsed = Max(0, NetworkTime - StartNetTime)`（443 行付近）でシーク開始し、`FireInitialTracks` が `elapsed > 0` なら連続系でないワンショット（Time=0 の VFX / SE）を**すべてスキップ**する（1086 行付近）。遅延 0ms では Client の ServerTime が約 80 ms 遅れて推定されるため差が負 → 0 にクランプされて発火していたが、遅延があると差が正になり、**開始直後のワンショット演出がリモートでは必ず見えない**。v1 の 200ms はシミュレーターが no-op で実際には遅延が無かったため顕在化しなかった
  - 修正方針: 「少し遅れて届いただけ」のワンショットは遅れて発火する猶予（例 0.5 秒）を設け、それより古いもの（Late Join 等）だけスキップする
    → **2026-09-14 修正(6-0 修正6)**。`PresentationManager` に猶予(既定 0.5 秒)を追加し、`lateBySec`(= `elapsed - track.Time`)が猶予以内ならワンショットを遅れて発火させ、それを超える場合だけ従来どおりスキップするようにした。判定用ログ(`track_fired`/`track_skipped`)も追加した。詳細は [14_networking.md](14_networking.md) の「実装メモ（2026-09-14、6-0 修正6）」、回帰テストは `PresentationNetDeviceFixTests.RemoteOneShotVfx_WithinGrace_FiresLate_OnBothPeers`/`RemoteOneShotVfx_ExceedsGrace_IsSkipped_AndRaisesOnRemoteOneShotSkipped`、`PresentationLateJoinTests.LateJoin_LoopingVfx_IsRestored_ButOldOneShotMarker_IsNotFired_...`。ローカル結合確認は本ドキュメント §9 の「ラウンド3」に追記。
- **切断: 成功**（オーケストレーターが PC-B で再確認。PC-A の Host v2 を 14:04:34 に停止 → PC-B の Client に `[Net/Client] NgoNetBridge: Host から切断されました(reason=...[ProtocolTimeout] Connection closed due to timed out.)` と `[DDriveNetCheck] disconnected=1 role=client reason=...` が 1 回ずつ、以後 `connected=0`、Exception/Error/再接続 0 件。課題5(切断検知)は実機でも解消を再確認できた）
  - この再確認で追加で見つかった課題 4 件（オーケストレーターからの追加指示）は全て 6-0 修正6 として同じ PR で対応した。詳細は [14_networking.md](14_networking.md) の同節を参照:
    1. 切断後も `rtt_app_ms` が最後の値(212)を表示し続ける → `AppRoundTripMs` を切断時にリセット
    2. 切断後も 5 秒おきに偽造 Cancel を送ろうとして「未接続のため送信できません」警告が出続ける → 切断中は送らない
    3. **切断直後に Client の画面へ粒子エフェクトが薄く出る**(接続中の 200ms では出ていなかった VFX が、切断の瞬間だけ描画された)。原因はアプリ層遅延キューに残っていた `PlayMsg` が、切断で `NetworkTime` が 0 に巻き戻った状態で処理され `elapsed=0` と誤認されたこと → `NgoNetBridge` に切断時にキューを破棄する `CancellationTokenSource` を追加
    4. デバッグ表示に接続状態(「接続中/切断」)の行が無い → `NetDebugOverlay` に追加

### 修正版 v3（PR #31、遅れて届いたワンショットの猶予 0.5 秒）での再確認（2026-09-14 14:47〜、PC-A Host v3 + PC-B Client v3）

- **遅延 200ms（`Player_v3_200ms.log`、ClientId 1）: エフェクト描画の実バグは解消**
  - `track_fired kind=Vfx` 26 件（Se と 1 対 1）、`late_ms` 最小 107 / 最大 158 / 平均 151（3 件目以降 151〜158 で安定）。例 `track_fired=1 kind=Vfx time=0.00 key=0x00E7D818 late_ms=107 networkTime=175.37`
  - **画面**: PC-B のスクリーンショット 5 枚すべてで中央の粒子エフェクトを確認（白画素 76〜82。v2 200ms は 0、v2 0ms は 69）
  - `track_skipped` 10 件（Vfx / Se × 5 key）はすべて接続直後の Late Join で受け取った 2〜14 秒前の古い演出（`late_ms` 2069〜14072）だけ。以後増えない = 猶予の設計どおり
  - `rtt_app_ms` 100 サンプル: 最小 203 / 最大 230 / 平均 208.3（100/100 が 200 以上）。偽造 Cancel 送信 9 = 破棄 9（並び一致、v2 の「最初の key を 2 回送る」は解消）。Unregistered AssetId / Placeholder / Exception / Error / Disconnect 0 件。未接続時（`role=off`）の heartbeat は `connected=0`（v2 の指摘は解消）
  - デバッグ表示: `Role: Client (ClientId=1) / State: 接続中 / NetworkTime: 231.38 / RTT: 5 ms / App RTT: 206 ms / Received: 197 (4.0/s)`（PC-B `netcheck_v3_200ms.png` ほか burst 4 枚）
  - `signal_recv` の同一 key 4 行は onHit の OnSignal トラック 4 本分（仕様）
- **切断（PC-A の Host v3 を 14:49:51 に停止）: ログは期待どおり**。`[Net/Client] NgoNetBridge: Host から切断されました(...ProtocolTimeout...)` と `disconnected=1` が 1 回ずつ。切断後の `track_fired` / `track_skipped` / `forged_cancel_sent` / 「未接続のため送信できません」/ `signal_recv` はすべて 0 件、heartbeat は `connected=0 rtt_app_ms=n/a`（v2 の残留は解消）、デバッグ表示 `State: 切断`。Exception / Error 0 件、再接続の試行なし
  - **⚠ 新しい実バグ: 切断後も粒子エフェクト（VFX）が消えずに描画・アニメーションし続ける**（PC-B のスクリーンショット 4 枚、切断から約 40 秒後も白画素 87〜100 で変動。接続中より密）。`activeCount` は 0 なので Presentation の後片付けは済んでいるが、そこから Spawn した VFX インスタンスが Stop / Despawn されずに残っている疑い（VFX がループ系の場合に顕在化）。切断時にアクティブなネット演出を Cancel 相当（`StopOnCancel` に従って VFX を止める）で終了させる修正が必要。判定用に heartbeat へ `vfx_active=<VfxManager の生存数>` を足すと、スクリーンショットに頼らず確認できる
    → **修正（6-0 修正7、PR #32）**。原因は 2 点: (1) `NgoNetBridge.ClientDisconnected` を購読して実際に演出を
    止めるコードが無かった(2) `PRES_Demo_SkillSlash` の Vfx トラックが `StopOnCancel=false` のままで、かつ
    参照先 `VFX_Player_Slash` の実体(`vfx_sample.prefab`)の `ParticleSystem` が `looping=true` のため
    `LifeMode=OneShot` でも自然終了しない。`PresentationManager.CancelAllNetworked()` を追加し、
    `DDriveRuntimeBootstrap` が Client 視点の切断時にだけ呼ぶようにした上で、デモの Vfx トラックを
    `StopOnCancel=true` に修正した。詳細は [14_networking.md](14_networking.md) の「実装メモ(6-0 修正7)」、
    ローカル結合確認は本ドキュメント §11。**スコープの限界**: この修正は「切断時点でまだアクティブな
    (`TotalDuration` 未満の)演出」だけを対象にする。すでに `Complete()` して台帳から外れていた演出の
    VFX は対象外(`Complete()` 自体が Fired 済みの Vfx/Se を止めない設計のため、切断固有ではない広い論点。
    [docs/31](31_phase5_decisions.md) に残した)

### 実機確認で見つかった課題（2026-09-14、修正チケットへ）

1. **遅延シミュレーターが効いていない疑い**: `-ddrive-sim-latency 200` でも RTT が 6 ms。`NgoTransportConfigurator` が `SetDebugSimulatorParameters` を `StartClient`/`StartHost` の後（ドライバ生成後）に呼んでいる、または UnityTransport 2.x で当該 API が無効、の可能性。RTT の値（`GetCurrentRtt`）がシミュレーター遅延を含まない可能性もあるので、アプリ層の往復時間（Ping の往復）も併記して判定できるようにする
   → **2026-09-14 修正**: 原因を特定した。呼び出し順序の問題ではなく、`Library/PackageCache/com.unity.netcode.gameobjects@.../Runtime/Transports/UTP/UnityTransport.cs` の `SetDebugSimulatorParameters`/`DebugSimulator` が `[Obsolete("... is no longer supported and has no effect. Use Network Simulator from the Multiplayer Tools package.")]` であり、`DebugSimulator` フィールドはドライバ生成時に一切参照されない(呼んでも何も起きない、真の no-op)。`NgoNetBridge` にアプリ層の送受信キュー遅延(`ConfigureAppLayerSimLatency`、開発ビルド+本引数指定時のみ)を実装して代替した。あわせて `NgoNetBridge` が Ping/Pong でアプリ層の往復時間を計測し(`AppRoundTripMs`)、`NetDebugOverlay` に「App RTT」として併記、`NetCheckRunner` の heartbeat に `rtt_app_ms` を追加した(トランスポート RTT に依存せず判定できるように)。
2. **Client 側で Signal 中継を観測できない（計測の穴）**: `NetCheckRunner` の `signal=hit` は Host が `handle.Signal("hit")` を呼んだ直後にだけ出す実装（Host 179 件 / Client 0 件）。§4 の「全ピア」は誤り。Client で OnSignal トラックがネット経由で発火したことをログに出す仕組みが無く、**Signal 中継は実機で未検証**
   → **2026-09-14 修正**: `PresentationManager` に「ネット受信で新規生成された Instance」を通知する開発用イベント `OnNetworkReceivedPlay` を追加し(定常経路では未使用のため 0 alloc)、`NetCheckRunner` が各ピアで `OnTrackFired` を購読して実際に OnSignal が発火した瞬間に `signal_recv=hit key=<HandleNetKey> networkTime=...` を出すようにした。Host 側も(PredictLocal でも自分の Broadcast が返ってくるまで実際には発火しないため)意図表明の `signal_fire` と実発火の `signal_recv` を両方出すようにし、§4 の「全ピア」表記を修正した。
3. **Late Join 直後に Presentation が Placeholder で解決される**: Client に `[DDrive] Unregistered AssetId 0xCD2986D134D20E66 resolved to Placeholder.` が 1 件（= `PRES_Demo_SkillSlash` 自身、Host 側には無し）。接続直後のスナップショット受信がカタログのロード完了より先に処理される順序の問題と推測。接続直後の `activeCount` 5 → 0 もこれが原因の可能性
   → **2026-09-14 修正**: 推測どおりだった。`PresentationManager.SetRegistryReady(bool)` を追加し、`DDriveRuntimeBootstrap` が構築直後に `false`、`RegisterCatalogsAsync()` 完了後に `true` を呼ぶようにした。`false` の間に受信した `PresentationPlayMsg`/`PresentationSignalMsg`/`PresentationCancelMsg` は到着順にキューへ保留し、`true` になった時点でまとめて処理する(ローカルの `Play()` API 呼び出しは影響を受けない)。回帰テスト `PresentationNetDeviceFixTests.OnReceivePlayMsg_BeforeRegistryReady_IsQueued_AndFlushedWithoutPlaceholder_AfterReady` を追加。`activeCount` 5→0 がこれで直るかは PC-B での再確認待ち(ローカル結合確認(§7)では再現しない条件だったため未確認、要判断として残す)。
4. **偽造 Cancel の破棄ログが 1 件欠落**（0ms、35 送信 / 34 破棄、`HandleNetKey 0x01047DA3` は Host ログに出現なし）: 対象の演出が完了済みで未知キーとして黙って破棄された可能性。未知キーの破棄も判定できるようログを出す（開発ビルドのみ）
   → **2026-09-14 修正**: `PresentationManager.OnReceiveSignalMsg`/`OnReceiveCancelMsg` が、`HandleNetKey` が発行者検証を通過した後も台帳に見つからない(未知、または対象の演出が既に完了して台帳から外れた)場合に `[Net/Host]`/`[Net/Client]` で「未知のキー、または対象の演出が既に完了しているため破棄しました」を開発ビルドでキーごとに 1 回出すようにした(`#if DEVELOPMENT_BUILD || UNITY_EDITOR`)。`NetCheckRunner.SendForgedCancel` も、可能なら「実在するが自分が発行していない」キーを狙うよう改修した(発行者不一致の経路を安定して踏ませつつ、対象が無ければ従来どおり完全ランダムにフォールバックして未知キー経路も踏む)。回帰テスト `PresentationNetDeviceFixTests.UnknownHandleNetKey_Cancel/Signal_LogsDiscardWarning` を追加。
5. **Host を停止しても Client が切断を検知しない**（オーケストレーターが PC-B の再実行で発見した追加課題）: 接続中の最後の heartbeat の次が `clientId=0 networkTime=0.00` に戻り、`[Net/Client]` の切断通知・Exception が 0 件のまま heartbeat 92 件が続いた。
   → **2026-09-14 修正**: `NgoNetBridge` が `NetworkManager.OnClientDisconnectCallback`/`OnTransportFailure` を購読し、`[Net/Host] Client <id> が切断しました(reason=...)`/`[Net/Client] Host から切断されました(reason=...)` をログに出す(`NetworkManager.DisconnectReason` を含む)。`ClientDisconnected` イベント(`(ulong clientId, string reason)`)を新設し、`NetCheckRunner` の heartbeat に `connected=0|1` を追加、切断時に `disconnected=1 role=... reason=...` を 1 回出す。自動再接続は MS2026 の規約に無いため実装しない(要判断: 将来必要になれば追加)。切断後の Presentation 側台帳(`_networkedHandles`/`_activeNetworked`)は、進行中の Cosmetic 演出が通常の Elapsed/Duration 経由で Complete/Cancel されるのに任せる設計のままにした(相手の接続状態に関わらず一定時間で自然に台帳から外れるため、切断によって新たに残留エントリが生じるわけではないと判断。専用のクリーンアップは追加していない)。
- 遅延 200ms（PC-B の Client を `-ddrive-sim-latency 200` で再起動、ログ `C:\DDriveTest\Player_200ms.log`）: **Host 側で再接続を確認**（新しい Client = ClientId 2 からの偽造 Cancel を 7 件破棄、Host は応答継続、`networkTime=383.73 activeCount=5`）。Host 側の破棄件数の合計は ClientId 1 = 34 件 / ClientId 2 = 7 件。PC-B 側の RTT・Signal 件数・位相は報告待ち（追記予定）

## 7. ローカル(このPC・ループバック)での結合確認結果

このセクションは 6-0 実装時にローカルで行った 2 プロセス(Host/Client、127.0.0.1)結合確認の結果を記録する(実機確認とは別。§5 のログ判定基準が実際に機能することを事前に確認する位置づけ)。

**手順**: `Tools > D-Drive > Build > 実機確認用 Windows 開発ビルド`(`NetCheckBuilder.Build()`)で
`Builds/DDriveNetCheck/DDriveNetCheck.exe` を作成 → 2 プロセスを `-batchmode -nographics` + 別々の `-logFile`
で起動(GUI 無しでログだけ確認。ファイアウォールの許可ダイアログはループバック(127.0.0.1)通信のため出なかった)。

```
DDriveNetCheck.exe -ddrive-net host   -ddrive-host 127.0.0.1 -ddrive-port 7777 -ddrive-autotest local4 -batchmode -nographics -logFile host.log
DDriveNetCheck.exe -ddrive-net client -ddrive-host 127.0.0.1 -ddrive-port 7777 -ddrive-autotest local4 -batchmode -nographics -logFile client.log
```

**結果: 成功**(3 回目の試行で成功。最初の 2 回で見つかった実バグを都度修正した。詳細は下記)。ログ抜粋:

```
[host.log]
[Net/Host] DDriveRuntimeBootstrap: Host として起動しました(port=7777)。
[DDriveNetCheck] play=1 startNetTime=3.23
[DDriveNetCheck] heartbeat=1 role=host clientId=0 networkTime=3.26 activeCount=1
[DDriveNetCheck] signal=hit
[Net/Host] Presentation: PresentationCancelMsg(HandleNetKey=0x399BC498) の送信元 ClientId(1) が発行者と一致しないため破棄しました。
...(play=2,3 も同様、activeCount が 1→2→3 と増える)

[client.log]
[Net/Client] DDriveRuntimeBootstrap: Client として起動しました(host=127.0.0.1:7777)。
[DDriveNetCheck] ready=1 role=client
[DDriveNetCheck] heartbeat=1 role=client clientId=1 networkTime=1.86 activeCount=0
[DDriveNetCheck] heartbeat=1 role=client clientId=1 networkTime=3.15 activeCount=1   ← Host の play=1(startNetTime=3.23)を受信して 1 に
[DDriveNetCheck] forged_cancel_sent=966509720
[Net/Client] Presentation: PresentationCancelMsg(HandleNetKey=0x399BC498) の送信元 ClientId(1) が発行者と一致しないため破棄しました。
...(activeCount が 1→2→3 と Host と同じタイミングで増える)
```

確認できたこと:
- Host/Client が UnityTransport(UDP)で実際に接続する(`clientId=1` が割り当てられる)
- Host の `play`(`PRES_Demo_SkillSlash`)が Client 側に伝わり `activeCount` が同じタイミングで増える(位相同期)
- Client が送った偽造 `PresentationCancelMsg`(存在しない `HandleNetKey`)が **Host 側・Client 側の両方**で
  「送信元 ClientId(1) が発行者と一致しないため破棄しました」と正しく破棄される(P1-1/P1-2 の修正が実際の
  NGO 通信で機能することを確認。ユニットテストだけでなく実プロセス間通信でも確認できた)
- Signal("hit") の中継(`signal=hit` が両ログに出る)

**この確認で見つかり、修正した実バグ 2 件**(いずれもユニットテスト(Fake/Delayed ブリッジ)では検出できず、実プロセスでの確認で初めて見つかった。ユニットテストだけに頼らずローカル結合確認をした価値が実際にあった):

1. **`NetworkManager.StartHost()`/`StartClient()` を `Awake()` から直接呼ぶと `NullReferenceException`**:
   `DDriveRuntimeBootstrap`(`DefaultExecutionOrder(-1000)`)が他の全 `Awake()` より先に走るため、
   `NetworkManager` 自身の内部初期化(`Awake()`/`OnEnable()`)が済む前に `StartHost()` を呼んでいた。
   → `DDriveRuntimeBootstrap.Start()`(全オブジェクトの `Awake()` が完了した後に呼ばれることが保証される)
   まで `StartHost()`/`StartClient()` の呼び出しを遅延させるよう修正した(`ResolveNetBridge()` は役割の
   決定と Transport 設定だけを行い、実際の起動は `StartNetworkingIfPending()` が `Start()` から行う)。
2. **`NetworkManager` と `NetworkObject` を同じ GameObject に置くと NGO が警告し機能しない**: 最初に
   作成した `NetCheckScene` は `NetworkManager` オブジェクトに `NetworkObject`+`NgoNetBridge` も直接
   付けていたが、NGO は「NetworkManager 自身は NetworkObject になれない」ため無効だった。→ `NgoNetBridge`
   用に別の GameObject(`NgoBridge`)を作り、そちらに `NetworkObject`+`NgoNetBridge`+`NetBridgeSmokeTest`
   を移した。
3. **(バグではないが判明した設定ミス)** ループバック確認では Host 側にも明示的に `-ddrive-host 127.0.0.1`
   を渡す必要がある(既定値 `192.168.137.1` のままだと UnityTransport がその IP で listen しようとし、
   このマシンにその IP が付いていない状況では 127.0.0.1 からの接続を受け付けられない)。実機確認(PC-A が
   実際に `192.168.137.1` を持つ)ではこの問題は起きない。

**この確認で見つかった、意図的に修正した既存アセットの変更**: `Assets/GameData/Presentation/Demo/PRES_Demo_SkillSlash.asset`
の `Flags.Net` を `Local`→`Cosmetic`、`PredictLocal` を `false`→`true` に変更した(Unity Editor 経由、
`Undo.RecordObject`+`SetDirty`+`SaveAssets`)。5-8 実装メモの要判断(「このデモアセットは NetMode=Local の
ままなのでネット経路を通すにはデモ側の変更が必要」)を解消する、当初から想定されていた変更である。
`PredictLocal=true` にしたことで、既存の `PresentationSkillSlashDemo.cs`(`PresentationSkillSlashPreviewScene`)
から見た挙動(`Play()` が即座に有効な Handle を返す)は変わらない(Loopback 経由でも予測再生と確定エコーが
同一フレームで一致するため)。

`Builds/`(ビルド出力・ログ)は `.gitignore` 済みのため、このセクションのログ抜粋以外はコミットしていない。

## 9. 修正版のローカル結合確認結果（2026-09-14、実機確認で見つかった課題1〜5の修正後）

`NetCheckBuilder.Build()` で再ビルド(`Builds/DDriveNetCheck/DDriveNetCheck.exe`、development build、zip
`Builds/DDriveNetCheck.zip` 109,171,666 bytes ≒ 104 MB)し、このPC上でループバック(127.0.0.1)2 プロセスを
2 ラウンド実行して確認した(§7 と同じ `-batchmode -nographics -ddrive-autotest`、`-ddrive-host 127.0.0.1`
を両方に明示)。

**ラウンド1(0ms、`host_fix1.log`/`client_fix1.log`)**:

```
[host_fix1.log]
[DDriveNetCheck] play=1 startNetTime=3.18
[DDriveNetCheck] heartbeat=1 role=host clientId=0 networkTime=3.21 activeCount=1 connected=1 rtt_app_ms=n/a
[DDriveNetCheck] signal_recv=hit key=0x008ED8B7 networkTime=3.68   ← 4 回(デモに OnSignal("hit") トラックが 4 本あるため。正常)
[DDriveNetCheck] signal_fire=hit key=0x008ED8B7 networkTime=3.68
[Net/Host] Presentation: PresentationCancelMsg(HandleNetKey=0x008ED8B7) の送信元 ClientId(1) が発行者と一致しないため破棄しました。

[client_fix1.log]
[DDriveNetCheck] heartbeat=1 role=client clientId=1 networkTime=3.10 activeCount=1 connected=1 rtt_app_ms=10
[DDriveNetCheck] signal_recv=hit key=0x008ED8B7 networkTime=3.59   ← Host の signal_fire(3.68)とほぼ同時刻(位相差 <0.1s、ループバックのため妥当)
[DDriveNetCheck] forged_cancel_sent=9361591   ← 0x008ED8B7 の 10 進数(実在する Host のキーを狙い撃ち)
[Net/Client] Presentation: PresentationCancelMsg(HandleNetKey=0x008ED8B7) の送信元 ClientId(1) が発行者と一致しないため破棄しました。
[DDriveNetCheck] disconnected=1 role=client reason=[Disconnect Event][Client-1][TransportClientId-4294967296][ClosedByRemote] Connection was closed by remote endpoint.
[DDriveNetCheck] heartbeat=1 role=client clientId=0 networkTime=0.00 activeCount=3 connected=0 rtt_app_ms=1
```

確認できたこと: ①`signal_fire`(Host)と`signal_recv`(Host自身+Client)が両方出て位相差が小さい(課題2解消)
②`rtt_app_ms` が実測 0〜10ms 台(ループバックなので妥当。ここでは未設定なので課題1の直接確認は次のラウンド)
③偽造 Cancel(既に稼働中の実在キーを狙い撃ち)が発行者不一致として Host/Client 双方で正しく破棄される
④**Host の autotest 終了(11秒後)で Client が切断を検知**(`disconnected=1` + `[Net/Client] NgoNetBridge: Host
から切断されました`。課題5解消、実際に発生した切断イベントで確認できた)。Placeholder 警告・Exception は
0 件(課題3は再現条件が異なる(後述)ため直接確認はできず)。

**ラウンド2(Client のみ `-ddrive-sim-latency 200`、`host_fix2_200ms.log`/`client_fix2_200ms.log`)**:

```
[client_fix2_200ms.log]
[Net] NgoTransportConfigurator: シミュレータ設定を適用しました(latency=200ms, loss=0%)。   ← 相変わらず出るが no-op(実効果なし)
[Net/Client] NgoNetBridge: UnityTransport のシミュレーターは無効化されている(SetDebugSimulatorParameters が Obsolete/no-op)ため、アプリ層の送受信キューで遅延(200ms)を代替します。
[DDriveNetCheck] heartbeat=1 role=client clientId=1 networkTime=3.30 activeCount=1 connected=1 rtt_app_ms=211
[DDriveNetCheck] heartbeat=1 role=client clientId=1 networkTime=4.31 activeCount=1 connected=1 rtt_app_ms=202
[DDriveNetCheck] heartbeat=1 role=client clientId=1 networkTime=5.32 activeCount=1 connected=1 rtt_app_ms=202
[DDriveNetCheck] disconnected=1 role=client reason=[Disconnect Event]...ClosedByRemote...
```

**課題1が直接確認できた**: `rtt_app_ms` が 0ms 台(ラウンド1)→ 202〜211ms(`-ddrive-sim-latency 200`)に明確に増加した。
今回のケースでは Client→Host の Ping 送信は Client 側の送信キューを経由しない経路(`RequestBroadcastRpc` を
直接呼ぶ)だったため、実測は設定値とほぼ 1:1 になった([14_networking.md] 実装メモに詳細と、Host 側にも
遅延を設定した場合や中継経路によっては倍数になり得るという要判断を記載)。Placeholder・Exception は
0 件、偽造 Cancel の破棄・切断検知(課題5)もラウンド1と同様に確認できた。

**課題3(Late Join 直後の Placeholder)がローカル結合確認では再現しない理由(要判断)**: Editor でビルドした
このマシンのローカル Addressables カタログはロードがほぼ瞬時に終わるため、`DDriveRuntimeBootstrap.
RegisterCatalogsAsync()` の完了と NGO の接続確立(`StartClient()`)の間に実機ほどの遅延窓が生まれず、
競合状態を再現できなかった。回帰テスト(`PresentationNetDeviceFixTests.
OnReceivePlayMsg_BeforeRegistryReady_IsQueued_AndFlushedWithoutPlaceholder_AfterReady`)で修正自体は
確認済みだが、実機(PC-B、モバイルホットスポット経由でネットワークが実機より低速)での再確認をオーケスト
レーターに依頼する。

起動した 4 プロセス(ラウンド1のホスト/クライアント、ラウンド2のホスト/クライアント)は各ラウンドの
`-ddrive-autotest` によりすべて自動終了した(確認後、`tasklist` で `DDriveNetCheck.exe` が残っていないことを
確認済み)。

## 10. 6-0 修正6 のローカル結合確認結果(2026-09-14)

`NetCheckBuilder.Build()` で再ビルド(`Builds/DDriveNetCheck.zip`、109,173,079 bytes)し、このPC上で
ループバック(127.0.0.1)2 プロセスを 4 ラウンド実行して確認した(`-batchmode -nographics`、
`-ddrive-host 127.0.0.1` を両方に明示)。

**ラウンド1(0ms、`host_grace_r2.log`/`client_grace_r2.log`)**:

```
[client_grace_r2.log]
[DDriveNetCheck] track_fired=1 kind=Vfx time=0.00 key=0x00AEA992 late_ms=0 networkTime=3.09
[DDriveNetCheck] track_fired=1 kind=Se time=0.00 key=0x00AEA992 late_ms=0 networkTime=3.09
```

**ラウンド2(Client のみ `-ddrive-sim-latency 200`、`host_grace_r3_200ms.log`/`client_grace_r3_200ms.log`)** —
6-0 修正6 の本題(修正版 v2 で見つかった実バグ)の直接確認:

```
[client_grace_r3_200ms.log]
[DDriveNetCheck] track_fired=1 kind=Vfx time=0.00 key=0x0059C254 late_ms=138 networkTime=3.30
[DDriveNetCheck] track_fired=1 kind=Se time=0.00 key=0x0059C254 late_ms=138 networkTime=3.30
[DDriveNetCheck] track_fired=1 kind=Vfx time=0.00 key=0x00E6D9CE late_ms=145 networkTime=6.31
[DDriveNetCheck] track_fired=1 kind=Se time=0.00 key=0x00E6D9CE late_ms=145 networkTime=6.31

[host_grace_r3_200ms.log]
[DDriveNetCheck] track_fired=1 kind=Vfx time=0.00 key=0x0059C254 late_ms=0 networkTime=3.16
[DDriveNetCheck] track_fired=1 kind=Se time=0.00 key=0x0059C254 late_ms=0 networkTime=3.16
```

**修正版で `late_ms≒140` の遅れで確実に発火する**(修正前は 200ms 級の遅延で一切発火しなかった実バグが
解消)。`late_ms` が厳密に 200 ではなく 138〜145 なのは、[14_networking.md] 実装メモに記載のとおり
Client→Host のアプリ層遅延経路(送信キュー/受信キューのどちらを経由するか)による(§4 判定基準の
`rtt_app_ms` は同じラウンドで 200 台を計測済み、docs 未転記だが実測は別途 §4 の記述と整合)。
Exception/Error は 0 件。

**ラウンド3(猶予超え、Client `-ddrive-sim-latency 700`、`client_grace_r4_700ms.log`)** — 猶予(0.5秒 = 500ms)
を超えた場合に正しくスキップされることの確認:

```
[client_grace_r4_700ms.log]
[DDriveNetCheck] track_skipped=1 kind=Vfx time=0.00 key=0x00E7B834 late_ms=636
[DDriveNetCheck] track_skipped=1 kind=Se time=0.00 key=0x00E7B834 late_ms=636
```

`track_fired` は 0 件、`track_skipped` のみ(`late_ms=636`>500)。猶予の境界判定が意図どおり機能している。

**ラウンド4(切断、Client `-ddrive-sim-latency 200` 常駐 + Host 通常終了、`host_disc_r7.log`/
`client_disc_r7.log`)** — オーケストレーター追加指示(1)〜(3)の直接確認。Host を `-ddrive-autotest` で
自動終了させ(グレースフルシャットダウン)、Client は `-ddrive-autotest` を付けずに常駐させて切断後も
長時間観測した(`Stop-Process` で最後に停止):

```
[client_disc_r7.log]
[DDriveNetCheck] heartbeat=1 role=client clientId=1 networkTime=11.33 activeCount=3 connected=1 rtt_app_ms=200
[DDriveNetCheck] disconnected=1 role=client reason=[Disconnect Event][Client-1][TransportClientId-4294967296][ClosedByRemote] Connection was closed by remote endpoint.
[DDriveNetCheck] heartbeat=1 role=client clientId=0 networkTime=0.00 activeCount=3 connected=0 rtt_app_ms=n/a
  … (以後 14 回以上 connected=0 rtt_app_ms=n/a が続く。切断後 14 秒以上観測)
```

確認できたこと:
- **(1) 解消**: 切断後は `rtt_app_ms` が最後の値(200)を表示し続けず、直後から一貫して `n/a`
- **(2) 解消**: 切断後 14 秒以上(forgedMessageIntervalSeconds=5s を 2 回以上跨ぐ時間)観測しても
  `forged_cancel_sent` は 1 件も出ず、`未接続のため送信できません` 警告もログ全体で 0 件
  (`grep -c` で確認)
- **(3) 解消**: 切断前後を通じて `track_fired`/`track_skipped` は切断直前(networkTime=9.33)が最後で、
  切断後は 1 件も出ない(=切断で NetworkTime が巻き戻った状態で古い PlayMsg が処理される実バグは
  再発していない)。Exception/Error は全体で 0 件
- 参考: `Stop-Process -Force`(強制終了、グレースフルシャットダウンなし)で Host を落とした別ラウンドでは、
  Client 側の固定実行時間(約 8 秒)内に切断検知(ProtocolTimeout、実機確認と同種)に至らなかった。
  グレースフルシャットダウン(`Application.Quit()`)は `ClosedByRemote` として即座に検知される一方、
  強制終了はタイムアウト検知のため数秒〜数十秒かかる(実機確認 v1/v2 で観測した `[ProtocolTimeout]` と
  同じ性質)。この非対称性自体は既知の NGO の挙動であり、6-0 修正6 のスコープ外として扱う

起動した全プロセスは確認後に `tasklist`/`Stop-Process` で残っていないことを確認済み。

## 11. 6-0 修正7 のローカル結合確認結果(2026-09-14)

`NetCheckBuilder.Build()` で再ビルド(`Builds/DDriveNetCheck.zip`、109,173,145 bytes)し、このPC上で
ループバック(127.0.0.1)2 プロセスを 3 ラウンド実行して確認した(`-batchmode -nographics`、
`-ddrive-host 127.0.0.1`)。

**ラウンド1(強制終了、`host_v7.log`/`client_v7.log`、ポート 7777)** — 「切断より前に自然完了した演出の
VFX は対象外」という修正のスコープの限界を確認する意図せぬ収穫:

```
[client_v7.log]
[DDriveNetCheck] heartbeat=1 role=client clientId=1 networkTime=21.34 activeCount=5 connected=1 rtt_app_ms=204 vfx_active=2
...(Host が 3 秒おきに Play を続け、VFX が Complete() で放置されたまま溜まり続ける)...
[DDriveNetCheck] heartbeat=1 role=client clientId=1 networkTime=74.26 activeCount=0 connected=1 rtt_app_ms=200 vfx_active=13
[DDriveNetCheck] disconnected=1 role=client reason=...[ProtocolTimeout] Connection closed due to timed out.
[DDriveNetCheck] heartbeat=1 role=client clientId=0 networkTime=0.00 activeCount=0 connected=0 rtt_app_ms=n/a vfx_active=13
```

`Stop-Process -Force` で Host を強制終了すると `ProtocolTimeout` の検知に 40 秒以上かかった(既知の非対称性、
docs §10 ラウンド4参照)。その間に Host が送り続けた Play で `vfx_active` が最大 13 まで蓄積し、`activeCount`
は(各演出が `TotalDuration`=15 秒で自然完了するため)0 まで下がった。**disconnected=1 の時点では対象の
演出がすべて `Complete()` 済みで `_active` から外れていたため、`CancelAllNetworked()` が何も見つけられず
`vfx_active` は 13 のまま変化しなかった**。これは実装メモに書いた「スコープの限界」どおりの挙動であり、
今回のバグ修正が対象にしていない既知の別論点(通常完了時に Fired 済み VFX を止めない設計、[docs/31](31_phase5_decisions.md))が単独で顕在化したもの。

**ラウンド2(`-ddrive-autotest`、`host_v7c.log`/`client_v7c.log`、ポート 7779)** — 切断時点でまだアクティブな
演出がある状態を狙って確認(Client を先に起動して Host の初回 Play(3 秒後)より前に接続を完了させた):

```
[client_v7c.log]
[DDriveNetCheck] heartbeat=1 role=client clientId=1 networkTime=3.32 activeCount=1 connected=1 rtt_app_ms=203 vfx_active=1
[DDriveNetCheck] heartbeat=1 role=client clientId=1 networkTime=6.32 activeCount=2 connected=1 rtt_app_ms=202 vfx_active=2
[DDriveNetCheck] heartbeat=1 role=client clientId=1 networkTime=9.32 activeCount=3 connected=1 rtt_app_ms=200 vfx_active=3
[DDriveNetCheck] heartbeat=1 role=client clientId=1 networkTime=11.32 activeCount=3 connected=1 rtt_app_ms=200 vfx_active=3
[DDriveNetCheck] disconnected=1 role=client reason=...[ClosedByRemote] Connection was closed by remote endpoint.
[DDriveNetCheck] heartbeat=1 role=client clientId=1 networkTime=11.40 activeCount=0 connected=0 rtt_app_ms=n/a vfx_active=0
```

**修正の確認: 成功**。Host の `-ddrive-autotest`(グレースフルシャットダウン、`Application.Quit()`)により
`disconnected=1` が即座に検知され(`ClosedByRemote`、強制終了時の `ProtocolTimeout` と異なり数十ms〜数百ms
で検知)、切断直前は `activeCount=3 vfx_active=3`(3 件ともまだ `TotalDuration`(15秒)未満でアクティブ、
それぞれ VFX が 1 個ずつ再生中)だったのが、**同じ heartbeat の直後(0.08 秒後)に `activeCount=0
vfx_active=0` へ落ちた**。`CancelAllNetworked()` が切断時点でまだアクティブな 3 件のネット経由 Presentation
をすべて強制終了し、`StopOnCancel=true` にした Vfx トラックの Fired VFX を止めたことを確認できた
(`VFX_Player_Slash` の `FadeOutSec=0` のため即時 0 になる)。Exception / Error は両ラウンドとも 0 件。

起動した全プロセス(host/client、各ラウンド)は確認後に `Stop-Process -Force` で終了し、残っていないことを
確認済み。

## 12. v4 実機確認（2026-09-15、A10 修正後。PC-A Host v4 + PC-B Client v4）

**対象ビルド**: `NetCheckBuilder.Build()`（2026-09-14 23:59、`Builds/DDriveNetCheck.zip` 109,217,804 bytes）。
P5 完了時点の main（A10 = `vfx_sample.prefab` の `looping=false`、6-0 修正6/7、Presentation エディタ改修を含む）。
PC-B は hotspot 経由で zip を取得（約 19 秒、サイズ一致）。

**ラウンド A（遅延 0ms、`Player_v4.log`）: 合格**
- `[Net/Client] DDriveRuntimeBootstrap: Client として起動しました(host=192.168.137.1:7777)`、clientId=1。Exception / Error / NullReference 0 件
- **Late Join**: Placeholder / Unregistered AssetId 0 件。接続直後の `track_skipped` 10 件は Late Join 時点で既に古い演出（late_ms 2523〜14524）の Vfx/Se のみ（仕様どおり）
- **偽造 Cancel**: Client 送信 19 件 = 破棄 19 件（HandleNetKey の並びも完全一致）。Host 側合計 35 件破棄
- **VFX の蓄積（A10 の確認）**: vfx_active は Client で 3〜4、Host で 3〜4（126 秒観察）で頭打ち。v3 の強制終了ラウンドでは 13 まで蓄積していた。
  定常 3〜4 は VFX の寿命（放出 5 秒 + 粒子寿命）と 3 秒周期の重なりによるもので、溜まり続けない（合格）。見た目は v3 までの「密な球」から、ワンショット放出の「まばらに散って消える粒子」に変わった（ループ解除の結果）
- RTT 3〜4ms（App RTT 平均 3.8ms、最大 17ms）、Received 2.0/s

**切断（Host をウィンドウを閉じて正常終了）: 合格**
- `[Net/Client] NgoNetBridge: Host から切断されました(reason=...[ClosedByRemote]...)` → `disconnected=1`。切断前最後の heartbeat から 0.1 秒で検知
- 切断直後の最初の heartbeat で `activeCount 5→0`、`vfx_active 3→0`。切断後の画面に VFX は残らない（地平線下の白画素 0、3 枚）。**v3 の「切断後に VFX が残る」は解消**
- 切断後の track_fired / signal_recv / 送信はすべて 0。Exception / Error 0 件（前後とも）

**ラウンド B（遅延 200ms、`-ddrive-sim-latency 200`、`Player_v4_200ms.log`）: 合格（課題 3 件を記録）**
- `NgoTransportConfigurator: シミュレータ設定を適用しました(latency=200ms)` → `NgoNetBridge` がアプリ層の送受信キューで 200ms 遅延を代替
- 定常時の rtt_app_ms 204〜208（✅ 200 前後）。track_fired 62 件（Vfx 31 / Se 31）、late_ms 最小 0・最大 156・平均 135.5、**500ms 超 0 件**（猶予 0.5 秒内で遅れて発火）。画面に VFX 表示あり
- vfx_active は定常 3〜4 で頭打ち（最大 4）。Placeholder / Unregistered / Exception / Error / NullReference 0 件
- 偽造 Cancel: 送信 30 = 破棄 30（並び一致）
- **課題 K1（環境）: ホットスポットの無線が数秒単位で止まる時間帯が数回あった**。その区間で rtt_app_ms が 1193〜2712 に跳ね、Ping 番号が飛び（#36→#40 等）、受信がまとめて届いた。ICMP ping は 10 発中 3 発落ち → 直後 30/30 成功（平均 2.4ms）。
  このため接続中にも track_skipped が 5 組（late_ms 675〜2671）出た。猶予 0.5 秒（A7）どおりの挙動だが、**通信状態が悪いと接続中でも演出が抜ける**。A7 は運用開始後に実回線で見直す（決定済み）
- **課題 K2（計測）: 通信停止中、rtt_app_ms が前回値のまま更新されない**（1713 が 12 行続いた）。デバッグ表示・ログの値が実態とずれる。Ping/Pong の未応答時間を反映する形に直す（P6 で対応）
- **課題 K3（要修正）: Signal が Play より先に届き「未知のキー」として破棄された**（`PresentationSignalMsg(HandleNetKey=0x00FFD6CE) は未知のキー…破棄しました` の直後に同じ key の Play が late_ms=675 で到着）。通信が止まってまとめて届いた区間で、Play と Signal の到着順が入れ替わった。
  ヒット時の演出（Signal("hit") の CameraShake/Haptic/SE）が悪い回線で抜けうる。候補: 未知キーの Signal を短時間（例 1 秒）保留し、同じ key の Play 到着時に適用する / アプリ層遅延キューとメッセージ種別ごとのチャネルで順序が保たれているか確認（P6 で対応）
- Host 側（`PlayerHost_v4_200ms.log`）も Exception 0 件

**ラウンド C（Host を `Stop-Process -Force` で強制終了 = タイムアウト切断）: 合格**
- 検知: `[Net/Client] NgoNetBridge: Host から切断されました(...[ProtocolTimeout]...)` → `disconnected=1`。最後の受信から約 30 秒（ProtocolTimeout の既定どおり）
- **切断を検知する前に VFX が自然に消える（A10 の確認）**: 最後の受信（nt≈319.2）から約 8.3 秒で `vfx_active=0`、約 13 秒で `activeCount=0`。切断検知（nt≈349）の約 22 秒前に 0 になった。
  v3（`looping=true`）では同じ状況で vfx_active が 13 のまま残っていた。スクリーンショット（強制終了から約 45〜59 秒後、2 枚）にも VFX は残っていない
- 未検知の間（connected=1 のまま）は NetCheckRunner が偽造 Cancel を送り続けた（6 回、送信先なし）。検知後は送信停止。想定どおり
- Exception / Error / NullReference 0 件

**v4 のまとめ**: A10（ループ解除）と 6-0 修正6/7 により、v3 までの「VFX の蓄積」「切断後の VFX 残留」は正常切断・タイムアウト切断の両方で解消。
残課題は K2（通信停止中の rtt_app_ms 固着）と K3（Signal が Play より先に届くと破棄）で、P6 の 6-6（受信検証）で対応する。
K1（ホットスポットの数秒停止）は環境要因で、A7（猶予 0.5 秒）の見直しは運用開始後に実回線で行う。
PC-A 側の配布用 HTTP サーバーは停止済み、両 PC とも `DDriveNetCheck` のプロセスは残っていない。

- **K2（対応: 6-6、PR #64）**: `NgoNetBridge.AppRoundTripMs` を計算プロパティ化し、未応答 Ping からの経過時間を
  下限として返すよう修正した（`IsAppRoundTripMsStale` を `NetDebugOverlay`/`NetCheckRunner` に反映）。
  詳細は [14_networking.md] §10「実装メモ（2026-09-15、6-6）」。ユニットテストでは NGO 実接続が必要な
  `NgoNetBridge` 内部のタイマー挙動を直接検証できないため（既存の慣習どおり、§7/§9/§11 参照）、v5 の
  実機/ローカル結合確認で直接確認する。
- **K3（対応: 6-6、PR #64）**: 真因（`NgoNetBridge` のアプリ層遅延キューが FIFO を保証していなかった）を
  `Queue<T>` 化で修正し、加えて `PresentationManager` 側にも未知キーの短時間保留（既定 1.0 秒、Play 到着時に
  適用・期限切れで従来どおり破棄）を防波堤として追加した。詳細は [14_networking.md] §9/§10「実装メモ
  （2026-09-15、6-6）」。PresentationManager 側のロジックは `Tests/Runtime/PresentationNetDeviceFixTests.cs`
  でユニットテスト済みだが、`NgoNetBridge` のアプリ層遅延キュー自体（K3 の真因側の修正）は実 NGO 接続が
  必要なため未検証。v5 で確認する。

## 13. v5 で確認すること（6-6 の K2/K3 修正後、次回の実機/ローカル結合確認で行う）

6-6（[11_tasks.md] / [14_networking.md] §9/§10）で K2/K3 を修正したが、`docs/12_review.md` の慣習どおり
ユニットテストだけでなく実機/ローカル結合での再確認が必要（`NgoNetBridge` に閉じた変更を含むため）。

1. **200ms + 通信停止の再現（K2/K3 の本題）**: `-ddrive-sim-latency 200` で起動した Client を、実機なら
   ホットスポットの電波が数秒途切れる状況を待つ（または PC-A/PC-B いずれかのネットワークアダプタを
   数秒無効化する等で模す）。`Player.log` を確認:
   - `rtt_app_ms` が通信停止中も前回値のまま固着せず、経過時間に応じて増え続けること（`rtt_app_stale=1`
     の行が出ること）。通信が復旧したら実測値に戻り `rtt_app_stale=0` になること
   - 通信停止からの復旧直後、Signal が対応する Play より先に処理されて「未知のキーとして破棄」される
     行が(理想的には)出ないこと。出る場合でも、その直後に「保留していた Signal を適用した」ことが
     間接的に確認できること(該当する OnSignal トラックの効果が実際に発生する。HitStop/CameraShake/Haptic
     等が復旧直後にも正しく発火する)。1 秒を大きく超えて Play が遅れて届いた場合は保留期限切れで従来どおり
     破棄されるのが仕様どおり(未知キー破棄ログが出ても即 NG ではない。「対応する Play が保留期限内に
     届いたのに破棄された」場合だけが不具合)
2. **既存の判定項目の再確認（回帰していないこと）**: 接続・Late Join・偽造 Cancel 破棄・切断検知・
   VFX の蓄積なし・切断後の VFX 残留なし（v4 で解消した項目、§12 参照）が引き続き成立すること
3. ログ取得後、`docs/28_manual_verification_phase5.md` と本ドキュメントの該当節に結果を追記する
4. **偽 Pong の破棄(2026-09-19 追加、[docs/44](44_review_2026-09-19.md) P2-1)**: 改造 Client(または
   デバッグビルドで手動送信)から `NetPongMsg` を Broadcast させ、受信した Host/他 Client の
   `rtt_app_ms`/`rtt_app_stale` が変化しないこと、`Player.log` に `NgoNetBridge: 送信元 ClientId(...) が
   正当な Pong の送信元と一致しないため破棄しました。` の警告が(開発ビルドのみ)1 回だけ出ることを確認する
   (`AppRoundTripTracker` 側は EditMode テストで固定済み。ここで見るのは `NgoNetBridge.OnPongMsgReceived`
   の `IsServer`/`senderId` 分岐という、NGO 接続が要る部分)

## 14. ハッシュ不一致の実機確認手順（6-5、次回の実機/ローカル結合確認で行う）

6-5（[11_tasks.md] / [14_networking.md] §7）で追加したカタログ ContentHash 照合は、`Tests/Runtime/
CatalogContentHashGateTests.cs`（`FakeNetBridge` を使った Host/Client 双方の match/mismatch/timeout の
純粋ロジック検証）でユニットテスト済みだが、実際の `NgoNetBridge`（`Broadcast`/`SendTo` の中継経路・
`NetworkManager.DisconnectClient` の実挙動）は未確認。既存の慣習（`docs/12_review.md` §5）どおり、
実機 2 台またはローカル 2 プロセスでの結合確認が必要。

### 手順（片方だけ Data を変えたビルドで接続）

1. PC-A（Host）用と PC-B（Client）用、2 つの `DDriveNetCheck` ビルドを用意する。うち片方だけ、GameData
   の内容を変える（例: `Assets/GameData/Catalogs/*.asset` のどれかに含まれる既存の Data の `Category`
   や `Tags` 等、カタログの Entry 自体には影響しない値を変えても ContentHash は変わらない — **ハッシュに
   含まれるのは CatalogEntry の Id/Type/Address/NetMode だけ**（[14_networking.md] §7 実装メモ）ので、
   確実に差を作るには次のいずれかを行う: ①新しい Se/Vfx 等の Data を 1 件追加してカタログに登録する
   ②既存 Data の `AssetFlags.Net`(NetMode)を変える ③一時的にカタログからどれかの Entry を削除する)。
2. `Tools > D-Drive > Build > 実機確認用 Windows 開発ビルド`（`NetCheckBuilder.Build()`）で変更前・変更後
   それぞれをビルドし、フォルダを分けて両 PC に配る（このビルドは常に development build。§注参照）。
3. PC-A で変更前のビルド、PC-B で変更後のビルドを起動し、通常どおり Host/Client で接続する（§3 の
   起動コマンド）。
4. `Player.log`（または画面左上の `NetDebugOverlay`）で以下を確認する:
   - Host 側: `[Net/Host] CatalogContentHashGate: ContentHash 不一致(Client <id>): <カタログ名>: entries
     local=N remote=M — 開発ビルド/エディタのため接続は継続します。` という警告が出る（`Debug.LogWarning`）。
     どのカタログが違うかが**カタログ名 + Entry 数**だけで分かること(実データが出力されないこと)
   - Client 側: 同様に `[Net/Client] CatalogContentHashGate: 不一致: ...` の警告が出ること（双方に警告が
     出ることの確認）
   - `NetDebugOverlay` の `ContentHash:` 行が両 PC とも "OK" ではなく不一致の詳細を表示すること
   - **接続が切断されずに継続すること**（開発ビルドでの方針）。剣攻撃デモ等の他の Presentation/VFX/SE が
     引き続き同期再生できることも合わせて確認する
5. 変更を元に戻し、通常のビルドで再接続 → `ContentHash: OK` に戻ることを確認する

### リリースビルド相当（切断）の確認について

**2026-09-18 対応済み**: `NetCheckBuilder.Build()` に `development` 引数（既定 `true` = 従来どおり
`BuildOptions.Development`）を追加し、`development: false` を渡すとリリース相当（`BuildOptions.None`、
`Debug.isDebugBuild=false`）でビルドできるようにした。呼び出し元は他に無かったため既存の呼び出し・CI には
影響しない（既定の開発ビルド用メニュー `Tools > D-Drive > Build > 実機確認用 Windows 開発ビルド` もそのまま）。
メニューに `Tools > D-Drive > Build > 実機確認用 Windows リリース相当ビルド`
(`NetCheckBuilder.BuildReleaseFromMenu()`)を新設した。zip の出力先は `outputDirectory` から自動導出する
（例: `Builds/v8_release_normal` → `Builds/v8_release_normal.zip`）。個別の出力先を使うビルド(v8_release_normal
等)は isuzu MCP の `execute_code` から `NetCheckBuilder.Build(outputDirectory: "Builds/...", development: false)`
を直接呼ぶ運用にした(メニューは既定の `Builds/DDriveNetCheck` 固定のため)。

**リリース相当ビルドでもログが残ることを実機ビルドで確認済み**: `Debug.Log`/`Debug.LogWarning`/`Debug.LogError`
(`NetCheckRunner.LogCheck` 経由の `[DDriveNetCheck] ...` 行、`CatalogContentHashGate` の警告/エラーを含む)は
`#if DEVELOPMENT_BUILD` 等で条件分岐しておらず、リリースビルドでも通常どおり実行される。実際に
`v8_release_normal\DDriveNetCheck.exe -ddrive-net off -logFile <path>` を約 18 秒単体起動したところ、
`-logFile` 出力に `[DDriveNetCheck]` 行が 48 件(`heartbeat=`/`ready=`/`track_fired=`/`signal_fire=` 等)残る
ことを確認した(Unity のプレイヤーログは Development Build でなくても `-logFile` で出力されるため、実機での
判定はこの経路で行える)。唯一 `NetCheckRunner.OnRemoteOneShotSkipped`(`track_skipped` ログ)だけが
`#if DEVELOPMENT_BUILD || UNITY_EDITOR` で意図的にリリースビルドでは出力しない設計(既存のコメントどおり、
製品ビルドでのログ汚染・コストを避けるため)だが、§21.4 の切断判定(基準 1/2/4)には影響しない。

実機での 2 段階確認手順(PC-B=Host / PC-C=Client、`v8_release_normal`/`v8_release_mismatch` の配布手順・
判定基準・うまくいかないときの切り分け)は §21 を参照。§21 は本節の対応を前提に書かれている。

## 15. 自動判定つきローカル 2 プロセス確認(6-7)

[11_tasks.md] 6-7。§7/§9〜§13 の「ローカル 2 プロセス確認」を毎回手動でログを読んで判定するのではなく、
`DDrive.Runtime.Net.NetCheckJudge`(純関数、Unity API 非依存。`NetLaunchArgs` と同じ方針で EditMode
テスト済み)による自動判定 + `Tools/CI/run-netcheck.cmd` による起動・集計へ置き換えたもの。CI(GitHub
Actions)への組込みは P7 末まで延期のため([33_ci_setup.md] §8 と同じ理由)、当面はこの手順がローカル
実行の唯一の経路になる。

### 使い方

1. Unity Editor で `Tools > D-Drive > Build > 実機確認用 Windows 開発ビルド`(`NetCheckBuilder.Build()`)
   を実行し、`Builds\DDriveNetCheck\DDriveNetCheck.exe` を最新化する
2. リポジトリ直下で `Tools\CI\run-netcheck.cmd`(1 シナリオだけなら `Tools\CI\run-netcheck.cmd pair0`)を
   実行する。Unity Editor を開いたままでもよい(ビルド済み exe を別プロセスとして起動するだけで、
   Unity 側のバッチ処理は行わない)
3. `Tools\CI\Run-NetCheck.ps1` が Host/Client を 127.0.0.1 上に起動し、両方のプロセスが自然終了するまで
   待って `TestResults\NetCheck\` にログとサマリを書き出す

### シナリオ

| シナリオ | 遅延 | Host 実行時間 | Client 実行時間 | 目的 |
|---|---|---|---|---|
| `pair0` | 0ms | 35秒 | 30秒 | 接続・Signal 中継・偽造 Cancel 破棄・ContentHash 一致の基本確認 |
| `pair200` | 200ms | 35秒 | 30秒 | 同上を A7 の猶予(0.5秒)下で確認(`track_fired`/`track_skipped` の境界判定含む) |
| `latejoin` | 0ms | 35秒 | 20秒(Host 起動 12 秒後に接続) | Late Join 直後の activeCount 復元・Placeholder 0 |
| `disconnect` | 0ms | 10秒(先に終了) | 25秒 | Host の正常終了を Client が検知し、演出(activeCount/vfx_active)が 0 になることを確認 |

`disconnect` は実機確認 v4 の「ラウンド A: 切断(Host をウィンドウを閉じて正常終了)」(§12)と同じ経路を、
`Stop-Process` 等でプロセスを外部から殺すのではなく **Host の `-ddrive-autotest-seconds` を Client より
短くする**ことで再現している(`NetCheckRunner.RunAutoTestAndQuit` が自分の判定を済ませてから
`Application.Quit()` で正常終了する。タイムアウト切断=ラウンド C の経路はこの自動テストの対象外)。

### 判定条件と担当箇所

各シナリオは次の 3 つがすべて PASS のときだけ PASS になる:

1. **Host 自身の `[DDriveNetCheck] RESULT=PASS|FAIL scenario=... reason=...`**(`NetCheckJudge.Evaluate`
   が Host 自身のログ/イベント購読だけで判定: Exception/Error 0・接続・Placeholder 0・A7 猶予の逆側
   チェック(`track_fired_over_grace`/`track_skipped_within_grace`)・切断後の演出 0(発生した場合)・
   ContentHash 一致)
2. **Client 自身の同じ RESULT 行**(同じ判定に加えて、`latejoin` シナリオでは Late Join 復元
   (`late_join_not_restored`)、偽造 Cancel の全件破棄(`forged_cancel_mismatch`。Client 発の Broadcast は
   Host 経由で ClientsAndHost へ中継され送信元自身にも同じ破棄ログが返るため、Client 自身のログだけで
   送信数=破棄数を検証できる)も判定する)
3. **`Tools/CI/Run-NetCheck.ps1` によるクロスログ判定**(Signal 中継の位相差。Host の `signal_fire` と
   Client の `signal_recv` を `HandleNetKey` で対にして `networkTime` 差を計算する。しきい値は
   「シナリオのシミュレート遅延(片道 ms、`$scenario.LatencyMs`)+ ノイズ耐性マージン 150ms
   (`$PhaseDiffMarginMs`)」。2026-09-15 修正、下記「初回実行結果と判定バグ修正」参照。分母(Host の
   `signal_fire` 件数)は Client が接続した時刻(Client 自身の最初の `heartbeat=1 role=client` 行の
   `networkTime`)以降の発火だけに絞る。単一プロセスのログだけでは分からない項目のため、両方の
   `Player.log` を突き合わせるここだけで判定する)

K3(6-6、Signal が Play より先に届いた場合の保留→適用)が実際に効いたかどうかは、`PresentationManager.
FlushPendingUnknownKey` が開発ビルドで `pending_applied=1` を含む 1 行をログに出すようにした(6-7 で追加)
ため、ローカル実行(遅延が小さく揺らぎも小さい)では通常出ないが、出ていれば K3 が機能した直接的な証拠に
なる。この行の有無は現時点では PASS/FAIL 条件には含めていない(発生が任意のため。§13 の実機/ローカル
結合での v5 確認で意図的に再現する場合に活用する)。

6-5(ContentHash)は 2026-09-15 に main へマージ済み(PR #70)のため、`NetCheckJudge` は Host/Client が
`CatalogContentHashGate.LastStatusText` で `OK` になっていることも判定に含める。Host/Client が同じビルド
(同じ GameData)を使うこの自動テストでは常に一致するはずなので、`content_hash_not_ok` で FAIL する場合は
ビルドの取り違え等の環境要因を疑う(意図的な不一致確認は §14 の手動手順を使う)。

要判断:
- Signal 中継の位相差のノイズ耐性マージン(150ms、`$PhaseDiffMarginMs`)は §4 の「目安 100ms 以内」に
  自動判定用の余裕を乗せた値であり、docs 側の目安自体は変えていない。ローカル実行環境の負荷次第で
  調整が必要になれば `Run-NetCheck.ps1` の `$PhaseDiffMarginMs` を変更する
- K1(通信の数秒停止)・K2(rtt_app_ms 固着からの復旧)・K3(保留→適用)を実際に再現する不安定な回線状態は
  ローカルループバックでは作れないため、この自動テストでは(K3 の `pending_applied` ログが偶然出ない限り)
  直接は検証していない。§13 の実機/ローカル結合での v5 確認が引き続き必要

### 初回実行結果と判定バグ修正(2026-09-15)

`Tools\CI\run-netcheck.cmd` を実際にビルド済み exe で初めて通したところ、4 シナリオ全てが FAIL した
(`TestResults\NetCheck\summary.md`)。原因を各ログ(`*_host.log`/`*_client.log`)で確認したところ、
**ネット機能そのものの新規バグは 1 件**(下記 a)、残りは判定条件・シナリオ設定側の不備だった。

**a. `CatalogContentHashGate` の実バグ(修正済み)**: NGO の `OnClientConnectedCallback` は Host が
`StartHost()` する際、Host 自身の自己接続でも発火する([14_networking.md] §2/§12 のコメントに既存の
既知事項として記載あり)。`CatalogContentHashGate.OnClientConnected` はこれを素通りさせており、
Host が自分の `clientId`(=`LocalClientId`)に対しても `_pendingHostSideDeadlines` へ保留期限を
登録していた。Host は自分にハッシュを送る必要が無く(`TrySendOwnHash` が `IsServer` を弾いて no-op)、
この自己分のエントリは誰からも解決されないため、**実クライアントの有無・一致に関わらず必ず
`_timeoutSeconds`(既定 5 秒)後にタイムアウトし、`LastStatusText` が一度 `"OK"` になっていても
`"ContentHash 未受信(Client 0): (タイムアウト: ContentHash が届きませんでした)"` に戻ってしまう**
実バグだった(pair0/pair200/latejoin/disconnect の全 Host ログで、接続後 5 秒強のタイミングで再現)。
`OnClientConnected` で `clientId == _netBridge.LocalClientId` を早期 return するよう修正し、
`CatalogContentHashGateTests` に自己接続イベント単体・実クライアント接続後の 2 パターンの回帰テストを
追加した。

**b. ⑤(切断後の演出 0)を Host にも要求していた(NetCheckRunner の判定バグ)**: `NgoNetBridge.
ClientDisconnected` は「Host が他 Client の切断を観測した」場合にも発火する(既存コメントに記載済みの
仕様どおり)。`NetCheckRunner.OnBridgeDisconnected` は役割を見ずに `_disconnectLogged` をそのまま
判定用フラグとして使っていたため、Host 側は「(他 Client の)切断を検知したのに、自分が周期再生している
デモの `activeCount`/`vfx_active` が 0 にならない」という偽陽性 FAIL になっていた(pair0/pair200/
latejoin の 3 シナリオで Host が `vfx_or_active_not_cleared_after_disconnect` で FAIL)。Host は他
Client の 1 人が抜けても自分のデモ演出を止めない設計であり、`CancelAllNetworked()` も Client 視点の
切断時にだけ呼ばれる(§8)。判定用フラグ(`_selfDisconnectedObserved`)は `_role == "client"` の場合
だけ立てるように修正した。

**c. `disconnect` シナリオで `ConnectedAtEnd=false` を無条件に FAIL にしていた(NetCheckJudge の判定
バグ)**: `disconnect` シナリオは Host が先に(正常終了で)いなくなり、Client は再接続しない設計のまま
自分の自動テスト時間を使い切って終了する。つまり Client の `ConnectedAtEnd=false` は「切断を正しく
検知して後片付けできたこと」の結果であり、それ自体を `not_connected` として即 FAIL にしてはならない
(修正前は Client が `RESULT=FAIL reason=not_connected` になっていた)。`NetCheckJudge.Evaluate` の
先頭チェックを `!ConnectedAtEnd && !DisconnectedObserved` に変更し、自分で切断を検知済みの場合は
後段の実質的なチェック(⑤ `vfx_or_active_not_cleared_after_disconnect`)に判定を委ねるようにした。
`NetCheckJudgeTests` に「切断検知+後片付け済みなら PASS」「切断検知したが後片付けが機能していなければ
`not_connected` ではなく実質的な理由で FAIL」「切断イベントに気づかず静かに切断された場合は従来どおり
`not_connected`」の 3 ケースを追加した。

**d. ③(偽造 Cancel の全件破棄)がシナリオ終了間際の in-flight 分で食い違っていた(`pair200`、レイテンシ
シナリオ限定)**: `-ddrive-sim-latency 200` の下では、Client 発の偽造 Cancel(`ReliableOrdered`、
Client→Host→ClientsAndHost の 2 ホップ中継)が Host を経由して自分に戻ってくるまでに実測で 700ms 前後
掛かる。シナリオ終了直前(残り時間がこの往復時間より短いタイミング)に送信した最後の 1 件は、破棄ログが
届く前に `Application.Quit()` が呼ばれてしまい、`forged_cancel_sent`(6件)と `forged_cancel_discarded`
(5件)が食い違う偽陽性 FAIL になっていた(ネット機能自体は正しく動いている。届く前にプロセスが
終了しただけ)。`NetCheckRunner` に `HasTimeForForgedCancelRoundTrip()` を追加し、残り時間が
`2 秒 + シミュレート遅延(片道 ms)× 4 / 1000` 未満になったら新規送信を止めるようにした。

**e. Signal 中継の位相差しきい値が遅延シナリオを考慮していなかった(`pair200`)**: 旧しきい値は
固定 150ms で、`-ddrive-sim-latency 200` では Host→Client の 1 ホップ分の遅延(実測 maxDiffMs=350ms)
がそのまま乗るため機械的に FAIL していた。しきい値を「シミュレート遅延(片道 ms)+ ノイズ耐性マージン
150ms」に変更した(§4/上記「判定条件と担当箇所」参照。比較前に整数 ms へ丸めて浮動小数の丸め誤差による
境界値の偽陽性も避けている)。

**f. Signal 中継の分母に Client 接続前の `signal_fire` が入っていた(`latejoin`)**: `latejoin` は
Client が Host 起動 12 秒後に接続するため、それ以前に Host が単独で発火させた 3 件の `signal_fire` は
Client からは原理的に受信できない(受信して当然の対象ではない)。これを分母に含めていたため中継率
(`ratio`)が実態より低く出て機械的に FAIL していた(実測 `ratio=0.64`。接続前 3 件を除くと
`7/8=0.875` で閾値 0.7 を超える)。`Run-NetCheck.ps1` に `Get-ClientConnectNetworkTime` を追加し、
Client の最初の `heartbeat=1 role=client` 行の `networkTime` 以降の `signal_fire` だけを分母にする
ように修正した。

**g. `Tools\CI\run-netcheck.cmd` の文字化け**: このファイルは UTF-8(BOM 無し)で保存されているため、
既定のコードページ(日本語 Windows では通常 932 = Shift-JIS)の `cmd.exe` から実行すると日本語の
`echo`/`REM` 行が文字化けする(cmd 側が UTF-8 のバイト列を Shift-JIS として誤読するため。実際に
`chcp 932` を強制した状態で再現し、`chcp 65001 >nul` で解消することを確認した)。`.gitattributes` の
CRLF 指定はそのまま維持し、ファイル先頭(`@echo off` の直後)に `chcp 65001 >nul` を追加して対応した。

これらの修正はいずれも判定条件・シナリオ設定・この自動テスト専用コード(`NetCheckRunner`)側の不備で、
Host/Client 間の実プレゼンテーション同期・偽造メッセージ検証・Late Join 復元・切断検知そのものは
(a を除き)正しく動いていた。a の `CatalogContentHashGate` の修正は 6-5(ContentHash)本体の実装
バグであり、実機確認(§8〜§12)では 1 対 1 構成の実機テストがこの自己接続タイムアウトを踏む前に
`LastStatusText` が最初の `"OK"` のまま画面キャプチャ・ログ確認を終えていたため見つからなかったと
推測される(要判断: 次回の実機確認では `content_hash` の値が長時間 `"OK"` を維持することも確認項目に
加えるとよい)。

### 2 回目（判定修正後）の実行結果（2026-09-15、PR #72 取り込み後）

メインで `compile`(エラー 0)→ EditMode `NetCheckJudge|NetLaunchArgs|ContentHash` 44 件 green → PlayMode
`ContentHash|Net` 58 件 green → `NetCheckBuilder.Build()` で再ビルド → `Tools\CI\run-netcheck.cmd` を実行し、
**4 シナリオ全て PASS**（終了コード 0、`=== すべてのシナリオが PASS です ===`）。起動した全プロセスは終了済み。

| シナリオ | Host | Client | Signal 中継（位相差） |
|---|---|---|---|
| pair0（0ms） | PASS（signal_fire 11、偽造 Cancel 破棄 5、content_hash=OK） | PASS（signal_recv 36、偽造 Cancel 送信 5 = 破棄 5、Late Join 復元、content_hash=OK） | PASS |
| pair200（200ms） | PASS | PASS（偽造 Cancel 5 = 5） | PASS（maxDiffMs=350、しきい値 = 遅延 200 + 150） |
| latejoin（Client を 12 秒後に接続） | PASS | PASS（signal_recv 28、Late Join 復元） | PASS（接続後の fire 8 件中 7 件、maxDiffMs=100） |
| disconnect（Host が先に終了） | PASS | PASS（切断検知、切断後の演出 0） | PASS（3/3、maxDiffMs=70） |

すべてのシナリオで `content_hash=OK` が最後まで維持された（上記 a の自己接続タイムアウトが解消したことの確認）。
初回実行のログは比較用に保存してある（リポジトリ外）。

## 16. v5 実機確認の結果（2026-09-18、PC-A Host + PC-B Client。§13 の実施）

構成: PC-A（Host、`Builds/DDriveNetCheck`、コード日時 2026-09-15 03:46 = v5）/ PC-B（`wrench_2nd`、192.168.137.74、
`C:\DDriveTest\v5`）。ログ: PC-A = `Builds/PlayerHost_v5.log`、PC-B = `C:\DDriveTest\client_v5.log` および
`client_v5_200ms.log`（**PC-B に保管。修正前の比較用に消さないこと**）。

### 16.1 通った項目（§13 項目 2 = 既存項目の回帰なし）

| 項目 | 結果 |
|---|---|
| 接続 | `heartbeat=1 role=client clientId=1 connected=1`、`rtt_app_ms=4〜5`（遅延なし時） |
| Signal の伝播 | `signal_recv=hit` が key ごとに HitStop / Se / CameraShake / Haptic の 4 種届く |
| ContentHash | 両端 `content_hash=OK` |
| 遅延の適用 | `-ddrive-sim-latency 200` で `rtt_app_ms=205〜208`。アプリ層キューによる代替（§3）が機能している |
| 復旧時の Signal | 通信停止からの復旧の瞬間（`networkTime=411.15`）に 3 つの key の Signal が 4 種まとめて発火。**「未知のキーとして破棄」は 0 件**。保留分のフラッシュが効いている |
| 偽造 Cancel の破棄 | 26 件（すべて `PresentationCancelMsg` の発行者不一致による破棄。仕様どおり） |
| 切断扱いにならないこと | `connected` は最後まで 1。disconnect / Exception / Error いずれも 0 件 |
| VFX の蓄積・残留なし | `vfx_active` が停止中に 3 → 0 まで落ち、復旧後 1 → 4 に戻る。`activeCount` も 5 → 2 → 5 に復帰（v4 で解消した項目の回帰なし） |

### 16.2 通らなかった項目（§13 項目 1 の本題）— **K2 修正は目的を達成していない**

通信停止（Wi-Fi を約 6 秒切断、`networkTime` 403.59〜411.16）の間、`rtt_app_stale=1` の行は出たが、
**`rtt_app_ms` は経過時間に応じて増え続けなかった**（実測 372 / 714 / 370 / 370 / 370 / 633 / 369 / 368 /
369 / 553 / 405 / 405 / 438 と上下する）。

**真因（2026-09-18 にコードで確認）**:

1. **経過時間の基準が「直近の Ping 送信時刻」になっている** — `NgoNetBridge.cs` の Ping ループは 1 秒ごとに
   `_lastPingSentRealtime` を現在時刻で上書きし `_awaitingPong` を立て直す。**前回の Pong が返っていなくても
   上書きする**ため、`AppRoundTripMs` が返す `elapsedMs` は常に直近 1 秒以内に制限され、停止がどれだけ
   長引いても 1000ms 前後までしか伸びない。観測された 368〜714 の上下は、1 秒周期のノコギリ波を 1Hz の
   ハートビートで拾った結果。
   **直すべき方向**: 基準を「最後に Pong が返った時刻」または「未応答のまま最も古い Ping の送信時刻」にする。
2. **`rtt_app_stale` が平常時にも立つ** — `IsAppRoundTripMsStale` は `_awaitingPong` が立っていれば true を
   返すだけなので、**正常動作中でも Ping 送信から Pong 到着までの約 200ms（1 秒周期の約 20%）は true になる**。
   実際、復旧後の `networkTime=419.37 / 420.37 / 421.37` で `rtt_app_stale=1` なのに `rtt_app_ms=209 / 206 / 205`
   （実測値のまま）という矛盾した行が出ている。フラグが「通信途絶」の指標として機能していない。

いずれも**実機でしか出ない**（ユニットテストは 1 秒周期の実時間経過を再現していなかった）。

### 16.3 次にやること

1. `NgoNetBridge` の K2 を上記の方向で直す（併せて `rtt_app_stale` の意味を「通信途絶の疑い」に変える。
   例: 連続して N 回 Pong が返っていない場合のみ true にする）。
2. **ビルドを作り直して**再確認する（今の v5 ビルドには修正が入らない）。判定は §13 項目 1 と同じ。
3. 単体テスト側にも「1 秒周期で Ping を送り続けたまま Pong が返らない」状況を再現するテストを足す
   （今回見逃した理由がここにあるため）。

**2026-09-18 修正済み**: 上記 1・3 をコードで対応した。

- **経過時間の基準**(1): 「未応答のまま最も古い Ping の送信時刻」を採用した(「最後に Pong を受信した
  時刻」案も検討したが、まだ 1 度も Pong を受信できていない接続直後からの断線を素直に扱えない・応答待ちが
  始まった時点そのものを指すためダウンタイムの下限としてより正確〔過大評価しない〕という 2 点でこちらを
  選んだ)。状態遷移ロジックは `Assets/DDrive/Runtime/Net/AppRoundTripTracker.cs`(Unity API 非依存の純粋
  クラス)に切り出し、`NgoNetBridge` はこれに委譲する形にリファクタした。
- **`rtt_app_stale` の意味**(上記の「併せて」): 連続 **3 回**(`AppRoundTripTracker.DefaultStalePongMissThreshold`)
  Pong が返らない場合だけ true にするよう変更した。Ping ループは 1Hz・Pong は NetChannel.Unreliable
  (パケットロス上等)のため、1 回だけの未達は Wi-Fi の日常的なロスと区別が付かない。3 回連続(≒3 秒間
  無応答)を閾値にすることで、単発ロスは吸収しつつ、実際の通信途絶は本節の実機確認(6 秒切断)の範囲内で
  十分早く検出できる。
- **単体テスト**(3): `Assets/DDrive/Tests/Editor/AppRoundTripTrackerTests.cs` に追加した。
  `AppRoundTripMs_GrowsMonotonically_WhileOutageContinues_WithPingSentEverySecond` が「1 秒周期で Ping を
  送り続けたまま Pong が返らない」状況(まさに今回見逃した状況)を再現し、次の Ping 送信直前という修正前
  バグが観測されたのと同じタイミングでサンプリングして単調増加を確認する(修正前の実装〔基準が「最後の
  Ping 送信時刻」で毎秒リセットされる〕のままだと、6 秒停止しても ~1000ms 前後で頭打ちになり
  `Assert.Greater(sample.Value, 4500d)` 等で赤くなることを、コードで再現して確認済み)。
  `IsStale_StaysFalse_DuringNormalOperation_EvenWhileAwaitingEachPong` は、Pong が毎回正常に(Ping 周期
  1 秒に対して 200ms で)返ってくる平常運用を 20 サイクル回し、`IsAppRoundTripMsStale` が一度も true に
  ならないことを確認する(修正前は Ping 送信〜Pong 到達の間〔毎秒約 20%〕が常に true になっていた)。
- `NetDebugOverlay`(画面表示: `(stale)` → `(途絶疑い)`)と `Assets/DDrive/Samples/NetCheckRunner.cs`
  (ログのコメント)も新しい意味に合わせて文言・コメントを更新した。
- compile 0 エラー、EditMode 814 件 / PlayMode 689 件がいずれも green であることを確認済み(2026-09-18)。
- **残課題**: 本節はコードレベルの修正・単体テストの追加のみで、**実機での再確認(ビルドを作り直して
  §13 項目 1 を再実施)はまだ行っていない**。次回の実機確認セッションで、v5 と同じ手順(Wi-Fi を数秒
  切って戻す)で `rtt_app_ms` が経過時間とともに増え続けること・`rtt_app_stale` が平常時には出ないことを
  確認すること。

## 17. K2 修正の実機再確認（2026-09-18、v6_normal Host + v6_mismatch Client）— **合格**

§16.2 で見つかった 2 件を `6315ae8` で直し、作り直したビルドで §13 項目 1 を再判定した。**両方とも直っている。**

| 判定基準 | 結果 |
|---|---|
| 切断中、`rtt_app_ms` が経過時間に応じて増え続ける | **合格**。204 →（切断）→ 1166 / 2167 / 3167 / 4086 / … / 24856 / 25856 / 25978 と**単調増加**（前回は 368〜714 で頭打ち） |
| `rtt_app_stale=1` が平常時に出ない | **合格**。平常時 0 件（前回は周期の約 20% で立っていた） |
| `rtt_app_stale=1` が「3 回連続で Pong 未達」のときだけ立つ | **合格**。1166 と 2167 の 2 行はまだ 0 で、**3 回目の 3167 から 1 になっている**。設計どおり |
| 復旧後、実測値と `rtt_app_stale=0` に戻る | **合格**。216 → 205ms、stale=0 |
| 切断扱いにならない | **合格**。切断中も `connected=1`、disconnect 0 件 |

今回の停止は 6 秒の予定だったが、Wi-Fi の再接続に時間がかかり**実際には約 26 秒**だった。結果としてより長い停止での挙動を確認できている（Host からの Ping は 14 個欠落）。

復旧後に `PresentationSignalMsg` の破棄が 4 件出ているが、対応する `track_skipped` の `late_ms` が 719 / 3719 / 6720 / 9720 で、**1 秒の猶予を大きく超えて遅れて届いた Play の破棄**であり §13 項目 1 の但し書きどおりの仕様動作（NG ではない）。破棄 66 件のうち残りはすべて偽造 Cancel の破棄。

> **注**: このセッションで使ったビルドは §18 のカタログ不良を含むため、`vfx_active` は常時 0 で VFX の判定材料にはならない。K2 は Ping/Pong の計測のみで成立するため、この不良の影響を受けない。

## 18. ビルドのカタログ不良（2026-09-18 に発見、§14 は未実施）

v6 のビルドで `§14`（ハッシュ不一致）を確認しようとしたところ、**Client の `content_hash` が「不一致」ではなく `OK` になった**。調べたところ、ビルドの作り方ではなく**リポジトリ側の既存の壊れ**だった。

```
UnityEngine.AddressableAssets.InvalidKeyException: No Location found for Key=VFX_Player_Slash
[DDrive] Addressables のラベル 'DDriveCatalog' からカタログを集められませんでした
[DDrive] カタログが 1 つも登録されていません(全 ID が Placeholder になります)
```

- `Assets/GameData/Vfx/Player/VFX_Player_Slash.asset`（guid `d7f9265ffc9ff0f41b743644daf1ac5f`、剣攻撃デモが実際に使う方）が **Addressables に登録されていない**
- 代わりに `VFX_Player_Slash2.asset`（guid `c21319e87221abf4ea996d0cc47f6779`）が address `VFX_Player_Slash2` で登録されている
- カタログは `VFX_Player_Slash` を指しているためロードに失敗し、**カタログ登録そのものが中断**して全 ID が Placeholder に落ちる
- 混入時期はコミット **`c67f81a`「テストデータ・カタログ・仕様書スナップショットの更新」**。1 つ前の `8630660` では address が `VFX_Player_Slash` で正常だった（エントリ数は 50 のまま変わっておらず、1 件の address が差し替わっている）
- **`c67f81a` 以降に作ったビルドはすべてこの状態**。v5（2026-09-15）はこれより前なので無事だった

**`content_hash=OK` は「一致した」ではなく「比較対象が無くて素通りした」結果**だったため、気づかなければ §14 が通ったと誤判定するところだった（PC-B 側の指摘で判明）。

後続: 登録を直したうえで **`Validation > Run All`（`AddressablesRegistrationValidator`）がこれを検出できるか確認する**こと。本来これを検出するための Validator なので、検出できていなければそれ自体が別の不具合。

### 経緯の補足（ユーザーからの情報、2026-09-18）

当時の操作は (1) `VFX_Player_Slash` を削除 → (2) 置き換えとして `VFX_Player_Slash2` を作成 → (3) その後 **git から `VFX_Player_Slash` の削除を discard**（差し戻し）、という流れだった。(3) で `VFX_Player_Slash.asset` はファイルとして復元されたが、**Addressables の登録は別ファイル（`Assets/AddressableAssetsData/AssetGroups/DDrive_GameData.asset`）にあるため復元されなかった**。これが「エントリ数は 50 のまま 1 件の address だけ差し替わっていた」理由。

**一般化: git で `.asset` を戻しても、Addressables の登録（別ファイル）は追従しない。** 気づけないまま実機で 2 台繋いで初めて発覚する、という点で再発しやすい事故クラス。

### 対応結果（2026-09-18）

**Validator は検出できていた。** 直す前に `DDrive.Editor.CI.RunValidation()` を直接実行して確認したところ、`AddressablesRegistrationValidator` が `VFX_Player_Slash.asset` に対して `Addressables 未登録: 'VFX_Player_Slash' がグループに入っていません(カタログの Address 'VFX_Player_Slash')。実行時にロードできません。` という Error を正しく報告していた（対象の Data ファイルが実在した限りは、per-Data チェックが機能していたため）。

気づかれなかった理由は「Validation を流していなかった」こと。この時点で `Run All` の結果は 137 件中 69 件が Error という非常にノイズの多い状態で、個別に眺めていても埋もれやすい状況ではあるが、そもそも当時（コミット `c67f81a` 〜 v6 ビルドの間）に `Run All` 自体が実行された記録がない。実機の 2 台接続で `content_hash` を見て初めて発覚した。

**ただし調査の過程で、この Validator 自体の本当の検出漏れも見つけた。** `AddressablesRegistrationValidator` は「渡された Data 自身が Addressables に登録されているか」を Data 単位（`ValidatorRegistry.RunAll` が列挙する、プロジェクトに実在する `AssetDataBase` アセット）で見る作りのため、**Data(.asset) ファイル自体が存在しない場合は Validate() が一度も呼ばれず、何も報告できない**。実際、調査中に `AnchorCatalog` の Address `ANC_Can_Vas`(ID `0x10207FFC96BD812C`)がこれに該当する孤立エントリだと判明した（対応する Data ファイルがプロジェクトに存在しない。おそらく同種の delete/discard 事故）。これは今回の本題とは別件で、**削除するか復元するかの判断が要るため今回は未対応**（ユーザーの朝の判断待ち）。

この抜けを塞ぐため `CatalogAddressCoverageValidator`（`Assets/DDrive/Editor/Validation/CatalogAddressCoverageValidator.cs`）を新設した。カタログ起点で「カタログの Address が Addressables のどのエントリにも存在しない」ことを検出する（`ContentHashCatalogCoverageValidator` と同じ実装パターン）。対応する `AddressablesSync.FindEntryByAddress` も追加。検出できることを確認するテストを `Assets/DDrive/Tests/Editor/CatalogAddressCoverageValidatorTests.cs` に追加した（`ContentHashCatalogCoverageValidatorTests` と同じ baseline 差分方式）。詳細は [02_core_framework.md](02_core_framework.md) §11 の 2026-09-18 追記を参照。

**直した内容**: `AddressablesSync.SyncAll(log: true)`（既存の同期メニュー「Addressables 登録を同期(カタログ → グループ)」と同じ経路）を実行し、`VFX_Player_Slash.asset` を Addressables グループ `DDrive_GameData` に address `VFX_Player_Slash` で再登録した（差分は `DDrive_GameData.asset` へのエントリ追加 1 件のみ）。

**`VFX_Player_Slash2` は削除していない。** `VfxCatalog` に ID `9666591439498022804` / Address `VFX_Player_Slash2` としてエントリがあり、Addressables にも同じ address で正しく登録されている(= カタログにエントリがある正規の登録)。ただし `Prefab` が未設定(Missing)という別の Error が出ており、剣攻撃デモでは使われていない(WIP か、Slash の置き換えを試した残骸の可能性がある)。データアセットの削除は取り消しにくいため、**Slash2 の去就はユーザーの朝の判断に委ねる**（削除するなら `VfxCatalog` からの登録解除も同じ PR で行う必要がある）。

**カタログのロードが成功することの確認**: 修正後、`PresentationSkillSlashPreviewScene`(剣攻撃デモの確認用シーン、`[D-Drive] Runtime` に `DDriveRuntimeBootstrap` あり)を開いて Play Mode に入り、コンソールを確認した。`[PresentationSkillSlashDemo] Play() -> IsPlaying=True` が出力され、`InvalidKeyException` も「カタログを集められませんでした」も出ず、**エラー 0 / 警告 0**。実 Manager 経由でカタログ登録・`VFX_Player_Slash` の解決が成功したことを確認した。

**Run All の残件**: 修正後の `Run All` は 137 件中 Error 69(修正前と同数だが内訳が変わっている: `VFX_Player_Slash` 関連の Error は解消、新設した `CatalogAddressCoverageValidator` が検出する `AnchorCatalog`/`ANC_Can_Vas` の Error が 1 件増えた)。Addressables 登録に関する残件はこの `ANC_Can_Vas` 孤立エントリのみ(前述、朝の判断待ち)。他の 68 件は本件と無関係の既存の Error(シェーダー/Prefab未設定等、うち 1 件は VFX_Player_Slash2 の Prefab 未設定)。

**検証**: `compile_status` でコンパイルエラー 0 を確認。`test_run`(EditMode) 818 passed / 0 failed(基準値 814 + 新規テスト 4 件)、`test_run`(PlayMode) 689 passed / 0 failed(基準値どおり)。テスト前後で `git status` に意図しない差分なし。コミットはしていない。

## 19. §14 が実施できなかった理由（2026-09-18 深夜、ファイアウォール）

`9705e2a` でカタログ不良（§18）を直し、`v7_normal` / `v7_mismatch` を作り直して §14 に再挑戦したが、
**PC-B の Client が Host に接続できず**（`role=off` / `connected=0` のまま、`[Net/Client]` の行は起動時の 1 行だけ）
判定に入れなかった。

**原因は PC-A の Windows ファイアウォール**。受信規則は実行ファイルの**パスごと**に作られるため、
`Builds/v7_*/` という新しいフォルダに出力したことで新規プログラム扱いになり、
**ユーザーが就寝中で確認ダイアログに応答できないまま Block 規則が作られていた**。

```
builds\ddrivenetcheck\ddrivenetcheck.exe  Inbound  Allow   (v1〜v5)
builds\v6_normal\ddrivenetcheck.exe       Inbound  Allow   (ユーザーが起きている時に許可)
builds\v7_normal\ddrivenetcheck.exe       Inbound  Block
builds\v7_mismatch\ddrivenetcheck.exe     Inbound  Block
```

Host 自体は正常だった（プロセス生存、`192.168.137.1:7777/UDP` で待ち受け、`vfx_active=3〜4` でカタログも健全）。
PC-B 側も健全で、**カタログの壊れ（§18）が再発していないことは確認できた**（`InvalidKeyException` 0 件、
Placeholder 0 件）。つまり v7 のビルドは健全で、残るのはファイアウォールだけ。

**ファイアウォール設定の変更は Claude が行わない**（システム／セキュリティ設定の変更にあたる）。
許可済みパス（`v6_normal`）に v7 のバイナリを置き換えて回避する手段も取らない（ユーザーがプログラム単位で
行った許可判断を迂回するため）。**ユーザーが Block 規則を削除するか許可に変えたうえで再実施する。**

### 再発防止（次回のビルドから）

**ビルドの出力先を毎回変えないこと。** `v6_normal` / `v7_normal` のように版ごとにフォルダを分けると、
その都度ファイアウォールの新規プログラム確認が発生する。**`Builds/DDriveNetCheck/`（Host 用）と
`Builds/DDriveNetCheck_Alt/`（不一致確認用）のように固定パス 2 つを使い回し**、中身だけ差し替えれば、
一度許可した規則がそのまま効く。版の区別は zip 名とログ名で行えばよい。

> **注**: ビルド前の健全性確認（プレイヤーを短時間起動してカタログのエラーが無いことを見る、§18 の再発防止）
> は有効だが、**新しいパスで初めて起動すると、そこでファイアウォール確認が発生する**。上の固定パス運用と
> 併用すること。

## 20. §14 ハッシュ不一致の実機確認（2026-09-18 朝、v7_normal Host + Client）— **合格**

§19 のファイアウォール Block をユーザーが解除したうえで実施。**2 段階とも通った。**

### 20.1 段階 1: 不一致を検出できること（PC-B = `v7_mismatch`）

差は `VFX_Player_Slash2` の `NetMode` のみ（`Local` / `Cosmetic`）。エントリ数は同じ。

| 判定基準 | 結果 |
|---|---|
| 接続できること | **合格**。`role=client` / `clientId=1` / `connected=1` |
| Client 側に不一致の警告 | **合格**。`[Net/Client] CatalogContentHashGate: 不一致: VfxCatalog: entries local=2 remote=2(Host と同じ GameData か確認してください)。` |
| Host 側にも警告 | **合格**。`[Net/Host] CatalogContentHashGate: ContentHash 不一致(Client 1): VfxCatalog: entries local=2 remote=2 — 開発ビルド/エディタのため接続は継続します。` |
| **カタログ名 + Entry 数だけ**で実データが出ない | **合格**。ハッシュ値・AssetId・アドレス・NetMode はログ全体で 1 件も出ていない |
| 接続が切れず同期も継続 | **合格**。`connected=1`、disconnect 0 件、`track_fired` 26 / `signal_recv` 52、`vfx_active` 3〜4 |

> `entries local=2 remote=2` と**件数が同じ**でも不一致を検出できている。今回の差は `NetMode` だけで
> エントリ数は変わらないため、件数比較では捕まらないケースの確認になっている。

### 20.2 段階 2: 一致なら通ること（PC-B = `v7_normal`、Host と同じビルド）

`content_hash` は起動直後の「検証中...」2 回のあと **42 回すべて `OK`**。`CatalogContentHashGate` を含む行は
**0 件**（段階 1 では警告が出ていたので対比になる）。disconnect / Exception / `InvalidKeyException` / Placeholder
いずれも 0 件。`track_fired` 18 / `signal_recv` 36 で同期も継続。

### 20.3 残っている確認（リリースビルド相当の切断）

開発ビルドでは「警告して継続」が正しい挙動。**リリースビルドでは切断する**側は未確認で、`NetCheckBuilder` が
`BuildOptions.Development` 固定だったため確認手段が無かった。2026-09-18 にリリース向けオプションを追加し、
**PC-B（Host）と PC-C（Client）の 2 台で確認する**ことにした（PC-A は使わない）。

### 20.4 軽微な改善余地

`heartbeat` の `content_hash` 欄に不一致の文字列が毎行出続けるため、長時間の実機確認でログが膨らむ。
判定には影響しない。要否はユーザー判断。

## 21. リリースビルド相当の切断確認 — 実施手順（PC-B = Host / PC-C = Client）

**まだ実施していない。** PC-C が PC-A のホットスポットに繋がった時点で、この節のとおり行えば 10 分程度で終わる。

### 21.0 前提と役割

| 役割 | 機材 | やること |
|---|---|---|
| ネットワークの親 | **PC-A** | モバイルホットスポットと配布用 HTTP サーバー（`192.168.137.1:8765`）を動かすだけ。**ゲームには参加しない** |
| Host | **PC-B**（`192.168.137.74`） | `v8_release_normal` を Host として起動 |
| Client | **PC-C** | `v8_release_mismatch` を Client として起動。**カタログの内容だけ違う** |

**確認したいこと**: 開発ビルドでは「警告して継続」が正しい挙動（§20 で確認済み）。
**リリースビルドでは切断する**という設計側を、実機で確かめる。

### 21.1 ユーザーの操作が要るところ（Claude は行わない）

- **PC-B と PC-C の両方で、初回起動時に Windows ファイアウォールの確認ダイアログが出る。**
  「プライベート ネットワーク」を許可すること。**放置すると Block 規則が作られ、無言で繋がらなくなる**
  （2026-09-18 に PC-A でこれが起き、一晩止まった。§19）。
- ファイアウォール設定の変更は Claude が行わない（システム／セキュリティ設定の変更にあたるため）。

### 21.2 PC-C の手順（Claude が居なくても人が手で実行できる形）

PC-C を PC-A のホットスポット（SSID `WRENCH 7080`）に接続してから、PowerShell で上から順に実行する。

```
New-Item -ItemType Directory -Force C:\DDriveTest | Out-Null
Invoke-WebRequest -UseBasicParsing -Uri http://192.168.137.1:8765/v8_release_mismatch.zip -OutFile C:\DDriveTest\v8_release_mismatch.zip
Expand-Archive -Path C:\DDriveTest\v8_release_mismatch.zip -DestinationPath C:\DDriveTest\v8_release_mismatch -Force
```

**PC-B の Host が起動していることを確認してから**、Client を起動する（`<PC-B の IP>` は PC-B が知らせる。
既定では `192.168.137.74`）:

```
C:\DDriveTest\v8_release_mismatch\DDriveNetCheck.exe -ddrive-net client -ddrive-host 192.168.137.74 -ddrive-port 7777 -logFile C:\DDriveTest\client_release.log
```

40 秒ほど待ってから、次の 3 つを実行して**出力をそのまま貼る**:

```
Select-String -Path C:\DDriveTest\client_release.log -Pattern 'ContentHash|一致しません|DisconnectReason' | Select-Object -First 10
Select-String -Path C:\DDriveTest\client_release.log -Pattern 'disconnect|切断|Shutdown' | Select-Object -First 10
Select-String -Path C:\DDriveTest\client_release.log -Pattern 'heartbeat=' | Select-Object -Last 6
```

### 21.3 PC-B（Host）の手順

```
Invoke-WebRequest -UseBasicParsing -Uri http://192.168.137.1:8765/v8_release_normal.zip -OutFile C:\DDriveTest\v8_release_normal.zip
Expand-Archive -Path C:\DDriveTest\v8_release_normal.zip -DestinationPath C:\DDriveTest\v8_release_normal -Force
C:\DDriveTest\v8_release_normal\DDriveNetCheck.exe -ddrive-net host -ddrive-port 7777 -logFile C:\DDriveTest\host_release.log
```

Client が繋いできたあと:

```
Select-String -Path C:\DDriveTest\host_release.log -Pattern 'ContentHash|切断|Disconnect' | Select-Object -First 10
Select-String -Path C:\DDriveTest\host_release.log -Pattern 'heartbeat=' | Select-Object -Last 6
```

### 21.4 判定基準

| # | 基準 | 開発ビルド（§20、参考） |
|---|---|---|
| 1 | **Host が該当 Client を切断すること** | 開発ビルドでは切断せず継続していた |
| 2 | Client 側に **`NetworkManager.DisconnectReason`**（「カタログの ContentHash が一致しません…」）を含む切断が出ること | — |
| 3 | 表示は**カタログ名 + Entry 数だけ**で、ハッシュ値や AssetId などの実データが出ないこと | 合格済み |
| 4 | Host 側の `heartbeat` で、切断後に当該 Client が居なくなること | — |

### 21.5 うまくいかないときの切り分け

| 症状 | 原因の候補 |
|---|---|
| Client が `role=off` / `connected=0` のまま | **ファイアウォール**（§19）。PC-B で 7777/UDP の受信が許可されているか |
| ログが空、または `[DDriveNetCheck]` の行が 1 つも無い | **リリースビルドで `Debug.Log` が出ていない**（§21.6） |
| `content_hash=OK` と出る | **カタログが読めていない可能性**（§18）。`InvalidKeyException` と `Placeholder` の行を先に確認すること。`OK` は「一致した」とは限らず「比較対象が無くて素通りした」ことがある |
| 不一致は出るが切断しない | リリースビルドになっていない（`Debug.isDebugBuild` が true のまま）。ビルドの作り方を確認 |

### 21.6 リリースビルドでログが残るか — **確認済み（2026-09-18）。残る**

懸念していた「リリースビルドでは `Debug.Log` が出ず、`NetCheckRunner` のログが残らないのでは」は**杞憂だった**。
`Builds/v8_release_normal/DDriveNetCheck.exe` を `-ddrive-net off -logFile <パス>` で 20 秒起動したところ、
**`[DDriveNetCheck]` で始まる行が 59 件**出力された（`heartbeat=` / `ready=1 role=host` など）。

```
[DDriveNetCheck] ready=1 role=host
[DDriveNetCheck] heartbeat=1 role=host clientId=0 networkTime=0.00 activeCount=0 connected=1 ... content_hash=検証中...
```

したがって **§21 の判定は開発ビルドのときと同じ方法（ログの抽出）で行える**。代替判定を用意する必要はない。

> Unity のプレイヤーログ（`-logFile`）は Development ビルドでなくても出力されるため。`Debug.Log` が
> 剥がされるのは `#if DEVELOPMENT_BUILD` 等で明示的に囲っている場合だけで、`NetCheckRunner.LogCheck` は
> そうなっていない。
>
> **例外が 1 つある**: `NetCheckRunner.OnRemoteOneShotSkipped`（`track_skipped` のログ）だけは
> `#if DEVELOPMENT_BUILD || UNITY_EDITOR` で囲われており、リリース相当ビルドでは出ない。
> §21.4 の判定基準（切断の確認）には関係しないが、`track_skipped` を当てにした判定はできない。

**リリース相当ビルドの作り方**: `Tools > D-Drive > Build > 実機確認用 Windows リリース相当ビルド`。
出力先を指定したい場合は `NetCheckBuilder.Build(outputDirectory: ..., development: false)` を直接呼ぶ
（`development` の既定は `true` = 従来どおり開発ビルド。既存の呼び出しと CI は影響を受けない）。

## 22. リリースビルド相当の切断確認（2026-09-18、PC-B Host + PC-C Client）— **合格**

§21 の手順で実施。**4 基準すべて合格**し、開発ビルド（§20「警告して継続」）との対比が取れた。

構成は §21 の想定から変わり、**PC-B がホットスポットの親（`192.168.137.1`）で Host**、**PC-C（`192.168.137.179`）が Client**。
PC-A は関与しない。ビルドはどちらも `NetCheckBuilder.Build(development: false)` のリリース相当。

| # | 基準 | 結果 |
|---|---|---|
| 1 | Host が該当 Client を切断 | **合格**。`[Net/Host] CatalogContentHashGate: ContentHash 不一致(Client 1): VfxCatalog: entries local=2 remote=2 — リリースビルドのため切断します。` |
| 2 | Client 側に `DisconnectReason` | **合格**。`[Net/Client] NgoNetBridge: Host から切断されました(reason=カタログの ContentHash が一致しません(GameData のバージョンが異なります)。)。` |
| 3 | カタログ名 + Entry 数だけ | **合格**。ハッシュ値・AssetId は出ていない |
| 4 | 切断後に当該 Client が居なくなる | **合格**（切断ログで確認。下記 22.2-3 参照） |

Host は切断後も稼働を継続（`vfx_active` 3〜4）。Client は静止し、**自動再接続はしない**（MS2026 の規約に無いため実装していない、という設計どおり）。
Exception / Error / `InvalidKeyException` / Placeholder はいずれも 0 件。

**ハッシュの実測値が PC-A と完全に一致した**（`4466529056671198312` → `4466527957159570875`）。別マシン・別ビルドで
同じ値が出たので、`CatalogContentHasher` が環境に依存しないことの裏付けにもなっている。

### 22.1 実装と docs のずれ（**要判断**）

**不一致で切断される Client が、切断される前に演出を再生し始めている。** Client は接続直後に `activeCount=5` /
`vfx_active=1` となり `track_fired`（Vfx/Se、`late_ms=11`）が発火し、その**約 0.2 秒後**に切断された。

原因は処理の順序:

- `PresentationManager.OnClientConnected`（`:760`）は **`ClientConnected` で即座に Late Join のスナップショットを送る**
- `CatalogContentHashGate` も `ClientConnected` を購読するが、照合は**往復が要る**（Client が
  `RegisterCatalogsAsync` 完了後に自分のハッシュを `Broadcast` → Host が比較 → 結果を `SendTo`）

つまり**照合が終わる前に Late Join の同期が走る**。`docs/14_networking.md` §7 の「接続ハンドシェイクで照合:
不一致 → 切断」を字義どおり取るなら、実装は「ハンドシェイクでのゲート」ではなく**「接続を通してから事後に照合」**である。

| 案 | 内容 | 影響 |
|---|---|---|
| A（現状維持） | 0.2 秒ほど誤ったデータで演出が出てから切断される。docs の表現を実装に合わせて直す | 実害は「一瞬おかしな絵が出る」だけ。正規 Client の接続は最速 |
| B | 照合が通るまで Late Join のスナップショットを送らない | 正規 Client も 1 往復分待たされる。Late Join の復元が遅れる |

**ユーザー判断が要る。** 現状は A の挙動。

### 22.2 軽微な指摘（PC-C 側から）

1. **Client 側の `content_hash` が最後まで「検証中...」のまま**。Host は「不一致: 切断しました」と終状態を出すのに、
   Client 側だけゲートの結果が `heartbeat` の欄に反映されない（切断から 2 分後も変わらず）。判定には影響しないが、
   **この欄だけ見ると「検証が終わる前に切られた」と誤読する**。直す価値がある
2. **Host の `heartbeat` に接続中の Client 数を示す欄が無い**（`clientId=0` は Host 自身）。基準 4 は切断ログで
   判断するしかない。欄を足すと実機確認が楽になる
3. `NetCheckRunner.OnRemoteOneShotSkipped` の `track_skipped` は `#if DEVELOPMENT_BUILD || UNITY_EDITOR` で
   囲われており、リリース相当ビルドでは出ない（§21.6）

### 22.3 実施してみて分かった環境の問題（**次にやる人は必ず踏む**）

| # | 問題 | 対処 |
|---|---|---|
| 1 | **学校のプロキシ**（`HTTP_PROXY=proxy01.osaka.hal.ac.jp:8080`）が環境変数に入っており、`NO_PROXY` にホットスポットの IP が無いため **`Invoke-WebRequest` と素の `curl` が失敗する** | `curl.exe --noproxy 192.168.137.1 -O http://192.168.137.1:8765/<zip 名>` を使う |
| 2 | **Addressables の「Build Report / Debug Build Layout を有効にするか」のモーダル**がビルドのたびに出て、**MCP のツール呼び出しが全部タイムアウトする** | ユーザーがダイアログを閉じる。無人で回さない |
| 3 | **Unity エディタを開いたままだとバッチモードのビルドが失敗する** | 開いているエディタを MCP の `execute_code` で操作してビルドする |
| 4 | **セッション間の伝達経路が Markdown として解釈するため、手順書の `_` と `*` が消える**（`client_release.log` → `clientrelease.log`、`$_.Line` → `$.Line`） | 人へ渡す文面ではアンダースコアとアスタリスクを避ける（`ForEach-Object Line` 等） |

4 は盲点だった。**手順書をコードブロックで書いても、経路によっては壊れる。**

## 23. 手動接続モードでの起動手順（2026-09-22、N-2）

N-1/N-2（[14_networking.md] §14・§15）で追加した「実行中に IP を入力して接続する」開発用モードの起動手順。上記の §1〜§22 は起動時に `-ddrive-net host`/`client` を渡して自動接続する前提だったが、こちらは接続先を実行中に選べる。

1. ビルド（`DDrive.Editor.Build.NetCheckBuilder.Build(development: true)` 等）は `-ddrive-net` を付けずに作ってよい。起動時の引数だけで挙動が変わる
2. 起動時の引数に `-ddrive-net manual` を渡す（`-ddrive-host`/`-ddrive-port` は省略可。省略時は Inspector の `DefaultHostAddress`/`DefaultPort` が Host ボタン押下時の初期値になる）
3. 起動すると自動接続はせず、画面**左下**に手動接続 UI（`NetManualConnectOverlay`）が出る（左上の `NetDebugOverlay` とは別物。開発ビルド/エディタでしか出ない）
4. Host にしたい端末で IP 欄はそのまま（無視される）、Port 欄を確認して「Host で開始」を押す。ログに `[Net/Host] ... Host として起動しました(listen=0.0.0.0:<port>)。` が出れば成功
5. Client にしたい端末で IP 欄に Host の実アドレス（LAN 外なら到達可能なグローバル/ポートフォワード先 IP、同一 PC 内のループバック確認なら `127.0.0.1` または `localhost`）と Port を入力し「Client で接続」を押す。ログに `[Net/Client] ... Client として起動しました(host=<ip>:<port>)。` が出れば成功
6. 「切断」を押すと `NetworkManager.Shutdown()` が走る（`[Net] ... ネットワークを停止しました` のログ）。もう一度「Host で開始」/「Client で接続」を押せば再接続を試せる（入力欄は未接続の間だけ編集できる)
7. 接続の成否・状態（未接続/Host listening/Client 接続中/切断）は UI の状態 1 行と、既存の `NetDebugOverlay`（左上、Role/RTT/NetworkTime 等）を併読して確認する

**未実施（2026-09-22 時点）**: 実ビルドを 2 プロセス起動しての Host/Client 接続・切断・`StopNetworking()` → 再 `StartHost` の再起動確認。実装完了時点で空きメモリが約 1.2GB（ビルドの目安閾値 1.3GB 未満）だったため見送った。次回、空きメモリに余裕があるときに §21/§22 と同様の形式で結果を追記すること。

## 24. N-3: Host 1 + Client 3 のローカル確認

[14_networking.md] §16（N-3）で追加した Host 1 + Client 3 対応（`NgoNetBridge.ConnectedClientCount`・`-ddrive-expect-clients`・`client_left` ログ・役割の遅延評価）の確認手順とシナリオ。`Tools/CI/Run-NetCheck.ps1` の `$quadScenarios`（§15 の `$scenarios` とは別配列、既存 4 シナリオは無改修）が実行する。

### シナリオ

| シナリオ | 構成 | 目的 |
|---|---|---|
| `quad0` | Host + Client×3、同一 Port、全員 0ms、同時参加 | Host 1 + Client 3 の基本接続・Signal 中継・`ConnectedClientCount`（Host を除くリモート Client 数）が 3 に達すること |
| `quad_latejoin` | 同上、うち 1 人だけ 12 秒遅れて参加 | 4 人構成での Late Join 復元。他 2 人は最初から接続したまま |
| `quad_leave` | 同上、うち 1 人だけ先に正常終了して抜ける | Host が「他 Client が 1 人抜けても自分と残り 2 人は継続する」こと。Host の `client_left=<clientId>` ログと `clients=<n>` の減少（3→2 等）、残り 2 Client の `signal_recv` 継続を判定 |
| `quad_hostquit` | 同上、Host が先に終了 | 既存 `disconnect`（1v1）の 4 人版。Client×3 全員が切断検知 + 演出後片付け（activeCount/vfx_active=0）を行うこと |

ポートは既存 4 シナリオ（7801/7811/7821/7831）と重ならない 7841（quad0）/7851（quad_latejoin）/7861（quad_leave）/7871（quad_hostquit）を使う。

### 使い方

```
Tools\CI\run-netcheck.cmd quad0
Tools\CI\run-netcheck.cmd quad_latejoin
Tools\CI\run-netcheck.cmd quad_leave
Tools\CI\run-netcheck.cmd quad_hostquit
Tools\CI\run-netcheck.cmd              # 8 シナリオ全部(pair0/pair200/latejoin/disconnect/quad0/quad_latejoin/quad_leave/quad_hostquit)
```

ログは `TestResults/NetCheck/<シナリオ名>_host.log`・`<シナリオ名>_client1.log`〜`_client3.log`。判定は §15 と同じ 2 段構え（各プロセス自身の `RESULT=PASS|FAIL` 行 + このスクリプトによるクロスログの Signal 位相差）を Client 3 本ぶん繰り返し、`quad_leave` だけ追加で `Test-ClientLeftAndCountDecrease`（Host ログの `client_left` 出現 + その後の `clients=<n>` 減少）を課す。

### 結果表

**コンパイル・EditMode・PlayMode は実施済み。ビルド/run-netcheck は未実施（2026-09-22 更新）**: main（N-4 マージ済み、6ae597b）を `feat/n3-netcheck-multi-client` へマージした後、空きメモリが不安定な状態（instance ファイル経由の直接 JSON-RPC 接続、設定済み MCP クライアントのポートは Unity 側の実ポートとズレたまま）で以下を実施した:

- **compile_status → error 0**（マージ直後・軽量確認時の 2 回とも）。軽量確認時に namespace 衝突による実コンパイルエラー 1 件を発見・修正済み（[14_networking.md] §16 実装メモ）
- **EditMode 全件 → 1164/1164 green**（0 failed / 0 skipped / 0 inconclusive、所要 179 秒）
- **PlayMode 全件（`DDrive.Tests.Runtime`）→ 775/775 green**（0 failed / 0 skipped / 0 inconclusive、所要 8 秒）
- テスト実行後に残った `Assets/Tests/`（一時フィクスチャ）・`ProjectSettings/DDriveProjectSettings.asset` の意図しない差分（`_previousPackageRef:` 空行の追加）を確認し、削除・`git checkout --` で復元済み。Addressables グループ・`Assets/GameData/` には差分なし
- **`NetCheckBuilder.Build()` での再ビルド・`Tools\CI\run-netcheck.cmd`（8 シナリオ）は未実施**: EditMode/PlayMode の完走後、空きメモリが 1.3GB 未満（Unity 自身のメモリ使用量が複数回の domain reload で累積して増加したことが主因と推測。他プロセスを閉じる/Unity 再起動などユーザー側の対応が必要）まで低下し、Player ビルドという最も重い操作を安全に実行できる状態に回復しなかったため見送った。以下は実行結果ではなく、次回メモリに余裕があるとき（できれば Unity Editor 再起動直後）に埋める表のプレースホルダ。

| シナリオ | Host | Client1 | Client2 | Client3 | Signal 中継（位相差） | 追加チェック |
|---|---|---|---|---|---|---|
| pair0 | 未実施 | 未実施 | - | - | 未実施 | - |
| pair200 | 未実施 | 未実施 | - | - | 未実施 | - |
| latejoin | 未実施 | 未実施 | - | - | 未実施 | - |
| disconnect | 未実施 | 未実施 | - | - | 未実施 | - |
| quad0 | 未実施 | 未実施 | 未実施 | 未実施 | 未実施 | - |
| quad_latejoin | 未実施 | 未実施 | 未実施 | 未実施 | 未実施 | - |
| quad_leave | 未実施 | 未実施 | 未実施 | 未実施 | 未実施 | 未実施(client_left/clients 減少) |
| quad_hostquit | 未実施 | 未実施 | 未実施 | 未実施 | 未実施 | - |

次回実施する手順: (1) Unity Editor を起動し MCP（isuzu-unity 優先）を繋ぐ、(2) `compile_request` → error 0、(3) EditMode 全件 + PlayMode の `Net|Presentation|ContentHash` を green、(4) `Tools > D-Drive > Build > 実機確認用 Windows 開発ビルド`、(5) 空きメモリ 1.3GB 以上を確認してから `Tools\CI\run-netcheck.cmd` を実行、(6) `TestResults/NetCheck/summary.md` の内容をこの表に転記する。

## 25. 実機 4 人テストの手順

N-3 のシナリオ確認がローカル（127.0.0.1）で PASS した後、実機での Host 1 + Client 3 確認を行う手順（§5〜§22 の 1 対 1/1 対 2 実機確認の 4 人版）。

### 構成

| | マシン A | マシン B | マシン C |
|---|---|---|---|
| 役割 | **Host** | **Client×2**（同一 PC で `DDriveNetCheck.exe` を 2 プロセス起動） | **Client×1** |
| ネット | §1 のホットスポット親、または LAN | ホットスポット/LAN に接続 | ホットスポット/LAN に接続 |
| Unity | Editor（Host は Play Mode でも可）または開発ビルド | 不要（ビルド済み Player のみ） | 不要（ビルド済み Player のみ） |

マシン B で 2 プロセス起動する場合は `-logFile` を別ファイルにする（例 `C:\DDriveTest\ClientB1.log`/`ClientB2.log`）。ポートは 3 プロセスとも同じ Host の Port（既定 7777）へ接続する（NGO は Client ごとに別ポートを使わないため、同一 PC から複数プロセスで接続しても Port は競合しない）。

### Auto 起動（`-ddrive-net host`/`client`）を使う場合

マシン A（Host）:

```
DDriveNetCheck.exe -ddrive-net host -ddrive-host 0.0.0.0 -ddrive-port 7777 -ddrive-expect-clients 3 -logFile PlayerHost.log
```

マシン B（Client×2、それぞれ別ウィンドウ/別コマンドプロンプトで起動）:

```
DDriveNetCheck.exe -ddrive-net client -ddrive-host <マシン A の IP> -ddrive-port 7777 -logFile C:\DDriveTest\ClientB1.log
DDriveNetCheck.exe -ddrive-net client -ddrive-host <マシン A の IP> -ddrive-port 7777 -logFile C:\DDriveTest\ClientB2.log
```

マシン C（Client×1）:

```
DDriveNetCheck.exe -ddrive-net client -ddrive-host <マシン A の IP> -ddrive-port 7777 -logFile C:\DDriveTest\ClientC.log
```

`-ddrive-expect-clients 3` は Host 側にだけ付ける（§16 のとおり Client 側では意味を持たない）。`-ddrive-autotest <name>` を追加すればヘッドレス自動判定（`RESULT=PASS|FAIL`）も使えるが、実機確認では省略して常駐させ、目視 + ログで確認してよい。

### N-2 の手動接続 UI（`-ddrive-net manual`）を使う場合

自動接続ではなく、起動後に画面左下の手動接続 UI（[14_networking.md] §15、[29] §23）から接続したい場合は、全端末を `-ddrive-net manual` で起動し、マシン A で「Host で開始」を押した後、マシン B（2 回）・マシン C で IP に「マシン A の IP」・Port に「マシン A の Port」を入力して「Client で接続」を押す。`-ddrive-expect-clients` は Manual モードでは `NetCheckRunner` の自動判定（`-ddrive-autotest`）を使わない限り意味を持たないため、目視確認（`NetDebugOverlay` の「Clients: n」が 3〔Host を除くリモート Client 数〕になること）で代用する。

### ファイアウォール / ポート開放の注意

- Host（マシン A）のファイアウォールで UDP 7777（または指定した Port）の**受信**を許可する必要がある（§2 の初回起動ダイアログ、または `netsh advfirewall` で `DDriveNetCheck.exe` の Inbound を許可）。マシン B・C 側は送信のみのため通常は追加設定不要
- 同一 PC（マシン B）で 2 プロセス起動する場合、Windows は送信元ポートを自動的に別々に割り当てるため、Host 側からは 2 つの異なる `ClientId` として区別される（同一 IP からの複数接続は NGO/UnityTransport の制約に抵触しない）
- ホットスポット経由の場合、DHCP でマシン B/C の IP が変わることがあるので、接続前に `ipconfig` で確認する

### 確認項目チェックリスト

- [ ] マシン A の `NetDebugOverlay`（または `PlayerHost.log` の `heartbeat`）で `clients=3`（Host を除くリモート Client 数。§16）になる
- [ ] マシン B・C それぞれで接続成功（`[Net/Client] ... Client として起動しました` ログ、Exception/Error 0 件）
- [ ] Signal 中継: マシン A の `signal_fire` と各マシンの `signal_recv` が対応する（§4 の位相差の目安、数ティック以内）
- [ ] 3 人のうち 1 人（例: マシン C）だけ終了 → マシン A の `client_left=<clientId>` ログ + `clients=2` への減少、マシン B・残る接続は継続（`signal_recv` が途切れない）
- [ ] マシン A（Host）を終了 → マシン B・C 全員が `disconnected=1` を検知し、進行中の演出（VFX 等）が消える
- [ ] 初回起動時のファイアウォール許可ダイアログが出た場合は、その旨と対応（プライベート/パブリックいずれを許可したか）をこの節に追記する

**未実施（2026-09-22 時点）**: 実機環境（複数 PC）を用意できなかったため、本節の手順に沿った実機確認は未実施。次回実機確認時にこの節へ結果（ログ抜粋・スクリーンショット・チェックリストの結果）を追記すること。
