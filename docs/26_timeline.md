# 26. Timeline 連携(Maya FBX 取り込み + D-Drive トラック) 詳細設計

関連: [08_presentation.md](08_presentation.md) §3 Timeline トラック / [05_model_animation.md](05_model_animation.md) / [02_core_framework.md](02_core_framework.md) §3 AssetEvent / [14_networking.md](14_networking.md) §3 NetMode・§5 Presentation の同期 / [16_camera_haptics.md](16_camera_haptics.md) Part A(CameraFx) / [13_extensions.md](13_extensions.md) B-9 カットシーン種別 / [42_distribution.md](42_distribution.md) §3.5 依存・§5.10 / [11_tasks.md](11_tasks.md) 6-10a〜6-10d

> 2026-09-13 ドラフト。ユーザー要望:「Maya のカメラやアニメを Timeline に流し込み、Unity 側で SE や VFX を足す。ほかのアセットと同様にイベントも付けたい。Maya 側スクリプトは無しが望ましい」。
>
> **2026-09-13 ユーザー決定: 実装は P6 の最後(6-10a〜d)、運用開始後に行う**(実際にカットシーンを作る段階がまだ先のため。旧チケット番号 5-3a〜d)。
>
> **2026-09-18 設計確定**: §7 の未決事項 6 件にユーザー回答が出た(短い演出のみ / カメラはシームレス / 1 ショット = 1 FBX(フレーム範囲の逃げ道あり)/ Humanoid・アニメ FBX 分離 / fps 30・60 切替 / ネットは両方)。あわせて追加要望 2 件(**カメラのステップ fps を Unity 側で調整できること**、**ピント等のカメラ設定の持ち越し**)を設計に落とした。§3.1(Presentation との使い分け)・§4.6(カメラ)・§4.7(ネット)・§5.3(fps)・§5.4(Humanoid)が新規/改定。**残る未決は §7.2**(統合可否・asmdef・Cinemachine アダプタ等)で、これらは 6-10a 着手前にユーザーが決める。

---

## 1. Unity Timeline の基礎(前提知識)

D-Drive の設計を読むのに必要な範囲だけまとめる。

### 1.1 登場人物

| 名前 | 何か | D-Drive で例えると |
|---|---|---|
| **TimelineAsset**(`.playable` ファイル) | トラックとクリップの並び = 「楽譜」。プロジェクトのアセット | Data |
| **PlayableDirector**(シーンのコンポーネント) | TimelineAsset を再生する「演奏者」。再生・停止・シーク・速度・更新方法を持つ | Manager の Instance |
| **Track**(トラック) | 1 行。何を動かすかの種類(Animation / Audio / Activation / Control / Signal …) | PresentationTrack の Kind |
| **Clip**(クリップ) | トラック上の「開始〜終了」の区間。例: 攻撃モーション 0〜1.2 秒 | 区間を持つ演出 |
| **Marker**(マーカー) | トラック上の「一点」。通過時に通知を出す | AssetEvent(Trigger=Time) |
| **Binding**(バインド) | トラックが「シーンのどのオブジェクトを動かすか」の結び付け | PlayContext.Self / Target |

### 1.2 一番大事な性質: 「アセット」と「バインド」は別物

- TimelineAsset には「Animation トラック 1 に、このクリップを 0 秒から」までしか入っていない。**どの Animator を動かすかは PlayableDirector 側(=シーン側)に保存される**
- なので 1 つの TimelineAsset を、プレイヤー A でも B でも使い回せる(バインドを差し替えるだけ)
- 逆に言うと、TimelineAsset を単体で渡されても「何を動かすか」が決まらない。**D-Drive はここを「役割名」で解決する**(§4.2)。プログラマーは `Cutscene.Play(CUTID.Opening, ctx)` だけ書き、誰を動かすかは ctx と役割名から自動で決まる

### 1.3 標準トラック

| トラック | 用途 | 備考 |
|---|---|---|
| Animation | Animator に AnimationClip を流す。Transform だけのオブジェクト(カメラ等)も可 | クリップ同士のブレンド(重ねるとクロスフェード)、Avatar Mask、Override トラック(上半身だけ差し替え)ができる |
| Audio | AudioSource で AudioClip を鳴らす | D-Drive の AudioManager(バス・ダッキング・同時発音制限)を**通らない**。D-Drive では原則使わず D-Drive SE トラックを使う |
| Activation | 区間中だけ GameObject を表示 | — |
| Control | 区間中に Prefab / ParticleSystem / 子 Timeline を出す | D-Drive の Pool・VfxManager を**通らない**。D-Drive VFX トラックで代替 |
| Signal | マーカー通過で SignalReceiver の UnityEvent を呼ぶ | シーンに Receiver を置く必要があり、ID で追えない。D-Drive Event トラックで代替 |

### 1.4 時間と再生

- `DirectorUpdateMode`: GameTime(Time.timeScale に従う)/ UnscaledGameTime / Manual(自分で `Evaluate` を呼ぶ)。**D-Drive は Manual にして GameLoopDriver から進める**(ポーズ・ヒットストップ・スロー・ネット同期を他 Manager と揃えるため)
- `WrapMode`: Hold(最後で止まる)/ Loop / None(終わったら解除)
- Timeline のフレームレート(既定 60)は表示・スナップ用。**Maya のシーン fps と合わせないとキーがフレームの間に来て見た目がずれる**(§5.3)
- 編集中(Edit Mode)は Timeline ウィンドウでスクラブすると PlayableDirector が `Evaluate` される。**独自トラックはこの「巻き戻し・飛ばし」に耐える作りが必要**(SE を鳴らしっぱなしにしない、VFX を時刻に合わせて Simulate する等。§4.4)

### 1.5 独自トラックの作り方(実装者向け)

- クリップ: `PlayableAsset`(データ)+ `PlayableBehaviour`(再生中の処理: `OnBehaviourPlay` / `ProcessFrame` / `OnBehaviourPause`)
- トラック: `TrackAsset` に `[TrackClipType]` / `[TrackBindingType]` を付ける
- マーカー: `Marker` + `INotification` を実装し、`INotificationReceiver` で受ける
- これらは Runtime asmdef に置き、`Unity.Timeline` を参照する(**asmdef 変更**。§6)

---

## 2. 目標(ユーザー要望の分解)

1. **Maya → Unity**: Maya は普通に FBX を書き出すだけ(スクリプト無し)。Unity 側で FBX を検知し、カメラ・キャラ・小物のアニメを Timeline のトラックとして自動で並べる
2. **Unity で演出を足す**: 並んだ Timeline に、D-Drive の SE / VFX / 配置セット / 画面揺れ / 触覚 / UI などをクリップやマーカーで足す。**中身は ID 参照で、実再生は各 Manager**(ADR-4 と同じ)
3. **イベント**: ほかのアセットと同じ `AssetEvent`(PlayAsset / SetParam / SendMessage / Duck、Repeat)をそのまま置ける。コード側へは Signal / Marker で通知できる
4. **プログラマーは ID だけ**: `Cutscene.Play(CUTID.Opening, ctx)`、`await` で終了待ち、スキップ、`OnMarker` 購読
5. **(2026-09-18 追加)カメラはゲームカメラと繋がる**: Maya カメラへの切り替わり・戻りが滑らか(§4.6.2)。カメラの「滑らかさ(ステップ fps)」と「ピント・絞り・画角」は **Unity 側で、ショットごとに、Maya をやり直さずに**調整できる(§4.6.3 / §4.6.4)
6. **(2026-09-18 追加)用途は数秒の短い演出**(必殺技のカットイン、ラウンド開始/勝利デモ等)。長いカットシーン(数十秒〜分)は作らない。PresentationData と用途が重なるので使い分けを決める(§3.1)

---

## 3. 全体像

```
Maya ──FBX 書き出し(標準機能のみ)──▶ Assets/SourceAssets/Cutscene/<カテゴリ>/<ショット>.fbx           … カメラ + 小物
                                     Assets/SourceAssets/Cutscene/<カテゴリ>/<ショット>__<Model識別子>.fbx … キャラの骨アニメ(キャラごと 1 本、§5.1)
                                               │ AssetPostprocessor(6-10c)
                                               ▼
                         CutsceneData(新種別)  ── TimelineAsset(自動生成・再生成可能)
                           ├ 役割バインド表(Camera / Self / Target / 名前付き)+ 原点(§4.2.1)
                           ├ FrameRate / SourceFrameRange(§5.3 / §5.1)
                           ├ Events(AssetEvent[] 共通)
                           └ Flags(Net = Local / Cosmetic で同期方式を切替、§4.7)
                                               │
Unity(デザイナー)── Timeline ウィンドウで D-Drive トラックを追加(6-10b)
                           ├ D-Drive Camera クリップ(自動生成。ステップ fps / ブレンド / ピント設定はここで調整、§4.6)
                           ├ D-Drive Event トラック(AssetEvent マーカー)
                           ├ D-Drive SE / VFX / AnchorGroup / Shake / Haptic / Presentation クリップ
                           └ Signal マーカー(コード通知)
                                               │
ゲーム ── Cutscene.Play(CUTID.X, ctx) → CutsceneManager
                           ├ PlayableDirector をプールから借りる(Manual 更新)
                           ├ 役割名 → ctx / シーンのオブジェクトへバインド
                           ├ カメラ: ゲームカメラの姿勢と Timeline カメラをブレンドして書き込む(§4.6)
                           └ 各クリップ・マーカーは既存 Manager / AssetEventDispatcher へ委譲
```

- **編集 UI は Unity 標準の Timeline ウィンドウを使う**(独自のタイムライン UI は作らない。業界標準で学習資料も多く、クリップのドラッグ・ブレンド・スナップ・カーブ編集が揃っている)。D-Drive 側は「D-Drive トラックの Inspector」「確認用シーン」「バインド検査」「Validation」を足す
- PresentationData(5-1)の `TrackKind.Timeline` は CutsceneData を 1 本のトラックとして再生する(短い技演出の中に Timeline を入れる用途。§3.1)

### 3.1 CutsceneData と PresentationData の使い分け(2026-09-18 追記)

用途が「短い演出のみ」に決まったため、両者は正面から重なる(どちらも数秒の SE/VFX/揺れ/HitStop の束)。**提案は「併存させ、判断基準を 1 つに絞る」(案 A)**。統合の可否そのものはユーザーの判断(§7.2-1)なので、ここでは案と根拠を示す。

**判断基準(デザイナー向け。上から順に当てはまったところで決まる)**

| 質問 | Yes なら |
|---|---|
| Maya で作ったカメラやキャラの動き(FBX)を使う? | **Cutscene** |
| 演出中、カメラをゲームから奪って別のカメラワークにする? | **Cutscene** |
| ヒット判定など「ゲームの結果を待ってから」続きを出す(`OnSignal`)? | **Presentation** |
| 既存の SE / VFX / 揺れ / HitStop を時間で並べるだけ(キャラの動きは Animator のモーション)? | **Presentation** |
| Maya カメラも使うし、ヒットも待ちたい | **Presentation を親**にして、Timeline トラック(`TrackKind.Timeline`)で Cutscene を呼ぶ |

一言で言うと **「Maya の FBX を使うなら Cutscene、使わないなら Presentation」**。それ以外の機能(SE/VFX/Shake/Haptic/UI/Event/Signal)はどちらにも同じ形で置けるので、迷う要素を FBX の有無だけにする。

**両者の性格の違い(併存させる理由)**

| 観点 | PresentationData(5-1、実装済み) | CutsceneData(6-10) |
|---|---|---|
| 時間の主役 | ゲームロジック(`OnSignal` で待つトラックがある。`AtTime` は補助) | Timeline の時間軸(全部 `AtTime` 相当)。**コードの合図を「待つ」トラックは持たない** |
| 編集 UI | PresentationEditor(D-Drive 独自、5-4) | Unity 標準 Timeline ウィンドウ + D-Drive の Inspector |
| Data の作り手 | デザイナーが手で作る | **ツール(FBX 取り込み)が作り、デザイナーが足す**(再取り込みで自動生成部分が更新される) |
| カメラ | 触らない(Shake は CameraFx の後段ノードのみ) | 奪う(ブレンドして戻す、§4.6) |
| fps の概念 | 無い(表示上 60fps 換算のみ) | ある(`FrameRate`、Maya の fps が真実、§5.3) |
| ネット | Cosmetic + `PredictLocal`(5-8) | 同じ流儀(§4.7) |
| 入れ子 | Timeline トラックで Cutscene を呼べる | Presentation クリップで Presentation を呼べる |

入れ子は両方向を許す(現行設計どおり)。**循環(Presentation → Cutscene → 同じ Presentation)は Validation Error**(既存の「Presentation が自身を含む」Error の拡張)。

**代案とトレードオフ**

| 案 | 内容 | 利点 | 欠点 |
|---|---|---|---|
| **A. 併存(提案)** | 上の判断基準で使い分け。相互に入れ子可 | 5-1〜5-16 で実装・ネット対応・エディタ済みの Presentation を変えない。FBX 取り込みが作る Data と人が作る Data が種別で分かれ、再取り込みが人の編集を壊さない(§5.2-3)。fps・カメラ・バインド表など Timeline 固有の欄が Presentation に増えない | 種別が 1 つ増える(`CUTID` / カタログ / Validator / エディタ導線)。「どっちで作るか」を一度は考える必要がある(→ 判断基準で吸収) |
| B. Presentation に統合 | `PresentationData` に `Timeline`・`Bindings`・`FrameRate`・カメラ設定を持たせ、CutsceneData を作らない | 種別・ID 系統・エディタ導線が 1 つ | Timeline を使わない大多数の演出に無意味な欄が並ぶ。FBX 取り込みが PresentationData を自動生成することになり「ツールが管理する Data」と「人が作る Data」が同じ種別に混在(`ImportSourceGuid` の有無で挙動を分ける実装が要る)。編集 UI が PresentationEditor と Timeline ウィンドウの 2 つに割れる(同じ Data の同じ時間軸を 2 つの UI が触る)。ネット経路が「AtTime トラック方式」と「Director シーク方式」の 2 系統になる |
| C. Cutscene に統合(Presentation 廃止) | 全部 Timeline で作る | 編集 UI が Unity 標準 1 つ | 実装済みの Presentation(5-1〜5-16、ネット・Late Join・エディタ・デモ・テスト)を捨てる。`OnSignal`(ゲーム結果待ち)を Timeline で素直に表せない(マーカーで止めて待つ拡張が要る)。不採用 |

---

## 4. D-Drive 側の設計

### 4.1 新種別 CutsceneData(AssetType.Cutscene、接頭辞 `CUT`)

```csharp
public sealed class CutsceneData : AssetDataBase
{
    public TimelineAsset Timeline;           // 自動生成 or 手置き
    public CutsceneBinding[] Bindings;       // 役割名 → 解決方法
    public CutsceneOrigin Origin;            // Self / World / AnchorPoint(§4.2.1、2026-09-18 追加)
    public float FrameRate = 30f;            // Maya のシーン fps(取り込みで自動設定。§5.3、2026-09-18 追加)
    public FrameRange SourceFrameRange;      // 取り込むフレーム範囲。Start=End=0 で FBX 全体(既定)。§5.1「逃げ道」(2026-09-18 追加)
    public CutsceneSkip Skip;                // 不可 / 即終了 / 指定マーカーまで飛ばす
    public DirectorWrapMode Wrap;            // 既定 None
    public bool LockInput;                   // 再生中はプレイヤー入力を止める(UiManager/入力側へ通知)
    public bool PredictLocal;                // Flags.Net=Cosmetic のとき、行為者は Broadcast を待たず即再生(PresentationData と同じ意味。§4.7、2026-09-18 追加)
    // Events(AssetEvent[])は AssetDataBase 共通。Trigger=Time で Timeline 上の秒として扱う
    // Flags.Net(AssetDataBase 共通): Local = ローカル再生 / Cosmetic = 同期再生(§4.7)
}

[Serializable]
public struct CutsceneBinding
{
    public string TrackName;                 // "Camera" / "Hero" / "Enemy01" …(FBX のノード名・ファイル名から自動)
    public CutsceneBindTarget Target;        // MainCamera / Self / Target / SpawnModel / SceneObjectByName / AnchorPoint
    public AssetId<ModelMarker> Model;       // SpawnModel のとき: ModelsManager で出して終了時に返す
    public string SceneObjectName;
}

[Serializable]
public struct FrameRange { public int Start; public int End; }   // End は含む。Start=End=0 で「全体」
```

- `AssetType` enum に `Cutscene` を**末尾追加**(既存値は不変。[13] B-9 の枠を使う)
- 定数クラスは `CUTID`。`KnownPrefixes` にも `CUT` を追加する([42] §5.13 に記載済み。P 発効前なので今なら追加できる)
- カメラごとの調整値(ステップ fps・ブレンド・ピント)は CutsceneData ではなく **Timeline 上の D-Drive Camera クリップ**が持つ(§4.6)。ショットごとに変えたいものはクリップ側、カットシーン全体で 1 つのものは Data 側、という分け方
- プロジェクト共通の既定値(既定 fps、カメラのステップ fps・ブレンド秒の初期値)は **`CutsceneImportProfile`**([05] の `Anim2DImportProfile` と同じ `FindOrDefault()` パターンの設定 SO)に持つ(§5.3)

### 4.2 バインド解決(「誰を動かすか」)

| Target | 解決先 | 典型 |
|---|---|---|
| MainCamera | ゲームのメインカメラ。再生中は Transform / 画角 / ピントを **ブレンド付きで** 書き込み、終了時にゲームカメラへ戻す(§4.6) | Maya カメラ |
| Self / Target | `PlayContext.Self` / `Target` の Animator | 主人公・敵 |
| SpawnModel | ModelData を ModelsManager で生成して結び、終了時に返却 | カットシーン専用の登場人物・小物 |
| SceneObjectByName | シーン内の名前一致 | 背景ギミック |
| AnchorPoint | AnchorPoint([21])の位置に置く | 「右手に剣を持たせる」等 |

未解決は **警告 + そのトラックだけミュートで続行**(TL;DR #4)。Validation で事前に検出する。

#### 4.2.1 原点(Maya のワールド座標をゲームのどこに置くか)(2026-09-18 追記)

Maya のカメラ・キャラ・小物は Maya シーンのワールド座標で焼かれているが、ゲームでは「プレイヤーが今いる場所」で再生したい。`CutsceneData.Origin` で決める。

| Origin | 意味 | 典型 |
|---|---|---|
| Self(既定) | `ctx.Self` の位置・向きを Maya の原点に合わせる(Y 回転のみ。傾きは無視) | 必殺技カットイン(使用者の位置で) |
| World | Maya の座標をそのまま使う | ステージ固定の開始デモ |
| AnchorPoint | 名前付き AnchorPoint([21])の位置・向き | ステージごとの定位置 |

- 実装: プールした `CutsceneRoot`(空 GameObject)を原点に置き、MainCamera 以外の Maya 由来トラックの対象(SpawnModel した Model・小物)をその子にする。カメラは §4.6 の書き込み時に原点の姿勢を掛ける(親子付けはしない = CameraFx の Shake ノードと干渉させない)。Self / Target にバインドしたキャラは既にゲーム内の位置にいるので、Timeline の Animation トラックのオフセット(`ApplyTransformOffsets`)を原点の姿勢に設定して「Maya での相対位置」を保つ。**Humanoid のルートモーションの扱い(Bake Into Pose の可否)と Timeline トラックオフセットの組み合わせは 6-10a 着手時に実機で確認する**(要検証、§7.3)

### 4.3 D-Drive トラック(6-10b)

| トラック / マーカー | 中身 | 委譲先 |
|---|---|---|
| **Camera** クリップ(2026-09-18 追加) | Maya カメラの位置・回転・画角・ピント・絞りのカーブ + ステップ fps + ブレンド秒(§4.6)。取り込みが自動生成し、デザイナーは設定だけ触る | CutsceneManager(Camera + Volume へ書き込み) |
| **D-Drive Event** マーカー | `AssetEvent` 1 件(PlayAsset / SetParam / SendMessage / Duck、Repeat、Anchor) | `AssetEventDispatcher`(Anim / VFX と同じ経路) |
| **SE** クリップ | SeId。区間の長さ = ループ SE の鳴らし続け時間(OneShot は開始点のみ使用) | AudioManager |
| **VFX** クリップ | VfxId + AnchorDef。区間終了で Stop(ループ VFX 向け) | VfxManager |
| **AnchorGroup** クリップ | 配置セット | AnchorGroupPlayer |
| **Shake / Haptic** マーカー | ShakeId / HapticId(5-2 / 5-2b 完了後) | CameraFx / HapticsManager |
| **UI** クリップ | CanvasId(字幕・レターボックス等)を区間中だけ Open | UiManager |
| **Presentation** クリップ | PresId。区間開始で `Presentation.Play`、区間終了(または Cutscene の中断)で Cancel。`OnSignal` トラックを持つ Presentation は Signal を送る手段が無いので Validation Warning(§3.1) | PresentationManager |
| **Signal** マーカー | 文字列キー(`cutscene/xxx` 規約) | `CutsceneHandle.OnMarker` → コード |

- 1 本の「D-Drive トラック」に SE / VFX / Event を全部載せる案もあるが、**種類ごとに行を分ける方が Timeline ウィンドウ上で読みやすい**ので分ける
- クリップの Inspector は既存の IdRef ドロップダウン(AssetIdDrawer)を使う = 名前で選べる、Missing は赤表示
- **キャラ・小物のアニメは標準の Animation トラック**を使う(Humanoid のリターゲット・ブレンド・Avatar Mask をそのまま使うため)。**カメラだけ D-Drive Camera クリップ**にする理由: 標準 Animation トラックは Animator 経由でしか値を書けず、URP の被写界深度(Volume の `DepthOfField`)や「評価時のステップ fps 量子化」を扱えないため(§4.6)

### 4.4 編集中プレビュー(スクラブ対応)

- Timeline ウィンドウでの再生・スクラブ中も **実 Manager を駆動する**(ADR-4)。確認用シーン `CutscenePreviewScene` に Editor 用 Manager 群を置き、`PreviewService` と同じ仕組みで動かす
- 巻き戻し・飛ばし: SE は「区間に入った瞬間」だけ鳴らし、スクラブで同じ点を何度も通っても連打しない(ドラッグ中は無音、再生ボタン中のみ発音)。VFX は `SceneVfxPreviewDriver` の手動 Simulate で「クリップ開始からの経過秒」に合わせる
- Maya カメラのトラックは Game ビューで確認できるように、プレビュー中はメインカメラにバインドする。**ブレンドの確認**: 再生開始時のカメラ姿勢を「ゲームカメラの姿勢」とみなしてブレンドインし、終了時にそこへ戻る(確認用シーンのカメラを手で動かしておけば、その位置からの繋ぎを確認できる)。スクラブ中はブレンド無し(Timeline カメラの姿勢そのもの)にする — スクラブで「繋ぎ」は確認できない、と割り切る
- ステップ fps(§4.6.3)はスクラブ時にも効く(量子化は評価関数の中で行うため、Timeline ウィンドウの再生でもそのまま見える)

### 4.5 ランタイム(CutsceneManager)

- `Cutscene.Play(id, ctx)` → `CutsceneHandle`(Presentation と同じ API 形: Cancel / Skip / Pause / Resume / SetSpeed / Seek / IsPlaying / NormalizedTime / OnCompleted / OnCancelled / OnMarker / WaitAsync)。**`Signal`(コード → データ)は持たない**(§3.1: Cutscene は「待つ」トラックを持たない。ヒット待ちが要るなら Presentation を親にする。2026-09-18)
- PlayableDirector は Pool から借用。`timeUpdateMode = Manual`、GameLoopDriver の Tick で `time += dt; Evaluate()`
- ポーズ: `Flags.Pause` に従う。HitStop / スローは TimeService の dt に乗る
- 終了・キャンセル時: バインドを外し、SpawnModel を返却、カメラをブレンドアウトしてゲームカメラへ返す(§4.6.2)、KeepWhilePlaying の SE / VFX を止める(EventBus.End)
- ネット: §4.7(2026-09-18 に節として独立)

### 4.6 カメラ(シームレス化・ステップ fps・設定の持ち越し)(2026-09-18 新規)

#### 4.6.1 方式: Cinemachine は使わず、D-Drive 独自のブレンドで足りる

**確認した事実**: `Packages/manifest.json` に `com.unity.cinemachine` は**入っていない**(`com.unity.timeline` 1.8.12 はある)。Cinemachine を使うなら新規依存になる。

| 方式 | 内容 | 利点 | 欠点 |
|---|---|---|---|
| **A. D-Drive 独自ブレンド(採用)** | CutsceneManager が「ゲームカメラが今フレーム計算した姿勢」と「Timeline カメラの姿勢」を重み `w(t)` で補間して Camera に書く(§4.6.2)。新規依存なし | 要件(位置・回転・画角・ピントの数百 ms の繋ぎ)に対して過不足がない。Maya カメラは焼き済みなので Cinemachine の強み(追従・ノイズ・手続き的カメラ)は使わない。[42] §3.5 の依存表・ウィザードが増えない。ゲーム側のカメラ制御方式(自前 / Cinemachine)を問わず動く | ブレンドの補間は D-Drive が持つ(Cinemachine の Blend 曲線・BlendList は使えない)。ゲームカメラ制御が `LateUpdate` より後で姿勢を書いている場合は実行順の調整が要る(§4.6.5) |
| B. Cinemachine 導入 | Timeline の `CinemachineShot` クリップ + `CinemachineBrain` のブレンドに任せる | ブレンド曲線・優先度・Impulse(揺れ)が標準で揃う。Cinemachine を使うゲームなら自然 | **新規依存**(`com.unity.cinemachine`)。[42] §5.10「依存の追加 = MINOR + ウィザード検査」、§3.5 依存表と README の更新、MS2026 が Cinemachine を使わないなら持ち込み先に不要な依存を強いる。CameraFx(5-2)の Shake ノード方式とブレンドの主体が二重になる。「短い演出のみ」の用途に対して重い |

結論: **v1 は A**。将来ゲーム側が Cinemachine を採用したときは、[42] §7 A-7 の NGO と同じ流儀で `versionDefines`(`DDRIVE_CINEMACHINE`)を切り、「MainCamera 役割を `CinemachineCamera` にバインドする」任意アダプタを後付けできるようにしておく(v1 では作らない。§7.2-3)。

#### 4.6.2 ブレンド(ゲームカメラ ⇄ Timeline カメラ)

D-Drive Camera クリップ(§4.3)が持つ設定:

```csharp
public sealed class CutsceneCameraClip : PlayableAsset   // TimelineAsset のサブアセット。取り込みが自動生成
{
    // 焼かれたカーブ(取り込み時に FBX の AnimationClip から抽出。再取り込みで上書き)
    public AnimationCurve PosX, PosY, PosZ;
    public AnimationCurve RotX, RotY, RotZ, RotW;    // 四元数。評価後に正規化
    public AnimationCurve FieldOfView;               // 垂直画角(度)。Maya の焦点距離 + フィルムゲートから算出
    public AnimationCurve FocalLengthMm;             // 焦点距離(mm)。DoF の Bokeh 計算にも使う
    public AnimationCurve FocusDistance;             // ピント距離(m)。取れなければ空(§5.2 要検証)
    public AnimationCurve Aperture;                  // 絞り(f 値)。同上
    // デザイナーが触る設定(再取り込みで保持)
    public float StepFps;                            // 0 = 量子化なし(元 fps のまま)。§4.6.3
    public ValueDef BlendIn;                         // 0→1 の重み曲線 + 秒(TimeDef)。既定 0.25s / EaseInOutSine
    public ValueDef BlendOut;                        // 同上
    public CameraFocusMode Focus;                    // Off / Volume(既定。DoF を書く) / CameraOnly(Camera.focusDistance だけ。HDRP 向け)
}
```

ブレンドの規則:

- **重み `w`**: クリップ先頭から `BlendIn` の間 0→1、クリップ末尾(または Skip / Cancel 時点)から `BlendOut` の間 1→0。クリップの途中は 1。1 つの Timeline にカメラクリップが複数(ショット切替)あるとき、**クリップ間は Timeline の標準ブレンド(重なり)で Timeline カメラ同士を補間**し、ゲームカメラとのブレンドは最初のクリップの頭と最後のクリップの尻だけに掛かる
- **ゲームカメラの姿勢 `G`**: 「このフレームにゲーム側のカメラ制御が書いた姿勢」。書き込み直前に読む(§4.6.5 の実行順で保証)。画角・ピント等の「制御スクリプトが毎フレーム書くとは限らない値」は **再生開始時に控えた値を `G` とし、終了時に書き戻す**(毎フレーム読むと自分が前フレームに書いた値を読んでしまい、元に戻らなくなるため)
- **Timeline カメラの姿勢 `T`**: クリップのカーブを(ステップ fps で量子化した時刻で)評価し、原点(§4.2.1)の姿勢を掛けたもの
- **書き込み**: 位置 `lerp(G,T,w)`、回転 `slerp(G,T,w)`、画角 `lerp`、ピント距離・絞り `lerp`、DoF Volume の `weight = w`(§4.6.4)。`w = 0` のフレームは何も書かない(ゲームカメラに一切触らない = Cutscene が無いときと完全に同じ)
- Skip / Cancel: その時点の `T` を固定して `BlendOut` で戻す(急に切れない)。`BlendOut = 0` なら即座に戻る
- CameraFx(5-2)の Shake ノードは Camera の**親**にあり、本節の書き込みは Camera **本体**の Transform に行うため両立する(カットシーン中も Shake マーカーが効く)。`Camera.main` の付け替えは行わないので、CameraFx のノード再アタッチ処理も走らない

代案(不採用): **2 カメラ方式**(カットシーン専用 Camera をプールし、ゲームカメラを無効化して切り替える)。ゲームカメラ制御との実行順を気にしなくてよい利点があるが、URP の追加設定(`UniversalAdditionalCameraData` のレンダラー・ポストプロセス・Volume マスク・スタック)を毎回複製する必要があり、`Camera.main` が切り替わることで CameraFx のノードが付け替わる・AudioListener が別カメラに残る、と副作用が多い。1 カメラ上書き方式の方が「Cutscene が無いときと同じ」を保ちやすい。

#### 4.6.3 ステップ fps(「ぬるぬる過ぎる」対策。Unity 側・ショットごと・非破壊)

ユーザー要望: カメラだけコマ落ちさせたい(24fps 映画風・12fps ストップモーション風)。**Maya で焼く時ではなく Unity の再生時に調整できること**が必須(書き出し時に間引くと、気に入らなかったとき Maya からやり直しになる)。

| 方式 | 内容 | 利点 | 欠点 |
|---|---|---|---|
| **A. 評価時に量子化(採用)** | クリップの評価時刻を `tq = floor((t − clipStart) × StepFps) / StepFps + clipStart` に丸めてからカーブを評価する。`StepFps = 0` で無効 | 非破壊(カーブは元の fps のまま)。ショットごと(= クリップごと)に違う値にできる。Inspector の数値 1 つで即反映、スクラブでもプレビューでも同じ見え方。将来「オプション画面でカメラの滑らかさ」のような実行時変更もできる | 元 fps の約数でない値(30fps 元に 24)は間隔が不均一になる(Validation で Warning、§5.3)。量子化はカメラの全チャンネル(位置・回転・画角・ピント)に同時に掛ける(片方だけ滑らかだと気持ち悪い) |
| B. 取り込み時にキーを間引く(Constant 接線) | FBX 取り込みでカーブを再サンプル | Timeline ウィンドウのカーブ表示が「見た目どおり」になる | 値を変えるたび再取り込み。1 FBX に 1 値しか持てない(ショットごとに変えるには FBX を分ける)。ステップ有無を見比べるのに手間が掛かる |

採用 A の補足:

- **キャラ・小物・SE/VFX には掛けない**(量子化するのはカメラクリップの評価だけ)。「カメラだけコマ落ち、キャラは滑らか」がユーザーの意図
- スロー再生(TimeService)中は Timeline 時刻の進みが遅くなるので、同じ `StepFps` でも保持フレーム数が増える(= スローでもコマ落ち感が保たれる)。これは意図どおりとする
- ブレンド(§4.6.2)は量子化した `T` に対して掛ける。ブレンド中だけ滑らか、という見え方になるが、`BlendIn/Out` は数百 ms なので許容する
- 既定値は `CutsceneImportProfile.DefaultCameraStepFps`(初期値 0 = 無し)。取り込みで新規生成したクリップにだけ適用し、既存クリップの値は保持する

#### 4.6.4 カメラ設定の持ち越し(画角・ピント・絞り)と URP の Volume

ユーザー要望: Maya で決めたピント等をそのまま使いたい。**注意点**: URP では被写界深度は Camera ではなく **Volume の `DepthOfField` オーバーライド**で決まる。Maya のピント距離を `Camera` に入れても何も起きない(`Camera.focusDistance` は物理カメラの値で、URP の DoF はこれを読まない)。したがって **Camera と Volume の両方に書く**。

| 値 | Maya 側 | Unity で書く先 | 備考 |
|---|---|---|---|
| 画角 | 焦点距離(mm)+ フィルムゲート | `Camera.fieldOfView`(物理カメラ有効時は `focalLength` + `sensorSize`) | FBX 取り込みが Camera の画角カーブを作る(要検証、§5.2)。作らなければ焦点距離カーブから D-Drive が計算 |
| ピント距離 | `focusDistance` | **Volume: `DepthOfField.focusDistance`**(+ `Camera.focusDistance` にも書く。HDRP へ持ち込んだときはこちらが効く) | FBX に載るかは要検証。載らなければ Maya 側でカスタムアトリビュート `dd_focusDistance` をカメラに追加してキーを打つ(標準機能。§5.5 のロケーターと同じ経路) |
| 絞り | `fStop` | **Volume: `DepthOfField.aperture`** | 同上(`dd_fStop`) |
| 焦点距離 | 上と同じ | **Volume: `DepthOfField.focalLength`**(Bokeh モードのボケ量に効く) | 画角と同じカーブから |
| Near / Far | — | 引き継がない(ゲームカメラの値のまま) | Maya の値はモデリング用の値で、ゲームの描画距離とは無関係 |

Volume の書き方:

- **シーンの Volume(プロジェクトの VolumeProfile)は触らない**(Data 読み取り専用の原則と同じ。カットシーンが終わったあとに設定が残る事故を防ぐ)
- CutsceneManager が **D-Drive 専用の Global Volume**(`DDriveCutsceneVolume`、`priority` は大きめ、`isGlobal = true`)を初回に 1 つ作り、実行時に `ScriptableObject.CreateInstance<VolumeProfile>()` で作ったプロファイルに `DepthOfField`(`mode = Bokeh`)だけを載せる。**毎フレームの alloc は無し**(プロファイルとオーバーライドは 1 回だけ作り、`focusDistance.value` 等を書き換えるだけ)。`weight = w`(§4.6.2)で、ブレンド中は DoF もフェードする。`w = 0` のとき `weight = 0` = 通常時は存在しないのと同じ
- Volume は `Camera` の `UniversalAdditionalCameraData.volumeLayerMask` に含まれるレイヤーに置く(そのカメラの Volume マスクに含まれる最初のレイヤーを使う。無ければ警告 1 回 + DoF を書かない)。`renderPostProcessing = false` のカメラでは DoF が効かないので、これも警告 1 回(Validation は静的に検査できないため実行時警告のみ)
- `Focus = Off` にすればピントは一切書かない(低スペック向けに DoF を切る、既存の DoF 設定を尊重する、等)。`CameraOnly` は Volume を作らない(HDRP 移植時の逃げ道。[42] B-1 のとおり URP 以外は非対応だが、コードの分岐点だけ用意しておく)
- 型は `UnityEngine.Rendering.Volume` / `VolumeProfile`(Core RP)と `UnityEngine.Rendering.Universal.DepthOfField`(URP)。**`DDrive.Runtime.asmdef` は現状 URP を参照していない**(Editor のみ参照)ため asmdef 変更が要る(§6、§7.2-2)

#### 4.6.5 実行順(ゲームカメラ制御より後に書く)

- `GameLoopDriver` は `Update` で全 Manager を Tick する(確認済み: `Runtime/Loop/GameLoopDriver.cs`)。ゲームのカメラ制御は普通 `LateUpdate` で書くので、Tick の中で Camera に書くと**そのあとゲーム側に上書きされる**
- そこで CutsceneManager は Tick では「`T` と `w` の評価」までを行い、Camera への書き込みは **`DDriveCutsceneCameraApplier`**(`Camera.main` に初回自動追加する小さなコンポーネント。CameraFx の Shake ノードと同じ「無ければ作る、見つからなければ警告 1 回 + no-op」の流儀)が **`LateUpdate`(`DefaultExecutionOrder` を大きな値にして通常の LateUpdate より後)** で行う。書き込み直前に Camera の現在姿勢を `G` として読む(§4.6.2)
- Cinemachine の `CinemachineBrain` も `LateUpdate` で書くので、ゲーム側が Cinemachine を使っていてもこの順で上書きできる(Brain の Update Method が `SmartUpdate`/`LateUpdate` の場合。`FixedUpdate` の場合も LateUpdate より前)
- ゲーム側が PlayerLoop の後段(`PostLateUpdate` 等)で書いている場合は上書きされる。**MS2026 のカメラ制御がどこで姿勢を書いているかは要確認**(§7.2-6)。合わない場合は Applier の挿入点を PlayerLoop の `PostLateUpdate` 末尾に変える選択肢を残す

### 4.7 ネット(ローカル再生と同期再生の両方)(2026-09-18 新規)

ユーザー決定: 両方要る。**新しいフィールドは足さず、既存の `AssetFlags.Net`(全 Data 共通、[02] §2 / [14] §3)で切り替える。** Presentation(5-8 / 5-9 / 6-0)と同じ流儀にそのまま乗せる。

| `Flags.Net` | 意味 | 使いどころ |
|---|---|---|
| **Local**(既定) | 呼んだクライアントだけで再生。メッセージを送らない | 自分の画面だけの演出(勝利ポーズのカメラ等)。両者のゲームコードが同じタイミングで `Play` を呼ぶなら位相はほぼ揃うが、保証はしない |
| **Cosmetic** | 行為者が `CutscenePlayMsg` を Broadcast し、全員が `StartNetTime` に合わせてシークして再生。Skip / Cancel も中継。Late Join は Host の台帳から復元 | 両者が**同じ映像を同じ位相で**見る必要がある演出(必殺技カットイン、ラウンド開始デモ) |
| Simulated | Cutscene には意味を持たせない(Presentation と同じ)。Validation Info でローカル再生にフォールバック | — |

Cosmetic の中身(Presentation 5-8/5-9 の設計をそのまま流用。新規メッセージ型は 3 つ):

- `CutscenePlayMsg { CutId, SelfNetId, TargetNetId, Position, StartNetTime, Seed, HandleNetKey }` / `CutsceneSeekMsg { HandleNetKey, ToTime }`(Skip 用)/ `CutsceneCancelMsg { HandleNetKey }`。`HandleNetKey` の採番・`ReliableOrdered`・Host 経由中継・レート制限(60/秒/クライアント)・未知キー保留(6-6 の K3)はすべて `PresentationManager` の既存実装と同じ規則
- **受信側のシーク**: `elapsed = NetworkTime − StartNetTime` で `director.time` を合わせる。Timeline のカメラ・Animation トラックは連続系なので途中から再生できる。D-Drive SE / VFX クリップの one-shot は Presentation と同じ猶予(0.5 秒)以内なら遅れて発火、それより古ければスキップ
- **Skip**: `Skip` の種類(即終了 / マーカーまで)は Data で決まる。Cosmetic では Skip を Broadcast(`CutsceneSeekMsg`)し、自分を含む全員が受信してから Seek する(Cancel と同じ「Broadcast 前に自分だけ飛ばない」規則)。「相手にスキップさせない」はゲームロジック側の仕事(`CutsceneSkip.Disabled` にすれば Data として禁止もできる)。既定は「誰でも Skip でき、全員に効く」(§7.2-8)
- **PredictLocal**: Presentation と同じ意味。必殺技の入力に対して行為者は即再生し、自分の Broadcast を受信しても二重生成しない。既定 false
- **Late Join**: Host の台帳(`HandleNetKey → {Data, ctx, StartNetTime, Seed}`)から `CutscenePlayMsg` を再送 → 受信側はシーク再生で復元。Presentation と違い、カットシーンは連続系なので途中参加でも「今の位置から」見える
- **カメラ**: 受信側でもカメラを奪う(それが「同じ映像」の意味)。相手の画面でカメラを奪ってよいかはゲームデザインの問題で、奪いたくなければ Local にする
- **LockInput**: ローカル。各クライアントが自分の入力を止める(Host 権威のゲームロジックが入力を捨てる側の実装は MS2026 側)
- ContentHash([14] §7、6-5)は `CatalogEntry` の Id / Type / Address / `Flags.Net` を対象にするため、Cutscene のカタログを `ContentHashCatalogCoverageValidator` の対象に含める(6-10a。含めないと Error)

---

## 5. Maya → Unity 取り込み(6-10c、Maya スクリプト無し)

### 5.1 Maya 側でやること(標準 FBX 書き出しだけ)

| 項目 | 設定 | 理由 |
|---|---|---|
| 書き出し単位 | **1 ショット = 1 FBX セット**(決定 2026-09-18)。セット = カメラ + 小物の `<ショット>.fbx` 1 本 + **キャラごと**の `<ショット>__<Model識別子>.fbx`(§5.4 Humanoid の制約) | Unity 側で「このファイル群 = この CutsceneData」と 1 対 1 にできる |
| 逃げ道: 1 FBX に複数ショット | `CutsceneData.SourceFrameRange` にフレーム範囲を入れると、その範囲だけを切り出す。2 つ目以降の CutsceneData は AssetBrowser の「同じ FBX から別ショットを作る」で作り、`ImportSourceGuid`(既存フィールド)で同じ FBX を指す | 既定は A(1 ショット = 1 FBX)。範囲が空(0/0)なら FBX 全体 |
| Animation > Bake Animation | ON(開始〜終了フレーム) | IK・コンストレイント・エクスプレッションは Unity に来ないため焼く |
| Cameras | ON | カメラの位置・回転・画角(Focal Length)を持ってくる |
| 単位 / アップ軸 | cm / Y-up(Unity の既定取り込みに合わせる) | スケールずれ防止 |
| シーンの fps | プロジェクトの既定(30 または 60。`CutsceneImportProfile.DefaultFrameRate`)| §5.3。違っても取り込めるが Validation Info |
| 命名 | §5.5 の最小ルール(キャラ FBX のファイル名サフィックス = D-Drive のモデル識別子) | Unity 側でトラックと役割・使うモデルを自動判定するため |

> キャラクター本体(メッシュ・リグ)は通常どおり別 FBX で ModelData に登録し、カットシーン FBX には**アニメーションだけ**入れる(決定 2026-09-18。同じキャラを複数ショットで使い回すため、また Humanoid のリターゲットを ModelData の Avatar で揃えるため。§5.4)。

### 5.2 Unity 側の自動処理

1. `Assets/SourceAssets/Cutscene/<カテゴリ>/` に FBX が入る → `AssetPostprocessor` が検知(`ImportRule` の 10 種別目として `Cutscene` を追加。[10] §3 の 9 種別 + 1)
2. ファイル名で役割を分ける: `<ショット>.fbx` = カメラ + 小物(Generic)、`<ショット>__<Model識別子>.fbx` = そのキャラの骨アニメ(**Humanoid**。`ModelImporter.animationType = Humanoid`、`avatarSetup = CopyFromOther`、`sourceAvatar = ModelData.Avatar`。§5.4)。同じ `<ショット>` のファイル群を 1 つの CutsceneData にまとめる
3. FBX 内のアニメ付きノードを §5.5 のルールで分類し、まとまりごとに AnimationClip を切り出す(カメラ / キャラ / 小物 / イベント用ロケーター)。`SourceFrameRange` があればその範囲だけ
4. TimelineAsset を生成(既存なら**自動生成トラックだけ差し替え、デザイナーが足した D-Drive トラックは保持**)
   - カメラ → **D-Drive Camera クリップ**(§4.3、§4.6)。取り込み後の Camera コンポーネントのカーブ(位置・回転・画角)を `AnimationUtility.GetEditorCurve` で抽出してクリップのカーブに写す。画角: Unity の FBX 取り込みがカメラの画角アニメ(`field of view` / `focalLength`)を作るかは**要検証**。作らなければ焦点距離カーブ + フィルムゲートから D-Drive が垂直画角を計算する。**ピント距離・絞り**: FBX のカメラ属性(`FocusDistance` / `FStop`)を Unity が Camera に反映するかは**要検証**(§7.3)。反映しなければ Maya 側でカメラにカスタムアトリビュート `dd_focusDistance` / `dd_fStop` を追加してキーを打つ運用にし、`OnPostprocessGameObjectWithAnimatedUserProperties` で読む(ロケーターと同じ経路)。どちらも取れなければカーブは空 = ピントは書かない(`Focus = Off` 相当)
   - クリップの `StepFps` / `BlendIn` / `BlendOut` / `Focus` は新規生成時だけ `CutsceneImportProfile` の既定値を入れ、再取り込みでは保持する
   - キャラ → Animation トラック(Humanoid クリップ)。役割 = ファイル名サフィックスの `Hero`、使うモデルは ModelData 識別子 `Hero` を自動で探してバインド表に入れる(見つからなければ空欄 + Validation Warning。手で選び直せる)
   - 小物 → Animation トラック(Generic)。役割 = 名前空間 / `PRP_` 接頭辞
   - イベント用ロケーター → D-Drive Signal マーカー(§5.5、要検証)
5. CutsceneData を作成・更新(`Assets/GameData/Cutscene/<カテゴリ>/CUT_<カテゴリ>_<識別子>.asset`)、`FrameRate` を FBX の fps に設定、Addressables 登録、ID 採番
6. 再取り込み時の差分(ノードが増えた / 消えた / 長さが変わった / fps が変わった)をログと Validation に出す。**消えたノードのトラックは削除せずミュート**(デザイナーの手作業を消さない)

### 5.3 fps とタイミング(2026-09-18 改定)

ユーザー決定: 30 と 60 を切り替えられるようにする(どちらかに固定しない)。「切り替え」が何を指すかを 2 段に分ける。

| 段 | 何か | どこに持つ | 変えると何が起きるか |
|---|---|---|---|
| **プロジェクトの既定 fps** | 「このプロジェクトの Maya シーンは何 fps で作るか」の宣言 | `CutsceneImportProfile.DefaultFrameRate`(30 / 60。`FindOrDefault()` の設定 SO、[05] の `Anim2DImportProfile` と同型) | 新規 CutsceneData の初期値と、Validation の比較基準が変わるだけ。**既存の CutsceneData は変わらない** |
| **CutsceneData ごとの fps** | その FBX が実際に焼かれた fps(真実は Maya) | `CutsceneData.FrameRate`(取り込みで FBX から自動設定。手で変えられる) | `TimelineAsset.editorSettings.frameRate` に反映(Timeline ウィンドウのスナップ・フレーム表示)。**再生は秒ベース**なので、30fps のカットシーンも 60fps の描画で滑らかに補間される(逆も同じ)。カメラをコマ落ちさせたいなら `StepFps`(§4.6.3)であって `FrameRate` ではない |

- D-Drive のクリップ / マーカーは秒で持つが、Inspector ではフレーム表記も併記(Anim の Frame イベントと同じ。換算は `CutsceneData.FrameRate`)
- **Validation**(6-10d):

| 検査 | 重度 | 意味 |
|---|---|---|
| 同じ CutsceneData を構成する FBX 群(カメラ / キャラ / 小物)の fps が不一致 | Warning | 旧 §5.3 の「カメラ 24fps、キャラ 30fps」問題。必ずずれる |
| FBX の fps と `CutsceneData.FrameRate` が不一致(手で変えた / 再取り込みで変わった) | Warning | 自動修正(FixAction: FBX に合わせる) |
| FBX の fps とプロジェクト既定 fps が不一致 | Info | 混在自体は許す(30 で作った既存ショットと 60 の新ショットが同居してよい)。「宣言と違う」ことだけ知らせる |
| Camera クリップの `StepFps` > `FrameRate` | Warning | 元より細かくはできない(値は無視されず、単に効かない) |
| `FrameRate` が `StepFps` の整数倍でない(30 元に 24 等) | Warning | 保持フレーム数が不均一になる。候補(60 → 30/20/15/12/10、30 → 15/10/6)を提示 |
| `FrameRate` が 30 / 60 以外 | Info | 24fps 等も動くが、プロジェクト方針(30 / 60)から外れている |

### 5.4 Humanoid(決定 2026-09-18)

ユーザー決定: **Humanoid**。キャラ本体の FBX とアニメの FBX は分ける。

- **リターゲット**: カットシーン FBX(アニメ専用)を `Humanoid` + `CopyFromOther`(`sourceAvatar = ModelData.Avatar`)で取り込む。同じ Avatar 定義を使うので、ModelData の Prefab(Humanoid Animator)にそのまま流せ、筋肉マッピングのずれによる姿勢崩れが起きない。`ModelData.Avatar` が未設定なら既存の `ModelDataValidator` の Error(「Animator はあるが Avatar 未設定」)が先に出るので、Cutscene 側は Warning + `CreateFromThisModel` で仮に取り込む(骨だけの FBX からでも Avatar は作れる)
- **1 FBX = 1 Humanoid の制約**: Unity の `ModelImporter` は 1 ファイルに 1 つの Avatar / `animationType` しか持てない。**1 つの FBX に 2 キャラ(`Hero:` と `EnemyBoss:`)を入れると Humanoid では片方しかリターゲットできない**。したがって **キャラごとに FBX を分ける**(`<ショット>__Hero.fbx`、`<ショット>__EnemyBoss.fbx`。Maya では Export Selection をキャラ数分行う。スクリプト不要だが手数は増える。§7.2-4)。カメラ・小物は Generic なので 1 本にまとめてよい
- 同じキャラ 2 体(`Hero_2`)は `<ショット>__Hero_2.fbx`。ModelData は `Hero` を引く(末尾の `_2` 以降を落として検索)
- Generic は**小物専用**(剣・扉など。ボーン名一致で流す。Validation で検出)。キャラを Generic で取り込む経路は作らない(ModelData 側が Humanoid 前提のため、Generic クリップは Humanoid Animator で再生されない)
- Timeline の Animation トラック設定: `Apply Foot IK` は既定 OFF(Maya で焼いた足位置を優先)。ルートモーション(Hips のワールド移動)と原点(§4.2.1)の組み合わせは 6-10a で実機確認(§7.3)
- **Avatar Mask / Override トラック**(上半身だけ差し替え等)は Timeline 標準機能がそのまま使える(Humanoid の利点)

### 5.5 FBX に何を入れるか / Unity でどう見分けるか / 命名規則(2026-09-13 追記、2026-09-18 改定)

**入れるもの**

| 入れる | 内容 | 備考 |
|---|---|---|
| ○ カメラ | 位置・回転・画角(焦点距離)。可能ならピント距離・絞り(§5.2 要検証) | ショットの切り替えは Maya の Camera Sequencer の標準機能「Ubercam 作成」で 1 台のカメラに焼くのが簡単(スクリプト不要)。複数カメラのまま出す場合は Unity 側でカメラごとに Camera クリップを作り、Timeline 上で並べる(クリップ間は Timeline の標準ブレンド、§4.6.2) |
| ○ キャラの骨アニメ | ジョイントのアニメだけ(メッシュは入れない「アニメ専用」書き出し)。**キャラごとに別 FBX**(§5.4) | キャラ本体は別途 ModelData として登録済みのものを使う |
| △ 小物のアニメ | 剣・扉など動くもの | 動かない背景は入れない(Unity 側のシーンで配置) |
| △ イベント用ロケーター | 「ここで SE / VFX」の目印 | §下記。無くても Unity の Timeline で後から置ける |
| × メッシュ・マテリアル・ライト | — | 見た目は Unity 側(ModelData / MaterialData)が真実。ライトは Unity で作る |

**Unity での見分け方**

- **キャラは FBX のファイル名で分かる**(`<ショット>__<Model識別子>.fbx`。§5.4 の制約でキャラごとに 1 本なので、名前空間に頼る必要が無い)。名前空間(`Hero:`)は付いていてもよい(サフィックスが無いファイルの救済に使う)
- **カメラは名前に頼らず判別できる**。FBX はノードの「種類(カメラ / ジョイント / 空ノード)」を持っており、Unity の取り込み結果でも Camera コンポーネントが付くので確実に分かる
- 小物は名前空間(またはノード名の接頭辞 `PRP_`)で判別する

**最小の命名規則(Maya 作業者が守るのはこれだけ)**

| 対象 | ルール | 例 | Unity 側の対応 |
|---|---|---|---|
| ショット | ファイル名 `<ショット>.fbx`(カメラ + 小物) | `Opening01.fbx` | CutsceneData `CUT_<カテゴリ>_Opening01` |
| キャラ | ファイル名 `<ショット>__<Model識別子>.fbx`(`__` 2 つ) | `Opening01__Hero.fbx` | 役割 `Hero`、ModelData `MODEL_*_Hero` を自動でバインド、Humanoid + `ModelData.Avatar` で取り込み |
| 同じキャラを 2 体 | 識別子の末尾に `_2` 以降 | `Opening01__Hero_2.fbx` | 同じ ModelData を 2 体生成 |
| カメラ | 1 台なら自由。複数なら `CAM_<名前>` | `CAM_Main` | 役割 MainCamera(既定)。ピント・絞りは任意でカスタムアトリビュート `dd_focusDistance` / `dd_fStop`(§5.2) |
| 小物 | 名前空間 = Model 識別子、または `PRP_<Model 識別子>` | `PRP_Sword` | ModelData Sword |
| イベント(任意) | ロケーター `EVT_<キー>` | `EVT_Hit` | Signal マーカー `cutscene/hit` |

- `@` 区切り(`Model@Anim.fbx`)は使わない。Unity が「同じフォルダの `Model.fbx` にアニメを合流させる」レガシー挙動を起こすため、`__` にした
- **Maya のモデル名と Unity の ModelData 識別子を一致させるのが要**。Maya のキャラリグのファイル名や名前空間を、仕様書「アセット」タブ([27] §3.1)の Model 識別子に揃えて運用すれば、取り込み時の手作業がほぼ無くなる
- 一致しない場合でも CutsceneData のバインド表で手動で結び付けられる(ルールは「自動で埋まる」ためのもので、破ったら取り込めないわけではない)

**イベント用ロケーター(任意・要検証)**

- ロケーター `EVT_Hit` にカスタムアトリビュート(例 `ddEvent`、整数)を追加してキーを打つと、FBX にアニメ付きユーザープロパティとして出る。Unity の `AssetPostprocessor.OnPostprocessGameObjectWithAnimatedUserProperties` で読めるので、値が変わったフレームに Signal マーカーを自動で置ける
- Maya 側はアトリビュート追加とキー打ちだけ(標準機能)。ただし Unity 2023 以降の FBX 取り込みで確実に取れるかは 6-10c 着手時に検証する。取れなければこの機能は外し、Unity の Timeline 上で置く運用にする。**カメラのピント・絞り(`dd_focusDistance` / `dd_fStop`)も同じ経路に相乗りする**ので、検証は一度で済む

---

## 6. 影響範囲(実装時に確認が要るもの)(2026-09-18 改定)

- **asmdef(要承認、§7.2-2)**: `DDrive.Runtime` に `Unity.Timeline` を追加(1.5 のとおり)。加えて §4.6.4 の Volume 書き込みのため **`Unity.RenderPipelines.Universal.Runtime` と `Unity.RenderPipelines.Core.Runtime`** も必要(現状は `DDrive.Editor` だけが参照)。URP は [42] §3.5 / B-1 で「必須依存(URP のみ対応)」なので**新しいパッケージ依存は増えない**が、Runtime asmdef の参照が増えるのは CLAUDE.md §0-9「asmdef 構成は聞く」の対象。代案 = Timeline 関連を別 asmdef `DDrive.Runtime.Timeline`(Foundation / Runtime / Timeline / URP を参照)に分け、`DDrive.Runtime` を汚さない。ただし `PresentationManager` の `TrackKind.Timeline` から Cutscene を呼ぶには `DDrive.Runtime` 側にインタフェース(`ICutscenePlayer`)を置いて Bootstrap で差し込む逆依存の回避が要り、生成コード(`AssetIds.g.cs` の `AssetId<CutsceneMarker>`)と P-5 の `DDrive.Generated.asmdef` も新 asmdef を参照する必要がある。**提案は「直接追加」**(Timeline も URP も必須依存であり、任意依存の NGO と違って `versionDefines` で切る理由が無い)
- **Cinemachine は導入しない**(§4.6.1)。manifest 変更なし。[42] §3.5 依存表は `com.unity.timeline` が既に「○(6-10 以降)」で載っているので P-1 で「6-10 で有効化済み」に書き換えるだけ
- **AssetType enum**: `Cutscene` を末尾追加(シリアライズ値は不変)。`KnownPrefixes` に `CUT`([42] §5.13)
- **新規メッセージ型** 3 つ(§4.7)。P 発効前なので追加は自由だが、型名・名前空間はワイヤ互換([42] §5.6)になるので `Runtime/Net/CutsceneMessages.cs` に Presentation と同じ命名で置く
- **`ImportRule` の対象種別に `Cutscene` を追加**(9 → 10。[10] §3、[09] §1.1)。`SourceAssets/Cutscene/` の 1 つの `<ショット>` に複数 FBX が対応するため、既存の「1 元ファイル = 1 Data」の対応付け(`ImportSourceGuid`)は**カメラ FBX の GUID を代表**にし、キャラ FBX は CutsceneData 側のリスト(`SourceFbxGuids[]`)で追跡する
- **`CutsceneImportProfile`**(設定 SO、§4.1 / §5.3)を `Assets/GameData/Settings/` に `FindOrDefault()` で生成([42] §2.1 の「G: 持ち込み先で作る」分類)
- **ContentHash**: Cutscene カタログを `ContentHashCatalogCoverageValidator` の対象に含める(§4.7)
- **`DDriveCutsceneCameraApplier` / `DDriveCutsceneVolume`**: ランタイムがシーンに置く永続オブジェクト(CameraFx の `DDriveCameraShakeNode` と同じ流儀。DontDestroyOnLoad のカメラなら追従)
- 標準の Audio / Control / Signal トラックは禁止 API 規約(AudioSource.Play / Instantiate 直呼び)と衝突するので、Validation で「D-Drive トラックを使ってください」と Warning を出す。**標準 Animation トラックをカメラにバインドしている**場合も同様に Warning(Camera クリップを使う)
- `PresentationManager` の `TrackKind.Timeline`(現在は警告 + no-op)を `CutsceneManager` に接続。Presentation → Cutscene → Presentation の循環を `PresentationDataValidator` / `CutsceneDataValidator` の両方で Error にする

---

## 7. 決定事項と未決事項

### 7.1 決定(2026-09-18 ユーザー回答)

| # | 項目 | 決定 | 設計への反映 |
|---|---|---|---|
| 1 | 用途の比重 | **短い演出のみ**(数秒。長いカットシーンは作らない)。PresentationData と役割が重なることは承知のうえ | §3.1 使い分け(併存を提案。統合可否は §7.2-1) |
| 2 | カメラ | **シームレスにしたい**(Maya カメラをただ再生するのではなく、ゲームカメラとの繋ぎが要る) | §4.6.1 独自ブレンド(Cinemachine 不採用)、§4.6.2 ブレンド仕様 |
| 3 | 書き出し単位 | **A(1 ショット = 1 FBX)**。ただし「1 FBX に複数ショット」の逃げ道として `CutsceneData` にフレーム範囲を持てる(既定は A) | §4.1 `SourceFrameRange`、§5.1。Humanoid の制約でキャラごとに FBX を分ける「FBX セット」になった(§5.4) |
| 4 | キャラアニメ | **Humanoid**。キャラ本体の FBX とアニメの FBX は分ける | §5.4、§5.5 命名規則(ファイル名サフィックス) |
| 5 | fps | **30 と 60 を切り替えられる**(固定しない) | §5.3(プロジェクト既定 = `CutsceneImportProfile`、Data ごと = `FrameRate`、再生は秒ベース) |
| 6 | ネット | **ローカル再生と同期再生の両方**(`CutsceneData` で切替) | §4.7(既存 `Flags.Net` の Local / Cosmetic。新フィールド無し) |
| 7 | (追加要望)カメラの fps を自由に | Unity の再生時に、ショットごとに調整できること | §4.6.3 評価時量子化(`StepFps`、非破壊) |
| 8 | (追加要望)カメラ設定の持ち越し(ピント等) | URP では DoF は Volume の設定なので Camera と Volume の両方へ書く | §4.6.4 |

### 7.2 未決(6-10a 着手前にユーザーが決める)

| # | 内容 | 提案 | 代案 | 影響 |
|---|---|---|---|---|
| 1 | **CutsceneData と PresentationData の統合可否** | **併存(案 A)**: 「Maya の FBX を使うなら Cutscene、使わないなら Presentation」の 1 基準で使い分け。相互入れ子可、循環は Error | B: Presentation に統合 / C: Cutscene に統合(§3.1 の表) | B は PresentationData のシリアライズ形式が大きく変わる(§0-9)。C は 5-1〜5-16 の作り直し |
| 2 | **asmdef**: `DDrive.Runtime` に `Unity.Timeline` + URP(Universal.Runtime / Core.Runtime)を直接追加するか | **直接追加**(どちらも必須依存。§6) | 別 asmdef `DDrive.Runtime.Timeline` + `ICutscenePlayer` で逆依存回避 | 別 asmdef は生成コード・P-5 の `DDrive.Generated.asmdef` にも参照追加が要る |
| 3 | **Cinemachine アダプタ**を将来用意するか | **v1 は無し**。ゲーム側(MS2026)が Cinemachine を採用したら `DDRIVE_CINEMACHINE` の `versionDefines` で任意対応を後付け | 今から導入(新規依存、[42] §5.10 MINOR + ウィザード) | MS2026 のゲームカメラの実装方式による(未確認) |
| 4 | **キャラごとに FBX を分ける運用**でよいか(Humanoid の 1 FBX = 1 Avatar 制約。Maya の Export Selection がキャラ数分) | **分ける**(§5.4) | 1 ショットにキャラ 1 体までに制限 / 2 体目以降は Generic(ModelData 側も Generic にする必要があり非推奨) | Maya 側の手数(スクリプト無しは維持できる) |
| 5 | **AudioListener** をカットシーン中にカメラへ追従させるか | **追従させない**(1 カメラ上書き方式なので Listener はそのままカメラに付いてくる = 自動で追従する。別オブジェクトに Listener がある構成なら追従しない) | Listener を一時的にカメラへ移す | 数秒の演出では差が小さい |
| 6 | **カメラ書き込みの実行順**(`LateUpdate` 末尾)で MS2026 のカメラ制御と衝突しないか | **MS2026 のカメラ制御がどのタイミングで姿勢を書くか確認**してから 6-10a に入る(§4.6.5) | Applier を PlayerLoop `PostLateUpdate` 末尾に挿す | 衝突するとブレンド中にカメラが震える |
| 7 | **`LockInput` の受け口**(UiManager / 入力側の API がまだ無い) | 6-10a では `CutsceneHandle.IsInputLocked` と EventBus 通知だけ用意し、実際に入力を止めるのはゲーム側 | D-Drive 側に入力ロックの共通 API を作る | MS2026 の入力系に依存 |
| 8 | **Cosmetic 時の Skip 権限** | **誰でも Skip でき、全員に効く**(Broadcast) | Host のみ / 行為者のみ | ゲームルール寄り。Data の `CutsceneSkip.Disabled` で禁止はできる |
| 9 | `StepFps` の**既定値** | 0(量子化なし)。プロジェクト既定は `CutsceneImportProfile` で変えられる | 24 を既定にする | 見た目の好みなので実データで判断 |

### 7.3 要検証(6-10c 着手時に Unity 実機で確かめる。設計は変えない)

- Unity の FBX 取り込みがカメラの**画角アニメ**(`field of view` / `focalLength` カーブ)を作るか。作らなければ焦点距離から計算(§5.2)
- FBX のカメラ属性 **`FocusDistance` / `FStop`** が Unity の Camera に反映されるか。反映しなければ `dd_focusDistance` / `dd_fStop` のカスタムアトリビュート経路(§5.2)
- `OnPostprocessGameObjectWithAnimatedUserProperties` でアニメ付きカスタムアトリビュートが Unity 6 でも取れるか(ロケーターとピント・絞りの両方が依存)
- Humanoid + Timeline Animation トラックのオフセットで、Maya のワールド座標と原点(§4.2.1)の合成が期待どおりになるか(ルートモーション / Bake Into Pose の設定)
- URP の `Volume.weight` を毎フレーム書き換えたときの DoF の追従(Bokeh モードの `focusDistance` 変更にフレーム遅れが無いか)
