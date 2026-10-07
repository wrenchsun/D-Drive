# Validation コード早見(各 `## <CODE>` が 1 件。Severity は既定。正本は各 Validator のソース)

## DD-ADDR-CATALOG-MISSING
Error。Data がどのカタログにも無い(実行時に Placeholder)。直し方: FixAction(AssetCreationService.RegisterExisting)か、AssetBrowser から作り直す。

## DD-ADDR-NO-SETTINGS
Error。AddressableAssetSettings が無く全 Data がロードできない。直し方: Window > Asset Management > Addressables > Groups で作成。

## DD-ADDR-MISSING
Error。カタログにあるが Addressables のグループに入っていない。直し方: FixAction か「Addressables 登録を同期」(Tools/D-Drive/Generate)。

## DD-ADDR-MISMATCH
Error。カタログの Address と Addressables の address が違う。直し方: FixAction か同期メニューで揃える。

## DD-ADDR-PRELOAD-REQUIRED
Error。Ui.Open 等の同期解決でしか引かれない種別なのに Flags.Load が Preload でない(常に Placeholder)。直し方: Load を Preload にする(FixAction あり)。

## DD-ADDR-PRELOAD-RECOMMENDED
Warning。同期 API から参照するなら Preload が必要な種別。非同期の代替 API を明示的に呼ぶなら無視してよい。

## DD-SCHEMA-OUTDATED
Warning。Data の SchemaVersion が現行より古い。直し方: Tools/D-Drive/Update > マイグレーション(適用)。

## DD-CUTSCENE-LEGACY-SCRIPT-REF
Error。旧形式のスクリプト参照を持つ Timeline(.playable)がある(Player でトラック / マーカーが読めない)。直し方: Update > マイグレーション(適用)か Validation > 全体の指摘を修正。

## DD-SETUP-URP
Warning。URP がアクティブなレンダーパイプラインでない(標準シェーダーは URP 専用)。直し方: Graphics 設定に URP Asset を割り当てる。

## DD-SETUP-INPUT
Warning。Active Input Handling が Input System / Both でない(ゲームパッド振動等が動かない)。直し方: Project Settings > Player を変更。

## DD-SETUP-API-LEVEL
Warning。API Compatibility Level が .NET Standard 2.1 相当でない(R3 が要求)。直し方: Project Settings > Player で変更。

## DD-SETUP-ADDRESSABLES
Warning。Addressables が未初期化。直し方: セットアップウィザードの「初期化」。

## DD-SETUP-ADDR-NAME-SPACE
Warning。Addressables の既定アセット名にスペースがある(持ち込み先の命名規則に抵触)。直し方: ウィザード「5. Addressables 同期」でリネーム(FixAction あり)。

## DD-SETUP-GAMEDATA-ROOT
Warning。GameData ルートのフォルダが無い。直し方: セットアップウィザードで作成。

## DD-SETUP-UI-LAYER-SETTINGS
Warning。UI_LayerSettings.asset が無い。直し方: セットアップウィザードで作成。

## DD-SETUP-SPEC-SETTINGS
Warning。DDriveSpecSettings が無い。仕様書同期を使わないなら無視してよい。

## DD-SETUP-EMBEDDED-MODIFIED
Warning。D-Drive が埋め込みパッケージなのに開発リポジトリの印が無い(直接改造の疑い)。直し方: 拡張点で解決するか開発リポジトリへ PR。

## DD-SETUP-UPDATE-PENDING
Warning。D-Drive の更新が未適用の可能性。直し方: Tools/D-Drive/Update > 更新ウィンドウで「更新を適用」。

## DD-CANVAS-EMBED-ROOT
Warning。EmbeddedCanvases の RootPath が Prefab 内に無い(この埋め込みは無視)。直し方: RootPath を Prefab の階層に合わせる。

## DD-CANVAS-EMBED-DUP
Warning。EmbeddedCanvases の RootPath が重複(同じ場所に子 Canvas は 1 つだけ)。直し方: どちらかを外す。

## DD-CANVAS-EMBED-PATH-FORM
Warning。RootPath の書式が不正(先頭・末尾の `/`、`\`、`//`、`./` は不可。例 Group/OptionRoot)。直し方: 書式を直す。

## DD-CANVAS-EMBED-NESTED-ROOT
Warning。埋め込みが重なっている(同じ要素は 1 回だけ適用、内側が優先)。直し方: 入れ子は子の CanvasData 側で登録し、片方の登録を外す。

## DD-CANVAS-EMBED-UNSET
Warning。子 CanvasData が未設定(無視される)。直し方: 子を指定するか行を消す。

## DD-CANVAS-EMBED-SELF
Warning。自分自身を子 Canvas に指定(循環。無視される)。直し方: 別の CanvasData を指定。

## DD-CANVAS-EMBED-MISSING
Warning。子の CanvasData(Id)が見つからない(無視される)。直し方: Id を直すか、子を作る。

## DD-CANVAS-EMBED-NOT-PRELOAD
Warning。子 Canvas の Load が Preload でない(親を Open した時点で未ロードだと埋め込みが効かない)。直し方: 子を Preload にする。

## DD-CANVAS-EMBED-CYCLE
Warning。入れ子をたどると自分を埋め込んでいる(循環。無視される)。直し方: 循環を断つ。

## DD-CANVAS-EMBED-PREFAB
Warning。その場所の実体が子 Canvas の Prefab のインスタンスでない。直し方: RootPath か子の CanvasData 指定を確認。

## DD-CANVAS-EMBED-OVERRIDE
Info。親に同じ要素の ElementFx / 配線があるため、子 Canvas 側の同じ要素の設定は使われない(親が優先)。意図どおりなら対応不要。

## DD-CANVAS-WIRE-NO-UIBUTTON
Warning。ButtonWire の要素に UiButton が付いていない(配線が効かない)。直し方: UiButton を付けるか ButtonPath を直す。

## DD-CANVAS-WIRE-EMBED-UNKNOWN
Warning。ButtonWire の EmbeddedRootPath が EmbeddedCanvases に未登録(押しても何も起きない)。直し方: 埋め込みを登録するか Path を直す。

## DD-CANVAS-SLIDER-EMBED-ACTION
Warning。SliderWire に埋め込み系の Action を指定(ボタン専用で何も起きない)。直し方: Action を変える。

## DD-CANVAS-EMBED-FIRSTSELECTED-INACTIVE
Warning。FirstSelected が StartInactive の埋め込み配下(開いた直後は選択できない)。直し方: FirstSelected か StartInactive を見直す。

## DD-ANIM-BLENDSHAPE-EXTERNAL-OWNED
Warning。FC_ / fcs_ の BlendShape は外部パッケージ(T-Drive の表情)が管理(AnimData から書くと衝突)。直し方: 別のシェイプを使う。

## DD-SHAKE-ENVELOPE-ZERO-DURATION
Warning。Shake の Envelope の尺が 0 以下(すぐ終わり何も起きない)。直し方: Duration を正の値に。

## DD-HAPTICS-ZERO-DURATION
Warning。Haptics の LowFreq/HighFreq の尺がどちらも 0 以下(振動しない)。直し方: Duration を正の値に。

## DD-MAT-RENDERINGLAYERMASK-UNUSED
Info。MaterialData.RenderingLayerMask は実行時に使われない。直し方: ライトレイヤーは ModelData.LightLayerMask で指定。

## DD-MAT-PASS-UNKNOWN
Warning。DisabledPasses の値がシェーダーの LightMode に無い(無視される)。直し方: メッセージの「使える値」から選ぶ。

## DD-MAT-KEYWORD-UNDECLARED
Info。EnabledKeywords がシェーダー未宣言(効果が無い可能性。グローバルキーワードなら問題なし)。

## DD-FORBIDDEN-ALLOW-NO-REASON
Warning。`// ddrive-allow: 規則名(理由)` の理由が空。直し方: 括弧内に理由を書く。

## DD-FORBIDDEN-ALLOW-UNKNOWN-RULE
Warning。ddrive-allow の規則名が不明。直し方: 禁止 API の規則名を正しく書く。

## DD-FORBIDDEN-ALLOW-UNUSED
Info。ddrive-allow が何にも当たっていない。直し方: 不要なら消す。

## DD-FORBIDDEN-ALLOW-SETTINGS-INVALID
Warning。設定(ForbiddenApiAllowEntries)の許可項目が不正(規則名・パスの誤り)。直し方: Project Settings の許可リストを直す。
