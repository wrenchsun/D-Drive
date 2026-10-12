using System;
using System.Collections.Generic;
using DDrive.Editor.Preview;
using DDrive.Editor.Ui;
using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.CanvasTool
{
    // CanvasEditor の確認用表示(実 UiManager)・ElementFx の再生・「Idle を流す」・要素の選択・選択に追従・自動収集。
    // 中身の多くは旧 CanvasEditorWindow(Archive)から移した処理で、ウィンドウの見た目に依存しない部分。
    internal sealed partial class CanvasEditor
    {
        private UiManager _manager;
        private AssetRegistry _registry;
        private PoolService _pool;
        private UiTweenManager _tweenManager;
        private double _lastEditorTime;
        private UiPreset _bulkPreset;
        private CanvasData _previewData; // 確認用プレビューで OpenData した CanvasData(Target か、その親の連なりの最外側)
        private GameObject _previewRoot;
        private Handle<CanvasMarker> _previewHandle = Handle<CanvasMarker>.Invalid;
        private string _simFocusPath;

        private readonly List<(string label, UiTweenData tween)> _catalogChoices = new();

        // ElementFx 各行の直接再生(Appear / Idle / Disappear)。Key は (ElementPath, Phase ラベル)。
        private readonly Dictionary<(string path, string phase), Handle<UiTweenMarker>> _phasePreviewHandles = new();
        private readonly List<PhaseRowWidgets> _phaseRowWidgets = new();
        private readonly TweenTrack[] _presetPlayScratch = new TweenTrack[UiTweenManager.MaxTracksPerTween];

        // ElementFx の再生前の状態の控え。_previewStates = 確認用シーンの実体(作り直すまで保持)、
        // _stageStates = プレハブモードのステージ内の実体(プレハブに値を残さないよう、再生が終わったら戻して捨てる)。
        private readonly ElementFxStateSnapshot _previewStates = new();
        private readonly ElementFxStateSnapshot _stageStates = new();
        private readonly List<RectTransform> _targetScratch = new();

        // 「Idle を流す」(U-29b。プレハブモードで Idle が割り当てられた全要素の Idle を流し続ける)。トグルの状態は窓のセッション内だけ。
        private CanvasIdleFlow _idleFlow;
        private bool _idleFlowOn;
        private bool _idleHeldByPhase;
        private int _idleSignature;
        private bool _idleBuilt;
        private double _lastIdlePoll;
        private double _lastIdleRepaint;
        private int _idleSelectionActiveId;
        private int _idleSelectionCount;
        private readonly List<CanvasIdleFlow.Entry> _idleEntries = new();
        private Toggle _idleFlowToggle;
        private Label _idleFlowHint;
        private const double IdlePollInterval = 0.25;
        private const double IdleRepaintInterval = 1.0 / 30.0;

        // Id → UiTweenData の対応を 1 回の走査でまとめて作り、ヒットしなかったときだけ作り直す。
        private readonly Dictionary<ulong, UiTweenData> _tweenLookup = new();
        private readonly List<(string path, string phase)> _handleScratch = new();

        // 行の ▶ 再生に結びついた部品(状態の表示・一時停止・停止)。詳細を開いた行だけが持つ。
        private sealed class PhaseRowWidgets
        {
            public string ElementPath;
            public string Phase;
            public Button PlayButton;
            public Button PauseButton;
            public Button StopButton;
            public Label StatusLabel;
        }

        private bool IsPreviewUsable() => TryGetPreviewPrefix(out _);

        // 選択の変化(Selection.selectionChanged)。流している Idle のうち選択した要素の分だけ止め、選択に追従で編集対象を切り替える。
        private void OnSceneSelectionChanged()
        {
            if (_idleFlowOn && _idleFlow != null && _idleFlow.IsActive)
            {
                _idleFlow.SuspendFor(Selection.transforms);
                UpdateIdleFlowUi();
            }

            if (_followSelection && !Locked)
            {
                FollowSceneSelection();
            }
        }

        // 戻り値: データを書き換えたか。
        // (レビュー対応 2026-09-14) PlacePreview(▶ で暗黙に呼ばれる)が毎回これを実行し、差分が無くても Undo を
        // 2 件積んでアセットを Dirty にしていた。CollectMerged の結果が既存と同じ(キーの並びが一致)なら何も書かない。
        private bool CollectSelectables()
        {
            if (Target == null || Target.Prefab == null)
            {
                SetStatus("Prefab を設定してください");
                return false;
            }

            var merged = CanvasNavigationCollector.CollectMerged(Target.Prefab, Target.Navigation);
            if (SameNavigationKeys(Target.Navigation, merged))
            {
                SetStatus($"Selectable は収集済みです({merged.Length} 件)");
                return false;
            }

            Undo.RecordObject(Target, "Collect Selectables");
            Target.Navigation = merged;
            EditorUtility.SetDirty(Target);

            AfterDataWrite();
            SetStatus($"Selectable を {merged.Length} 件収集しました");
            return true;
        }

        // U-21: CanvasData.Prefab をプレハブモードで開く(ModelEditorWindow.OpenPrefab / VfxEditorWindow.OpenPrefab
        // と同じ導線)。プレビュー実体はここで片付ける(プレハブモードへ切り替わるとプレビュー実体の所属ステージが
        // 不定になり、閉じ忘れの残骸になりやすいため)。
        private void OpenPrefab()
        {
            if (Target == null || Target.Prefab == null)
            {
                Debug.LogWarning("[DDrive] 対象 CanvasData に Prefab がありません。");
                return;
            }

            RemovePreview();
            AssetDatabase.OpenAsset(Target.Prefab);
        }

        // U-21: ElementFx の各行から、その要素(RectTransform)を選んで実際に動かせるようにする。プレビュー実体
        // (「確認用シーンを開く」で置く物、OpenData 生成、Prefab リンク無し)を動かしても Prefab には反映されない
        // ため、必ず Prefab 自身をプレハブモードで開いてから選択する。プレハブモードは通常のシーン編集と同じ
        // Undo 機構に乗るため、Move/Rotate/Rect ツールでの移動はそのまま Ctrl+Z で戻せる(CLAUDE.md §0-5 は
        // 「エディタがコードで書き換えるとき」の規約であり、ここはユーザー自身が Unity 標準ツールで動かす
        // 経路なので該当しない)。
        private void SelectElementForMove(string elementPath)
        {
            if (Target == null || Target.Prefab == null)
            {
                SetStatus("Prefab を設定してください");
                return;
            }

            // 2026-10-03(Canvas の埋め込み): 対象自身の Prefab、または対象を埋め込んでいる親の Prefab のステージが既に開いて
            // いればそのまま使う(子のプレハブモードから親へ、親から子へ勝手に切り替えない)。どちらも開いていなければ対象自身の Prefab を開く。
            var stage = GetTargetStage(out var stagePrefix);
            if (stage == null)
            {
                RemovePreview();
                AssetDatabase.OpenAsset(Target.Prefab);
                stage = GetTargetStage(out stagePrefix);
            }

            if (stage == null || stage.prefabContentsRoot == null)
            {
                SetStatus("Prefab を開けませんでした");
                return;
            }

            var rootTransform = stage.prefabContentsRoot.transform;
            var viewPath = EmbeddedPaths.Combine(stagePrefix, elementPath);
            var found = string.IsNullOrEmpty(viewPath) ? rootTransform : rootTransform.Find(viewPath);
            var label = string.IsNullOrEmpty(elementPath) ? RootElementLabel : elementPath;
            if (found == null)
            {
                SetStatus($"要素が見つかりません({label})");
                return;
            }

            SelectGameObject(found.gameObject);
            PreviewPlacement.Focus(found.gameObject);
            SetStatus($"'{label}' を選択しました。SceneView の移動/回転/リサイズツールで編集できます(Ctrl+Z で戻せます)");
        }

        // 4-9: Graphic(Image 等)/UiInteractable(UiButton 等)/パネル(RectTransform+Graphic)を持つパスを
        // ElementEffects に追加する(既存の行・値は保持する)。戻り値: データを書き換えたか(レビュー対応 2026-09-14)。
        private bool CollectElementFx()
        {
            if (Target == null || Target.Prefab == null)
            {
                SetStatus("Prefab を設定してください");
                return false;
            }

            // 登録済みの埋め込みルートの配下は子の CanvasData の担当なので集めない(既に親にある行は消えず残る)。
            var merged = CanvasElementFxCollector.CollectMerged(Target.Prefab, Target.ElementEffects, CanvasEmbeddedEditing.RegisteredRoots(Target));
            if (SameElementFxKeys(Target.ElementEffects, merged))
            {
                SetStatus($"ElementFx は収集済みです({merged.Length} 件)");
                return false;
            }

            Undo.RecordObject(Target, "Collect ElementFx");
            Target.ElementEffects = merged;
            EditorUtility.SetDirty(Target);

            AfterDataWrite();
            SetStatus($"ElementFx を {merged.Length} 件収集しました");
            return true;
        }

        // CollectMerged は既存の行をそのままの値・順序で残し、無いキーだけ末尾に足す(重複キーは 1 行にまとまる)。
        // よって「件数とキーの並びが一致」なら内容も同一で、書き換える必要が無い(レビュー対応 2026-09-14)。
        private static bool SameNavigationKeys(NavNode[] existing, NavNode[] merged)
        {
            var count = existing?.Length ?? 0;
            if (count != merged.Length)
            {
                return false;
            }

            for (var i = 0; i < count; i++)
            {
                if (!string.Equals(existing[i].Element ?? string.Empty, merged[i].Element ?? string.Empty, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SameElementFxKeys(ElementFx[] existing, ElementFx[] merged)
        {
            var count = existing?.Length ?? 0;
            if (count != merged.Length)
            {
                return false;
            }

            for (var i = 0; i < count; i++)
            {
                if (!string.Equals(existing[i].ElementPath ?? string.Empty, merged[i].ElementPath ?? string.Empty, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private void FollowSceneSelection()
        {
            var go = Selection.activeGameObject;
            if (go != null && go.GetInstanceID() == _selfSelectedId)
            {
                return; // このウィンドウ自身が選んだもの(「選択」「▶ 再生」など)には反応しない
            }

            _selfSelectedId = 0;
            if (go == null || Target == null || EditorUtility.IsPersistent(go) || IsEditingText())
            {
                return;
            }

            if (!TryFindViewForSelection(go, out var view, out var viewRoot))
            {
                return;
            }

            var rel = TransformPath.GetRelative(viewRoot, go.transform);
            var owner = CanvasEmbeddedEditing.ResolveOwner(view, rel, Lookup);
            if (owner.Data == null)
            {
                return;
            }

            if (owner.Data != Target)
            {
                // 既存の文脈の中の祖先へ戻るだけなら、文脈(その外側)は保つ。
                var index = _ancestors.FindIndex(l => l.Data == owner.Data);
                SetTargetIn(owner.Data, index >= 0 ? _ancestors.GetRange(0, index) : owner.Ancestors);
            }

            HighlightRow(owner.Path);
        }

        // フォーカスのある入力欄(テキスト・数値)がこのウィンドウにあるあいだは切り替えない(入力中の値を失わない)。
        // このウィンドウ以外(Hierarchy など)にフォーカスがあるときは入力中ではない。
        private bool IsEditingText()
        {
            if (focusedWindow != this)
            {
                return false;
            }

            var focused = rootVisualElement.panel?.focusController?.focusedElement as VisualElement;
            return CanvasEmbeddedEditing.IsTextInputElement(focused);
        }

        // 選んだ GameObject が「どの CanvasData の Prefab を表示している実体」の中にあるか。
        // プレハブステージ(その Prefab を持つ CanvasData)か、確認用プレビューの実体。
        private bool TryFindViewForSelection(GameObject go, out CanvasData view, out Transform viewRoot)
        {
            view = null;
            viewRoot = null;
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.prefabContentsRoot != null && stage.IsPartOfPrefabContents(go))
            {
                view = FindCanvasByPrefabPath(stage.assetPath);
                viewRoot = stage.prefabContentsRoot.transform;
                return view != null;
            }

            if (_manager != null && _manager.IsOpen(_previewHandle) && _previewData != null)
            {
                var rootGo = _manager.GetGameObject(_previewHandle);
                if (rootGo != null && go.transform.IsChildOf(rootGo.transform))
                {
                    view = _previewData;
                    viewRoot = rootGo.transform;
                    return true;
                }
            }

            return false;
        }

        private CanvasData FindCanvasByPrefabPath(string assetPath)
        {
            if (Target != null && Target.Prefab != null && AssetDatabase.GetAssetPath(Target.Prefab) == assetPath)
            {
                return Target;
            }

            for (var i = _ancestors.Count - 1; i >= 0; i--)
            {
                var a = _ancestors[i].Data;
                if (a != null && a.Prefab != null && AssetDatabase.GetAssetPath(a.Prefab) == assetPath)
                {
                    return a;
                }
            }

            return Lookup.FindByPrefabPath(assetPath);
        }

        private void SelectGameObject(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            _selfSelectedId = go.GetInstanceID();
            Selection.activeGameObject = go;
        }

        // 範囲外(Undo で行が減った等)なら default を返す(レビュー対応 2026-09-14)。
        internal static ElementFx GetFx(CanvasData owner, int index)
            => owner != null && owner.ElementEffects != null && index >= 0 && index < owner.ElementEffects.Length
                ? owner.ElementEffects[index]
                : default;

        // 範囲外なら何もしない(レビュー対応 2026-09-14)。Undo.RecordObject / SetDirty は呼び出し側が行う。
        internal static void UpdateFx(CanvasData owner, int index, Func<ElementFx, ElementFx> mutate)
        {
            if (owner == null || owner.ElementEffects == null || index < 0 || index >= owner.ElementEffects.Length)
            {
                return;
            }

            owner.ElementEffects[index] = mutate(owner.ElementEffects[index]);
        }

        // プレハブモードで、このウィンドウの対象 CanvasData の Prefab(または、対象を埋め込んでいる親の Prefab)を
        // 開いているときのステージ(それ以外は null)。開いているのが無関係な Prefab のときは対象外(プレビュー実体側で再生する)。
        // prefix = そのステージのルートから見た対象 CanvasData のルートのパス(対象自身の Prefab なら空文字)。
        // 2026-10-03(Canvas の埋め込み): 以前は「対象 CanvasData の Prefab と完全一致」だけだった。
        private PrefabStage GetTargetStage() => GetTargetStage(out _, out _);

        private PrefabStage GetTargetStage(out string prefix) => GetTargetStage(out prefix, out _);

        // owner = そのステージの Prefab を持つ CanvasData(対象自身か、対象を埋め込んでいる親)。
        private PrefabStage GetTargetStage(out string prefix, out CanvasData owner)
        {
            prefix = string.Empty;
            owner = null;
            if (Target == null)
            {
                return null;
            }

            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null || stage.prefabContentsRoot == null)
            {
                return null;
            }

            if (Target.Prefab != null && stage.assetPath == AssetDatabase.GetAssetPath(Target.Prefab))
            {
                owner = Target;
                return stage;
            }

            // 親の連なりを内側から見る(子のプレハブモードから親へ勝手に戻さない。親のプレハブモードでも再生できる)。
            for (var i = _ancestors.Count - 1; i >= 0; i--)
            {
                var ancestor = _ancestors[i].Data;
                if (ancestor != null && ancestor.Prefab != null && stage.assetPath == AssetDatabase.GetAssetPath(ancestor.Prefab))
                {
                    prefix = CanvasEmbeddedEditing.ToAncestorPath(_ancestors, i, string.Empty);
                    owner = ancestor;
                    return stage;
                }
            }

            return null;
        }

        private static RectTransform FindInStage(PrefabStage stage, string elementPath)
        {
            var root = stage.prefabContentsRoot.transform;
            var found = string.IsNullOrEmpty(elementPath) ? root : root.Find(elementPath);
            return found as RectTransform;
        }

        // 確認用プレビューが開いていて、その実体が対象(または対象を埋め込んでいる親)の Prefab なら true。
        // prefix = プレビュー実体のルートから見た対象 CanvasData のルートのパス。
        private bool TryGetPreviewPrefix(out string prefix)
        {
            prefix = string.Empty;
            if (_manager == null || !_manager.IsOpen(_previewHandle) || _previewData == null)
            {
                return false;
            }

            if (_previewData == Target)
            {
                return true;
            }

            for (var i = _ancestors.Count - 1; i >= 0; i--)
            {
                if (_ancestors[i].Data == _previewData)
                {
                    prefix = CanvasEmbeddedEditing.ToAncestorPath(_ancestors, i, string.Empty);
                    return true;
                }
            }

            return false;
        }

        // 今「表示されている」再生対象(プレハブモードならステージ内、そうでなければ確認用シーンのプレビュー実体)。
        // 表示されていなければ null。states には対応する初期状態の控えを返す。プレビューの新規配置はしない。
        // elementPath は対象 CanvasData のルート基準(親の中に埋め込まれているときは、親の実体の中の位置へ変換して探す)。
        private RectTransform FindPlaybackTarget(string elementPath, out ElementFxStateSnapshot states)
        {
            var stage = GetTargetStage(out var stagePrefix);
            if (stage != null)
            {
                states = _stageStates;
                return FindInStage(stage, EmbeddedPaths.Combine(stagePrefix, elementPath));
            }

            states = _previewStates;
            return TryGetPreviewPrefix(out var previewPrefix)
                ? _manager.GetComponent<RectTransform>(_previewHandle, EmbeddedPaths.Combine(previewPrefix, elementPath))
                : null;
        }

        // 戻り値: 実際に再生を開始したか(レビュー対応 2026-09-14。「▶ 全〜」が失敗も再生件数に数えていた)。
        // 2026-09-29: プレハブモードで対象の Prefab を開いているときは、ステージ内の実体を実 UiTweenManager で再生する
        // (プレビュー実体を置き直さない)。再生前の値を控え、再生のたびに・止めるとき・終わったとき・プレハブモードを
        // 閉じる / 保存するときに元へ戻す(Undo には積まない。プレハブに値を残さない)。
        private bool PlayPhasePreview(string elementPath, string phase, UiPresetRef preset, AssetId<UiTweenMarker> id, bool selectPlayed = false)
        {
            if (Target == null || _tweenManager == null)
            {
                return false;
            }

            elementPath ??= string.Empty; // 行 UI 側のキー(null → 空文字に正規化済み)と揃える(レビュー対応 2026-09-14)

            var stage = GetTargetStage();
            if (stage == null && _manager != null && !IsPreviewUsable())
            {
                PlacePreview();
            }

            // 「Idle を流す」がオンなら、この再生の間は Idle を止めて元の値へ戻す(終わったら UpdateIdleFlow が再開する)。
            // Idle の途中の値を再生前の状態として控えてしまわないよう、控える前に止める。
            if (stage != null)
            {
                HoldIdleFlowForPhasePreview();
            }

            var elementTarget = FindPlaybackTarget(elementPath, out var states);
            if (elementTarget == null)
            {
                if (stage == null && !IsPreviewUsable())
                {
                    return false;
                }

                SetStatus($"{phase}: 要素が見つかりません({(string.IsNullOrEmpty(elementPath) ? RootElementLabel : elementPath)})");
                return false;
            }

            // 初期状態(最初に再生する前の値)を控える。既に控えてあれば上書きしない。
            states.Capture(elementTarget);

            StopPhasePreview(elementPath, phase);
            // (レビュー対応 2026-09-14) 同じ要素で UiManager 自身の ElementFx(開いた直後の Appear や自動の Idle ループ)が
            // 走っていると同じプロパティを取り合うため、この要素の Tween を全て止めてから再生する
            // (_tweenManager はこのウィンドウ専用のプレビュー用インスタンスなので、止めてよいのはプレビューの Tween だけ)。
            // 2026-09-29: U-23 の StopAll(complete:true) は Disappear(SlideOut 等)の終端値が残って次の SlideIn の基準
            // (UiPresetFactory.Build が読む現在位置)になり、連打でずれていた。止めたあとで必ず初期状態へ戻してから
            // 組み立てることで、何度押しても 1 回目と同じ再生になる。
            _tweenManager.StopAll(elementTarget);
            states.Restore(elementTarget);

            Handle<UiTweenMarker> handle;
            if (id.IsValid)
            {
                var tween = FindUiTweenData(id.Value);
                if (tween == null)
                {
                    return false;
                }

                handle = _tweenManager.PlayData(tween, elementTarget);
            }
            else if (preset.Preset != UiPreset.None)
            {
                var count = UiPresetFactory.Build(in preset, elementTarget, _presetPlayScratch);
                if (count <= 0)
                {
                    return false;
                }

                handle = _tweenManager.PlayTracks(_presetPlayScratch, count, elementTarget);
            }
            else
            {
                return false;
            }

            _phasePreviewHandles[(elementPath, phase)] = handle;

            if (selectPlayed)
            {
                SelectForPlayback(elementTarget);
                if (stage != null)
                {
                    SetStatus($"{phase}: プレハブモードの要素で再生中(終わると元の値へ戻します)");
                }
            }

            return true;
        }

        private void SelectForPlayback(RectTransform target)
        {
            if (target != null && EditorPrefs.GetBool(SelectOnPlayPrefKey, true))
            {
                SelectGameObject(target.gameObject);
            }
        }

        // 「選択」「フォーカス」ボタンの実体。今表示している実体(プレハブモード → プレビュー実体 → Prefab アセット内の順)を
        // Selection にして Inspector に出す。focus=true なら SceneView も寄せる(アセット内の要素は SceneView に無いので Ping のみ)。
        private void SelectElement(string elementPath, bool focus)
        {
            if (Target == null)
            {
                return;
            }

            elementPath ??= string.Empty;
            var label = string.IsNullOrEmpty(elementPath) ? RootElementLabel : elementPath;
            var stage = GetTargetStage();
            var previewOpen = IsPreviewUsable();

            if (stage != null || previewOpen)
            {
                var displayed = FindPlaybackTarget(elementPath, out _);
                if (displayed == null)
                {
                    SetStatus($"要素が見つかりません({label})");
                    return;
                }

                SelectGameObject(displayed.gameObject);
                if (focus)
                {
                    PreviewPlacement.FocusRect(displayed);
                }

                SetStatus(focus ? $"'{label}' を選択して SceneView を寄せました" : $"'{label}' を選択しました");
                return;
            }

            // 表示中の実体が無い: Prefab アセット内の該当要素を選択 + Ping。
            if (Target.Prefab == null)
            {
                SetStatus("Prefab を設定してください");
                return;
            }

            var assetRoot = Target.Prefab.transform;
            var inAsset = string.IsNullOrEmpty(elementPath) ? assetRoot : assetRoot.Find(elementPath);
            if (inAsset == null)
            {
                SetStatus($"要素が見つかりません({label})");
                return;
            }

            Selection.activeObject = inAsset.gameObject;
            EditorGUIUtility.PingObject(inAsset.gameObject);
            SetStatus(focus
                ? $"'{label}' は表示中の実体が無いため Prefab アセットを選択しました(SceneView へ寄せるには「確認用シーンを開く」かプレハブモードで表示してください)"
                : $"'{label}' を Prefab アセット内で選択しました");
        }

        // プレハブモードで再生した値を元へ戻して控えを捨てる(実行中のトゥイーンも止める)。
        private void ReleaseStageStates()
        {
            if (_stageStates.Count == 0)
            {
                return;
            }

            if (_tweenManager != null)
            {
                _stageStates.CollectTargets(_targetScratch);
                for (var i = 0; i < _targetScratch.Count; i++)
                {
                    _tweenManager.StopAll(_targetScratch[i]);
                }

                _targetScratch.Clear();
            }

            _stageStates.RestoreAllAndClear();
        }

        // 再生が全部終わった(停止した)ら、プレハブモードの値を元へ戻す(プレハブに値を残さない)。
        private void ReleaseStageStatesIfIdle()
        {
            if (_stageStates.Count > 0 && !AnyPhasePreviewActive())
            {
                ReleaseStageStates();
            }
        }

        // 行の ▶ 再生がまだ動いているか(終わった Handle はここで掃除する)。
        private bool AnyPhasePreviewActive()
        {
            if (_phasePreviewHandles.Count == 0)
            {
                return false;
            }

            if (_tweenManager == null)
            {
                _phasePreviewHandles.Clear();
                return false;
            }

            _handleScratch.Clear();
            foreach (var kv in _phasePreviewHandles)
            {
                if (!_tweenManager.IsPlaying(kv.Value))
                {
                    _handleScratch.Add(kv.Key);
                }
            }

            for (var i = 0; i < _handleScratch.Count; i++)
            {
                _phasePreviewHandles.Remove(_handleScratch[i]);
            }

            return _phasePreviewHandles.Count > 0;
        }

        private void OnPrefabStageClosing(PrefabStage stage)
        {
            StopIdleFlow(); // プレハブモードを閉じる前に Idle の値を戻す(閉じる時の保存確認にも途中の値を残さない)
            ReleaseStageStates();
            _phasePreviewHandles.Clear();
        }

        private void OnPrefabSaving(GameObject prefabRoot)
        {
            // 保存の直前に Idle・再生の途中の値を元へ戻す(Prefab に途中の値を書かない)。保存後は UpdateIdleFlow が作り直して再開する。
            StopIdleFlow();
            ReleaseStageStates();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                SetIdleFlow(false); // Play Mode に入る前に Idle の値を戻し、トグルもオフにする
            }
        }

        // テストから呼べるよう public(ウィンドウを画面に出さず、トグルと同じ経路を検証する)。
        public void SetIdleFlow(bool on)
        {
            _idleFlowOn = on;
            _idleFlowToggle?.SetValueWithoutNotify(on);
            if (!on)
            {
                _idleHeldByPhase = false;
                StopIdleFlow();
                UpdateIdleFlowUi();
                return;
            }

            PollIdleFlow();
        }

        private void StopIdleFlow()
        {
            _idleFlow?.Stop();
            _idleBuilt = false;
        }

        private void HoldIdleFlowForPhasePreview()
        {
            if (!_idleFlowOn)
            {
                return;
            }

            StopIdleFlow();
            _idleHeldByPhase = true;
        }

        private void UpdateIdleFlow(double now)
        {
            if (_idleFlow == null)
            {
                return;
            }

            if (now - _lastIdlePoll >= IdlePollInterval)
            {
                _lastIdlePoll = now;
                if (_idleFlowOn)
                {
                    PollIdleFlow();
                }
                else
                {
                    UpdateIdleFlowUi();
                }
            }

            // 選択の変化は OnSelectionChange に加えて毎フレーム確認する(OnSelectionChange はウィンドウが非アクティブなときや
            // コード経由の選択変更で遅れることがあるため。active と本数だけの軽い比較)。
            if (_idleFlowOn && _idleFlow.IsActive)
            {
                var activeId = Selection.activeInstanceID;
                var count = Selection.count;
                if (activeId != _idleSelectionActiveId || count != _idleSelectionCount)
                {
                    _idleSelectionActiveId = activeId;
                    _idleSelectionCount = count;
                    _idleFlow.SuspendFor(Selection.transforms);
                    UpdateIdleFlowUi();
                }
            }

            // 再描画は必要最小限: 実際に動いている Idle があるときだけ、30Hz まで。
            if (_idleFlowOn && _idleFlow.HasRunning && now - _lastIdleRepaint >= IdleRepaintInterval)
            {
                _lastIdleRepaint = now;
                SceneView.RepaintAll();
            }
        }

        // いまの表示先(プレハブモードのステージ)と割り当てに合わせて Idle を流す / 流し直す / 止める。
        // 署名(ステージ + 割り当ての内容)が変わらない限り流し直さない(絞り込み入力などでアニメが途切れないように)。
        private void PollIdleFlow()
        {
            if (_idleFlow == null)
            {
                return;
            }

            var stage = GetTargetStage(out _, out var owner);
            if (stage == null || owner == null || stage.prefabContentsRoot == null)
            {
                if (_idleBuilt)
                {
                    StopIdleFlow(); // 対象の Prefab のステージでなくなった
                }

                UpdateIdleFlowUi();
                return;
            }

            if (_idleHeldByPhase)
            {
                if (AnyPhasePreviewActive() || _stageStates.Count > 0)
                {
                    UpdateIdleFlowUi();
                    return;
                }

                _idleHeldByPhase = false; // 個別再生が終わって元の値へ戻った: 再開
            }

            CanvasIdleFlow.CollectEntries(owner, Lookup, _idleEntries);
            var signature = CanvasIdleFlow.Signature(stage.GetInstanceID(), _idleEntries);
            if (_idleBuilt && signature == _idleSignature && !_idleFlow.HasDestroyedTargets)
            {
                UpdateIdleFlowUi();
                return;
            }

            _idleFlow.Stop();
            _idleFlow.Start(stage.prefabContentsRoot.transform, _idleEntries);
            _idleFlow.SuspendFor(Selection.transforms);
            _idleSignature = signature;
            _idleBuilt = true;
            UpdateIdleFlowUi();
        }

        private void UpdateIdleFlowUi()
        {
            if (_idleFlowToggle == null || _idleFlowHint == null)
            {
                return;
            }

            var stage = Target != null ? GetTargetStage() : null;
            var previewOnly = Target != null && stage == null && IsPreviewUsable();
            _idleFlowToggle.SetEnabled(!previewOnly);

            string text;
            if (Target == null)
            {
                text = string.Empty;
            }
            else if (previewOnly)
            {
                text = "確認用プレビューでは Idle は常に流れています(このトグルはプレハブモード用)";
            }
            else if (stage == null)
            {
                text = "プレハブモードで対象の Prefab(または対象を埋め込んでいる親)を開いているときに流せます";
            }
            else if (!_idleFlowOn)
            {
                text = "オンにすると、このプレハブモードの中で Idle が割り当てられた全要素の Idle を流します(止めると元の値へ戻ります)";
            }
            else if (_idleHeldByPhase)
            {
                text = "行の ▶ 再生が終わるまで Idle を止めています(終わったら再開します)";
            }
            else
            {
                var count = _idleFlow != null ? _idleFlow.Count : 0;
                var suspended = _idleFlow != null ? _idleFlow.SuspendedCount : 0;
                text = count == 0
                    ? "Idle が割り当てられた要素がありません"
                    : suspended > 0
                        ? $"● {count} 件の要素で Idle を流しています(選択した要素 {suspended} 件は止めて元の値にしています。選択を外すと再開します)"
                        : $"● {count} 件の要素で Idle を流しています(要素を選択すると、その要素だけ止まります)";

                // 入れ子 Prefab(埋め込みの子など)の要素を流している間は、その値が「入れ子インスタンスへの上書き」に見える。
                // その状態で Overrides の Apply をすると、Idle の途中の値が子の Prefab に書かれる(Apply の前に止める手段が無い)。
                var nested = _idleFlow != null ? _idleFlow.NestedInstanceCount : 0;
                if (count > 0 && nested > 0)
                {
                    text += $"\n⚠ うち {nested} 件は入れ子 Prefab(埋め込みの子など)、または Prefab Variant の base に属する要素です。流している間は Hierarchy で太字(上書きあり)に見えます。Overrides の「Apply」は、このトグルをオフにしてから行ってください(流したまま Apply すると、途中の値が子の Prefab に書かれます)";
                }
            }

            if (_idleFlowHint.text != text)
            {
                _idleFlowHint.text = text;
            }
        }

        // 行の「■ 停止」: 止めて再生前の状態へ戻す。
        private void StopPhasePreviewAndReset(string elementPath, string phase)
        {
            elementPath ??= string.Empty;
            StopPhasePreview(elementPath, phase);
            var target = FindPlaybackTarget(elementPath, out var states);
            if (target != null)
            {
                _tweenManager?.StopAll(target);
                states.Restore(target);
            }
        }

        // 実体を新しく置いた直後(まだ Tick が進む前)の値を初期状態として控える。
        private void CapturePreviewBaselines()
        {
            _previewStates.Clear();
            if (!TryGetPreviewPrefix(out var prefix))
            {
                return;
            }

            _previewStates.Capture(_manager.GetComponent<RectTransform>(_previewHandle));
            if (Target?.ElementEffects == null)
            {
                return;
            }

            foreach (var fx in Target.ElementEffects)
            {
                _previewStates.Capture(_manager.GetComponent<RectTransform>(_previewHandle, EmbeddedPaths.Combine(prefix, fx.ElementPath)));
            }
        }

        private void TogglePausePhasePreview(string elementPath, string phase)
        {
            if (_tweenManager == null || !_phasePreviewHandles.TryGetValue((elementPath, phase), out var handle) || !_tweenManager.IsPlaying(handle))
            {
                return;
            }

            _tweenManager.SetPaused(handle, !_tweenManager.IsPaused(handle));
        }

        private void StopPhasePreview(string elementPath, string phase)
        {
            if (_tweenManager != null && _phasePreviewHandles.TryGetValue((elementPath, phase), out var handle))
            {
                _tweenManager.Stop(handle);
            }

            _phasePreviewHandles.Remove((elementPath, phase));
        }

        // 2026-09-12 ユーザー要望: 登録済みの ElementFx を同じ区間(Appear/Idle/Disappear)でまとめて再生する。
        // 各要素は自分に割り当てられたプリセット/直接指定をそれぞれ再生する(何も割り当てが無い要素はスキップ)。
        private void PlayAllPhasePreview(string phase)
        {
            if (Target == null || Target.ElementEffects == null || Target.ElementEffects.Length == 0)
            {
                return;
            }

            // プレハブモードで対象の Prefab を開いているときはステージ内の実体で再生する(プレビューを置かない)。
            var stageMode = GetTargetStage() != null;
            if (!stageMode && _manager != null && !IsPreviewUsable())
            {
                PlacePreview();
            }

            // (レビュー対応 2026-09-14) 開けなかったときに要素ごとに PlacePreview(RemovePreview/OpenData)を繰り返していた。
            // 1 回で打ち切る。
            if (!stageMode && !IsPreviewUsable())
            {
                SetStatus($"{phase}: プレビューを開けませんでした");
                return;
            }

            var played = 0;
            var assigned = 0;
            foreach (var fx in Target.ElementEffects)
            {
                var (preset, id) = GetPhaseValue(fx, phase);
                if (!id.IsValid && preset.Preset == UiPreset.None)
                {
                    continue;
                }

                assigned++;
                if (PlayPhasePreview(fx.ElementPath, phase, preset, id))
                {
                    played++;
                }
            }

            SetStatus(assigned == 0
                ? $"{phase} が割り当てられた要素がありません"
                : played == assigned
                    ? $"{phase} を {played} 件まとめて再生しました"
                    : $"{phase} を {played} / {assigned} 件再生しました(要素または Tween が見つからない行はスキップ)");
        }

        private static (UiPresetRef preset, AssetId<UiTweenMarker> id) GetPhaseValue(ElementFx fx, string phase) => phase switch
        {
            "Appear" => (fx.AppearPreset, fx.Appear),
            "Idle" => (fx.IdlePreset, fx.Idle),
            "Disappear" => (fx.DisappearPreset, fx.Disappear),
            _ => (default, default),
        };

        private void StopAllPhasePreview()
        {
            if (_tweenManager != null)
            {
                foreach (var handle in _phasePreviewHandles.Values)
                {
                    _tweenManager.Stop(handle);
                }
            }

            _phasePreviewHandles.Clear();

            // 止めた要素を再生前の状態へ戻す(プレビュー実体は控えを残し、プレハブステージは戻して捨てる)。
            if (_tweenManager != null)
            {
                _previewStates.CollectTargets(_targetScratch);
                for (var i = 0; i < _targetScratch.Count; i++)
                {
                    _tweenManager.StopAll(_targetScratch[i]);
                }

                _targetScratch.Clear();
            }

            _previewStates.RestoreAll();
            ReleaseStageStates();
            SetStatus("すべて停止しました(再生前の状態へ戻しました)");
        }

        // OnEditorUpdate から毎フレーム呼ぶ(AnimEditorWindow の状態ラベル更新と同じ方針)。行数分だけ
        // 軽い問い合わせ(IsPlaying/IsPaused、いずれも Dictionary 参照)をするだけなので許容範囲。
        private void RefreshPhaseRowStatuses()
        {
            for (var i = _phaseRowWidgets.Count - 1; i >= 0; i--)
            {
                var w = _phaseRowWidgets[i];
                if (w.PlayButton == null || w.PlayButton.panel == null)
                {
                    _phaseRowWidgets.RemoveAt(i); // 行が作り直された / 仮想化で外れた
                    continue;
                }

                UpdatePhaseRowWidgets(w);
            }
        }

        private void UpdatePhaseRowWidgets(PhaseRowWidgets widgets)
        {
            var key = (widgets.ElementPath, widgets.Phase);
            var handle = Handle<UiTweenMarker>.Invalid;
            var playing = _tweenManager != null && _phasePreviewHandles.TryGetValue(key, out handle) && _tweenManager.IsPlaying(handle);
            if (!playing)
            {
                _phasePreviewHandles.Remove(key);
            }

            var paused = playing && _tweenManager.IsPaused(handle);
            widgets.PlayButton.text = playing ? "■" : "▶";
            widgets.PlayButton.tooltip = playing ? "止めて、再生前の状態へ戻す" : "この動きを、確認用シーン(またはプレハブモード)の実要素で再生する。押すたびに再生前の状態へ戻してから再生する";
            if (widgets.PauseButton != null)
            {
                widgets.PauseButton.SetEnabled(playing);
                widgets.PauseButton.text = paused ? "▶ 再開" : "⏸ 一時停止";
            }

            if (widgets.StatusLabel != null)
            {
                var status = !playing ? "■ 停止中" : paused ? "⏸ 一時停止" : "● 再生中";
                if (widgets.StatusLabel.text != status)
                {
                    widgets.StatusLabel.text = status;
                }
            }
        }

        // (レビュー対応 2026-09-14) キャッシュ(_tweenLookup)に生きた一致があればそれを返し、無ければ 1 回だけ全走査して
        // 作り直す(アセットの削除・Id の変更は「キャッシュの値が null / Id 不一致」で検出される)。
        private UiTweenData FindUiTweenData(ulong id)
        {
            if (id == 0)
            {
                return null;
            }

            if (_tweenLookup.TryGetValue(id, out var cached) && cached != null && cached.Id == id)
            {
                return cached;
            }

            _tweenLookup.Clear();
            foreach (var guid in AssetSearch.FindAssets("t:" + nameof(UiTweenData)))
            {
                var asset = AssetDatabase.LoadAssetAtPath<UiTweenData>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null && asset.Id != 0)
                {
                    _tweenLookup.TryAdd(asset.Id, asset); // 同じ Id が複数あるときは従来どおり最初に見つかったもの
                }
            }

            return _tweenLookup.TryGetValue(id, out cached) ? cached : null;
        }

        // 4-10:「この要素の設定を他の要素へコピー」。実体は CanvasElementFxCollector.CopyPhases(配列操作のみ切り出し済み)。
        private void CopyRowToOthers(int index)
        {
            if (Target == null || Target.ElementEffects == null)
            {
                return;
            }

            Undo.RecordObject(Target, "ElementFx: 設定を他の要素へコピー");
            var rows = Target.ElementEffects;
            CanvasElementFxCollector.CopyPhases(ref rows, index);
            Target.ElementEffects = rows;
            EditorUtility.SetDirty(Target);
            AfterDataWrite();
            SetStatus("設定を他の要素へコピーしました");
        }

        // 4-9: Prefab 内の全 UiButton へ、選択中のプリセットを AppearPreset として一括設定する(行が無ければ追加する)。
        private void ApplyBulkPresetToButtons()
        {
            if (Target == null || Target.Prefab == null)
            {
                SetStatus("Prefab を設定してください");
                return;
            }

            var merged = CanvasElementFxCollector.ApplyPresetToButtons(Target.Prefab, Target.ElementEffects, _bulkPreset, CanvasEmbeddedEditing.RegisteredRoots(Target));

            Undo.RecordObject(Target, "Apply Preset To Buttons");
            Target.ElementEffects = merged;
            EditorUtility.SetDirty(Target);

            AfterDataWrite();
            SetStatus($"全ボタンの AppearPreset に {_bulkPreset} を設定しました");
        }

        private void EnsurePreviewRoot()
        {
            if (_previewRoot != null)
            {
                return;
            }

            _previewRoot = EditorPreviewRoots.CreateRoot(PreviewRootName); // DontSave(レビュー対応 2026-09-14)
            StageUtility.PlaceGameObjectInCurrentStage(_previewRoot);
            _pool.SetInstanceParent(_previewRoot.transform);
        }

        // 2026-09-12: 「確認用シーンを開く」ボタンの実体。専用シーンへの切り替えと配置を 1 手で行う
        // (切り替えた直後に必ず置きたいだけなので、2 ボタンに分ける意味が無かった)。
        // (レビュー対応 2026-09-14) 表示中に押すと、シーンの読み直しで実体だけが消え UiManager の _stack / _instances に
        // 古い CanvasInstance が残っていた。先に RemovePreview で Manager 側を片付ける。また既に確認用シーンを
        // 開いているときはシーンを開き直さない(保存確認ダイアログや読み直しが無駄なため。表示だけやり直す)。
        // 2026-09-17(U-5): 「既に確認用シーンを開いているなら開き直さない」判定は
        // CanvasPreviewSceneSetup.TryOpenOrCreate に集約した。ここは mode の分岐だけを見る。
        private void OpenPreviewSceneAndPlace(PreviewPlaceMode mode)
        {
            RemovePreview();
            if (!PreviewPlacement.PrepareScene(mode, CanvasPreviewSceneSetup.TryOpenOrCreate))
            {
                return;
            }

            if (PreviewPlacement.IsPersistent(mode))
            {
                if (Target == null)
                {
                    SetStatus("CanvasData を選択してください");
                    return;
                }

                // 本配置は UiManager が追跡しない実体(Prefab リンク付き)にする。
                var placed = PreviewPlacement.PlacePrefabPersistent(Target.Prefab, Vector3.zero, Quaternion.identity);
                SetStatus(placed != null
                    ? "このシーンに本配置しました(シーンを保存すると残ります。プレビュー表示ではありません)"
                    : "本配置に失敗しました(CanvasData に Prefab がありません)");
                return;
            }

            PlacePreview();
        }

        private void PlacePreview()
        {
            if (Target == null)
            {
                SetStatus("CanvasData を選択してください");
                return;
            }

            RemovePreview();
            EnsurePreviewRoot();
            EditorAnchorRegistry.Refresh(_registry);

            // 2026-10-03(Canvas の埋め込み): 子を編集中は、親(連なりの最外側)を Open する。UiManager が親の中の
            // 埋め込み実体に子の ElementFx・配線を適用するので、本番と同じ見え方で子のパスの再生・選択ができる。
            var viewData = ViewData;
            _previewHandle = _manager.OpenData(viewData);
            _previewData = _manager.IsOpen(_previewHandle) ? viewData : null;
            SetStatus(_manager.IsOpen(_previewHandle) ? "プレビュー表示中" : "表示に失敗しました");
            _simFocusPath = Target.FirstSelected;
            UpdatePadEnabled();
            UpdateFocusLabel();
            _graphView?.SetFocusedPath(_simFocusPath);

            if (_manager.IsOpen(_previewHandle))
            {
                // パッド操作シミュレーション/ノードグラフ/ElementFx 割当がすぐ使えるよう、プレビューを開いたら
                // Selectable と要素(Image/UiButton/パネル)を両方自動収集する(どちらも既存の行を保持する
                // CollectMerged 経由なので、何度押しても安全)。
                // (レビュー対応 2026-09-14) 差分が無ければ書き込まない(Undo を積まない・Dirty にしない)。
                var changed = CollectSelectables();
                changed |= CollectElementFx();
                CapturePreviewBaselines();
                if (!changed)
                {
                    SetStatus("プレビュー表示中");
                }

                // U-5(2026-09-17): 配置場所が SceneView のカメラから遠いと何も映らないため、必ず寄せる。
                PreviewPlacement.Focus(_previewRoot);
            }

            SceneView.RepaintAll();
        }

        private void RemovePreview()
        {
            // (レビュー対応 2026-09-14) 以前は _phasePreviewHandles をクリアするだけで止めていなかった。Canvas 実体は
            // UI Root のレイヤー下に残り、プールに戻っても SetActive(false) されるだけなので、Idle ループ等が非表示の
            // 要素上で走り続け、開き直した後の Appear とぶつかっていた。
            // 1) 行ごとの直接再生を止める → 2) UiManager.StopAll(自身の ElementFx は Appear を終端値で完了させて閉じる)
            // → 3) 残りをこのウィンドウ専用の _tweenManager ごと止める、の順(2 を先にすると行の再生の途中値が残るため)。
            if (_tweenManager != null)
            {
                foreach (var handle in _phasePreviewHandles.Values)
                {
                    _tweenManager.Stop(handle);
                }
            }

            _phasePreviewHandles.Clear();

            // 実体を失った(シーン切り替え等)インスタンスも片付けるため、IsOpen に関係なく呼ぶ(開いていなければ no-op)。
            _manager?.StopAll(DDrive.Foundation.Manager.StopReason.Manual);
            _tweenManager?.StopAll(DDrive.Foundation.Manager.StopReason.Manual);

            _previewHandle = Handle<CanvasMarker>.Invalid;
            _previewData = null;
            _previewStates.Clear(); // 実体ごと破棄するので戻さず捨てる

            if (_previewRoot != null)
            {
                DestroyImmediate(_previewRoot);
            }

            _previewRoot = null;

            if (Target != null)
            {
                SetStatus("「確認用シーンを開く」で確認できます");
            }

            _simFocusPath = null;
            UpdatePadEnabled();
            UpdateFocusLabel();
            _graphView?.SetFocusedPath(null);

            SceneView.RepaintAll();
        }
    }
}
