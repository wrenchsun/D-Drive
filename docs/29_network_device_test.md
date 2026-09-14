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
| `-ddrive-sim-latency <ms>` | 未指定 = 0 | UnityTransport の Network Simulator の遅延(ms)。`SetDebugSimulatorParameters` の `packetDelay` 引数 |
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
- `[DDriveNetCheck] heartbeat=1 role=... clientId=... networkTime=... activeCount=N` — 1 秒おき(または activeCount が変化した時)。**Late Join の判定**: 新規接続したクライアントの `activeCount` が `0` → `1` に変わる行が出れば復元成功
- `[DDriveNetCheck] play=<回数> startNetTime=...` — Host が剣攻撃デモ(`PRES_Demo_SkillSlash`)を Play したとき(Host 側のみ)
- `[DDriveNetCheck] signal=hit` — Signal("hit") を発火したとき(Play から `signalDelaySeconds`(既定 0.5s)後、全ピア)
- `[DDriveNetCheck] forged_cancel_sent=<key>` — Client が偽造 Cancel を送信したとき(既定 5 秒おき、Client のみ)。**偽造メッセージ破棄の判定**: この行の直後(同じフレーム〜数フレーム以内)に **Host または他クライアントの `Player.log` に `[Net/Host]` または `[Net/Client]` の警告(「送信元 ClientId(...) が発行者と一致しないため破棄しました」)が出て、`heartbeat` の `activeCount` が変化しない**ことを確認する
- `[Net/Host]` / `[Net/Client]` — `NgoNetBridge`/`PresentationManager` のログ全般(接続・レート制限・発行者検証の破棄など)

**位相差の判定**: Host/Client 双方の `heartbeat` 行を `play` の直後(数秒間)で突き合わせ、`networkTime` の差が概ね RTT/2 以内(数十 ms 以内、`-ddrive-sim-latency` を上げた場合はその分)であれば OK(`NetDebugOverlay` の RTT 表示も併用)。

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

### 実機確認で見つかった課題（2026-09-14、修正チケットへ）

1. **遅延シミュレーターが効いていない疑い**: `-ddrive-sim-latency 200` でも RTT が 6 ms。`NgoTransportConfigurator` が `SetDebugSimulatorParameters` を `StartClient`/`StartHost` の後（ドライバ生成後）に呼んでいる、または UnityTransport 2.x で当該 API が無効、の可能性。RTT の値（`GetCurrentRtt`）がシミュレーター遅延を含まない可能性もあるので、アプリ層の往復時間（Ping の往復）も併記して判定できるようにする
2. **Client 側で Signal 中継を観測できない（計測の穴）**: `NetCheckRunner` の `signal=hit` は Host が `handle.Signal("hit")` を呼んだ直後にだけ出す実装（Host 179 件 / Client 0 件）。§4 の「全ピア」は誤り。Client で OnSignal トラックがネット経由で発火したことをログに出す仕組みが無く、**Signal 中継は実機で未検証**
3. **Late Join 直後に Presentation が Placeholder で解決される**: Client に `[DDrive] Unregistered AssetId 0xCD2986D134D20E66 resolved to Placeholder.` が 1 件（= `PRES_Demo_SkillSlash` 自身、Host 側には無し）。接続直後のスナップショット受信がカタログのロード完了より先に処理される順序の問題と推測。接続直後の `activeCount` 5 → 0 もこれが原因の可能性
4. **偽造 Cancel の破棄ログが 1 件欠落**（0ms、35 送信 / 34 破棄、`HandleNetKey 0x01047DA3` は Host ログに出現なし）: 対象の演出が完了済みで未知キーとして黙って破棄された可能性。未知キーの破棄も判定できるようログを出す（開発ビルドのみ）
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
