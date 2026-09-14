# 14. ネットワーク（通信）設計

関連: [01_architecture.md](01_architecture.md) / [02_core_framework.md](02_core_framework.md) / [08_presentation.md](08_presentation.md)

---

## 1. 基本方針

**アセットシステム本体はトランスポート非依存**（Netcode for GameObjects / Photon / Mirror のどれでも動く）。ただし以下の 3 点を最初から基盤に織り込む。後付けが最も困難な部分だからである。

1. **ID がそのままネットワークメッセージになる** — 「何を再生するか」を Prefab 参照でなく ulong ID で送れる。本システムの ID 駆動設計はネットワークと本質的に相性が良い
2. **全アセットに複製区分（NetMode）を持たせる** — 「クライアント演出」か「サーバー権威」かをデータで宣言する
3. **再生の再現は Instance 同期ではなくイベント + 経過時間で行う** — 遅参加者(Late Join)・観戦に必要

```
Server                                 Client A / B / C
Presentation.Play(SkillSlash, ctx)
  │  権威判定・ゲームロジック
  └─▶ NetBridge.Broadcast(
        PlayMsg{ presId, selfNetId,     ──▶ 受信 → Presentation.PlayRemote(msg)
                 targetNetId, pos,           → ローカルの各 Manager で再生
                 serverTime })                 (SE/VFX はクライアント側で自律実行)
```

## 2. INetBridge（トランスポート抽象）

```csharp
public interface INetBridge          // Foundation 層。実装はアダプタとして差し替え
{
    bool IsServer { get; }
    bool IsClient { get; }
    double NetworkTime { get; }                       // 全端末で単調同期した時刻
    void Broadcast<T>(in T msg, NetChannel ch) where T : INetMessage;
    void SendTo<T>(ulong clientId, in T msg, NetChannel ch);
    IDisposable Subscribe<T>(Action<ulong, T> handler);
    Transform ResolveNetObject(ulong netId);          // NetId → Transform
}
// NetChannel: ReliableOrdered / Unreliable(演出用)
```

- シングルプレイでは `LocalLoopbackBridge`（自分に即時配送）を挿す → **ゲームコード・データは通信の有無で一切変わらない**
- アダプタは v1 で NGO（Netcode for GameObjects **2.13.2**）実装 `NgoNetBridge` を 1 つ用意。Photon 等は同インタフェースで追加

### NgoNetBridge の配送規則（2026-09-08 改定・MS2026 統一）

| 呼び出し | 実装 | 備考 |
|---|---|---|
| Host から `Broadcast` | `[Rpc(SendTo.ClientsAndHost)]` | **ホスト自身にも届く**。旧 `[ClientRpc]` は NGO 2.x では `SendTo.NotServer` 扱いでホストに届かず、ホストの Manager が Cosmetic を再生できなかった |
| Client から `Broadcast` | `[Rpc(SendTo.Server)]` で Host に「依頼」→ Host がレート制限（60/秒/クライアント）と種別登録を検証して全員へ配る | §9 の「無条件中継しない」の実装。Simulated はこの経路を通さない |
| Host から `SendTo(clientId)` | `[Rpc(SendTo.SpecifiedInParams)]` + `RpcTarget.Single` | Client からの呼び出しは警告して破棄 |
| `NetChannel.Unreliable` | `RpcDelivery.Unreliable`。ペイロードが 1000 bytes を超える場合は Reliable にフォールバック | NGO の Unreliable は 1 パケット(MTU)制限があるため |
| ログ | `[Net/Host]` / `[Net/Client]` プレフィックス | MS2026 Networking.md §5 と同じ |

## 3. NetMode（AssetFlags 拡張）

```csharp
public enum NetMode
{
    Local,        // 完全ローカル (UI, メニューSE, 自分専用演出)
    Cosmetic,     // 全クライアントで再生するが結果に影響しない (SE/VFX/CameraShake)
                  //   → Unreliable 配送可・欠落しても再送しない・クライアント自律実行OK
    Simulated,    // ゲーム結果に影響する (PrefabのSpawn, 当たり判定持ちProjectile)
                  //   → サーバー権威。クライアントは要求のみ、生成はサーバー
}
```

- `AssetFlags.Net : NetMode` を全種別共通で追加（[02] §2）
- 既定値: Audio/VFX/Canvas/Material = Cosmetic または Local、Prefab(Projectile 等) = Simulated
- Manager は Play/Spawn 時に NetMode を見て自動で配送経路を選ぶ。**プログラマーは通常 API を呼ぶだけ**

## 4. 種別ごとの通信規則

| 種別 | 規則 |
|---|---|
| SE / BGM | 常にクライアントローカル実行。Cosmetic はイベントとして受信して各自再生（音量・距離減衰は各クライアントのリスナー基準） |
| VFX | Cosmetic。`VfxNetMsg{ vfxId, anchorNetId or pos, paramOverrides }`。欠落許容（Unreliable） |

> **実装メモ(2026-07-27, Phase 2 の 2-8 時点)**: SE/VFX とも `SeNetMsg`/`VfxNetMsg{ SeId/VfxId, AnchorNetId(予約, 常に0), Position }` を実装済み。**`Position` のみが実際に使われる**(送信時点でのワールド座標を1回送るだけで、受信側でのアンカーへのライブ追従は行わない)。`AnchorNetId` は将来の NGO アダプタ向けの予約フィールド — `INetBridge` に Transform→NetId の逆引きが無く、Foundation 層だけでは安全に解決できないため。`paramOverrides` の同期も未実装（VFX の Params は各クライアントのローカルデフォルト値で再生される）。Cosmetic 指定時、Play/Spawn 呼び出しは即座に再生せず内部バッチへ積まれ、同一 Tick 内の呼び出しは1つの `SeNetBatchMsg`/`VfxNetBatchMsg` にまとめて Broadcast される（§8）。送信元自身も Broadcast を自分で受信して初めて再生する（直接再生しない。二重再生防止）ため、`PlaySe`/`Vfx.Spawn` は Cosmetic データに対して常に Invalid ハンドルを返す。
| Animation | 原則 NetworkAnimator 等の既存同期に任せ、本システムの Frame イベント（SE/VFX）は**各クライアントがローカルの Animator から発火**（イベントを送らない = 帯域ゼロ・ズレなし） |
| Prefab | Simulated はサーバーが `Prefabs.Spawn` → NetBridge の NetworkObject 複製。PrefabData に `NetworkPrefab` 検証（§8） |
| Canvas / UI | 常に Local。ネット対象外 |
| Material | Cosmetic（スキン替え等は見た目のみ）。装備など結果に影響する場合はゲームロジック側の同期変数から駆動 |
| Presentation | §5 参照。ネット対応の主役 |

> **実装メモ（2026-09-11、4-13）**: `PrefabsManager` に `INetBridge netBridge = null` を追加（既定 null = シングルプレイ相当で今までどおり常にローカル Spawn）。`Assets/DDrive/Runtime/Net/PrefabMessages.cs` に3種のメッセージを追加。
> - `PrefabSpawnRequestMsg{ PrefabId, Position, Rotation, RequestKey }`: クライアント→サーバーの Spawn 要求。`netBridge.IsServer == false` のとき `SpawnData` はローカル Instantiate せず、`SendTo(serverClientId=0, ...)` でこのメッセージを送って `Handle<PrefabMarker>.Invalid` を返す（クライアントは Simulated Prefab のローカル Handle を一切持たない。観測は将来 NGO の NetworkObject 経由に委ねる）。
> - `PrefabSpawnedMsg{ PrefabId, NetObjectId, Position, Rotation, RequestKey }`: サーバーが権威生成した直後に `Broadcast` する通知のみのメッセージ。`NetObjectId` は現状常に 0（`LocalLoopbackBridge` 経路。NGO の `NetworkObjectId` 配線は Phase 6）。
> - `PrefabDespawnedMsg{ NetObjectId }`: サーバーが Simulated インスタンスを Despawn した際に `Broadcast`。
> - サーバー（`netBridge.IsServer == true`）は `PrefabSpawnRequestMsg` を Subscribe し、クライアントごとに 60 件/秒のレート制限（`NgoNetBridge.ConsumeRelayBudget` と同じ考え方の簡易ウィンドウ）を掛けたうえで、`PrefabId` がレジストリ上 `NetMode.Simulated` の `PrefabData` に解決できる場合のみ権威 Spawn する。非 Simulated ID への要求は ID ごとに 1 回だけ警告して無視する。
> - Cosmetic/Local な Prefab は今までどおり無条件でローカル Spawn（挙動変更なし）。
> - Validator（`PrefabDataValidator`）: `NetMode.Simulated` かつ Prefab に `Unity.Netcode.NetworkObject` が無い → Error。`Kind` が Projectile/Gimmick/Character 以外 → Info。`Flags.Pool.Kind == Pooled` との併用 → Warning。
> - 見送り: 実際の NGO `NetworkObject` 複製（`NetworkManager.SpawnManager.InstantiateAndSpawn` 等）と `NetObjectId` の実配線、Late Join 時の Simulated インスタンス一覧のスナップショット同期は Phase 6（NGO 統合）で行う。テストは `PrefabSimulatedSpawnTests`（`FakeNetBridge` で IsServer/IsClient を切替、ループバック配送は `SendTo`/`Broadcast` が同一インスタンス内の Subscribe ハンドラへ即時配送する簡易実装）。

## 5. Presentation のネットワーク再生

```csharp
// サーバー(または行為者)側 — 通常APIのまま。NetMode=Cosmetic なら自動 Broadcast
Presentation.Play(PRESENTID.SkillSlash, ctx);

[NetMessage] struct PresentationPlayMsg
{
    ulong PresId;
    ulong SelfNetId; ulong TargetNetId;   // 0 = なし → Position 使用
    Vector3 Position;
    double StartNetTime;                  // ★NetworkTime 基準の開始時刻
    ushort Seed;                          // ランダム要素の同期(SE選択等)
}
```

- 受信側は `PlayRemote(msg)`: `NetworkTime - StartNetTime` 分だけ**シークして再生開始**（遅延分を吸収し全端末で位相が揃う）
- `OnSignal("hit")` トラック: ヒット判定はサーバー権威 → サーバーが `SignalMsg{ handleNetKey, "hit" }` を Broadcast。クライアントの HitStop/Shake はそれを受けて発火
- 行為者クライアントは**予測再生**（自分の入力に対し即時ローカル再生）可。フラグ `PredictLocal` を PresentationData に持ち、サーバー確定と重複しないよう handle キーで抑制
- Late Join / 観戦: ループ中の Presentation・常駐 VFX は「アクティブ演出リスト（presId + StartNetTime + ctx）」をサーバーが保持し、接続時スナップショットとして送る → 受信側はシーク再生で復元。ワンショット演出は復元しない（設計として割り切る）

## 実装メモ（2026-09-14、5-8）

実装: `Runtime/Net/PresentationMessages.cs`（新規: `PresentationPlayMsg`/`PresentationSignalMsg`/`PresentationCancelMsg`）、`Runtime/Presentation/PresentationManager.cs`（ネット再生本体）、`Runtime/Presentation/PresentationData.cs`（`PredictLocal` フィールド追加）、`Runtime/Presentation/PresentationDataValidator.cs`（Info 検査追加）、`Runtime/Loop/DDriveRuntimeBootstrap.cs`（`netBridge: NetBridge` を渡すよう変更）。テストは `Tests/Runtime/PresentationNetTests.cs`（新規）。Late Join（5-9）は依存チケットのため別コミットで追記する（下記§5実装メモ「5-9」参照）。

- **メッセージ設計**: `PresentationPlayMsg{ PresId, SelfNetId, TargetNetId, Position, StartNetTime, Seed, HandleNetKey }` / `PresentationSignalMsg{ HandleNetKey, SignalKeyHash }` / `PresentationCancelMsg{ HandleNetKey }`。SelfNetId/TargetNetId は SE/VFX の Cosmetic メッセージ（`CosmeticMessages.cs`）と同じ既知の制約により常に 0 で送る（§4 実装メモ参照）。`HandleNetKey` は送信者(行為者)が `PresentationManager.NextHandleNetKey()`（インスタンスごとの乱数 salt ⊕ NetworkTime のビット ⊕ ローカル連番、0 は予約して返さない）で 1 回だけ生成し、以後の Signal/Cancel/Late-Join スナップショットの突き合わせキーとして使う（`PrefabSpawnRequestMsg.RequestKey` と同じ考え方）。**要判断**: 32bit の salt/連番方式は衝突確率が十分低いという判断だが、`INetBridge` にまだ `LocalClientId` が無いため厳密な一意性(clientId を上位ビットに埋める等)は保証していない。NGO 統合(Phase 6)で `LocalClientId` 相当が手に入ったら見直すこと。
- **SignalKey の帯域節約**: 事前登録テーブル方式ではなく、16bit FNV-1a ハッシュ（`PresentationManager.HashSignalKey`、0 alloc・純関数）を採用した。同一 Presentation 内で衝突する可能性はゼロではないが、実運用の SignalKey 種類数は少数（"hit" 等）のため許容した。
- **NetChannel**: Play/Signal/Cancel はいずれも `NetChannel.ReliableOrdered` で送る（§8 のバッチ化・Unreliable は VFX/SE 単位の高頻度イベント向けであり、Presentation の Play/Signal/Cancel は頻度が低く、欠落してよい類のイベントでもないため）。SE/VFX のような同一 Tick 内バッチ化も行わない(Presentation の呼び出し頻度は VFX/SE ほど高くないと想定した設計判断)。
- **配送経路**: `PresentationManager` は `AudioManager`/`VfxManager`/`PrefabsManager` と同じ形で `INetBridge netBridge = null` を受け取る（null=シングルプレイ相当で常にローカル、既存の原則どおり）。`Flags.Net == NetMode.Cosmetic` かつ `netBridge != null` のときだけネット経路に乗る。`NetMode.Simulated` は Presentation には意味を持たせず(Info 検査で警告)、通常のローカル再生にフォールバックする。
- **予測再生と重複抑制**: `PresentationData.PredictLocal=true` のとき、`Play()` 呼び出し元(行為者)は即座にローカル Instance を生成して `Handle` を返す(`FireDueTracks` 相当が同期的に走る)と同時に Broadcast する。Broadcast は Host/Client 問わず(NGO では Client→Host→全員の中継を `NgoNetBridge` が既存のとおり行う。Loopback/テストでは直接)、送信元自身にも `ClientsAndHost` 経由で返ってくる。受信側は `HandleNetKey` を `_networkedHandles` で引き、既に自分の Instance(予測済みまたは受信生成済み)があれば**新規生成せず**、Host なら「アクティブ演出リスト」への登録だけ行う(二重発火・二重生成を防ぐ)。`PredictLocal=false` の Presentation は SE/VFX の既存 Cosmetic と同じく、自分の Broadcast を受信して初めて再生する。
- **開始時刻シーク（`PlayRemote` = `OnReceivePlayMsg`）**: `elapsed = max(0, NetworkTime - StartNetTime)`。`duration > 0 && elapsed >= duration` なら「到着時点で既に終わっている演出」として復元しない（Late Join のワンショット非復元と同じロジックを再利用）。それ以外は `Elapsed = elapsed` で Instance を作り、`Time <= elapsed` の AtTime トラックのうち **one-shot は鳴らさずスキップ**（`Fired` だけ立てる）、**continuous(ループ)系だけ今から再生開始**する。判定は `PresentationManager.IsContinuousAtSeek`: Anim/Anim2D/Bgm は常に continuous、**Vfx/Se はデータ側のループ設定（`VfxData.LifeMode==Loop` / `SeData.Loop`）を見て判定**する（一撃 VFX・単発 SE はワンショットのままスキップし、常駐 VFX・ループ SE だけ復元対象にする。5-9 の「途中参加でループ VFX/BGM が復元」AC に必要な判定で、当初は Kind 単位の固定分類だけだったが Vfx を一律ワンショット扱いにしていたため late-join の VFX 復元テストが失敗し、この形に修正した)。Anim/Anim2D はさらに `AnimManager.Seek(handle, normalizedTime)` で位相を合わせる（`AnimData.LengthSec` から算出。**要判断**: Bgm は `BgmManager` に Seek API が無いため頭から再生するだけで位相は合わせていない。厳密な同期が必要になったら `BgmManager` 側に再生位置指定 API を追加すること）。
- **Signal 中継**: `handle.Signal(key)` は Instance が `IsNetworked` なら直接発火せず `PresentationSignalMsg` を Broadcast する（Host 権威。Client 発は `NgoNetBridge` が既存の「Client→Host 依頼→レート制限検証→全員へ配る」経路を通るため、Presentation 側で追加の検証コードは書いていない。受信側で `HandleNetKey` が未知なら何もしない、というのが ID 未検証時の安全側フォールバックになっている）。受信側 `OnReceiveSignalMsg` は `SignalKeyHash` が一致する未発火の OnSignal トラックを発火する。
- **Cancel 中継**: 同様に `PresentationCancelMsg` を Broadcast してから、自分を含む全員が受信して初めて `CancelInternal` する（直接 Cancel すると Broadcast 前に自分だけ止まってしまうため）。
- **HitStop は全員が実行する（観戦者を区別しない、既定）**: §6 の「HitStop はローカル演出として各自実行」を、MS2026 の 1v1 前提（当事者は必ず 2 人だけ）ではそのまま「Signal を受け取った全ピアが同じように HitStop する」でよいと判断した。**要判断**: 将来 3 人以上の観戦者が入る構成になった場合、観戦者は HitStop すべきでない可能性があるため、その時点で PlayContext 側に「当事者かどうか」を判定する仕組みを追加すること。
- **Haptic の LocalPlayerOnly 誤爆防止（オーケストレーターの追加指示、2026-09-14）**: `SelfNetId`/`TargetNetId` を常に 0 で送る既知の制約により、受信側は「この事象が自分に起きたことか」を判定できない。そのため `PresentationInstance` に `PlayedViaNetworkReceive`(= `PresentationPlayMsg` を受信して生成した Instance かどうか。予測再生した行為者自身の Instance は false)を持たせ、`FireHaptic` で `PlayedViaNetworkReceive && HapticsData.LocalPlayerOnly` のときは再生をスキップする（誤爆防止の安全側デフォルト）。**要判断**: この結果、`PredictLocal=false` の Presentation では行為者自身も(自分の Broadcast を受信して初めて再生する経路を通るため) `PlayedViaNetworkReceive=true` になり、LocalPlayerOnly な Haptic が鳴らない。真に「自分の事象か」を判定するには `INetBridge` に `LocalClientId` 相当を追加し `SelfNetId` と比較する必要がある(Phase 6、NGO 統合時に見直す)。当面、行為者自身にも確実に Haptic を鳴らしたい演出は `PredictLocal=true` にする運用で回避できる。
- **DDriveRuntimeBootstrap**: `Presentation = new PresentationManager(..., NetBridge)` に変更。現状 `NetBridge` は常に `LocalLoopbackBridge`(NGO 統合は Phase 6)のため、この変更自体はランタイムの挙動を変えない(Audio/Vfx/Prefabs と同じ配線パターンに揃えただけ)。
- **見送り(Phase 6 へ)**: `NgoNetBridge` の実配線(Bootstrap への差し込み)、`SelfNetId`/`TargetNetId`/`LocalClientId` の実解決、SE のランダム選択(`Seed` の実消費)、`PresentationSignalMsg`/`PresentationCancelMsg` へのクライアント別レート制限の追加検証(現状は `NgoNetBridge` の汎用レート制限のみに依存)。

## 実装メモ（2026-09-14、5-9）

実装: `Foundation/Net/INetBridge.cs`（`event Action<ulong> ClientConnected` 追加）、`Foundation/Net/LocalLoopbackBridge.cs` / `Runtime/Net/NgoNetBridge.cs` / `Tests/Runtime/{FakeNetBridge,CountingNetBridge}.cs`（同イベントの実装を追加）、`Runtime/Presentation/PresentationManager.cs`（アクティブ演出台帳 + Late Join 送信、5-8 で追加した基盤の上に積む）。テストは `Tests/Runtime/PresentationLateJoinTests.cs`（新規）。

- **Late Join**: `INetBridge` に `event Action<ulong> ClientConnected` を追加した(最小限の新規接続通知)。`LocalLoopbackBridge`/`Tests/Runtime/{FakeNetBridge,CountingNetBridge}` は手動発火用の `RaiseClientConnected(clientId)` を持つ(シングルプレイでは通常発火しない)。`NgoNetBridge` は `OnNetworkSpawn`/`OnNetworkDespawn` で `NetworkManager.OnClientConnectedCallback` を中継する。`PresentationManager` は Host(`_netBridge.IsServer`)のときだけ「アクティブ演出台帳」(`_activeNetworked: Dictionary<HandleNetKey, {Data, Ctx, StartNetTime, Seed}>`)を保持し、`OnClientConnected` で台帳の全エントリを新規クライアントへ `SendTo`(専用の Late Join メッセージは用意せず、既存の `PresentationPlayMsg` をそのまま送ることで `OnReceivePlayMsg` の開始時刻シーク/ワンショットスキップ・LocalPlayerOnly 判定をそのまま再利用する)。台帳への登録・削除は Instance の生成(`OnReceivePlayMsg`/予測確定)・消滅(`Cleanup`、Complete/Cancel 双方)に同期させているため、**ワンショットは尺が短いため自然に台帳から外れて復元されない**(専用の「これは復元しない」フラグを増やさない設計判断)。

## 6. 時刻・乱数・決定性

- 演出のスケジュール（AtTime トラック、Frame イベント）は `NetworkTime` 基準に統一。`Time.time` を Foundation で直接使わない（`ITimeSource` 注入。ローカル時は Time.time 実装）
- SeData の Random 選択・PitchRange は `Seed` から決定的に引く → 全クライアントで同じ音が鳴る（こだわらない場合は Local 乱数でも可、データフラグで選択）
- HitStop / TimeScale はローカル演出として各自実行（サーバーのシミュレーション時間には影響させない）

## 7. カタログ整合性（コンテンツバージョン照合）

クライアントとサーバーでアセット定義がズレていると「相手には見えない VFX」等の不具合になる。

- ビルド時にカタログごとの **ContentHash**（全 Entry の ID + Data ハッシュ）を生成
- 接続ハンドシェイクで照合: 不一致 → 切断 or 互換モード（ID 存在チェックのみ）をプロジェクト方針で選択
- Addressables Remote 更新時はカタログバージョンを合わせて配信。`MinCompatibleVersion` で下位互換範囲を宣言

## 8. 帯域・最適化

- ID は ulong(8B) だが、接続時に「セッション ID テーブル」（登場しうる ID → u16 インデックス）を交換し **2B に圧縮**（オプション。v1 は ulong 直送で可）
- Cosmetic は Unreliable + 集約（同フレームの複数演出を 1 パケットにバッチ）
- 距離カリング: `NetRelevanceRadius` を AssetFlags に追加可能（遠くのプレイヤーの足音 SE は送らない）— NetBridge の関心管理(Interest Management)に委譲

## 9. セキュリティ / チート耐性

- クライアント発の `PlayMsg` をサーバーは無条件中継しない: Simulated は必ずサーバー生成。Cosmetic の中継もレート制限 + 発信者の状態検証（死亡中に攻撃演出を送っていないか等はゲームロジック側フック）
- ID 存在検証: 受信 ID が Registry に無い → 破棄 + ログ（Placeholder は**ローカル開発時のみ**。ネット受信では出さない）

## 10. Validation（ネットワーク関連）

| 検査 | 重度 |
|---|---|
| NetMode=Simulated の Prefab に NetworkObject 相当が無い | Error |
| Presentation 内に Simulated トラックと PredictLocal の競合 | Error |
| Cosmetic なのに Reliable 大容量パラメータ（Texture 等）をイベント送信 | Warning |
| NetMode 未設定（既定値のまま大量放置） | Info（レポート） |
| ContentHash 生成対象外のカタログ | Error（CI） |

## 11. 導入方針（マルチプレイは最初から対応）

マルチプレイは将来拡張ではなく **v1 の必須要件**とする。各機能は単体で作ってから通信対応を後付けするのではなく、**対応する Manager の実装と同じフェーズで通信対応まで完成させる**（[11_tasks.md](11_tasks.md) に組込済み）。

| 時期 | 実装内容 |
|---|---|
| Phase 0 | NetBridge 抽象 + Loopback + **NGO アダプタ** / ITimeSource(NetworkTime) / NetMode フラグ / Seed 決定的乱数 |
| Phase 2 | VFX/SE の Cosmetic 配送（Broadcast + Unreliable バッチ） |
| Phase 4 | Prefab の Simulated Spawn（サーバー権威生成 + NetworkObject 検証） ✅ 2026-09-11(4-13。NetworkObject の実複製は Phase 6 で接続) |
| Phase 5 | Presentation ネット再生（開始時刻シーク / Signal 中継 / 予測再生）+ Late Join 復元 |
| Phase 6 | カタログ ContentHash 照合 / 受信検証・レート制限 / ネット Validator / 2 クライアント自動テスト |

- 各マイルストーンのデモは**常に 2 クライアント + サーバー構成で実施**する（[12_review.md](12_review.md)）。シングル動作のみのデモは合格としない
- CI に Loopback ⇔ NGO の両ブリッジでの PlayMode テストを含め、「シングルでしか動かない実装」の混入を機械的に防ぐ

## 12. MS2026 移植方針（統一ルール・2026-09-08）

D-Drive は最終的に **MS2026（LAN 内 1v1、NGO 2.13.2、Host+Client 方式）** へ移植する。ネットワーク関連は MS2026 の `Docs/Networking.md` を正とし、D-Drive 側の用語・実装・パッケージをそれに揃える。

| 項目 | MS2026 のルール | D-Drive での対応 |
|---|---|---|
| ネットコード | NGO **2.13.2** 固定（勝手に上げない。上げるときは全員同時） | `manifest.json` を 2.13.2 に統一（旧 2.2.0 から更新）。Multiplayer Play Mode 2.0.2 / Multiplayer Center 1.0.1 も同一 |
| 接続モデル | Host（= Server + Client）+ Client。専用サーバなし。IP 直打ち | D-Drive の「Server」は MS2026 の「Host」を指す。`INetBridge.IsServer` = Host 判定 |
| 権威 | 判定・スコア・勝敗は Host。クライアントは入力を送り、結果を受け取って描くだけ | `NetMode.Simulated` = Host 権威生成（Phase 4）。`NetMode.Cosmetic` = 「きっかけ（イベント）」を配って各自ローカル再生（§3/§4 と同じ思想）。Client 発の Cosmetic は Host 経由で中継（§2） |
| 同期してよいもの | 位置・状態・HP・「攻撃した/被弾した」イベント。パーティクル 1 粒・UI・カメラ揺れは同期しない | VFX/SE は ID + 位置だけを送る `VfxNetMsg`/`SeNetMsg`。Canvas/UI/CameraShake は `Local`。HitStop/Shake は Presentation の Signal を受けて各自ローカル発火（§5） |
| 乱数 | `Random.Range` をホストとクライアントで別々に呼ばない。ホストで引いて送る／シード同期 | `SeedRandom` + `PresentationPlayMsg.Seed`（§6）。`AnchorPoint` のランダム散らばりは **見た目専用**なので各自ローカルで可（結果に影響しない） |
| 時刻 | `Time.time` 基準の同期判定禁止。ネットワーク時刻を使う | `ITimeSource` 注入。`NetworkTimeSource` が `NetworkManager.ServerTime` を返す（§6） |
| static 状態 | ゲーム状態を static に持たない | D-Drive の静的ファサード（`Vfx`/`Audio`）は **Manager 実体への参照だけ**を持ち、状態は Manager インスタンス側。Host/Client は別プロセスなので衝突しない（MPPM の Virtual Player も別プロセス） |
| RPC 頻度 | `Update()` 内で毎フレーム RPC を投げない | Cosmetic は Tick 内でバッチ → 1 パケット（§8）。Client→Host 依頼はレート制限 |
| Prefab 登録 | NetworkObject を持つ Prefab は NetworkPrefabsList 登録必須。`Assets/DefaultNetworkPrefabs.asset` は `Assets/` 直下のまま | `NgoNetBridge` を載せる Prefab も登録対象。`DefaultNetworkPrefabs.asset` は移動しない（D-Drive でも `Assets/` 直下） |
| 初期化 | `Awake()` ではなく `OnNetworkSpawn()` | `NgoNetBridge` は状態を持たない。購読は各 Manager のコンストラクタ、Bind は `OnNetworkSpawn` 以降に行う |
| ログ | `[Net]`、Host/Client を分けて `[Net/Host]` `[Net/Client]` | `NgoNetBridge` のログを同プレフィックスに統一 |
| テスト | 1 台での確認だけで「動いた」と言わない。MPPM → 実機 2 台 + 実 LAN | [12_review.md](12_review.md) §5 の「M2 以降は 2 クライアント + サーバー構成」と同じ。`NetBridgeSmokeTest` を MPPM で回す |

**移植時の配置**: D-Drive は `Assets/DDrive/`（asmdef ごと）を MS2026 にそのまま持ち込み、ゲームコード（`Assets/_Project/Scripts/`）は `DDrive.Runtime` のみを参照する（Editor 参照禁止）。`Assets/GameData/` はカタログごと移す。MS2026 側の `Docs/Networking.md` が `[ServerRpc]`/`[ClientRpc]` を主要 API として挙げているが、NGO 2.x では統一 RPC `[Rpc(SendTo.*)]` が推奨（旧属性の `RequireOwnership` は Obsolete 警告）であり、D-Drive の bridge は統一 RPC で書く。移植時に MS2026 側の文書も同じ記述に揃える。
