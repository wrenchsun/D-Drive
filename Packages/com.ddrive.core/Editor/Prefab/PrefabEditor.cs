using System;
using System.Collections.Generic;
using DDrive.Editor.Inspector;
using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Editor.Ux;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Prefab;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.PrefabTool
{
    // Prefab のエディター(docs/1010 UX-14。旧 PrefabEditorWindow は Archive メニューへ退避)。
    // 共通基盤(UxEditorWindow<PrefabData> / FieldGuide / UxRow / UxCard)で組む。欄は少ないので、見た目(Prefab)・ゲームでの扱い・確認する・管理情報の 4 枚。
    // プレビューは旧と同じ: 実 PrefabsManager で Spawn した実体を、開いているシーン / 確認用シーンの DontSave ルートに置いて SceneView で確認する(ADR-4)。
    [DataEditor(typeof(PrefabData), "Prefab Editor で開く")]
    internal sealed class PrefabEditor : UxEditorWindow<PrefabData>
    {
        // 空状態の「サンプルを作る」(docs/1010 UX-19)
        protected override bool HasSample => true;

        protected override PrefabData CreateSample() => UxSamples.Prefab();

        private const string Page = "prefab-data";
        public const string PreviewRootName = "[D-Drive] Prefab Preview";

        private PrefabsManager _manager;
        private AssetRegistry _registry;
        private PoolService _pool;
        private GameObject _previewRoot;
        private Handle<PrefabMarker> _previewHandle = Handle<PrefabMarker>.Invalid;
        private Label _sceneStatus;
        private string _note = string.Empty;

        // ── 入口 ──

        [MenuItem(DDriveMenu.Editors + "Prefab")]
        public static void OpenFromMenu() => Open(Selection.activeObject as PrefabData);

        public static void Open(PrefabData target)
        {
            var w = OpenWindow<PrefabEditor>("Prefab Editor");
            if (target != null)
            {
                w.SetTarget(target);
            }
        }

        // ── 土台への指定 ──

        protected override string PrefsKey => "Prefab";
        protected override FieldGuide<PrefabData> Guide => PrefabFieldGuide.Instance;
        protected override string ManualPage => Page;
        protected override Texture DefaultIcon => EditorGUIUtility.IconContent("Prefab Icon").image;

        protected override string ManualAnchor(string purpose)
        {
            switch (purpose)
            {
                case PrefabFieldGuide.PurPrefab: return "prefab";
                case PrefabFieldGuide.PurGame: return "game";
                case PrefabFieldGuide.PurCheck: return "check";
                case FieldGuide<PrefabData>.AdminPurpose: return "admin";
                default: return null;
            }
        }

        protected override string[] ModeTips => new[]
        {
            "必須の欄だけ。Prefab",
            "よく使う欄まで。種類・タグ・コリジョンレイヤー・動作の取り決め(Pool / ネット)・確認する",
            "すべての欄(LOD の上書き・イベント)",
        };

        // 検証メッセージ(ValidationResult.Field が無い結果)を欄に結びつける語。上にあるものを先に当てる。
        private static readonly KeyValuePair<string, string>[] MessageTokens =
        {
            new KeyValuePair<string, string>("GameplayTags", "GameplayTags"),
            new KeyValuePair<string, string>("CollisionLayer", "CollisionLayer"),
            new KeyValuePair<string, string>("Flags", "Flags"),
            new KeyValuePair<string, string>("Prefab", "Prefab"),
        };

        protected override IReadOnlyList<KeyValuePair<string, string>> ValidationTokens => MessageTokens;

        protected override string CardDescription(string purpose)
        {
            switch (purpose)
            {
                case PrefabFieldGuide.PurPrefab: return "Spawn する実体の Prefab";
                case PrefabFieldGuide.PurGame: return "種類・ゲームロジック用タグ・コリジョンレイヤー・Pool / ネットの取り決め";
                case PrefabFieldGuide.PurCheck: return "実際の PrefabsManager で Spawn した実体を、確認用シーンで見る";
                default: return "一覧に出る名前・分類・メモ";
            }
        }

        // ── ヒーロー ──

        protected override void AddLeadingHeroActions(VisualElement actions)
        {
            var scene = PreviewPlacementButton.Create(
                "確認用シーン",
                "確認用シーン(PreviewScene)を開き、実 PrefabsManager で Spawn して SceneView をそこへ向ける。右クリックで、いま開いているシーンへ配置 / 本配置",
                PlacePreview);
            scene.AddToClassList("ux-btn");
            actions.Add(scene);
        }

        // ── ライフサイクル ──

        protected override void OnWindowEnable()
        {
            _registry = EditorAnchorRegistry.Build();
            _pool = new PoolService();
            _manager = new PrefabsManager(_pool, _registry);
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
        }

        protected override void OnWindowDisable()
        {
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
            RemovePreview();
            _manager = null;
            _pool = null;
            _registry = null;
        }

        protected override void OnTargetChanged()
        {
            RemovePreview();
            _note = string.Empty;
        }

        protected override void OnBuildBodyStart()
        {
            _sceneStatus = null;
        }

        // 既定はすべて折りたたみ: 構造体・配列の欄(PropertyField)が作る Foldout も、組み終えたあとに閉じる。
        protected override void OnBodyBuilt(VisualElement page)
        {
            page.schedule.Execute(() => CollapseFoldouts(page)).StartingIn(30);
            page.schedule.Execute(() => CollapseFoldouts(page)).StartingIn(300);
            page.schedule.Execute(() => CollapseFoldouts(page)).StartingIn(1000);
            SetLabel(_sceneStatus, StatusText());
        }

        private static void CollapseFoldouts(VisualElement page)
        {
            foreach (var f in page.Query<Foldout>().ToList())
            {
                if (f.value && !f.ClassListContains("ux-sec__fold"))
                {
                    f.SetValueWithoutNotify(false);
                }
            }
        }

        protected override void OnTick()
        {
            SetLabel(_sceneStatus, StatusText());
        }

        // ── 欄 ──

        protected override UxRow CreateRow(FieldGuideEntry e, UxCard card, bool isAdmin)
        {
            if (e.Field == PrefabFieldGuide.RowScene)
            {
                return CreateSceneRow(e);
            }

            GuidedField.Collapse(So, e.Field); // 構造体・配列の Foldout は既定で閉じておく
            return base.CreateRow(e, card, isAdmin);
        }

        private UxRow CreateSceneRow(FieldGuideEntry e)
        {
            var box = new VisualElement();
            var line = new VisualElement();
            line.AddToClassList("ux-toolsline");
            var scene = PreviewPlacementButton.Create(
                "確認用シーン",
                "確認用シーン(PreviewScene)を開き、実 PrefabsManager で Spawn して SceneView をそこへ向ける。右クリックで、いま開いているシーンへ配置 / 本配置",
                PlacePreview);
            scene.AddToClassList("ux-btn");
            line.Add(scene);
            line.Add(MakeButton("今のシーンに配置", null, () => PlacePreview(PreviewPlaceMode.CurrentScene), "開いているシーンの SceneView の視点中心に、実体を置く(保存されない)"));
            line.Add(MakeButton("撤去", null, RemovePreview, "置いたプレビューをシーンから消す"));
            line.Add(MakeButton("Prefab で開く", null, OpenPrefab, "PrefabData.Prefab をプレハブモードで開く"));
            box.Add(line);
            _sceneStatus = new Label(StatusText());
            _sceneStatus.AddToClassList("ux-dim");
            _sceneStatus.AddToClassList("ux-note");
            box.Add(_sceneStatus);
            return new DDrive.Editor.Anim.AnimAuxRow(e, e.Label, e.Hint, e.Tier, box, null);
        }

        // ── 配置 ──

        private void OnActiveSceneChanged(UnityEngine.SceneManagement.Scene previous, UnityEngine.SceneManagement.Scene current)
        {
            // シーンが切り替わると DontSave の配置物は Unity 側で既に失われているので、参照だけ捨てる。
            _previewRoot = null;
            _previewHandle = Handle<PrefabMarker>.Invalid;
        }

        private void EnsurePreviewRoot()
        {
            if (_previewRoot != null)
            {
                return;
            }

            _previewRoot = new GameObject(PreviewRootName) { hideFlags = HideFlags.DontSave };
            StageUtility.PlaceGameObjectInCurrentStage(_previewRoot);
            var pivot = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            _previewRoot.transform.position = pivot;
            _pool.SetInstanceParent(_previewRoot.transform);
        }

        // 「片付ける → 確認用シーンを開く → 配置」の順(他のエディタと同じ)。右クリックの「このシーンに配置 / 本配置」は mode で分岐する(PreviewPlacement に集約)。
        private void PlacePreview(PreviewPlaceMode mode)
        {
            if (Target == null)
            {
                SetNote("PrefabData を選択してください");
                return;
            }

            RemovePreview();
            if (!PreviewPlacement.PrepareScene(mode, VfxPreviewSceneSetup.TryOpenOrCreate))
            {
                return;
            }

            if (PreviewPlacement.IsPersistent(mode))
            {
                // 本配置は Pool / Manager が追跡しない実体にする(後で Despawn されないように)。
                var pivot = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
                var placed = PreviewPlacement.PlacePrefabPersistent(Target.Prefab, pivot, Quaternion.identity);
                SetNote(placed != null ? "このシーンに本配置しました(シーンを保存すると残ります)" : "本配置に失敗しました");
                return;
            }

            EnsurePreviewRoot();
            EditorAnchorRegistry.Refresh(_registry);

            _previewHandle = _manager.SpawnData(Target, _previewRoot.transform.position, Quaternion.identity);
            var ok = _manager.IsValid(_previewHandle);
            _note = ok ? string.Empty : "配置に失敗しました";
            if (ok)
            {
                PreviewPlacement.Focus(_manager.GetGameObject(_previewHandle) ?? _previewRoot);
            }

            SetLabel(_sceneStatus, StatusText());
            SceneView.RepaintAll();
        }

        private void RemovePreview()
        {
            if (_manager != null && _manager.IsValid(_previewHandle))
            {
                _manager.Despawn(_previewHandle);
            }

            _previewHandle = Handle<PrefabMarker>.Invalid;
            if (_previewRoot != null)
            {
                DestroyImmediate(_previewRoot);
            }

            _previewRoot = null;
            _note = string.Empty;
            SetLabel(_sceneStatus, StatusText());
            SceneView.RepaintAll();
        }

        private void OpenPrefab()
        {
            if (Target != null && Target.Prefab != null)
            {
                RemovePreview();
                AssetDatabase.OpenAsset(Target.Prefab);
            }
            else
            {
                SetNote("Prefab が未設定です");
            }
        }

        private void SetNote(string text)
        {
            _note = text;
            SetLabel(_sceneStatus, StatusText());
        }

        private string StatusText()
        {
            if (!string.IsNullOrEmpty(_note))
            {
                return _note;
            }

            if (Target == null)
            {
                return "PrefabData を選択してください";
            }

            return _manager != null && _manager.IsValid(_previewHandle) ? "● プレビュー配置中(保存されません)" : "「確認用シーン」で確認できます";
        }

        private static void SetLabel(Label l, string text)
        {
            if (l != null && l.text != text)
            {
                l.text = text;
            }
        }
    }
}
