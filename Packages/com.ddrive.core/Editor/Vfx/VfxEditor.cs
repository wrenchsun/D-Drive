using System;
using System.Collections.Generic;
using System.Linq;
using DDrive.Editor.Anchor;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Editor.Ux;
using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Vfx;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Vfx
{
    // VFX のエディター(docs/1010 UX-1 で共通基盤と共に作り、UX-2 で正式化。旧 VfxEditorWindow は Archive メニューへ退避)。
    // 共通基盤(UxEditorWindow<VfxData> / FieldGuide / UxRow / UxCard / DurationBar / AnchorPad / TuneKnobs)だけで組み、
    // ここに書くのは Vfx 固有の部分だけ: 欄の表(VfxFieldGuide)・視覚部品の結びつけ・Anchor ツール・LightLayer・複数同時再生・再生(実 Manager)。
    // プレビューは実 VfxManager を Editor から駆動する(ADR-4。SceneVfxPreviewDriver)。
    [DDrive.Editor.Inspector.DataEditor(typeof(VfxData), "VFX Editor で開く")]
    internal sealed class VfxEditor : UxEditorWindow<VfxData>
    {
        private const string Page = "vfx-editor";

        [SerializeField] private GameObject _attachTarget;
        [SerializeField] private bool _sceneHandle = true;

        private SceneVfxPreviewDriver _driver;
        private Handle<VfxMarker> _handle;
        private readonly VfxData[] _slotData = new VfxData[VfxSlotsRow.MaxSlots];
        private readonly Handle<VfxMarker>[] _slotHandle = NewInvalidHandles();

        private AnchorPad _anchorPad;
        private VfxAnchorToolsRow _toolsRow;
        private HelpBox _sceneHelp;
        private string _sceneHelpKey;

        private static Handle<VfxMarker>[] NewInvalidHandles()
        {
            var a = new Handle<VfxMarker>[VfxSlotsRow.MaxSlots];
            for (var i = 0; i < a.Length; i++)
            {
                a[i] = Handle<VfxMarker>.Invalid;
            }

            return a;
        }

        // ── 入口 ──

        [MenuItem(DDriveMenu.Editors + "VFX Editor")]
        public static void OpenFromMenu() => Open(Selection.activeObject as VfxData);

        public static void Open(VfxData target)
        {
            var w = OpenWindow<VfxEditor>("VFX Editor");
            if (target != null)
            {
                w.SetTarget(target);
            }
        }

        // PresentationEditor 等、他のエディタが開いている確認用シーン・配置済みモデルをそのまま使う(スポーン先を渡すだけ)。
        public static void Open(VfxData target, GameObject attachTarget)
        {
            var w = OpenWindow<VfxEditor>("VFX Editor");
            if (target != null)
            {
                w.SetTarget(target);
            }

            w._attachTarget = attachTarget;
            w._toolsRow?.Sync();
        }

        // ── 土台への指定 ──

        protected override string PrefsKey => "Vfx";
        protected override FieldGuide<VfxData> Guide => VfxFieldGuide.Instance;
        protected override string ManualPage => Page;

        // カードの「?」でマニュアルの該当節へ飛ぶ(docs/DesignerManual/vfx-editor.html の節 id)。
        protected override string ManualAnchor(string purpose)
        {
            switch (purpose)
            {
                case VfxFieldGuide.PurShow: return "show";
                case VfxFieldGuide.PurPlace: return "place";
                case VfxFieldGuide.PurLife: return "life";
                case VfxFieldGuide.PurLook: return "look";
                case VfxFieldGuide.PurLink: return "link";
                case FieldGuide<VfxData>.AdminPurpose: return "admin";
                default: return null;
            }
        }
        protected override bool SupportsPlayback => true;
        protected override bool HasSample => true;
        protected override string SampleLabel => "サンプル VfxData を作る（vfx_sample）";
        protected override VfxData CreateSample() => VfxSample.GetOrCreate();
        protected override Texture DefaultIcon => EditorGUIUtility.IconContent("ParticleSystem Icon").image;
        protected override string SearchHint => "ラベル・説明・検索語に一致する設定だけ出す(例: 高さ / ループ / レイヤー)";

        protected override string[] ModeTips => new[]
        {
            "必須の欄だけ。Prefab を入れて ▶ で確かめるところまで",
            "よく使う欄まで。位置・長さ・調整つまみ",
            "すべての欄",
        };

        private static readonly KeyValuePair<string, string>[] MessageTokens =
        {
            new KeyValuePair<string, string>("Pool", "Flags"),
            new KeyValuePair<string, string>("Domain", "Flags"),
            new KeyValuePair<string, string>("マテリアル", "Prefab"),
            new KeyValuePair<string, string>("シェーダー", "Prefab"),
        };

        protected override IReadOnlyList<KeyValuePair<string, string>> ValidationTokens => MessageTokens;

        protected override string CardDescription(string purpose)
        {
            switch (purpose)
            {
                case VfxFieldGuide.PurShow: return "どのエフェクトを、どこに描くか";
                case VfxFieldGuide.PurPlace: return "エフェクトが出る位置と高さ";
                case VfxFieldGuide.PurLife: return "どれくらい続いて、どう消えるか";
                case VfxFieldGuide.PurLook: return "色やサイズなど、あとから変えられる調整つまみ";
                case VfxFieldGuide.PurLink: return "節目で別の音や演出を一緒に鳴らす";
                default: return "一覧に出る名前・分類・メモ";
            }
        }

        private SceneVfxPreviewDriver Driver => _driver;

        // スポーン先(Anchor のボーン / AnchorPoint の検索起点。シーン内オブジェクト)。
        private Transform AttachTransform => _attachTarget != null ? _attachTarget.transform : null;

        // ── ライフサイクル・再生(実 Manager) ──

        protected override void OnWindowEnable()
        {
            _driver = new SceneVfxPreviewDriver { Speed = Speed };
            // default(Handle) は (0,0) で「最初のスロットの旧世代」と衝突し、毎フレームの IsPlaying が無効アクセス警告を出すため明示的に Invalid にする。
            _handle = Handle<VfxMarker>.Invalid;
            EditorSceneManager.activeSceneChangedInEditMode += OnSceneChanged;
            PrefabStage.prefabStageOpened += OnPrefabStageChanged;
            PrefabStage.prefabStageClosing += OnPrefabStageChanged;
            PrefabStage.prefabSaved += OnPrefabSaved;
        }

        protected override void OnWindowDisable()
        {
            EditorSceneManager.activeSceneChangedInEditMode -= OnSceneChanged;
            PrefabStage.prefabStageOpened -= OnPrefabStageChanged;
            PrefabStage.prefabStageClosing -= OnPrefabStageChanged;
            PrefabStage.prefabSaved -= OnPrefabSaved;
            _driver?.Dispose();
            _driver = null;
            SceneView.RepaintAll();
        }

        private void OnSceneChanged(UnityEngine.SceneManagement.Scene a, UnityEngine.SceneManagement.Scene b)
        {
            _handle = Handle<VfxMarker>.Invalid;
            ResetPlayback();
        }

        private void OnPrefabStageChanged(PrefabStage stage) => OnSceneChanged(default, default);

        // プレハブモードで対象の Prefab を保存したら、再生中の実体を撮り直して編集内容を反映する。
        private void OnPrefabSaved(GameObject prefabRoot)
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null || Target == null || Target.Prefab == null)
            {
                return;
            }

            if (stage.assetPath == AssetDatabase.GetAssetPath(Target.Prefab))
            {
                RestartIfPlaying();
            }
        }

        protected override bool IsPlaying => _driver != null && _driver.IsPlaying(_handle);

        protected override void StartPlayback(bool focus)
        {
            if (Target == null || _driver == null)
            {
                return;
            }

            _handle = _driver.Play(Target, AttachTransform);
            if (focus)
            {
                PreviewPlacement.Focus(_driver.Manager.GetGameObject(_handle));
            }
        }

        protected override void StopPlayback()
        {
            if (_driver != null && _driver.IsPlaying(_handle))
            {
                _driver.Stop(_handle);
            }

            _handle = Handle<VfxMarker>.Invalid;
        }

        protected override void RestartPlayback()
        {
            _driver.Kill(_handle);
            StartPlayback(false);
        }

        protected override void OnSpeedChanged(float speed)
        {
            if (_driver != null)
            {
                _driver.Speed = speed;
            }
        }

        protected override void OnPlaybackReset()
        {
            for (var i = 0; i < _slotHandle.Length; i++)
            {
                _slotHandle[i] = Handle<VfxMarker>.Invalid;
            }
        }

        protected override void OnTargetChanged()
        {
        }

        private void OpenPreviewScene(PreviewPlaceMode mode)
        {
            StopMain();
            if (!PreviewPlacement.PrepareScene(mode, VfxPreviewSceneSetup.TryOpenOrCreate))
            {
                return;
            }

            if (PreviewPlacement.IsPersistent(mode))
            {
                if (Target != null && Target.Prefab != null)
                {
                    PreviewPlacement.PlacePrefabPersistent(Target.Prefab, Vector3.zero, Quaternion.identity);
                }

                return;
            }

            PlayMain(true);
        }

        // ── 編集のあと ──

        protected override void OnSideEffect() => _driver?.ReapplyAnchorToAll();

        protected override void OnPropertyEdited(string path)
        {
            if (path == "Prefab")
            {
                RestartIfPlaying();
            }
            else if (path.StartsWith("AnchorId") || path == "Anchor.Space" || path == "Anchor.Path")
            {
                _driver?.ReapplyAnchorToAll();
                RestartIfPlaying();
            }
            else if (path.StartsWith("Anchor"))
            {
                _driver?.ReapplyAnchorToAll();
            }
        }

        protected override void OnRowsRefreshed() => RefreshAnchorNote();

        protected override void OnBuildBodyStart()
        {
            _anchorPad = null;
            _toolsRow = null;
            _sceneHelp = null;
            _sceneHelpKey = null;
        }

        protected override void OnSlowTick() => RefreshSceneHelp();

        // ── ヒーローの操作・ヒーロー直下 ──

        protected override void AddLeadingHeroActions(VisualElement actions)
        {
            var scene = PreviewPlacementButton.Create("確認用シーン", "ライト / カメラ / 床を備えた確認用シーンを開き、対象をそこで再生する", OpenPreviewScene);
            scene.AddToClassList("ux-btn");
            actions.Add(scene);
            actions.Add(MakeButton("Prefab で開く", null, OpenPrefab, "VfxData.Prefab をプレハブモードで開く(パーティクルの中身を編集)"));
        }

        protected override void AfterHero(VisualElement page)
        {
            _sceneHelp = new HelpBox(string.Empty, HelpBoxMessageType.Warning);
            _sceneHelp.AddToClassList("ux-scenehelp");
            _sceneHelp.Show(false);
            page.Add(_sceneHelp);
        }

        private void OpenPrefab()
        {
            if (Target != null && Target.Prefab != null)
            {
                AssetDatabase.OpenAsset(Target.Prefab);
            }
            else
            {
                EditorUtility.DisplayDialog("Prefab で開く", "この Data には Prefab が設定されていません。「エフェクトの実体(Prefab)」に Prefab を入れてください。", "OK");
            }
        }

        // 再生先の案内(「シーンにカメラ / ライトが無い」警告とプレハブモードの説明)。
        private void RefreshSceneHelp()
        {
            if (_sceneHelp == null)
            {
                return;
            }

            string text = null;
            var type = HelpBoxMessageType.Warning;
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
            {
                type = HelpBoxMessageType.Info;
                var stageName = System.IO.Path.GetFileNameWithoutExtension(stage.assetPath);
                text = Driver != null && Driver.IsInPlaceTarget(Target)
                    ? $"プレハブモード '{stageName}' はこの VFX の Prefab 自身なので、ステージ内の実体をその場で再生します。Anchor・パラメータの即時反映は対象外です。"
                    : $"プレハブモード '{stageName}' の中で再生します。スポーン物はプレハブには保存されません。プレハブの編集内容は保存(Ctrl+S)すると再生中の実体に反映されます。";
            }
            else
            {
                var hasCamera = Camera.main != null || FindFirstObjectByType<Camera>() != null;
                var hasLight = FindFirstObjectByType<Light>() != null;
                if (!hasCamera || !hasLight)
                {
                    var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                    text = $"開いているシーン '{scene.name}' に{(hasCamera ? string.Empty : "カメラ")}{(!hasCamera && !hasLight ? "・" : string.Empty)}{(hasLight ? string.Empty : "ライト")}がありません。見た目の確認には「確認用シーン」を開いてください。";
                }
            }

            var key = type + "|" + text;
            if (key == _sceneHelpKey)
            {
                return;
            }

            _sceneHelpKey = key;
            _sceneHelp.messageType = type;
            _sceneHelp.text = text ?? string.Empty;
            _sceneHelp.Show(!string.IsNullOrEmpty(text));
        }

        // ── 欄(視覚部品・Anchor ツール・つまみ・スロット) ──

        protected override void BeforeCard(string purpose, VisualElement page)
        {
            if (purpose == FieldGuide<VfxData>.AdminPurpose)
            {
                AddSlotCard(page);
            }
        }

        private void AddSlotCard(VisualElement page)
        {
            var slotCard = new UxCard("複数同時再生", "打撃 + 火花 + 煙など、重なりの確認(最大 " + VfxSlotsRow.MaxSlots + ")", UxCard.Help(Page, null));
            var slotEntry = new FieldGuideEntry
            {
                Field = "(slots)",
                Label = "複数同時再生",
                Hint = "ほかの VFX も同時に鳴らして、重なり方を確かめます。スポーン先はメインと共通です。",
                Tier = FieldTier.Advanced,
                Purpose = VfxFieldGuide.PurLink,
                Keywords = new[] { "同時", "重ねる", "複数", "スロット" },
            };
            var slots = new VfxSlotsRow(slotEntry, i => _slotData[i], (i, d) => _slotData[i] = d, i => Driver != null && Driver.IsPlaying(_slotHandle[i]), ToggleSlot);
            AddAuxRow(slotCard, slots);
            AddCard(page, slotCard);
        }

        protected override UxRow CreateRow(FieldGuideEntry e, UxCard card, bool isAdmin)
        {
            var data = Target;
            switch (e.Field)
            {
                case "Duration":
                case "FadeOutSec":
                case "RenderLayer":
                    return null; // 長さ・描画の複合欄に含める

                case nameof(VfxData.LifeMode):
                {
                    var bar = new DurationBar(
                        data,
                        () => new DurationBar.Values { Mode = ToUx(data.LifeMode), Duration = data.Duration, Fade = data.FadeOutSec },
                        v =>
                        {
                            data.LifeMode = FromUx(v.Mode);
                            data.Duration = v.Duration;
                            data.FadeOutSec = v.Fade;
                        },
                        final => VisualEdited(final, false));
                    var entries = new[] { e, Guide.Find(nameof(VfxData.Duration)), Guide.Find(nameof(VfxData.FadeOutSec)) };
                    return new UxVisualRow(So, DefaultSo, entries, "長さ・消え方", "再生の終わり方・続く秒数・停止後の余韻を、1 本のバーで。", FieldTier.Common, new VfxLifeInput(So, bar), RowChanged);
                }

                case nameof(VfxData.Params):
                {
                    var panel = new TuneKnobs(new VfxTuneSource(data, () => Driver, () => _handle), So, () => VisualEdited(false, false), () => { So?.Update(); RunValidation(); RefreshAll(false); });
                    return new TuneKnobsRow(So, DefaultSo, new[] { e }, "色やサイズを回して調整します。値はこの Data の Default に保存され、再生中は即時に反映されます。", panel, RowChanged);
                }

                case nameof(VfxData.Render):
                {
                    var entries = new[] { e, Guide.Find(nameof(VfxData.RenderLayer)) };
                    return new VfxRenderRow(So, DefaultSo, entries, "通常のシーンに出すか、UI の上に重ねて出すか。右の表示レイヤー(スポーン物の Layer)も、ここで選べます。", RowChanged) { LayerAlways = true, NoFold = true };
                }

                case nameof(VfxData.LightLayerMask):
                    return new VfxLightLayerRow(So, DefaultSo, e, RowChangedRestart);

                case nameof(VfxData.AnchorId):
                    return new VfxAnchorIdRow(So, DefaultSo, e, () => Target != null && Target.AnchorId.IsValid, AnchorAssetText, OpenAnchorEditor, ConvertEmbeddedAnchorToAsset, RowChanged);

                case nameof(VfxData.Anchor):
                    return CreateAnchorRows(e, card);

                default:
                    return base.CreateRow(e, card, isAdmin);
            }
        }

        private UxRow CreateAnchorRows(FieldGuideEntry e, UxCard card)
        {
            var data = Target;
            var pad = new AnchorPad(
                data,
                () => data.Anchor.LocalOffset,
                v =>
                {
                    var a = data.Anchor;
                    a.LocalOffset = v;
                    data.Anchor = a;
                },
                final => VisualEdited(final, true));
            _anchorPad = pad;
            var anchorRow = new UxVisualRow(So, DefaultSo, new[] { e }, "出る位置", "上から見たパッドをドラッグ。高さは右のスライダー。", FieldTier.Common, pad, RowChanged);
            RefreshAnchorNote();

            // 同じ欄の全設定(基準・回転・スケール)は詳細でだけ。
            var full = new UxRow(So, DefaultSo, new[] { e }, "位置の全設定(基準・回転)", e.Hint, FieldTier.Advanced, true,
                GuidedField.MakeInput(So, e.Field), RowChanged) { IsDuplicate = true };
            AddRow(card, anchorRow, false);

            var toolsEntry = new FieldGuideEntry
            {
                Field = "(attach)",
                Label = "出る場所の基準",
                Hint = "スポーン先・Path・SceneView 表示",
                Tier = FieldTier.Common,
                Purpose = VfxFieldGuide.PurPlace,
                Keywords = new[] { "スポーン先", "ボーン", "Path", "SceneView", "ハンドル", "AnchorPoint", "キャラクター" },
            };
            _toolsRow = new VfxAnchorToolsRow(
                toolsEntry,
                () => _attachTarget,
                SetAttach,
                () => _sceneHandle,
                v =>
                {
                    _sceneHandle = v;
                    SceneView.RepaintAll();
                },
                () => VfxAnchorSceneGui.DescribeResolution(Target, _attachTarget),
                () => SceneGuiOwner.DescribeFor(this),
                (menu, attach) => VfxAnchorSceneGui.FillPathMenu(menu.menu, attach, SelectAnchorPath));
            AddAuxRow(card, _toolsRow);
            return full;
        }

        // ── Anchor / スポーン先 / 複数再生 ──

        private void SetAttach(GameObject go)
        {
            _attachTarget = go;
            RestartIfPlaying();
            _toolsRow?.Sync();
        }

        private void ApplyAnchorChange(Func<AnchorDef, AnchorDef> mutate)
        {
            if (Target == null)
            {
                return;
            }

            Undo.RecordObject(Target, "Change VFX Anchor");
            Target.Anchor = mutate(Target.Anchor);
            EditorUtility.SetDirty(Target);
            So?.Update();
            OnSideEffect();
            RefreshAll(true);
            SceneView.RepaintAll();
        }

        private void SelectAnchorPath(string name)
        {
            ApplyAnchorChange(a =>
            {
                a.Path = name;
                if (a.Space == AnchorSpace.World || a.Space == AnchorSpace.ContextTarget)
                {
                    a.Space = AnchorSpace.NamedObject;
                }

                return a;
            });
            RestartIfPlaying();
        }

        protected override bool WantsSceneGui => _sceneHandle && _driver != null;

        protected override void DrawSceneGui() =>
            VfxAnchorSceneGui.Draw(this, Target, Driver, _handle, _attachTarget, ApplyAnchorChange, () => RefreshAll(true));

        private string AnchorAssetText()
        {
            if (Target == null || !Target.AnchorId.IsValid)
            {
                return string.Empty;
            }

            var asset = EditorAnchorRegistry.Find(Target.AnchorId.Value);
            return asset != null
                ? $"Anchor アセット '{asset.name}' を使用中(埋め込みの位置は無視されます)。位置・ランダム・ディレイの調整は「Anchor Editor で開く」から。"
                : $"⚠ AnchorId 0x{Target.AnchorId.Value:X} のアセットが見つかりません(World 原点扱い)。";
        }

        private void OpenAnchorEditor()
        {
            var asset = Target != null && Target.AnchorId.IsValid ? EditorAnchorRegistry.Find(Target.AnchorId.Value) : null;
            if (asset != null)
            {
                AnchorEditorWindow.Open(asset);
            }
        }

        // 埋め込み Anchor → Anchor アセット化(移行補助)。埋め込み値は残す。
        private void ConvertEmbeddedAnchorToAsset()
        {
            if (Target == null || Target.AnchorId.IsValid)
            {
                return;
            }

            var category = string.IsNullOrEmpty(Target.Category) ? AnchorAssetFactory.DefaultCategory : AnchorAssetFactory.ToIdentifier(Target.Category);
            var identifier = AnchorAssetFactory.ToIdentifier(Target.name) + "Anchor";
            var asset = AnchorAssetFactory.CreateFromDef(Target.Anchor, $"{(Target.DisplayName ?? Target.name)} の Anchor", category, identifier);
            if (asset == null)
            {
                return;
            }

            Undo.RecordObject(Target, "Set VFX AnchorId");
            Target.AnchorId = new AssetId<AnchorMarker>(asset.Id, AssetType.Anchor);
            EditorUtility.SetDirty(Target);
            So?.Update();
            EditorAnchorRegistry.Refresh(Driver?.Registry);
            RestartIfPlaying();
            RowChanged();
            EditorGUIUtility.PingObject(asset);
        }

        private void ToggleSlot(int i)
        {
            if (Driver == null)
            {
                return;
            }

            if (Driver.IsPlaying(_slotHandle[i]))
            {
                Driver.Stop(_slotHandle[i]);
                _slotHandle[i] = Handle<VfxMarker>.Invalid;
                return;
            }

            if (_slotData[i] != null)
            {
                _slotHandle[i] = Driver.Play(_slotData[i], AttachTransform);
            }
        }

        private void RefreshAnchorNote()
        {
            if (_anchorPad == null || Target == null)
            {
                return;
            }

            if (Target.AnchorId.IsValid)
            {
                _anchorPad.SetDimmed(true);
                _anchorPad.SetNote("Anchor アセットが設定されているため、この位置は使われません(詳細の「共通の出る位置」)。");
            }
            else if (Target.Anchor.Space != AnchorSpace.World)
            {
                _anchorPad.SetDimmed(false);
                _anchorPad.SetNote($"基準が {Target.Anchor.Space} です。位置は基準からの相対(メートル)になります。");
            }
            else
            {
                _anchorPad.SetDimmed(false);
                _anchorPad.SetNote(null);
            }
        }

        // ── LifeMode の対応づけ ──

        private static UxLifeMode ToUx(VfxLifeMode m)
        {
            switch (m)
            {
                case VfxLifeMode.Loop: return UxLifeMode.Loop;
                case VfxLifeMode.Duration: return UxLifeMode.Duration;
                default: return UxLifeMode.OneShot;
            }
        }

        private static VfxLifeMode FromUx(UxLifeMode m)
        {
            switch (m)
            {
                case UxLifeMode.Loop: return VfxLifeMode.Loop;
                case UxLifeMode.Duration: return VfxLifeMode.Duration;
                default: return VfxLifeMode.OneShot;
            }
        }
    }

    // 空状態の「サンプルを作る」: 同梱の vfx_sample.prefab を使う VFX_Sample_Hit を作る(既にあれば選ぶだけ)。
    internal static class VfxSample
    {
        private const string AssetName = "VFX_Sample_Hit";
        private const string PrefabFileName = "vfx_sample.prefab";

        public static VfxData GetOrCreate()
        {
            var existing = AssetDatabase.FindAssets(AssetName + " t:VfxData")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => System.IO.Path.GetFileNameWithoutExtension(p) == AssetName)
                .Select(AssetDatabase.LoadAssetAtPath<VfxData>)
                .FirstOrDefault(d => d != null);
            if (existing != null)
            {
                return existing;
            }

            var prefab = FindPrefab();
            if (prefab == null)
            {
                Debug.LogWarning("[D-Drive] 同梱の vfx_sample.prefab が見つかりません。");
                return null;
            }

            var created = AssetCreationService.Create(
                typeof(VfxData), AssetType.Vfx, "サンプル: ヒット", "Sample", "Hit",
                configure: asset =>
                {
                    var v = (VfxData)asset;
                    v.Prefab = prefab;
                    v.LifeMode = VfxLifeMode.OneShot;
                    v.Duration = 1f;
                    EditorUtility.SetDirty(v);
                }) as VfxData;
            if (created != null)
            {
                DDrive.Editor.Versioning.DDriveAssetSave.SaveAllSuppressed();
            }

            return created;
        }

        private static GameObject FindPrefab()
        {
            foreach (var guid in AssetDatabase.FindAssets("vfx_sample t:Prefab"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("/Samples/" + PrefabFileName, StringComparison.Ordinal))
                {
                    return AssetDatabase.LoadAssetAtPath<GameObject>(path);
                }
            }

            return null;
        }
    }
}
