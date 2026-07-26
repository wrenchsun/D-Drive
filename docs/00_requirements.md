# 00. 要件定義書 — D-Drive (Designer-Driven Re: IDE Visual Environment)

- 対象: Unity 6 / URP / Addressables 必須 / UniTask + R3 使用可
- 版: v1.0 (2026-07-13)
- 関連: [01_architecture.md](01_architecture.md) / [11_tasks.md](11_tasks.md) / [12_review.md](12_review.md)

---

## 1. 目的

ゲーム内で使用する全アセット（BGM / SE / VFX / モデル / アニメーション / マテリアル / 画像 / Canvas / Prefab）を **ID をキーとするデータベース** で一元管理し、以下を実現する。

1. **プログラマーはアセット抜きでモックを完成できる**
   - コード上に存在するのは `AssetId` と Manager API 呼び出しのみ
   - アセット未登録でもエラーにならず、ダミー（Placeholder）で動作する
2. **デザイナーは専用エディタのみで演出を完成できる**
   - ID に紐づく中身（Prefab / Clip / パラメータ / イベント）を自由に差し替え・調整
   - プレビューで実行時と同じ結果を Editor 上で確認できる
3. **責務の完全分離**
   - プログラマー: 「いつ・どの ID を再生するか」
   - デザイナー: 「その ID がどう見える・聞こえるか」
   - Manager: 「ロード・生成・再生・破棄・プール」の実行のみ

## 2. 用語定義

| 用語 | 定義 |
|---|---|
| D-Drive | 本システムの名称。**D**esigner-**D**riven **Re:** **I**DE **V**isual **E**nvironment。Re: は「Unity 標準機能の再定義・再実装」（uGUI Button/Slider の全面新規実装、AnimationEvent・Resources.Load の置換）を表す |
| AssetId | アセットを一意に識別する ID（種別ごとに型を分ける。例: `SeId`, `VfxId`） |
| AssetData | ID に紐づく定義データ（ScriptableObject）。不変・共有 |
| AssetInstance | 実行時に生成される実体。位置・寿命・状態を持つ。可変・個別 |
| Handle | Manager が呼び出し元に返す Instance への安全な参照 |
| Presentation | 複数アセット（Anim+SE+VFX+カメラ等）を束ねた演出定義 |
| Catalog | 種別ごとの AssetData 一覧（ID→Data の辞書の実体） |
| AssetBrowser | 全種別横断の検索・管理・プレビュー用エディタウィンドウ |

## 3. 機能要件

### FR-1 ID 管理
- FR-1.1 全アセットは種別ごとに強い型の ID（struct）で識別する。int や string の生値をゲームコードに書かない
- FR-1.2 ID は enum ではなく **自動生成される定数クラス + 内部は安定した ulong/GUID** とする（enum は追加・削除でシリアライズが壊れるため）
- FR-1.3 ID 定数クラスはエディタから 1 クリックで再生成できる
- FR-1.4 未登録 ID の再生要求はエラーではなく警告ログ + Placeholder 再生とする

### FR-2 AssetData（定義層）
- FR-2.1 全種別は共通基底 `AssetDataBase` を継承する（メタ情報・Flags・Events を共通化）
- FR-2.2 特殊制御が必要な場合はデータ側の派生クラス追加のみで対応でき、Manager 改修を要しない（Strategy 差し込み口を持つ）
- FR-2.3 データは ScriptableObject とし、Addressables でロードする

### FR-3 Instance（実体層）
- FR-3.1 Data と Instance を分離する。Move / Attach / Stop 等の操作は Handle 経由で Instance に対して行う
- FR-3.2 Handle は破棄済み Instance へのアクセスで例外を出さない（世代カウンタで無効化検知）

### FR-4 Manager（実行層）
- FR-4.1 種別ごとに Manager を置き、共通機能（ロード・プール・ポーズ・イベント発火）は基盤サービスに委譲する
- FR-4.2 API は `Play/Spawn(id, context) → Handle` に統一する
- FR-4.3 全 Manager は Pause / Resume / StopAll / SetVolume(Duck) 等のグローバル操作に応答する

### FR-5 共通イベント
- FR-5.1 `OnSpawn / OnEnable / OnLoop / OnDisable / OnDestroy / Custom(string)` を全種別共通の `AssetEvent` として定義
- FR-5.2 イベントには他アセット ID を紐づけられる（例: Animation の Frame15 → SE 再生）
- FR-5.3 イベント設定はすべて専用エディタから行える

### FR-6 共通フラグ
- FR-6.1 `PauseMode / LoadMode / PoolPolicy / Priority / Persistent / Domain(UI・3D)` を共通 `AssetFlags` に集約

### FR-7 Presentation（演出統合）
- FR-7.1 Animation / SE / VFX / CameraShake / HitStop / Timeline を 1 つの PresentationData に束ね、`PresentationManager.Play(id)` の 1 呼び出しで再生できる
- FR-7.2 トラック単位の遅延・条件（ヒット時のみ等）を設定できる
- FR-7.3 PresentationHandle は制御関数（Signal/Cancel/Pause/SetSpeed/Seek）とイベント（OnCompleted/OnCancelled/OnMarker/OnTrackFired、await 対応）を提供する
- FR-7.4 タイムライン型の専用エディタで全トラック（2D/3D Anim・SE・VFX・UiTween 含む）を統合プレビューできる

### FR-8 AssetBrowser
- FR-8.1 全種別横断のリスト表示・検索（ID / 名前 / タグ / 種別 / 作成者）
- FR-8.2 使用箇所検索（この ID をどの Data / Scene / Prefab が参照しているか）
- FR-8.3 未使用アセット検出
- FR-8.4 依存関係ツリー表示（Prefab→Material→Shader→VFX→SE）
- FR-8.5 お気に入り・最近使った・ドラッグ&ドロップ登録

### FR-9 プレビュー
- FR-9.1 全種別共通: 再生 / 停止 / ループ / 速度 / 背景切替 / ライト切替 / 比較（2 分割）表示
- FR-9.2 種別固有プレビュー（詳細は各設計書）: Audio の 3D 距離確認、VFX の Anchor 表示・複数同時、Animation のブレンド・遷移・SE/VFX 同時、Material の球/板/任意モデル
- FR-9.3 プレビューは実行時と同一の Manager コードパスを通す（Editor 専用再生経路を作らない）

### FR-10 Validation
- FR-10.1 保存時: 参照欠落（Clip/Prefab/Shader なし）を即時に赤/黄で表示
- FR-10.2 プロジェクト全体: Missing / 循環参照 / ID 重複 / 未使用 / Addressable 登録漏れ / Pool 設定漏れを一括検査し一覧化
- FR-10.3 CI（バッチモード）から実行でき、エラー時に非 0 終了コードを返す

### FR-11 ロード・プール
- FR-11.1 ロードは Addressables 経由のみ。`Resources.Load` 禁止
- FR-11.2 共通 PoolManager が VFX / SE Source / Prefab / Canvas / Projectile を扱う
- FR-11.3 Preload API（シーン遷移時にまとめてロード）を提供

### FR-12 バージョン・変更管理
- FR-12.1 AssetData に更新者・更新日時・バージョン・変更メモを保持（保存フックで自動記録）
- FR-12.2 Git コミットハッシュとの紐づけは任意（CI で付与）

### FR-13 ネットワーク対応（詳細: [14_networking.md](14_networking.md)）
- FR-13.1 システムはトランスポート非依存とし、`INetBridge` 抽象を介して Netcode for GameObjects 等に接続する。シングルプレイでは Loopback 実装で完全に同一コードで動く
- FR-13.2 全アセットは複製区分 `NetMode`（Local / Cosmetic / Simulated）を AssetFlags に持つ。Manager が NetMode に応じて配送を自動選択し、ゲームコードは通常 API のまま
- FR-13.3 演出の時刻は `NetworkTime` 基準（ITimeSource 注入）、ランダム要素は Seed から決定的に選択できる
- FR-13.4 Presentation はネット越し再生（開始時刻シーク・予測再生・Late Join 復元）に対応する
- FR-13.5 接続時にカタログ ContentHash を照合し、クライアント/サーバーのアセット定義不一致を検出する
- FR-13.6 ネット受信 ID の存在検証を行い、未登録 ID は破棄する（Placeholder はローカル開発時のみ）

### FR-14 UiButton（ボタン全面新規実装、詳細: [15_ui_interaction.md](15_ui_interaction.md)）
- FR-14.1 uGUI Button を使わず EventSystem インタフェースから新規実装する
- FR-14.2 Click / DoubleClick / LongPress / Repeat / Press / Hover / Focus / StateChanged のイベントを R3 Observable と UniTask（WaitClickAsync）で提供する
- FR-14.3 連打防止（Cooldown）・多重発火防止・Locked 状態（理由付き）を標準装備する
- FR-14.4 状態別の見た目・SE・Tween は ButtonSkinData（ID 管理）で共通定義する

### FR-15 アニメーションの 2D / 3D 分離（詳細: [05_model_animation.md](05_model_animation.md)）
- FR-15.1 3D（AnimId）と 2D スプライト（Anim2DId）を別系統として設計する
- FR-15.2 2D は既存ツール Katsuya.Tools.SpriteAnimation を D-Drive に統合し、スプライト分割（Grid/自動/既存）→ Clip 生成 → Animator/BlendTree 割当 → Anim2DData 登録までワンストップで行える
- FR-15.3 2D も共通 AssetEvent（Frame→SE/VFX）・共通プレビュー・Validation に乗せる

### FR-16 イージング・Tween システム（詳細: [15_ui_interaction.md](15_ui_interaction.md)）
- FR-16.1 31 種以上のイージング + CubicBezier + AnimationCurve を Foundation の EasingCore として提供する（既存実装を昇格）
- FR-16.2 CatmullRom / Bezier / Hermite / BSpline のスプライン経路移動（弧長等速化済み）に対応する
- FR-16.3 任意の UI 要素（スプライト・ボタン・パネル）に、出現 / 常時 / 消滅の 3 フェーズそれぞれへ UiTweenData（ID 管理）をデザイナーが割当できる
- FR-16.4 コード向けに UiFx の 1 行関数群（MoveTo/Scale/Fade/MoveAlong 等、await 可能な Handle 返却）を提供する
- FR-16.5 FadeIn / SlideIn 各方向 / PopIn / Shake / Pulse 等の定番演出を **50 種以上のプリセット**として標準搭載する。デザイナーはギャラリーから選んで時間・距離を調整するだけで使え、コード側にも全プリセット分の同名 1 行関数を用意する。プロジェクト独自プリセットの追加登録も可能とする

### FR-17 カメラシェイク / コントローラー振動（詳細: [16_camera_haptics.md](16_camera_haptics.md)）
- FR-17.1 画面の揺れ（CameraShakeData / ShakeId）とコントローラー振動（HapticsData / HapticId）を ID 管理のアセットとし、デザイナーが専用エディタで作成・調整できる
- FR-17.2 シェイクは Trauma 方式で多重合成し、揺れ専用ノードを介してカメラ制御と干渉しない。振動は Low/High 2 モーターカーブで定義し全機種共通で動く
- FR-17.3 プレビュー対応: シェイクはプレビューカメラを実際に揺らして確認、振動は**エディタから接続中のパッドを直接鳴らして**体感確認できる。Presentation の統合プレビューにも含まれる
- FR-17.4 オプション設定（揺れ 0〜100% / 振動 0〜100%）に全再生が追従する（アクセシビリティ要件）
- FR-17.5 Presentation のトラックは直値でなく必ず ID 参照とする

### FR-18 UiSlider（スライダー全面新規実装、詳細: [18_ui_controls.md](18_ui_controls.md)）
- FR-18.1 uGUI Slider / Selectable を使わず EventSystem インタフェースから新規実装する
- FR-18.2 UiButton と共通の UiInteractable 基底（状態機械・Skin・Locked・ナビゲーション）を共有する
- FR-18.3 マウス / タッチ / ゲームパッド / キーボードに対応し、Step 移動・長押しリピート・微調整修飾を標準装備する
- FR-18.4 入力位置→値の応答曲線、表示の追従演出を ValueDef（FR-19）で指定できる
- FR-18.5 値変更通知は R3 Observable + UniTask で提供し、スロットルと確定（Commit）を標準装備する
- FR-18.6 見た目・SE・触覚は SliderSkinData（ID 管理）で共通定義する
- FR-18.7 CanvasData の SliderWire で配線でき、音量・画面揺れ・振動スケール等の標準オプションにコード 0 行で接続できる

### FR-19 調整値の定義形式の統一（詳細: [17_value_definition.md](17_value_definition.md)）
- FR-19.1 デザイナーが調整する値は **定数 / パラメトリック曲線（Ease 31 種 + CubicBezier）/ 任意カーブ（AnimationCurve）** の 3 モードのいずれかで定義する
- FR-19.2 上記に加えて **スピード（Duration / Speed / Rate）** を必ず独立に指定できる。「形」と「速さ」を分離する
- FR-19.3 3 モードの切替は 1 クリックで、切替時に他モードの設定値を破棄しない
- FR-19.4 評価は `ValueDef.Evaluate(t)` に統一し、呼び出し側はモードを意識しない（定常経路 0 alloc）
- FR-19.5 調整パラメータは ValueDef で定義する。生の float + AnimationCurve の組み合わせを書かない
- FR-19.6 時刻は ITimeSource 経由で取得し、評価は純関数とする（全クライアントで同一結果）

## 4. 非機能要件

| ID | 要件 |
|---|---|
| NFR-1 | ID→Data 解決は O(1)。1000 件登録時のカタログ初期化 100ms 以内 |
| NFR-2 | Play 呼び出しのメインスレッドブロッキング禁止（ロードは UniTask で非同期） |
| NFR-3 | ランタイム部は Editor 非依存（`UNITY_EDITOR` 分離、asmdef 分割） |
| NFR-4 | GC Alloc: Play/Spawn の定常経路で 0 alloc を目標（Handle は struct） |
| NFR-5 | デザイナー操作はすべて Undo 対応 |
| NFR-6 | カタログ・Data の追加がコンフリクトしにくい構成（1 アセット 1 ファイル） |
| NFR-7 | 新アセット種別の追加が基盤改修なしで可能（Timeline / Camera / Dialogue 等の将来拡張） |
| NFR-8 | デザイナーが触るパラメータの編集 UI は種別ごとに作らず、ValueDef 共通 PropertyDrawer に集約する（学習コストを種別数に比例させない） |

## 5. 禁止事項（コーディング規約に組込む）

```
× Instantiate(prefab) の直接呼び出し（Manager 外）
× Resources.Load / 直接の Addressables.Load（Manager 外）
× AudioSource.Play の直接呼び出し
× ゲームコードから AssetData の中身を書き換える
× Editor コードからランタイム asmdef 外の内部実装に触る
× 調整パラメータを「float + AnimationCurve」の生の組み合わせで新規定義する（ValueDef を使う）
× メニューパス・namespace の文字列直書き（DDriveMenu 定数 / asmdef 定義を経由する）
```

## 6. スコープ外（v1 では作らない）

- ローカライズ・Dialogue・Quest・AI データ（将来拡張枠）
- アセットのネットワーク配信管理（Addressables Remote は設定のみ対応）
- Maya 側プラグイン（FBX 読込→Material 自動生成は Unity 側 Importer で対応）

## 7. 成功基準（受け入れ条件）

1. プログラマーがアセット 0 件の状態でモックシーンを実装し、後からデザイナーが ID の中身を埋めるだけで演出が完成するデモが通る
2. 「剣攻撃」Presentation（Anim+SE+VFX+HitStop+CameraShake）をコード変更なしでデザイナーが調整できる
3. Validation を CI で回し、参照欠落・ID 重複が検出できる
4. AssetBrowser から任意アセットの使用箇所・依存関係が辿れる
5. 2 クライアント + サーバー構成で剣攻撃 Presentation が同期再生され（遅延 200ms でも位相一致）、途中参加者にも常駐演出が復元される

**マルチプレイは v1 の必須要件**であり、将来拡張ではない。FR-13 の全項目（NGO アダプタ・Cosmetic/Simulated 配送・同期再生・Late Join・ContentHash 照合）を v1 スコープに含む。
