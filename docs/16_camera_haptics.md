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

## 実装メモ（2026-09-14、5-2 整理）

下記「名前空間の衝突（要修正）」を解消した: `Runtime/Camera/*.cs` の名前空間を `DDrive.Runtime.Camera` から
**`DDrive.Runtime.CameraShake`** に改名した（フォルダ名 `Runtime/Camera/` はそのまま。衝突しない別名にする
だけで済み、フォルダをファイル名に揃える必要はないと判断した）。これに伴い、衝突回避のために入っていた
`Runtime/Anim2D/Anim2DFacing.cs` の `UnityEngine.Camera` 完全修飾、`CameraFxManager.cs` 内の
`UnityEngine.Camera.main` 完全修飾は不要になったため、どちらも `Camera` / `Camera.main` の非修飾に戻した。
`DDriveRuntimeBootstrap.cs` の `Runtime.Camera.CameraFx.Bind(...)`（こちらは名前空間衝突とは別に、同クラス内の
`CameraFxManager CameraFx` プロパティと静的ファサード `CameraFx` を区別するための完全修飾）は
`Runtime.CameraShake.CameraFx.Bind(...)` に更新した。参照側（`PresentationManager.cs` / `OptionStore.cs` /
関連テスト 3 本の `using` と `ShakeId` エイリアス）もすべて追従した。
`CameraShakeData` はアセットとして保存済みだが、ScriptableObject のスクリプト参照は `.meta` の GUID で結ばれる
ため名前空間変更では壊れない。`ShakeId`(`AssetId<ShakeMarker>`)や `ShakePattern`/`ShakeSpace` 列挙体は
`[SerializeReference]` を使う多態フィールドではなく普通の struct/enum フィールドなので、シリアライズされた
YAML に型名文字列は書き込まれない(フィールド順で復元される) — したがって `[MovedFromAttribute]` は不要と
判断した。

## 実装メモ（2026-09-14、5-2 / 5-2b）

実装: `Runtime/Camera/{CameraShakeData,CameraFxManager,CameraFx,CameraShakeDataValidator}.cs`、
`Runtime/Haptics/{HapticsData,IHapticOutput,GamepadHapticOutput,HapticsManager,Haptics,HapticsDataValidator}.cs`。
専用エディタ（§C-2 ShakeEditor / HapticsEditor）は 5-2c でまだ未実装のため、`DataEditorRegistryTests` の
Exempt に `CameraShakeData` / `HapticsData` を追加した（Inspector から直接編集する）。

- **名前空間の衝突（要修正）**: `DDrive.Runtime.Camera` という名前空間を作ると、`DDrive.Runtime.*` 配下の
  ファイルから `Camera`（`UnityEngine.Camera`）を非修飾で参照している箇所が `CS0118`（namespace が type
  として使われている）でコンパイルエラーになる（C# の非修飾名解決は `using` より先に「囲む名前空間の直下
  にある入れ子の名前空間/型」を優先するため）。影響したのは `Runtime/Anim2D/Anim2DFacing.cs`（[05] C-4、
  CLAUDE.md が明記していた唯一の `Camera.main` 使用箇所）のみで、`UnityEngine.Camera` とフル修飾して解消
  した。新たに `DDrive.Runtime.*` 配下で `Camera`（`UnityEngine.Camera`）を使うコードを書くときは、同様に
  フル修飾すること。
- **Trauma 合成の実装**（AC「多重発火で破綻しない」）: 各 ShakeInstance の重み
  `w_i = clamp(Envelope.EvaluateAt(elapsed)) × TraumaWeight × StrengthScale`（Stop() 後はフェード用の
  線形減衰に切り替える）を求め、`rawSum = Σw_i`・`totalTrauma = clamp01(rawSum)`・
  `shakeAmount = totalTrauma²` を計算する。各 Instance の波形ベクトル（Pattern 別。§SampleWave）を `w_i` で
  加重平均し（`Σ(w_i × wave_i) / rawSum`）、最後に `shakeAmount` を掛けて最終オフセットにする。加重平均は
  個々のベクトルの最大値を超えないため、何個 Shake を積んでも最終オフセットが単体の振幅を大きく超えることが
  ない（`CameraFxManagerTests.ShakeData_ManyOverlappingInstances_DoesNotExceedMaxAmplitude` で検証）。
  MaxStack は「同一 ShakeData の同時 Instance 数」の上限として実装し、超過分は `Handle.Invalid` を返して
  無視する（警告なし。連打は想定内の使い方のため）。
- **Space の扱い（簡略化。要判断）**: `CameraLocal` はノードのローカル空間にそのまま適用する（既定）。
  `World` はノードの親の回転を打ち消して変換し、親の向きに関わらず同じワールド方向に揺れるようにする。
  `FromSource` は Pattern が算出した振幅の大きさ（`magnitude`）だけを流用し、方向は
  `(Camera.position - sourcePos).normalized` で「奥から手前」を再現する。Rot（回転）は Space を見ず常に
  ノードのローカル空間に適用する（ワールド回転の意味付けが曖昧なため v1 では簡略化）。5-2c でカーブ
  プレビューを作る際に、World/FromSource の回転版が必要かどうかを判断してほしい。
- **Pattern の実装（簡略化。要判断）**: `PerlinNoise`＝軸ごとに乱数位相をずらした `Mathf.PerlinNoise`、
  `DecaySine`＝`Frequency` で振動する正弦波（減衰そのものは Envelope 側が担う）、`Impulse`＝振動せず
  `PosAmplitude`/`RotAmplitude` をそのまま定数として返す（方向性のある一撃）。`CustomCurve` は
  専用の波形カーブ入力をデータ構造に追加していないため、現状は `Impulse` と同じ実装にフォールバックして
  いる（要判断: 5-2c で専用カーブが要るか判断してほしい）。
- **unscaled/scaled の決定**: CameraFxManager 自身の `Tick(float dt)` は渡された `dt` をそのまま使う
  純関数のまま（テスト容易性のため、`Time` に直接依存しない）。実配線だけ
  `DDriveRuntimeBootstrap.UnscaledCameraFxAdapter`（`AnchorGroupLoopAdapter` と同じ「非 IAssetManager/
  別 dt 系列を IAssetManager でラップする」パターン）が `Time.unscaledDeltaTime` を渡す形にし、
  `GameLoop.Register` にはこのアダプタを登録する（`CameraFxManager` 自体は登録しない）。これにより
  HitStop（`TimeService.TimeScale=0`）中も CameraFx は止まらず揺れ続ける（推奨仕様どおり）。一方
  **HapticsManager は他の全 Manager と同じ ScaledDeltaTime のまま**（Part B にはこの要件が明記されて
  いないため、既定に合わせた。要判断: HitStop 中に振動を止めたい/止めたくない、の意図が固まったら見直す）。
- **CameraFxManager のカメラノード挿入**: `Camera.main` が見つかったら、その直上に
  `DDriveCameraShakeNode` という空 GameObject を作り、カメラの直前の親（無ければ null=シーンルート）配下
  に同じワールド姿勢で置いてから、カメラをその子にする（カメラ自身の localPosition/localRotation は
  以後 (0,0,0)/identity のまま触らない）。揺れはこのノードの localPosition/localRotation にだけ適用する。
  カメラが差し替わった場合（`Camera.main` の参照先 Transform が変わった、またはカメラの親がノードでなく
  なった）は `Tick` 内で自動的に付け直す。`Camera.main` が存在しない間は警告 1 回 + no-op（カメラが現れたら
  自動的に有効化される）。シーン跨ぎで残す必要がある場合（カメラが DontDestroyOnLoad シーンにいる）は
  ノードも `DontDestroyOnLoad` にする。
- **Haptics の Max 合成**: 毎 Tick、再生中の全 HapticInstance について
  `LowFreq.EvaluateAt(elapsed) × StrengthScale` / `HighFreq.EvaluateAt(elapsed) × StrengthScale` を求め、
  チャンネルごとに `Mathf.Max` で合成する（加算しない。`HapticsManagerTests.PlayData_OverlappingInstances_
  ComposeWithMax_NotSum` で検証）。最後に `GlobalScale` を掛けて `IHapticOutput.SetMotors` へ渡す。
  `HapticPriority` は現状ロジックに未使用（Max 合成自体が「強い方が勝つ」を実現しているため。要判断:
  将来 MaxStack 的な上限を導入する場合の淘汰基準として使う想定）。`LocalPlayerOnly` も NGO 統合前の v1
  では判定先が無いため常にローカル再生扱い（要判断: NGO 統合時に PlayContext/送信元から誰の操作かを判定
  する経路を追加すること）。
- **Pause と実機モーターの安全側設計（要判断）**: AC「Pause で出力 0」を確実に満たすため、HapticsManager
  は Vfx/CameraFx のように per-instance の `Flags.Pause`（`IgnorePause` で継続させる等）を見ず、Pause
  チャンネルが立った瞬間に一律で `SetMotors(0,0)` にし、Pause 中は `Tick` 自体を早期リターンする（進行も
  再合成もしない）。実機のモーターを鳴らし続ける事故を避けるための安全側の判断で、`Flags.Pause` フィールド
  自体は残っている（将来 per-instance 制御が必要になったら見直すこと）。アプリ終了時
  （`OnApplicationQuit`）・フォーカス喪失時（`OnApplicationFocus(false)`）にも
  `DDriveRuntimeBootstrap` から `HapticsManager.ResetOutput()`（Instance は止めずモーターだけ 0 に戻す）
  を呼ぶ。
- **Preload 既定への追加**: `AssetCreationService.Create` は `CameraFxManager.ShakeData` /
  `HapticsManager.PlayData` もこれまでの Vfx/Audio 等と同じく `ResolveOrPlaceholder`（同期解決のみ、
  ロードを開始しない）で引くため、Canvas/ControlSkin/Presentation と同じ理由で `AssetType.Shake` /
  `AssetType.Haptics` を Preload 既定に追加した（さもないと新規作成した Shake/Haptics は常に
  Placeholder になる）。
- **確認用デモ**: `Assets/GameData/Camera/Demo/SHAKE_Demo_DemoHitSmall.asset`
  （PerlinNoise、PosAmplitude=(0.12, 0.08, 0)、RotAmplitude=(0,0,1.5)、MaxStack=3）と
  `Assets/GameData/Haptics/Demo/HAPTIC_Demo_DemoHitPunch.asset`（既定の LowFreq/HighFreq カーブのまま）
  を作成し、5-1 の剣攻撃デモ `Assets/GameData/Presentation/Demo/PRES_Demo_SkillSlash.asset` の
  `onHit`（`SignalKey="hit"`）トラックに `CameraShake` / `Haptic` トラックを追記した
  （`StopOnCancel=true`）。確認用シーンは 5-1 と同じ
  `Assets/GameData/PreviewScenes/PresentationSkillSlashPreviewScene.unity`。
- **Addressables**: `AssetType.Shake`/`AssetType.Haptics` の新規カタログ `CameraFxCatalog.asset` を
  `Editor/AssetBrowser/AssetCreationService.GetCatalogName` の既存マッピングどおり作成した（コード変更
  不要、既に対応表にあった）。デモアセット作成に伴う Addressables グループ（`DDrive_GameData.asset` /
  `DDrive_Catalogs.asset`）への追記はユーザーの未コミット変更と同じファイルのためコミットしていない
  （追加された行は `docs/28_manual_verification_phase5.md` の要判断に列挙）。

