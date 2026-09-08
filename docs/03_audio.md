# 03. Audio (BGM / SE) 詳細設計

関連: [02_core_framework.md](02_core_framework.md) / [09_editor_tools.md](09_editor_tools.md)

---

## 1. 要件

- BGM / SE の一元管理。ゲームコードは `Audio.PlaySe(SEID.X)` のみ
- 3D サウンド対応（Anchor 指定: 呼出し Transform / シーン内オブジェクト / ボーン + オフセット / 座標。VFX と同一の AnchorDef を共用）
- 生成・常時・消滅イベント、ポーズ中挙動フラグ
- 基底クラス + 派生でビート同期など特殊制御を簡単に追加できる
- 音量 Duck（会話中に BGM を下げる等）を Manager 連携でデザイナーが設定可能
- Animator / Animation イベントからの再生（フレーム指定）
- プレビュー（ループ・3D 距離確認・Mixer 確認）

## 2. データ構造

```csharp
public class SeData : AssetDataBase
{
    public AudioClip[] Clips;            // 実再生用(トリミング適用後)。Manager はこれだけを見る
    public SeClipSource[] Sources;       // 非破壊編集用の元データ+トリム範囲（下記）。Editor専用
    public float StartOffsetSec;         // 再生開始位置。トリムと別に、常に軽量にランタイム適用
    public ClipSelectMode SelectMode;    // Random / RoundRobin / First
    public AudioMixerGroup Mixer;
    [Range(0,1)] public float Volume = 1f;
    public Vector2 PitchRange = new(1f, 1f);   // ランダムピッチ
    public bool Loop;
    [Header("3D")]
    public SpatialMode Spatial;          // None(2D) / Anchor / AtPosition
    public AssetId<AnchorMarker> AnchorId; // Anchor アセット(AnchorData)。設定時は埋め込み Anchor より優先(2026-09-08 [21] §3.3。ランダム/ディレイ/確率/入れ子はアセット側)
    public AnchorDef Anchor;             // ★VFX と同一の AnchorDef を共用（[04] §2）。AnchorId=0 のときだけ使う
                                         //   シーン配置型アンカー(AnchorPoint)も共用（[04] §2.5）。
                                         //   解決先に AnchorPoint があれば SpawnOffset + 位置ランダムを追加適用（SEは位置のみ）
                                         //   Space: World / BoneName / NamedObject / ContextTarget
                                         //   + LocalOffset / FollowRotation / DetachOnStop
                                         //   （DetachOnStop: 発生源破棄後も鳴り終わりまで残す）
    public float MinDistance = 1, MaxDistance = 30;
    public AnimationCurve Rolloff;       // 距離→減衰。横軸が時間でないため ValueDef 対象外（[17] FR-19 の明示的例外）
    public float Spread;                 // 0=点音源 / 180=無指向（近接時の定位の硬さ）
    public bool DopplerEnabled;          // 既定 false（演出音での違和感防止）
    [Header("制御")]
    public int MaxConcurrent = 8;        // 同一SEの同時再生上限
    public float CooldownSec = 0.03f;    // 連打防止
}

// Clips[i] と対応する非破壊トリミング編集用データ(要素数を揃えて使う)。
// Source は常にインポートしたままの状態を保持し、上書きしない。「トリミングを適用」操作で
// Source を TrimStart/TrimEnd に基づき切り出したコピーを生成し、Clips[i] を差し替える
// （= 元データ置換ではなく、常に Source から再生成できる非破壊編集）。
[Serializable]
public struct SeClipSource
{
    public AudioClip Source;
    public float TrimStartSec;
    public float TrimEndSec;   // TrimEndSec <= TrimStartSec は「クリップ終端まで」の意味
}

public class BgmData : AssetDataBase
{
    public AudioClip Intro;              // イントロ→ループ本体の2部構成対応
    public AudioClip LoopBody;
    public double LoopStartSec, LoopEndSec;   // サンプル精度ループ
    public AudioMixerGroup Mixer;
    [Range(0,1)] public float Volume = 1f;
    public ValueDef FadeIn, FadeOut;     // 既定 0.5s / 1s。フェードカーブをデザイナーが指定可能（[17]）
    public float Bpm;                    // ビート同期演出用（任意）
}
```

- 特殊制御例: `BeatSyncBgmData : BgmData` — `CreateBehaviour()` でビート同期挙動を返す。Manager 無改修（[02] §9）

## 3. Manager API

```csharp
public static class Audio   // static ファサード
{
    // SE
    public static SeHandle PlaySe(SeId id);
    public static SeHandle PlaySe(SeId id, Vector3 pos);
    public static SeHandle PlaySe(SeId id, Transform follow);
    public static SeHandle PlaySe(SeId id, AnchorId anchor);                   // Anchor アセットを明示(2026-09-08 [21] §3.3)
    public static SeHandle PlaySe(SeId id, AnchorId anchor, Transform follow);
    public static void Stop(SeHandle h, float fade = 0f);

    // BGM
    public static void PlayBgm(BgmId id, float fadeIn = -1);   // -1=Data既定値
    public static void StopBgm(float fadeOut = -1);
    public static void CrossFade(BgmId next, float duration);

    // グローバル
    public static void SetBusVolume(AudioBus bus, float db);   // Master/BGM/SE/Voice
    public static void PushDuck(DuckChannel ch, float db, float attack = 0.2f);
    public static void PopDuck(DuckChannel ch);
}
```

内部実装:

- AudioSource は PoolService から Rent（SE 用に 32 個 Prewarm）
- 同時再生上限超過 → Priority 最低を停止（Data.MaxConcurrent + Flags.Priority）
- BGM は 2ch クロスフェード（AudioSource ×2 を保持）。Intro→Loop は `PlayScheduled` でサンプル精度接続
- Duck はスタック式チャンネル。AnimationEvent / Presentation / Canvas から `PushDuck` を発行可能（AssetEvent の Action=Duck）

### 位置指定の優先規則

1. 呼出し側の引数（`pos` / `Transform` / `PlayContext`）は**常に Data の Anchor を上書き**する（VFX と同一規則）
2. 引数なしの `PlaySe(id)` は Data の Anchor 定義に従う。Spatial=Anchor で Anchor 未設定は Validation Error（鳴らす位置が決まらない）
   - 2026-09-08: 優先順位は `PlaySe(id, anchorId)` の引数 > 明示座標/Transform > `Data.AnchorId` > 埋め込み `Data.Anchor`。AnchorData の DelaySec は「Source を借りて位置を決めた状態で待ち、Tick 後に Play」（`IsPlaying=true` / `IsPending=true`）。SpawnChance に外れたら `Handle.Invalid`。詳細は [04] §2.5.5 / [21]
3. Spatial=None(2D) の Data に位置引数を渡した場合は位置を無視して 2D 再生 + 開発ビルドで警告 1 回（「2D のつもりが 3D」より「3D 引数が無視される」方が事故として軽い）
4. Presentation トラックからの再生は `TrackTargetMode`（Self / ContextTarget / World / Anchor）が規則 1 に該当する

## 4. Instance / Handle

- `SeInstance`: source, elapsed, followTarget, state。Tick で追従・寿命・フェード処理
- `SeHandle` 操作: `SetVolume / SetPitch / Stop(fade) / Move / IsPlaying`
- Pause: `Flags.Pause` に従い `AudioSource.Pause()` / 継続

## 5. 専用エディタ（AudioEditor）

AssetBrowser から開く Inspector 拡張 + プレビューペイン。

| 機能 | 内容 |
|---|---|
| 波形表示 | Clip の波形 + ループ範囲をドラッグで設定（BGM の LoopStart/End） |
| 再生プレビュー | 再生/停止/ループ/音量/ピッチのスライダをその場で試聴に反映 |
| 3D 距離確認 | AudioEditor 内の **2D パッド UI**（Animator のブレンドスペース風）。中央に音源（赤点）、リスナー（青点）をマウスドラッグで矩形内移動。矩形の縦横の縮尺（±m）は個別に変更可能。音源までの距離・リスナー角度（スライダで調整、ステレオ定位に反映）を表示。**再生中にリスナーを動かすと距離減衰・パンが即時に反映**される。Min/MaxDistance の円もパッド上に表示（設定値がすべて実 AudioManager 経由で適用される）。**3D プレビューが成立しない場合（BGM / Spatial=None の 2D 設定 / Clips 未設定）はパッドを暗転し「無効である旨と理由」を明示する**（設定不備が「効いていないだけ」に見える事故を防ぐ） |
| Mixer 確認 | 割当先 Mixer グループと現在の dB を表示 |
| ランダム試聴 | Clips 複数時に SelectMode 通りの挙動で連続試聴 |
| イベント設定 | AssetEvent の編集（OnSpawn 等） |
| 再生開始位置調整 | `StartOffsetSec` をスライダ/数値入力で調整し、その場で試聴に反映 |
| 非破壊トリミング | `Sources[i]` の TrimStart/TrimEnd を波形上でドラッグ設定 →「トリミングを適用」で `Clips` を再生成（Source は不変） |
| 無音自動トリミング | `Sources[i]` ごとにワンボタンで先頭/末尾の無音区間を検出し TrimStart/TrimEnd に反映（振幅しきい値ベース） |

プレビューは実 AudioManager を EditMode で駆動する（[01] ADR-4）。
現状の実装（Phase 1）はドラッグ操作前の数値入力ベース GUI（`SeDataEditor`）まで。波形上のドラッグ編集は本 AudioEditor（1-7）で波形表示と統合する。

## 6. 運用方法

1. デザイナー: AssetBrowser →「新規 SE」→ Clip を D&D → **表示名（「剣の斬撃音」等）・カテゴリ・識別子を入力** → ファイル名 `SE_Player_Slash` 等・ID・カタログ登録はツールが自動生成（[10] §3）→ パラメータ調整 → 試聴 → 保存
2. 保存時に ID 定数が再生成され、プログラマーは `SEID.XXX` で参照可能に
3. アニメ連携: AnimationData のイベントトラックに `Frame(15) → PlayAsset(SE)` を設定（コード不要）
4. 会話シーン: CanvasData(会話UI) の OnEnable イベントに `Duck(Dialogue, -12dB)`、OnDisable に `PopDuck`
5. 環境音（滝・焚き火等）: シーンに `SeEmitter`（SeIdRef を 1 つ持つ配置用マーカーコンポーネント）を置くだけ。OnEnable で `Audio.PlaySe(id, transform)`、OnDisable で Stop を自動発行する薄いラッパで、禁止事項（AudioSource.Play 直呼び）に抵触しない正規の配置手段。カリングは MaxDistance + Priority の既存機構に乗る。**配置には標準プレハブ `Assets/GameData/Prefabs/Audio/SeEmitter.prefab` を使うこと推奨**（無ければ `Tools/D-Drive/Generate/標準プレハブを生成`。[01] §5 / [10] §3.3）

## 7. Validation

| 検査 | 重度 |
|---|---|
| Clip 未設定 / Missing | Error |
| Mixer 未割当 | Warning |
| Spatial=Anchor で Anchor 未設定（AnchorId も 0） | Error |
| AnchorId 設定済みなのに埋め込み Anchor が既定値以外 | Warning（埋め込みは無視される。2026-09-08） |
| Anchor.BoneName がプレビューモデルに無い | Warning |
| MaxDistance ≤ MinDistance | Error |
| DopplerEnabled かつ Loop=false | Warning（ワンショットでは知覚されにくい） |
| LoopEnd ≤ LoopStart | Error |
| MaxConcurrent ≤ 0 | Error |
| Volume=0（鳴らない設定） | Warning |
