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
- アダプタは v1 で NGO（Netcode for GameObjects）実装を 1 つ用意。Photon 等は同インタフェースで追加

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
| Animation | 原則 NetworkAnimator 等の既存同期に任せ、本システムの Frame イベント（SE/VFX）は**各クライアントがローカルの Animator から発火**（イベントを送らない = 帯域ゼロ・ズレなし） |
| Prefab | Simulated はサーバーが `Prefabs.Spawn` → NetBridge の NetworkObject 複製。PrefabData に `NetworkPrefab` 検証（§8） |
| Canvas / UI | 常に Local。ネット対象外 |
| Material | Cosmetic（スキン替え等は見た目のみ）。装備など結果に影響する場合はゲームロジック側の同期変数から駆動 |
| Presentation | §5 参照。ネット対応の主役 |

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
| Phase 4 | Prefab の Simulated Spawn（サーバー権威生成 + NetworkObject 検証） |
| Phase 5 | Presentation ネット再生（開始時刻シーク / Signal 中継 / 予測再生）+ Late Join 復元 |
| Phase 6 | カタログ ContentHash 照合 / 受信検証・レート制限 / ネット Validator / 2 クライアント自動テスト |

- 各マイルストーンのデモは**常に 2 クライアント + サーバー構成で実施**する（[12_review.md](12_review.md)）。シングル動作のみのデモは合格としない
- CI に Loopback ⇔ NGO の両ブリッジでの PlayMode テストを含め、「シングルでしか動かない実装」の混入を機械的に防ぐ
