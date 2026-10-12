using System.Collections.Generic;
using DDrive.Editor.Anim;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.CanvasTool;
using DDrive.Editor.Materials;
using DDrive.Editor.Ux;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Material;
using DDrive.Runtime.Model;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Model
{
    // Material スロットの欄(docs/1010 UX-9 / D-10): 1 スロット = 1 行(番号・Renderer の種類のチップ・Renderer 名 [スロット番号]・MaterialData の選び箱・開く・↺)。
    // CanvasItemList(絞り込み・件数・20 行超は ListView)に載せ、AnimSyncBox(署名が変わったときだけ作り直す)で包む。
    // 「Slot 自動収集」「元ファイル再読み込み」は旧 ModelEditorWindow と同じ ModelSlotBinder をそのまま使う。
    internal sealed partial class ModelEditor
    {
        private static readonly string[] SlotKinds = { "Skinned", "Mesh", "ほか", "見つからない" };

        private CanvasItemList _slotList;
        private AnimSyncBox _slotBox;
        private Label _slotStatus;
        private Button _slotPlacedButton;
        private Dictionary<ulong, MaterialData> _materialsById = new Dictionary<ulong, MaterialData>();
        private string _slotNote = string.Empty;

        // ── 操作の行(自動収集・再読み込み・配置中への反映) ──

        private UxRow CreateSlotToolsRow(FieldGuideEntry e)
        {
            var box = new VisualElement();
            var line = new VisualElement();
            line.AddToClassList("ux-toolsline");
            line.Add(MakeButton("Slot 自動収集", "ux-btn--primary", () => RunSlotBinder(false),
                "Prefab の Renderer を走査してスロット一覧を作り、Renderer が使っている Material から生成済みの MaterialData を割り当てる(既に割り当て済みのスロットは変更しない)"));
            line.Add(MakeButton("元ファイル再読み込み", null, () => RunSlotBinder(true),
                "Prefab が参照している FBX / Material から MaterialData(+ TextureData)を作り直してから、Slot を貼り直す。FBX を差し替え・修正した後に使う(MaterialData の固有調整は保持される)"));
            _slotPlacedButton = MakeButton("配置中のモデルに反映", null, ApplySlotsToPreview,
                "Slots の Material ID を、配置中のモデルの Renderer に実 MaterialManager 経由で適用する(Spawn 時と同じ経路)");
            line.Add(_slotPlacedButton);
            box.Add(line);
            _slotStatus = new Label(_slotNote);
            _slotStatus.AddToClassList("ux-dim");
            _slotStatus.AddToClassList("ux-note");
            box.Add(_slotStatus);
            return new AnimAuxRow(e, e.Label, e.Hint, e.Tier, box, null);
        }

        private void RunSlotBinder(bool ensureMaterials)
        {
            if (Target == null || Target.Prefab == null)
            {
                SetSlotNote("Prefab が未設定です。先に Prefab を選んでください");
                return;
            }

            var report = new ModelSlotBinder.Report();
            // ボタンから直接起こした操作なので対話的(知らないシェーダーがあれば 1 回だけ確認する。FC-15)。
            var changed = ModelSlotBinder.Rebuild(Target, ensureMaterials, report, AssetCreationService.DefaultGameDataRoot,
                interactive: UnknownShaderGuard.IsInteractiveSession());
            Debug.Log($"[DDrive] Model Slot {(ensureMaterials ? "再読み込み" : "自動収集")}: {Target.name}\n{report}");
            SetSlotNote($"{(ensureMaterials ? "元ファイル再読み込み" : "自動収集")}: {report.ToString().Split('\n')[0]}{(changed ? "" : "(変更なし)")}");

            // Binder が新規に MaterialData / TextureData を作った場合、プレビュー用 Registry が古いままだと ID が解決できず白いモデルになる。
            // Registry と共有 Material キャッシュを再走査させ、配置中なら貼り直すために配置し直す(docs/43 item12)。
            _scene?.RefreshRegistry();
            if (_scene != null && _scene.OwnsCurrent)
            {
                PlaceMain(false);
            }

            _infoSig = int.MinValue;
            RefreshAfterExternalWrite();
            _slotBox?.Rebuild();
        }

        private void ApplySlotsToPreview()
        {
            if (Target == null || Target.Slots == null || _scene == null || !_scene.OwnsCurrent)
            {
                SetSlotNote("配置中のモデルがありません。「確認用シーン」か「今のシーンに配置」で置いてください");
                return;
            }

            for (var i = 0; i < Target.Slots.Length; i++)
            {
                _scene.Models.SetMaterial(_scene.SpawnedModelHandle, i, Target.Slots[i].Material);
            }

            SetSlotNote($"配置中のモデルに {Target.Slots.Length} スロットを反映しました");
        }

        private void SetSlotNote(string text)
        {
            _slotNote = text;
            SetLabel(_slotStatus, text);
        }

        // ── スロットの一覧 ──

        private UxRow CreateSlotsRow(FieldGuideEntry e)
        {
            _slotList = new CanvasItemList("スロット", "割当済み", SlotKinds, "「Slot 自動収集」を押すと、Prefab の Renderer ごとに行ができます。", "未割当");
            var box = new AnimSyncBox(SlotSignature, RebuildSlots, null);
            box.Add(_slotList);
            _slotBox = box;
            box.Rebuild();
            return new UxVisualRow(So, DefaultSo, new[] { e }, e.Label, e.Hint, e.Tier, box, RowChanged)
            {
                NoFold = true,
                NoBulkReset = true,
                MaxIssues = 3,
                ExtraMatch = q => _slotList != null && _slotList.AnyMatch(q),
            };
        }

        private int SlotSignature()
        {
            var t = Target;
            if (t == null)
            {
                return 0;
            }

            var h = CanvasSig.Add(17, t.GetInstanceID());
            h = CanvasSig.Add(h, t.Prefab != null ? t.Prefab.GetInstanceID() : 0);
            var slots = t.Slots;
            h = CanvasSig.Add(h, slots != null ? slots.Length : 0);
            for (var i = 0; slots != null && i < slots.Length; i++)
            {
                h = CanvasSig.Add(h, slots[i].RendererPath);
                h = CanvasSig.Add(h, slots[i].SlotIndex);
                h = CanvasSig.Add(h, slots[i].Material.Value);
            }

            return h;
        }

        // 全 MaterialData を 1 回だけ集めて Id → Data の表にする(行ごとに探さない)。
        private void LoadMaterialMap()
        {
            _materialsById = new Dictionary<ulong, MaterialData>();
            foreach (var guid in AssetSearch.FindAssets("t:" + nameof(MaterialData)))
            {
                var data = AssetDatabase.LoadAssetAtPath<MaterialData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data != null && data.Id != 0 && !_materialsById.ContainsKey(data.Id))
                {
                    _materialsById[data.Id] = data;
                }
            }
        }

        private MaterialData MaterialOf(MaterialSlot slot)
        {
            if (!slot.Material.IsValid)
            {
                return null;
            }

            _materialsById.TryGetValue(slot.Material.Value, out var data);
            return data;
        }

        private static string MaterialName(MaterialData data)
            => data == null ? string.Empty : string.IsNullOrEmpty(data.DisplayName) ? data.name : data.DisplayName;

        private void RebuildSlots()
        {
            if (_slotList == null)
            {
                return;
            }

            LoadMaterialMap();
            var slots = Target != null ? Target.Slots : null;
            var prefab = Target != null ? Target.Prefab : null;
            var items = new List<CanvasListItem>(slots != null ? slots.Length : 0);
            for (var i = 0; slots != null && i < slots.Length; i++)
            {
                var index = i;
                var slot = slots[i];
                var kind = KindOf(FindRenderer(prefab, slot.RendererPath));
                var material = MaterialOf(slot);
                items.Add(new CanvasListItem
                {
                    Key = "slot:" + i,
                    Title = SlotName(slot),
                    Kind = kind,
                    Configured = slot.Material.IsValid,
                    Search = slot.RendererPath + " " + MaterialName(material),
                    Make = () => MakeSlotRow(index),
                });
            }

            _slotList.SetItems(items);
        }

        // 0 = SkinnedMeshRenderer / 1 = MeshRenderer / 2 = そのほかの Renderer / 3 = Prefab に見つからない。
        private static int KindOf(Renderer renderer)
        {
            if (renderer == null)
            {
                return 3;
            }

            return renderer is SkinnedMeshRenderer ? 0 : renderer is MeshRenderer ? 1 : 2;
        }

        // RendererPath(Prefab ルートからの相対パス。空 = ルート)の Renderer。ModelDataValidator の判定と同じ。
        private static Renderer FindRenderer(GameObject prefab, string path)
        {
            if (prefab == null)
            {
                return null;
            }

            if (string.IsNullOrEmpty(path))
            {
                return prefab.GetComponent<Renderer>();
            }

            var t = prefab.transform.Find(path);
            return t != null ? t.GetComponent<Renderer>() : null;
        }

        private static string SlotName(MaterialSlot slot)
        {
            var path = slot.RendererPath;
            var name = string.IsNullOrEmpty(path) ? "(ルート)" : path.Substring(path.LastIndexOf('/') + 1);
            return $"{name} [{slot.SlotIndex}]";
        }

        private VisualElement MakeSlotRow(int index)
        {
            var slots = Target != null ? Target.Slots : null;
            if (slots == null || index >= slots.Length)
            {
                return new VisualElement();
            }

            var slot = slots[index];
            var kind = KindOf(FindRenderer(Target.Prefab, slot.RendererPath));
            var material = MaterialOf(slot);
            var row = new VisualElement();
            row.AddToClassList("ux-fxrow");
            var head = new VisualElement();
            head.AddToClassList("ux-fxrow__head");
            head.AddToClassList("ux-trk__head");

            if (kind == 3 || !slot.Material.IsValid)
            {
                var flag = new Label("⚠") { tooltip = kind == 3 ? "RendererPath が Prefab 内で見つかりません" : "Material が未割当です" };
                flag.AddToClassList("ux-lineflag");
                head.Add(flag);
            }

            var idx = new Label("#" + (index + 1));
            idx.AddToClassList("ux-dim");
            idx.AddToClassList("ux-trk__idx");
            head.Add(idx);

            var chip = new Label(SlotKinds[kind]) { tooltip = kind == 3 ? "この RendererPath の Renderer が Prefab に無い(名前を変えた / 消した)" : "この Renderer の種類" };
            chip.AddToClassList("ux-chip");
            chip.AddToClassList("ux-fxrow__chip");
            chip.AddToClassList("ux-mdl-kind");
            chip.AddToClassList("ux-kind--" + kind);
            head.Add(chip);

            var name = new Label(SlotName(slot)) { tooltip = string.IsNullOrEmpty(slot.RendererPath) ? "Prefab のルートの Renderer" : slot.RendererPath };
            name.AddToClassList("ux-fxrow__name");
            head.Add(name);

            var field = new ObjectField { objectType = typeof(MaterialData), value = material, allowSceneObjects = false };
            field.tooltip = material != null ? MaterialName(material) : "割り当てる MaterialData";
            field.AddToClassList("ux-mdl-slotfield");
            field.RegisterValueChangedCallback(evt => SetSlotMaterial(index, evt.newValue as MaterialData));
            head.Add(field);

            var open = new Button(() => OpenSlotMaterial(index)) { text = "開く", tooltip = "この Material を Material Editor で開く" };
            open.AddToClassList("ux-btn");
            open.SetEnabled(material != null);
            head.Add(open);

            var reset = new Button(() => ResetSlotMaterial(index)) { text = "↺", tooltip = "Prefab の Renderer が使っている Material から割り当て直す(対応する MaterialData があるときだけ)" };
            reset.AddToClassList("ux-btn");
            reset.AddToClassList("ux-btn--icon");
            head.Add(reset);

            row.Add(head);
            return row;
        }

        private void OpenSlotMaterial(int index)
        {
            var slots = Target != null ? Target.Slots : null;
            if (slots == null || index >= slots.Length)
            {
                return;
            }

            var material = MaterialOf(slots[index]);
            if (material != null)
            {
                DDrive.Editor.Materials.MaterialEditor.Open(material);
            }
        }

        // 1 スロットの Material を書く(Undo 対応)。null = 未割当に戻す。
        private void SetSlotMaterial(int index, MaterialData data)
        {
            if (Target == null || So == null)
            {
                return;
            }

            So.Update();
            var slots = So.FindProperty("Slots");
            if (slots == null || index >= slots.arraySize)
            {
                return;
            }

            var material = slots.GetArrayElementAtIndex(index).FindPropertyRelative("Material");
            material.FindPropertyRelative("value").ulongValue = data != null ? data.Id : 0UL;
            material.FindPropertyRelative("type").enumValueIndex = data != null ? (int)AssetType.Material : 0;
            So.ApplyModifiedProperties();
            EditorUtility.SetDirty(Target);
            RowChanged();
            _slotBox?.Rebuild();
        }

        private void ResetSlotMaterial(int index)
        {
            var slots = Target != null ? Target.Slots : null;
            if (slots == null || index >= slots.Length)
            {
                return;
            }

            var slot = slots[index];
            var renderer = FindRenderer(Target.Prefab, slot.RendererPath);
            UnityEngine.Material source = null;
            if (renderer != null)
            {
                var mats = renderer.sharedMaterials;
                source = slot.SlotIndex >= 0 && slot.SlotIndex < mats.Length ? mats[slot.SlotIndex] : null;
            }

            var found = source != null ? ModelSlotBinder.FindMaterialData(source) : null;
            if (found == null)
            {
                SetSlotNote($"{SlotName(slot)}: Prefab の Material に対応する MaterialData が見つからないので変更しません");
                return;
            }

            SetSlotMaterial(index, found);
            SetSlotNote($"{SlotName(slot)}: Prefab の Material から '{MaterialName(found)}' を割り当て直しました");
        }
    }
}
