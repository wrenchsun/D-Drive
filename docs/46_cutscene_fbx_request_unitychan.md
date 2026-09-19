# 46. カットシーン確認用 FBX の作成依頼（UnityChan、2026-09-19）

関連: [26_timeline.md](26_timeline.md) §5（取り込み仕様）/ [43 §7](43_manual_verification_2026-09-17.md)（Timeline の人による確認）/ [DesignerManual/cutscene-maya-export.html](DesignerManual/cutscene-maya-export.html)（Maya 側の一般手順）

> **目的**: Timeline（Cutscene）機能の人による確認（docs/43 §7）には、Maya で書き出した実 FBX が要る。この環境には無いので、デザイナーに **UnityChan を素体にした短い確認用ショット**を 1 本作ってもらう。この書面は**デザイナーに渡す依頼票**と、**Unity 側で事前に用意するもの**の 2 部構成。
>
> **2026-09-19 決定**: Timeline 全般の人による確認（docs/43 §7 / §10）はこの FBX の作成が入るため、**P チケット（移植・更新・互換性、[42](42_distribution.md)）の後**に行う。

---

## A. Unity 側で事前に用意するもの（プログラマー、FBX を受け取る前に）

| # | 項目 | 現状（2026-09-19 に確認） | やること |
|---|---|---|---|
| A-1 | UnityChan の `ModelData` | `Assets/GameData/Model/Player/MODEL_Player_Model.asset`（DisplayName `UnityChan`、識別子は **`Model`**、Category `Player`）。**`Avatar` が未設定（`{fileID: 0}`）** | Humanoid リターゲットに `ModelData.Avatar` が必須（[26] §5.4）。`unitychan.fbx`（`animationType: 3` = Humanoid、Avatar 生成済み）の Avatar を設定する。Model Editor か Inspector から |
| A-2 | 識別子の決定 | キャラ FBX のファイル名は `<ショット>__<Model識別子>.fbx`。今の識別子だと `Shot01__Model.fbx` になる | **推奨: 識別子 `UnityChan` の ModelData を新しく作る**（AssetBrowser「新規」→ Model、表示名 UnityChan、カテゴリ Player、識別子 `UnityChan`、Prefab は既存と同じ `unitychan`、Avatar を設定）。既存の `MODEL_Player_Model` は他の Data（Presentation の確認用など）が参照しているので**残す**。ファイル名は `<ショット>__UnityChan.fbx` になり、依頼票もこの前提で書いてある。既存を使い回す場合は依頼票の識別子を `Model` に読み替える |
| A-3 | 置き場所 | `Assets/SourceAssets/Cutscene/` は未作成（`CutsceneFbxPostprocessor` が検知する入口） | 受け取った FBX を `Assets/SourceAssets/Cutscene/Test/` に置く（カテゴリ `Test`。`Assets/GameData/Cutscene/Test/` が既にあり、CutsceneData は `CUT_Test_<ショット>` になる）。フォルダは置くときに作ればよい |
| A-4 | fps | `CutsceneImportProfile.DefaultFrameRate = 30` | 依頼票は **30fps** で書いてある。60 にしたいなら Profile と依頼票を同時に変える |
| A-5 | ピント・絞り | `CutsceneCameraCurveExtractor` は `m_FocusDistance` / `focus distance` 等の候補名を試し、無ければ空カーブ | 依頼票では「入れられるなら入れる（任意）」にした。取れるかどうか自体が §7.3 の要検証項目 |
| A-6 | 素体の受け渡し | `Assets/SourceAssets/Data/UnityChan/Models/unitychan.fbx`（メッシュ + スケルトン、Humanoid、`globalScale 0.01`＝cm 単位） | この FBX をデザイナーに渡す（Maya に読み込んでアニメを付けてもらう）。ライセンスは同フォルダの `License/` を同梱 |

---

## B. デザイナーへの依頼票（このまま渡す）

### B-1. 作ってほしいもの

Unity の Timeline 機能を確認するための、**5 秒程度の短いカットシーン 1 本**（例: UnityChan が構えて剣を振る → カメラが寄る）。見た目の完成度は問いません。**機能を一通り通せる中身**であることが目的です。

| 入れるもの | 必須 | 内容 |
|---|---|---|
| カメラ | ○ | 5 秒（30fps で 150 フレーム）。**位置・回転が動く**ことと、途中で**画角（焦点距離）が変わる**キーを入れてください。カットを 2 つ以上作る場合は Camera Sequencer の「Ubercam 作成」で 1 台に焼いてください |
| UnityChan の骨アニメ | ○ | 渡した `unitychan.fbx` を Maya に読み込み、ルートジョイント以下にアニメを付ける。**その場で少し移動する動き**（ルートモーション）を含めてください（原点の扱いを確認するため） |
| 小物 1 つ | △ | 剣や箱など、動く小物を 1 つ。ノード名は `PRP_Sword` のように **`PRP_` + 名前**。カメラと同じ FBX に入れる |
| ピント距離・絞り | △（任意） | カメラのフォーカス距離と f 値にキーを打てるなら打ってください（Unity 側で取れるかどうかを確かめるのが目的。取れなくても失敗ではありません） |
| イベント用ロケーター | × | 今回は不要（Unity 側が未対応） |
| メッシュ・マテリアル・ライト | × | 入れない。キャラの見た目は Unity 側の登録済みモデルを使います |

### B-2. 先に合わせておくこと

| 項目 | 値 |
|---|---|
| シーンの fps | **30** |
| 単位 / 上方向 | cm / Y-up（Maya の既定のまま） |
| キャラの名前空間 | `UnityChan:`（Unity 側のモデル識別子と同じ。付けなくても取り込めますが、付けると安全） |
| フレーム範囲 | 開始 0 〜 終了 150。**カメラ FBX とキャラ FBX で同じ範囲**にする（ずれると警告が出ます） |

### B-3. ファイル名（これだけは正確に）

| 対象 | ファイル名 | 中身 |
|---|---|---|
| カメラ + 小物 | `Shot01.fbx` | カメラ（Ubercam）と `PRP_*` の小物だけを選択して書き出し。キャラは選ばない |
| UnityChan | `Shot01__UnityChan.fbx` | UnityChan のルートジョイント（骨の一番上）だけを選択して書き出し。メッシュは選ばない。**アンダースコアは 2 つ** |

- ショット名を変えたければ `Shot01` の部分を両方同じ名前に変えてください（英数字のみ）。
- `@` は使わないでください（`UnityChan@Shot01.fbx` の形は NG）。

### B-4. 書き出し設定（`File > Export Selection`、形式 FBX。2 ファイル共通）

| 項目 | 設定 |
|---|---|
| Animation | ON |
| Bake Animation | ON、開始 0 〜 終了 150、Step 1 |
| Cameras | ON |
| Lights | OFF |
| Embed Media | OFF |
| Include > Input Connections | OFF |
| Deformed Models / Skins / Blend Shapes | **キャラ FBX は OFF**（骨のアニメだけ） |
| Units / Up Axis | Automatic（cm）/ Y |

### B-5. 納品

- `Shot01.fbx` と `Shot01__UnityChan.fbx` の 2 ファイル。
- 可能なら Maya シーン（`.ma` / `.mb`）も一緒に（直しが出たときに、同じファイル名で上書き書き出しをお願いするため）。
- 「どのフレームで何が起きるか」（例: 30F で振り始め、90F でカメラ寄り）を 2〜3 行のメモで添えてください。Unity 側で SE / エフェクトを置く目安にします。

### B-6. 受け取った後に Unity 側で確認すること（参考。デザイナーの作業ではない）

docs/43 §7 の項目を順に行う。要点は次の 4 つ。

1. フォルダに置くだけで `CUT_Test_Shot01` と Timeline ができ、カメラクリップと UnityChan の Animation トラックが自動で付くこと。
2. 画角のカーブが実際に取れていること。ピント・絞りは取れれば加点（§7.3 要検証）。
3. 確認用シーンで Play Mode に入り、ゲームカメラから Maya カメラへ滑らかに繋がって戻ること。ルートモーションと原点（Self）の組み合わせで UnityChan がずれないこと。
4. Maya で直して同じファイル名で上書きすると、カメラとアニメだけが差し替わり、Unity 側で足した SE / エフェクトが残ること。

---

## 変更履歴

- 2026-09-19: 新規作成。Timeline の人による確認を P チケット後に回す決定を記録。
