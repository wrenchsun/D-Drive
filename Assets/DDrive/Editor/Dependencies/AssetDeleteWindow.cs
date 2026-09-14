using System;
using System.Collections.Generic;
using System.Linq;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Codegen;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Dependencies
{
    // [削除要望(2026-09-14)] 「削除するときに Unreal Engine のように依存関係などわかりやすく、消した後どうするかも」。
    // AssetBrowser の行コンテキストメニュー「削除...」から開く(複数選択にも対応)。これまでの
    // EditorUtility.DisplayDialog ベースの確認(旧: AssetBrowserWindow.DeleteRow → SafeDeleteService.TryDelete)を
    // このウィンドウに置き換える。実際の削除・差し替え・アーカイブの判断ロジックは
    // AssetDeleteAnalysisService / ReferenceReplaceService / AssetDeleteExecutionService に分離してあり、
    // このクラスは UI 配線と状態遷移(分析画面 → 結果画面)だけを担う(EditMode テストはロジック側を直接叩く)。
    //
    // CLAUDE.md §0-6: 新規 EditorWindow は ScrollView ルート必須。
    // CLAUDE.md §0-7: プレビュー(実 Manager 駆動)の対象外(削除確認 UI そのものなので該当しない)。
    public sealed class AssetDeleteWindow : EditorWindow
    {
        private List<DeleteTarget> _targets = new();
        private Action _onCompleted;

        private AssetDeleteAnalysis _analysis;
        private readonly Dictionary<string, AssetDataBase> _replacementByTargetPath = new();
        private readonly HashSet<(AssetType, ulong)> _cascadeSelected = new();
        private bool _forceConfirmChecked;

        private ScrollView _root;
        private Button _executeButton;
        private RadioButtonGroup _actionGroup;
        private VisualElement _replaceSection;
        private VisualElement _forceSection;

        private static readonly string[] ActionLabels =
        {
            "参照を差し替えてから削除",
            "強制削除(参照が残ったまま削除)",
            "アーカイブのみ(削除しない)",
        };

        private const int ActionReplace = 0;
        private const int ActionForce = 1;
        private const int ActionArchive = 2;

        public static void Open(IReadOnlyList<DeleteTarget> targets, Action onCompleted = null)
        {
            if (targets == null || targets.Count == 0)
            {
                return;
            }

            var window = CreateInstance<AssetDeleteWindow>();
            window.titleContent = new GUIContent("アセットを削除");
            window._targets = new List<DeleteTarget>(targets);
            window._onCompleted = onCompleted;
            window.minSize = new Vector2(620, 480);
            window.Show();
        }

        private void CreateGUI()
        {
            BuildAnalysisView();
        }

        // ── 分析画面 ──

        private void BuildAnalysisView()
        {
            rootVisualElement.Clear();

            _root = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            rootVisualElement.Add(_root);

            if (DependencyGraphService.CachedFileCount == 0)
            {
                _root.Add(new HelpBox(
                    "依存関係グラフが未構築です。参照の有無を確認できないため削除できません。先に再構築してください。",
                    HelpBoxMessageType.Warning));
                _root.Add(new Button(() =>
                {
                    DependencyGraphService.RebuildAll();
                    BuildAnalysisView();
                })
                { text = "依存関係グラフを再構築" });
                return;
            }

            _analysis = AssetDeleteAnalysisService.Analyze(_targets);
            _replacementByTargetPath.Clear();
            _cascadeSelected.Clear();
            _forceConfirmChecked = false;

            BuildTargetsSection();
            BuildUsagesSection();
            BuildDependenciesSection();
            BuildActionSection();
        }

        private void BuildTargetsSection()
        {
            _root.Add(SectionHeader($"削除するアセット({_targets.Count} 件)"));

            foreach (var t in _targets)
            {
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginLeft = 8 } };

                var icon = new Image { scaleMode = ScaleMode.ScaleToFit };
                icon.style.width = 16;
                icon.style.height = 16;
                icon.style.marginRight = 4;
                icon.image = t.Asset != null
                    ? (t.Asset.Icon != null ? (Texture)t.Asset.Icon : AssetPreview.GetMiniThumbnail(t.Asset))
                    : null;
                row.Add(icon);

                var name = t.Asset != null ? DependencyAssetResolver.DisplayNameOrFileName(t.Asset, t.Path) : "?";
                var idText = t.Asset != null ? $"#{t.Asset.Id:X}" : "?";
                row.Add(new Label($"[{t.Type}] {name} ({idText}) — {t.Path}"));

                _root.Add(row);

                // 削除前にも警告する(旧: 確認ダイアログの文言に載せていたのと同じチェックを分析画面にも出す。
                // 結果画面(実行後)にも同じ警告+ファイル:行を出すが、実行前に気づけるようここでも出す)。
                if (t.Asset != null)
                {
                    var warning = CodeReferenceScan.FindPossibleReferences(t.Asset, t.Path);
                    if (!string.IsNullOrEmpty(warning))
                    {
                        _root.Add(new HelpBox(warning, HelpBoxMessageType.Warning));
                    }
                }
            }
        }

        private void BuildUsagesSection()
        {
            var external = _analysis.Usages.Where(u => !u.IsFromDeleteTarget).ToList();
            var internalRefs = _analysis.Usages.Where(u => u.IsFromDeleteTarget).ToList();

            _root.Add(SectionHeader($"このアセットを使っている場所(参照元) — 外部から {external.Count} 件"));

            AddUsageGroup("Data", external.Where(u => u.Kind == ReferenceFileKind.Data));
            AddUsageGroup("Prefab", external.Where(u => u.Kind == ReferenceFileKind.Prefab));
            AddUsageGroup("Scene", external.Where(u => u.Kind == ReferenceFileKind.Scene));

            if (internalRefs.Count > 0)
            {
                var foldout = new Foldout { text = $"削除対象どうしの参照(まとめて消すなら問題ありません、{internalRefs.Count} 件)", value = false };
                foreach (var u in internalRefs)
                {
                    foldout.Add(UsageRow(u.Reference, jumpable: false));
                }

                _root.Add(foldout);
            }
        }

        private void AddUsageGroup(string label, IEnumerable<ClassifiedReference> items)
        {
            var list = items.ToList();
            if (list.Count == 0)
            {
                return;
            }

            var foldout = new Foldout { text = $"{label}({list.Count} 件)", value = true };
            foreach (var u in list)
            {
                foldout.Add(UsageRow(u.Reference, jumpable: true));
            }

            _root.Add(foldout);
        }

        private static VisualElement UsageRow(DependencyReference reference, bool jumpable)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginLeft = 8 } };
            var objectPart = string.IsNullOrEmpty(reference.ObjectPath) ? string.Empty : $" / {reference.ObjectPath}";
            var label = new Label($"{reference.SourcePath}{objectPart} ({reference.ComponentType}.{reference.PropertyPath})") { style = { flexGrow = 1 } };
            row.Add(label);

            if (jumpable)
            {
                row.Add(new Button(() => DependencyJumpService.Reveal(reference)) { text = "ジャンプ" });
            }

            return row;
        }

        private void BuildDependenciesSection()
        {
            if (_analysis.Dependencies.Count == 0)
            {
                return;
            }

            _root.Add(SectionHeader("このアセットが使っているもの(依存先) — 一緒に削除できます"));

            foreach (var dep in _analysis.Dependencies)
            {
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginLeft = 8 } };

                var canCascade = dep.WouldBecomeUnused && !dep.IsAlsoDeleteTarget && !dep.IsUnresolved;
                var toggle = new Toggle { value = _cascadeSelected.Contains((dep.Type, dep.Id)) };
                toggle.SetEnabled(canCascade);
                var key = (dep.Type, dep.Id);
                toggle.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue)
                    {
                        _cascadeSelected.Add(key);
                    }
                    else
                    {
                        _cascadeSelected.Remove(key);
                    }
                });
                row.Add(toggle);

                var status = dep.IsAlsoDeleteTarget ? "(削除対象に含まれています)"
                    : dep.IsUnresolved ? "(見つかりません)"
                    : dep.WouldBecomeUnused ? "(この削除で未使用になります。一緒に削除できます)"
                    : "(他でも使用中のため一緒に削除できません)";

                row.Add(new Label($"[{dep.Type}] {dep.DisplayName} {status}"));
                _root.Add(row);
            }
        }

        private void BuildActionSection()
        {
            _root.Add(SectionHeader("削除方法を選ぶ"));

            var hasBlocking = _analysis.HasBlockingExternalUsages;

            if (!hasBlocking)
            {
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                row.Add(new Button(() => ExecuteAndShowResult(DeleteAction.ForceDelete)) { text = "削除する" });
                row.Add(new Button(() => ExecuteAndShowResult(DeleteAction.ArchiveOnly)) { text = "アーカイブのみ" });
                row.Add(new Button(Close) { text = "キャンセル" });
                _root.Add(row);
                return;
            }

            _root.Add(new HelpBox(
                $"{_analysis.ExternalUsageCount} 件から参照されているため、そのままでは削除できません。下の選択肢から進め方を選んでください。",
                HelpBoxMessageType.Warning));

            _actionGroup = new RadioButtonGroup(string.Empty, ActionLabels.ToList()) { value = ActionReplace };
            _actionGroup.RegisterValueChangedCallback(_ => UpdateActionSectionVisibility());
            _root.Add(_actionGroup);

            _replaceSection = BuildReplaceSection();
            _root.Add(_replaceSection);

            _forceSection = BuildForceSection();
            _root.Add(_forceSection);

            var buttonsRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            _executeButton = new Button(OnExecuteClicked) { text = "実行" };
            buttonsRow.Add(_executeButton);
            buttonsRow.Add(new Button(Close) { text = "キャンセル" });
            _root.Add(buttonsRow);

            UpdateActionSectionVisibility();
        }

        private VisualElement BuildReplaceSection()
        {
            var section = new VisualElement();
            section.Add(new Label("参照元の Data / Prefab は、選んだ置き換え先の ID に書き換えます(Ctrl+Z: Data は戻せます。Prefab は戻せません)。Scene 内の参照は自動で書き換えないため、Scene から使われている対象は削除せずアーカイブのみ行います。"));

            foreach (var t in _targets)
            {
                var hasExternalUsage = _analysis.Usages.Any(u => !u.IsFromDeleteTarget
                    && u.Reference.TargetType == t.Type && u.Reference.TargetId == (t.Asset != null ? t.Asset.Id : 0));
                if (!hasExternalUsage || t.Asset == null)
                {
                    continue;
                }

                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
                row.Add(new Label($"[{t.Type}] {DependencyAssetResolver.DisplayNameOrFileName(t.Asset, t.Path)} の代わりに使う:") { style = { minWidth = 220 } });

                var field = new ObjectField { objectType = t.Asset.GetType(), allowSceneObjects = false };
                var capturedTarget = t;
                field.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue is AssetDataBase candidate && IsValidReplacement(candidate, capturedTarget))
                    {
                        _replacementByTargetPath[capturedTarget.Path] = candidate;
                    }
                    else
                    {
                        _replacementByTargetPath.Remove(capturedTarget.Path);
                        if (evt.newValue != null)
                        {
                            Debug.LogWarning("[DDrive] 削除対象自身や、別の削除対象は置き換え先に選べません。");
                            field.SetValueWithoutNotify(null);
                        }
                    }

                    UpdateExecuteButtonState();
                });
                row.Add(field);

                section.Add(row);
            }

            return section;
        }

        private bool IsValidReplacement(AssetDataBase candidate, DeleteTarget target)
        {
            if (candidate == null || candidate.Id == 0 || candidate == target.Asset)
            {
                return false;
            }

            foreach (var t in _targets)
            {
                if (t.Asset == candidate)
                {
                    return false; // 削除対象どうしを置き換え先にはできない
                }
            }

            return true;
        }

        private VisualElement BuildForceSection()
        {
            var section = new VisualElement();
            section.Add(new HelpBox(
                $"参照している {_analysis.ExternalUsageCount} 件は実行時に Placeholder になり、Validation で Error になります。",
                HelpBoxMessageType.Error));

            var toggle = new Toggle("参照が残ることを理解した上で強制削除します") { value = false };
            toggle.RegisterValueChangedCallback(evt =>
            {
                _forceConfirmChecked = evt.newValue;
                UpdateExecuteButtonState();
            });
            section.Add(toggle);
            return section;
        }

        private void UpdateActionSectionVisibility()
        {
            var mode = _actionGroup?.value ?? ActionReplace;
            if (_replaceSection != null)
            {
                _replaceSection.style.display = mode == ActionReplace ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_forceSection != null)
            {
                _forceSection.style.display = mode == ActionForce ? DisplayStyle.Flex : DisplayStyle.None;
            }

            UpdateExecuteButtonState();
        }

        private void UpdateExecuteButtonState()
        {
            if (_executeButton == null)
            {
                return;
            }

            var mode = _actionGroup?.value ?? ActionReplace;
            if (mode == ActionForce)
            {
                _executeButton.SetEnabled(_forceConfirmChecked);
                _executeButton.text = "強制削除を実行";
            }
            else if (mode == ActionArchive)
            {
                _executeButton.SetEnabled(true);
                _executeButton.text = "アーカイブのみ実行";
            }
            else
            {
                _executeButton.SetEnabled(true);
                _executeButton.text = "差し替えて削除を実行";
            }
        }

        private void OnExecuteClicked()
        {
            var mode = _actionGroup?.value ?? ActionReplace;
            var action = mode switch
            {
                ActionForce => DeleteAction.ForceDelete,
                ActionArchive => DeleteAction.ArchiveOnly,
                _ => DeleteAction.ReplaceThenDelete,
            };

            ExecuteAndShowResult(action);
        }

        private void ExecuteAndShowResult(DeleteAction action)
        {
            var request = new DeleteExecutionRequest
            {
                PrimaryTargets = new List<DeleteTarget>(_targets),
                Action = action,
            };

            foreach (var dep in _analysis.Dependencies)
            {
                if (_cascadeSelected.Contains((dep.Type, dep.Id)) && !string.IsNullOrEmpty(dep.AssetPath))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<AssetDataBase>(dep.AssetPath);
                    if (asset != null)
                    {
                        request.CascadeTargets.Add(new DeleteTarget(asset, dep.Type, dep.AssetPath));
                    }
                }
            }

            if (action == DeleteAction.ReplaceThenDelete)
            {
                foreach (var t in _targets)
                {
                    if (t.Asset != null && _replacementByTargetPath.TryGetValue(t.Path, out var replacement))
                    {
                        request.ReplacementPlans.Add(new ReplacementPlan(t.Type, t.Asset.Id, replacement.Id));
                    }
                }
            }

            var result = AssetDeleteExecutionService.Execute(request);
            BuildResultView(result);
            _onCompleted?.Invoke();
        }

        // ── 結果画面 ──

        private void BuildResultView(DeleteExecutionResult result)
        {
            rootVisualElement.Clear();

            _root = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            rootVisualElement.Add(_root);

            _root.Add(SectionHeader("削除結果"));

            foreach (var r in result.Results)
            {
                var name = r.Target.Asset != null ? DependencyAssetResolver.DisplayNameOrFileName(r.Target.Asset, r.Target.Path) : r.Target.Path;
                var outcome = r.Deleted
                    ? "OS のゴミ箱へ移動しました(カタログ登録解除・Addressables エントリ削除・アイコンも削除済み)。"
                    : "参照が残っているため削除せず、アーカイブ済みの印だけ付けました。";

                _root.Add(new Label($"[{r.Target.Type}] {name}: {outcome}"));

                if (!string.IsNullOrEmpty(r.CodeReferenceWarning))
                {
                    _root.Add(new HelpBox(r.CodeReferenceWarning, HelpBoxMessageType.Warning));

                    if (r.Target.Asset != null)
                    {
                        foreach (var hit in CodeReferenceScan.FindPossibleReferenceHits(r.Target.Asset, r.Target.Path))
                        {
                            var capturedHit = hit;
                            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginLeft = 8 } };
                            row.Add(new Label($"{capturedHit.RelativePath}:{capturedHit.Line}") { style = { flexGrow = 1 } });
                            row.Add(new Button(() => OpenCodeReference(capturedHit)) { text = "開く" });
                            _root.Add(row);
                        }
                    }
                }
            }

            if (result.ChangedDataPaths.Count > 0 || result.ChangedPrefabPaths.Count > 0)
            {
                _root.Add(SectionHeader("差し替えた参照"));
                foreach (var path in result.ChangedDataPaths)
                {
                    _root.Add(new Label($"[Data、Ctrl+Z で戻せます] {path}"));
                }

                foreach (var path in result.ChangedPrefabPaths)
                {
                    _root.Add(new Label($"[Prefab、Ctrl+Z では戻せません] {path}"));
                }
            }

            if (result.RemainingSceneUsages.Count > 0)
            {
                _root.Add(SectionHeader($"手動で直す(Scene 内の参照、{result.RemainingSceneUsages.Count} 件)"));
                foreach (var usage in result.RemainingSceneUsages)
                {
                    _root.Add(UsageRow(usage, jumpable: true));
                }
            }

            if (result.SkippedCascadeStillUsed.Count > 0)
            {
                _root.Add(SectionHeader("「一緒に削除」を選んだが、他から使われているため削除しなかったもの"));
                foreach (var usage in result.SkippedCascadeStillUsed)
                {
                    _root.Add(UsageRow(usage, jumpable: true));
                }
            }

            _root.Add(SectionHeader("元に戻す手順 / 後始末"));
            _root.Add(new Label("ゴミ箱から復元した場合: ファイルは元の場所に戻りますが、カタログ・Addressables 登録は自動では戻りません。下のボタンで同期してから Validation を確認してください。"));

            var toolsRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            toolsRow.Add(new Button(() => DependencyGraphService.RebuildAll()) { text = "依存関係グラフを再構築" });
            toolsRow.Add(new Button(() => AddressablesSync.SyncAll(log: true)) { text = "Addressables 登録を同期" });
            toolsRow.Add(new Button(AssetIdGenerator.RegenerateMenuItem) { text = "ID 定数を再生成" });
            toolsRow.Add(new Button(Close) { text = "閉じる" });
            _root.Add(toolsRow);
        }

        private static void OpenCodeReference(CodeReferenceScan.Hit hit)
        {
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(hit.RelativePath);
            if (obj != null)
            {
                AssetDatabase.OpenAsset(obj, hit.Line);
            }
        }

        private static Label SectionHeader(string text)
        {
            return new Label(text) { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8 } };
        }
    }
}
