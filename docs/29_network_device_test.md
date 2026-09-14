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
