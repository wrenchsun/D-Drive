# 26. Timeline 連携(Maya FBX 取り込み + D-Drive トラック) 詳細設計(ドラフト)

関連: [08_presentation.md](08_presentation.md) §3 Timeline トラック / [05_model_animation.md](05_model_animation.md) / [02_core_framework.md](02_core_framework.md) §3 AssetEvent / [13_extensions.md](13_extensions.md) B-9 カットシーン種別 / [11_tasks.md](11_tasks.md) 6-10a〜6-10d

> 2026-09-13 ドラフト。ユーザー要望:「Maya のカメラやアニメを Timeline に流し込み、Unity 側で SE や VFX を足す。ほかのアセットと同様にイベントも付けたい。Maya 側スクリプトは無しが望ましい」。§7 の未決事項をユーザーと詰めてから 6-10a に着手する。
>
> **2026-09-13 ユーザー決定: 実装は P6 の最後(6-10a〜d)、運用開始後に行う**(実際にカットシーンを作る段階がまだ先のため。旧チケット番号 5-3a〜d)。

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

---

## 3. 全体像

```
Maya ──FBX 書き出し(標準機能のみ)──▶ Assets/SourceAssets/Cutscene/<カテゴリ>/<ショット>.fbx
                                               │ AssetPostprocessor(6-10c)
                                               ▼
                         CutsceneData(新種別)  ── TimelineAsset(自動生成・再生成可能)
                           ├ 役割バインド表(Camera / Self / Target / 名前付き)
                           ├ Events(AssetEvent[] 共通)
                           └ Flags / Net 同期設定
                                               │
Unity(デザイナー)── Timeline ウィンドウで D-Drive トラックを追加(6-10b)
                           ├ D-Drive Event トラック(AssetEvent マーカー)
                           ├ D-Drive SE / VFX / AnchorGroup / Shake / Haptic / Presentation クリップ
                           └ Signal マーカー(コード通知)
                                               │
ゲーム ── Cutscene.Play(CUTID.X, ctx) → CutsceneManager
                           ├ PlayableDirector をプールから借りる(Manual 更新)
                           ├ 役割名 → ctx / シーンのオブジェクトへバインド
                           └ 各クリップ・マーカーは既存 Manager / AssetEventDispatcher へ委譲
```

- **編集 UI は Unity 標準の Timeline ウィンドウを使う**(独自のタイムライン UI は作らない。業界標準で学習資料も多く、クリップのドラッグ・ブレンド・スナップ・カーブ編集が揃っている)。D-Drive 側は「D-Drive トラックの Inspector」「確認用シーン」「バインド検査」「Validation」を足す
- PresentationData(5-1)の `TrackKind.Timeline` は CutsceneData を 1 本のトラックとして再生する(短い技演出の中に Timeline を入れる用途)

---

## 4. D-Drive 側の設計

### 4.1 新種別 CutsceneData(AssetType.Cutscene、接頭辞 `CUT`)

```csharp
public sealed class CutsceneData : AssetDataBase
{
    public TimelineAsset Timeline;           // 自動生成 or 手置き
    public CutsceneBinding[] Bindings;       // 役割名 → 解決方法
    public CutsceneSkip Skip;                // 不可 / 即終了 / 指定マーカーまで飛ばす
    public DirectorWrapMode Wrap;            // 既定 None
    public bool LockInput;                   // 再生中はプレイヤー入力を止める(UiManager/入力側へ通知)
    // Events(AssetEvent[])は AssetDataBase 共通。Trigger=Time で Timeline 上の秒として扱う
}

[Serializable]
public struct CutsceneBinding
{
    public string TrackName;                 // "Camera" / "Hero" / "Enemy01" …(FBX のノード名から自動)
    public CutsceneBindTarget Target;        // MainCamera / Self / Target / SpawnModel / SceneObjectByName / AnchorPoint
    public AssetId<ModelMarker> Model;       // SpawnModel のとき: ModelsManager で出して終了時に返す
    public string SceneObjectName;
}
```

- `AssetType` enum に `Cutscene` を**末尾追加**(既存値は不変。[13] B-9 の枠を使う)
- 定数クラスは `CUTID`

### 4.2 バインド解決(「誰を動かすか」)

| Target | 解決先 | 典型 |
|---|---|---|
| MainCamera | ゲームのメインカメラ(再生中だけ Transform/FOV を奪い、終了時に元へ戻す) | Maya カメラ |
| Self / Target | `PlayContext.Self` / `Target` の Animator | 主人公・敵 |
| SpawnModel | ModelData を ModelsManager で生成して結び、終了時に返却 | カットシーン専用の登場人物・小物 |
| SceneObjectByName | シーン内の名前一致 | 背景ギミック |
| AnchorPoint | AnchorPoint([21])の位置に置く | 「右手に剣を持たせる」等 |

未解決は **警告 + そのトラックだけミュートで続行**(TL;DR #4)。Validation で事前に検出する。

### 4.3 D-Drive トラック(6-10b)

| トラック / マーカー | 中身 | 委譲先 |
|---|---|---|
| **D-Drive Event** マーカー | `AssetEvent` 1 件(PlayAsset / SetParam / SendMessage / Duck、Repeat、Anchor) | `AssetEventDispatcher`(Anim / VFX と同じ経路) |
| **SE** クリップ | SeId。区間の長さ = ループ SE の鳴らし続け時間(OneShot は開始点のみ使用) | AudioManager |
| **VFX** クリップ | VfxId + AnchorDef。区間終了で Stop(ループ VFX 向け) | VfxManager |
| **AnchorGroup** クリップ | 配置セット | AnchorGroupPlayer |
| **Shake / Haptic** マーカー | ShakeId / HapticId(5-2 / 5-2b 完了後) | CameraFx / HapticsManager |
| **UI** クリップ | CanvasId(字幕・レターボックス等)を区間中だけ Open | UiManager |
| **Signal** マーカー | 文字列キー(`cutscene/xxx` 規約) | `CutsceneHandle.OnMarker` → コード |

- 1 本の「D-Drive トラック」に SE / VFX / Event を全部載せる案もあるが、**種類ごとに行を分ける方が Timeline ウィンドウ上で読みやすい**ので分ける
- クリップの Inspector は既存の IdRef ドロップダウン(AssetIdDrawer)を使う = 名前で選べる、Missing は赤表示

### 4.4 編集中プレビュー(スクラブ対応)

- Timeline ウィンドウでの再生・スクラブ中も **実 Manager を駆動する**(ADR-4)。確認用シーン `CutscenePreviewScene` に Editor 用 Manager 群を置き、`PreviewService` と同じ仕組みで動かす
- 巻き戻し・飛ばし: SE は「区間に入った瞬間」だけ鳴らし、スクラブで同じ点を何度も通っても連打しない(ドラッグ中は無音、再生ボタン中のみ発音)。VFX は `SceneVfxPreviewDriver` の手動 Simulate で「クリップ開始からの経過秒」に合わせる
- Maya カメラのトラックは Game ビューで確認できるように、プレビュー中はメインカメラにバインドする

### 4.5 ランタイム(CutsceneManager)

- `Cutscene.Play(id, ctx)` → `CutsceneHandle`(Presentation と同じ API 形: Signal / Cancel / Pause / Resume / SetSpeed / Seek / IsPlaying / NormalizedTime / OnCompleted / OnMarker / WaitAsync)
- PlayableDirector は Pool から借用。`timeUpdateMode = Manual`、GameLoopDriver の Tick で `time += dt; Evaluate()`
- ポーズ: `Flags.Pause` に従う。HitStop / スローは TimeService の dt に乗る
- 終了・キャンセル時: バインドを外し、SpawnModel を返却、MainCamera を戻す、KeepWhilePlaying の SE / VFX を止める(EventBus.End)
- ネット(5-8 と同じ方針): サーバーが「開始時刻 + ID + 役割の NetObjectId」を送り、クライアントは遅延分シークして開始

---

## 5. Maya → Unity 取り込み(6-10c、Maya スクリプト無し)

### 5.1 Maya 側でやること(標準 FBX 書き出しだけ)

| 項目 | 設定 | 理由 |
|---|---|---|
| 書き出し単位 | **1 ショット(またはカットシーン 1 本)= 1 FBX** を推奨 | Unity 側で「このファイル = この CutsceneData」と 1 対 1 にできる |
| Animation > Bake Animation | ON(開始〜終了フレーム) | IK・コンストレイント・エクスプレッションは Unity に来ないため焼く |
| Cameras | ON | カメラの位置・回転・画角(Focal Length)を持ってくる |
| 単位 / アップ軸 | cm / Y-up(Unity の既定取り込みに合わせる) | スケールずれ防止 |
| シーンの fps | プロジェクトで統一(例: 30fps)| §5.3 |
| 命名 | §5.5 の最小ルール(リファレンスの名前空間 = D-Drive のモデル識別子) | Unity 側でトラックと役割・使うモデルを自動判定するため |

> キャラクター本体(メッシュ・リグ)は通常どおり別 FBX で ModelData に登録し、カットシーン FBX には**アニメーションだけ**入れる運用を推奨(同じキャラを複数ショットで使い回すため)。

### 5.2 Unity 側の自動処理

1. `Assets/SourceAssets/Cutscene/<カテゴリ>/` に FBX が入る → `AssetPostprocessor` が検知
2. FBX 内のアニメ付きノードを §5.5 のルールで分類し、まとまりごとに AnimationClip を切り出す(カメラ / キャラ(名前空間ごと)/ 小物 / イベント用ロケーター)
3. TimelineAsset を生成(既存なら**自動生成トラックだけ差し替え、デザイナーが足した D-Drive トラックは保持**)
   - カメラ → Animation トラック(役割 MainCamera)。画角は Camera の fieldOfView カーブとして取り込む(Unity の FBX 取り込みがカメラの画角アニメに対応しているかは**要検証**。非対応なら焦点距離カーブから FOV を計算するカーブ変換を D-Drive 側で行う)
   - キャラ → Animation トラック。名前空間 `Hero:` なら役割 "Hero"、使うモデルは ModelData 識別子 `Hero` を自動で探してバインド表に入れる(見つからなければ空欄 + Validation Warning。手で選び直せる)
   - イベント用ロケーター → D-Drive Signal マーカー(§5.5、要検証)
4. CutsceneData を作成・更新(`Assets/GameData/Cutscene/<カテゴリ>/CUT_<カテゴリ>_<識別子>.asset`)、Addressables 登録、ID 採番
5. 再取り込み時の差分(ノードが増えた / 消えた / 長さが変わった)をログと Validation に出す。**消えたノードのトラックは削除せずミュート**(デザイナーの手作業を消さない)

### 5.3 fps とタイミング

- Timeline の `editorSettings.frameRate` を FBX のフレームレートに合わせて自動設定
- D-Drive のクリップ / マーカーは秒で持つが、Inspector ではフレーム表記も併記(Anim の Frame イベントと同じ)
- Maya でカメラを 24fps、キャラを 30fps のように混ぜると必ずずれるので、Validation で FBX 間の fps 不一致を Warning にする

### 5.4 Humanoid / Generic

- キャラが Humanoid(Avatar あり)なら、カットシーン FBX 側も同じ Avatar 設定で取り込めば別キャラへのリターゲットが効く
- Generic ならボーン名が一致するキャラにしか流せない(Validation で検出)

### 5.5 FBX に何を入れるか / Unity でどう見分けるか / 命名規則(2026-09-13 追記)

**入れるもの**

| 入れる | 内容 | 備考 |
|---|---|---|
| ○ カメラ | 位置・回転・画角(焦点距離) | ショットの切り替えは Maya の Camera Sequencer の標準機能「Ubercam 作成」で 1 台のカメラに焼くのが簡単(スクリプト不要)。複数カメラのまま出す場合は Unity 側でカメラごとにトラックを作り、切り替えは Activation で行う |
| ○ キャラの骨アニメ | ジョイントのアニメだけ(メッシュは入れない「アニメ専用」書き出し) | キャラ本体は別途 ModelData として登録済みのものを使う |
| △ 小物のアニメ | 剣・扉など動くもの | 動かない背景は入れない(Unity 側のシーンで配置) |
| △ イベント用ロケーター | 「ここで SE / VFX」の目印 | §下記。無くても Unity の Timeline で後から置ける |
| × メッシュ・マテリアル・ライト | — | 見た目は Unity 側(ModelData / MaterialData)が真実。ライトは Unity で作る |

**Unity での見分け方**

- **カメラは名前に頼らず判別できる**。FBX はノードの「種類(カメラ / ジョイント / 空ノード)」を持っており、Unity の取り込み結果でも Camera コンポーネントが付くので確実に分かる
- **キャラは「どのキャラか」までは種類から分からない**(ジョイントの塊が 3 つある、としか分からない)。そこで Maya の**リファレンスの名前空間**を使う。キャラをリファレンスで読み込むと、ノード名が `Hero:Hips` のように `名前空間:` 付きで FBX に出るので、Unity 側で「`Hero:` の付いたジョイント群 = Hero のアニメ」とまとめられる
- 小物も同じく名前空間(またはノード名の接頭辞 `PRP_`)で判別する

**最小の命名規則(Maya 作業者が守るのはこれだけ)**

| 対象 | ルール | 例 | Unity 側の対応 |
|---|---|---|---|
| キャラ | リファレンスの名前空間 = D-Drive の Model 識別子 | `Hero:` / `EnemyBoss:` | ModelData `MODEL_*_Hero` を自動でバインド |
| 同じキャラを 2 体 | 名前空間の末尾に `_2` 以降 | `Hero_2:` | 同じ ModelData を 2 体生成 |
| カメラ | 1 台なら自由。複数なら `CAM_<名前>` | `CAM_Main` | 役割 MainCamera(既定)|
| 小物 | 名前空間 = Model 識別子、または `PRP_<Model 識別子>` | `PRP_Sword` | ModelData Sword |
| イベント(任意) | ロケーター `EVT_<キー>` | `EVT_Hit` | Signal マーカー `cutscene/hit` |

- **Maya のモデル名と Unity の ModelData 識別子を一致させるのが要**。Maya のキャラリグのファイル名や名前空間を、仕様書「アセット」タブ([27] §3.1)の Model 識別子に揃えて運用すれば、取り込み時の手作業がほぼ無くなる
- 一致しない場合でも CutsceneData のバインド表で手動で結び付けられる(ルールは「自動で埋まる」ためのもので、破ったら取り込めないわけではない)

**イベント用ロケーター(任意・要検証)**

- ロケーター `EVT_Hit` にカスタムアトリビュート(例 `ddEvent`、整数)を追加してキーを打つと、FBX にアニメ付きユーザープロパティとして出る。Unity の `AssetPostprocessor.OnPostprocessGameObjectWithAnimatedUserProperties` で読めるので、値が変わったフレームに Signal マーカーを自動で置ける
- Maya 側はアトリビュート追加とキー打ちだけ(標準機能)。ただし Unity 2023 以降の FBX 取り込みで確実に取れるかは 6-10c 着手時に検証する。取れなければこの機能は外し、Unity の Timeline 上で置く運用にする

---

## 6. 影響範囲(実装時に確認が要るもの)

- **asmdef**: `DDrive.Runtime` に `Unity.Timeline` 参照を追加(CLAUDE.md §0-9「asmdef 構成は聞く」)。Timeline 関連を別 asmdef(`DDrive.Runtime.Timeline`)に分ける案もある
- **AssetType enum**: `Cutscene` を末尾追加(シリアライズ値は不変)
- **Cinemachine は未導入**。Maya カメラをそのまま再生するだけなら不要。「ゲームカメラから Maya カメラへ滑らかに切り替える」「手ブレを足す」をやるなら Cinemachine 導入を検討(manifest 変更)
- 標準の Audio / Control / Signal トラックは禁止 API 規約(AudioSource.Play / Instantiate 直呼び)と衝突するので、Validation で「D-Drive トラックを使ってください」と Warning を出す

---

## 7. 未決事項(着手前にユーザーと決める)

1. **用途の比重**: 長いカットシーン(数十秒〜分)が主か、必殺技のような短い演出(数秒、PresentationData と役割が重なる)も Timeline で作りたいか
2. **カメラ**: Maya カメラをそのまま再生するだけで良いか、ゲームカメラとのブレンドや Cinemachine が欲しいか
3. **書き出し単位**: 1 ショット = 1 FBX で運用できるか(1 本の FBX に複数ショット入れたい場合は、Unity 側でフレーム範囲で切る表を持つ)
4. **キャラアニメ**: Humanoid か Generic か。キャラ本体の FBX とアニメの FBX を分けられるか
5. **fps**: プロジェクトの基準フレームレート(30 / 60)
6. **ネット**: カットシーンを 2 プレイヤーで同期再生する必要があるか(1 対 1 対戦なので、演出中は両者同じ映像が必要か / 各自ローカルでよいか)
