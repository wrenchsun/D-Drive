# 16. カメラシェイク / コントローラー振動（ハプティクス） 詳細設計

関連: [08_presentation.md](08_presentation.md) / [02_core_framework.md](02_core_framework.md) / [14_networking.md](14_networking.md)

画面の揺れとコントローラーの振動を**他アセットと同じ ID 管理のデータ**にし、デザイナーが専用エディタで作成・調整・プレビューできるようにする。Presentation のトラックとしても単体 API としても使える。

---

# Part A — カメラシェイク（ShakeId / CameraShakeData）

## A-1. データ構造

```csharp
public class CameraShakeData : AssetDataBase
{
    public ShakePattern Pattern;      // PerlinNoise / DecaySine / Impulse / CustomCurve
    [Header("強さ")]
    public Vector3 PosAmplitude;      // 位置揺れ (m)。軸ごとに設定
    public Vector3 RotAmplitude;      // 回転揺れ (deg)。Roll だけ等も可
    public ValueDef Frequency;        // Hz (Perlin/Sine)。時間変化する周波数も表現可（[17]）
    [Header("時間・減衰")]
    public ValueDef Envelope;         // 減衰カーブ + 尺（TimeDef）の統一表現（[17]）。既定 0.3s
    [Header("方向")]
    public ShakeSpace Space;          // CameraLocal / World / FromSource(発生源→カメラ方向)
    [Header("合成")]
    public float TraumaWeight = 1f;   // 多重シェイク時の寄与度
    public int MaxStack = 3;          // 同一 Shake の同時許容数
    // Flags.Net は既定 Cosmetic (全クライアントで再生・結果に影響しない)
}
```

設計方針:

- **Trauma 方式で合成**: 複数のシェイクが重なった場合、単純加算でなく trauma 値（0..1、二乗で振幅化）に加算 → 揺れすぎ・カクつきを構造的に防止。爆発の連鎖でも破綻しない
- 揺らす対象は**カメラ本体ではなく Shake 専用の親ノード**（Cinemachine 使用時は Impulse Listener 相当の後段オフセット）。ゲーム側のカメラ制御と干渉しない
- `FromSource`: PlayContext の発生位置から「爆発は奥から手前に押される」方向性シェイクを自動計算

## A-2. Manager API

```csharp
public static class CameraFx
{
    public static ShakeHandle Shake(ShakeId id);
    public static ShakeHandle Shake(ShakeId id, Vector3 sourcePos);      // FromSource 用
    public static ShakeHandle Shake(ShakeId id, float strengthScale);    // 距離減衰等の外部係数
    public static void StopAll(float fadeOut = 0.1f);
    public static void SetGlobalScale(float s);   // オプション画面の「画面揺れ 0〜100%」
}
```

- Handle 操作: `Stop(fade)` / `SetStrength(s)`
- グローバル設定（酔い対策で揺れオフ）はオプションから `SetGlobalScale(0)` — **アクセシビリティ要件として v1 必須**
- HitStop（TimeService）とは独立。Presentation で並べて使う

# Part B — コントローラー振動（HapticId / HapticsData）

## B-1. データ構造

```csharp
public class HapticsData : AssetDataBase
{
    [Header("モーター")]
    public ValueDef LowFreq;          // 低周波モーター ドスン系。形 + 尺は ValueDef（[17]）。既定 0.2s
    public ValueDef HighFreq;         // 高周波モーター ビリビリ系
    [Header("制御")]
    public HapticPriority Priority;   // 同時再生時に強い方を優先 (Max合成)
    public bool LocalPlayerOnly = true; // 自分に起きた事象のみ振動 (既定)
    [Header("拡張")]
    public HapticExt[] Extensions;    // DualSense アダプティブトリガー等 (プラットフォーム別、任意)
}
```

- 実装は Input System の `Gamepad.SetMotorSpeeds(low, high)` を HapticsManager が毎 Tick 合成して出力（複数 Haptic の同時再生は **チャンネルごとの Max 合成**。加算だと飽和する）
- `LocalPlayerOnly`: マルチプレイで「自分が殴られた時だけ振動」。false なら Cosmetic 扱いで全員（画面内イベント通知等）
- プラットフォーム差（Switch HD振動 / DualSense）は `HapticExt` の実装クラスで吸収。基本の 2 モーターカーブだけ作れば全機種で動く

## B-2. Manager API

```csharp
public static class Haptics
{
    public static HapticHandle Play(HapticId id);
    public static HapticHandle Play(HapticId id, float strengthScale);
    public static void StopAll();
    public static void SetGlobalScale(float s);   // オプション「振動 0〜100%」/ OFF
}
```

# Part C — Presentation 統合・エディタ・プレビュー

## C-1. Presentation トラック

- TrackKind に `CameraShake(ShakeId)` / `Haptic(HapticId)` を追加（従来の「Small/0.08s」直値指定を廃止し、**すべて ID 参照に統一**）
- 典型構成: `[onHit] CameraShake: Hit_Small` + `[onHit] Haptic: Hit_Punch` + `[onHit] HitStop: 0.08`
- ネット時は Cosmetic として配送（[14] §4）。Haptic は LocalPlayerOnly 判定を受信側で実施

## C-2. 専用エディタ（ShakeEditor / HapticsEditor）

| 機能 | 内容 |
|---|---|
| Shake ライブプレビュー | プレビューシーンのカメラを**実際に揺らして**確認。任意の背景・モデルの前で再生。Game ビュー連動 |
| 波形表示 | 揺れの時系列波形（pos/rot 別）と Envelope カーブを重ねて表示・編集。編集 UI は ValueDef 共通 Drawer（[17] §5）を使用し、シェイク専用のカーブエディタは作らない |
| 連打テスト | ボタン連打で多重発火 → Trauma 合成の挙動を確認 |
| Haptic 実機プレビュー | **接続中のゲームパッドをエディタから直接振動させる**「Test on Pad」ボタン（Input System はエディタ再生外でも出力可能）。パッド未接続時は波形のみ |
| モーターカーブ編集 | Low/High 2 本のカーブを並べて編集。プリセット（Pulse/Rumble/Heartbeat/Explosion 等 10 種）から開始可能 |
| 同時プレビュー | PresentationEditor 内で Shake + Haptic + SE + VFX を同時再生（実機パッド振動込み） ★ |

## C-3. 運用方法（例: ヒット感を強めたい）

1. AssetBrowser で `Shake_Hit_Small` を開く → 振幅・Envelope を調整 → プレビューで揺れ確認
2. `Haptic_Hit_Punch` の LowFreq カーブを盛る → 手元のパッドで即体感
3. PresentationEditor で SkillSlash を開き、onHit トラックの ID 差し替えやタイミング調整 → 統合プレビュー
4. 保存 → ゲームに即反映。コード変更なし

## C-4. Validation

| 検査 | 重度 |
|---|---|
| カーブ未設定・尺 ≤ 0 等の ValueDef 共通検査 | [17] §6 に集約 |
| PosAmplitude・RotAmplitude 両方ゼロ | Warning（揺れない） |
| 振幅が規定値超（酔いリスク、BudgetProfile で閾値定義） | Warning |
| Haptic Duration > 2s | Warning（長すぎる振動） |
| Presentation の CameraShake/Haptic トラックが直値（ID なし） | Error（ID 参照に統一） |
