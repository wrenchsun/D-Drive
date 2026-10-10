using System;
using System.Collections.Generic;
using DDrive.Editor.Preview;
using DDrive.Runtime.Vfx;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace DDrive.EditorPrototypes
{
    // E: 旧 VFX Editor にあって E に無かった欄(機能同等 UX-0f)。いずれも PdRow を継承し、カードに普通の欄として並ぶ。

    // 出る場所の基準: スポーン先(シーン内オブジェクト)/ Path の一覧から選択 / 解決状況 / SceneView 表示。
    internal sealed class PeAnchorToolsRow : PdRow
    {
        private readonly Func<GameObject> _getAttach;
        private readonly Func<bool> _getHandle;
        private readonly ToolbarMenu _menu;
        private readonly ObjectField _attachField;
        private readonly Label _status;
        private readonly Toggle _handleToggle;
        private readonly Label _owner;
        private readonly Func<string> _describe;
        private readonly Func<string> _ownerText;
        private GameObject _lastAttach;
        private bool _built;

        public PeAnchorToolsRow(
            FieldGuideEntry entry,
            Func<GameObject> getAttach,
            Action<GameObject> setAttach,
            Func<bool> getHandle,
            Action<bool> setHandle,
            Func<string> describe,
            Func<string> ownerText,
            Action<ToolbarMenu, GameObject> fillMenu)
            : base(null, null, new[] { entry }, "出る場所の基準", "キャラクターのボーンや目印(AnchorPoint)に付けるとき、ここにキャラクターを入れます。SceneView にも位置の目印とハンドルが出ます。", FieldTier.Common, true, Build(out var af, out var menu, out var status, out var toggle, out var owner), null)
        {
            _getAttach = getAttach;
            _getHandle = getHandle;
            _describe = describe;
            _ownerText = ownerText;
            _attachField = af;
            _menu = menu;
            _status = status;
            _handleToggle = toggle;
            _owner = owner;
            NoFold = true;

            _attachField.RegisterValueChangedCallback(e => setAttach(e.newValue as GameObject));
            _handleToggle.RegisterValueChangedCallback(e =>
            {
                setHandle(e.newValue);
                Sync();
            });
            _fillMenu = fillMenu;
            _built = true;
            Sync();
        }

        private readonly Action<ToolbarMenu, GameObject> _fillMenu;

        private static VisualElement Build(out ObjectField attach, out ToolbarMenu menu, out Label status, out Toggle toggle, out Label owner)
        {
            var box = new VisualElement();
            attach = new ObjectField { objectType = typeof(GameObject), allowSceneObjects = true, tooltip = "Anchor(ボーン名 / AnchorPoint)の検索起点。キャラクターや AnchorRig を指定。未指定なら Anchor 定義のワールド座標に出る" };
            attach.AddToClassList("pe-attach");
            box.Add(attach);

            var line = new VisualElement();
            line.AddToClassList("pe-toolsline");
            menu = new ToolbarMenu { text = "Path を一覧から選ぶ", tooltip = "スポーン先の階層にあるボーン / ★AnchorPoint から Path を選ぶ" };
            menu.AddToClassList("pe-pathmenu");
            line.Add(menu);
            toggle = new Toggle("SceneView に表示") { tooltip = "OFF: この VFX の Anchor を SceneView に一切描かない。ON: 目印を描き、このウィンドウを最後に操作していれば移動 / 回転ハンドルも出す" };
            toggle.AddToClassList("pe-hidetoggle");
            line.Add(toggle);
            box.Add(line);

            status = new Label();
            status.AddToClassList("pd-dim");
            status.AddToClassList("pe-wrap");
            box.Add(status);
            owner = new Label();
            owner.AddToClassList("pd-dim");
            owner.AddToClassList("pe-wrap");
            box.Add(owner);
            return box;
        }

        public override void Sync()
        {
            if (!_built)
            {
                return;
            }

            var attach = _getAttach();
            if (_attachField.value != attach)
            {
                _attachField.SetValueWithoutNotify(attach);
            }

            if (_handleToggle.value != _getHandle())
            {
                _handleToggle.SetValueWithoutNotify(_getHandle());
            }

            if (_lastAttach != attach || _lastAttach == null && _menu.menu.MenuItems().Count == 0)
            {
                _lastAttach = attach;
                _fillMenu(_menu, attach);
            }

            SetIfChanged(_status, _describe());
            SetIfChanged(_owner, _getHandle() ? _ownerText() : "SceneView: 表示オフ");
        }

        private static void SetIfChanged(Label l, string text)
        {
            if (l.text != text)
            {
                l.text = text;
                l.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }
    }

    // Anchor アセット(AnchorId)の欄: 参照欄 + 「Anchor Editor で開く」+「埋め込みをアセット化」。
    internal sealed class PeAnchorIdRow : PdRow
    {
        private readonly Button _open;
        private readonly Button _toAsset;
        private readonly Label _label;
        private readonly Func<bool> _usesAsset;
        private readonly Func<string> _assetText;

        public PeAnchorIdRow(
            SerializedObject so,
            SerializedObject defaultSo,
            FieldGuideEntry entry,
            Func<bool> usesAsset,
            Func<string> assetText,
            Action openEditor,
            Action toAsset,
            Action changed)
            : base(so, defaultSo, new[] { entry }, "共通の出る位置(Anchor アセット)", entry.Hint, FieldTier.Common, true, Build(so, openEditor, toAsset, out var open, out var asset, out var label), changed)
        {
            _open = open;
            _toAsset = asset;
            _label = label;
            _usesAsset = usesAsset;
            _assetText = assetText;
            Sync();
        }

        private static VisualElement Build(SerializedObject so, Action openEditor, Action toAsset, out Button open, out Button asset, out Label label)
        {
            var box = new VisualElement();
            box.Add(PdRow.MakeInput(so, "AnchorId"));
            var line = new VisualElement();
            line.AddToClassList("pe-toolsline");
            open = new Button(openEditor) { text = "Anchor Editor で開く", tooltip = "この Anchor アセットを Anchor Editor で開く(位置・ランダム・ディレイの調整)" };
            open.AddToClassList("pd-btn");
            line.Add(open);
            asset = new Button(toAsset) { text = "埋め込みをアセット化", tooltip = "現在の埋め込み Anchor から Anchor アセットを作り、この VFX の AnchorId に設定する(埋め込み値は残る)" };
            asset.AddToClassList("pd-btn");
            line.Add(asset);
            box.Add(line);
            label = new Label();
            label.AddToClassList("pd-dim");
            label.AddToClassList("pe-wrap");
            box.Add(label);
            return box;
        }

        public override void Sync()
        {
            var uses = _usesAsset();
            _open.SetEnabled(uses);
            _toAsset.SetEnabled(!uses);
            var text = _assetText();
            if (_label.text != text)
            {
                _label.text = text;
                _label.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }
    }

    // LightLayerMask(uint): URP の Rendering Layer 名付きマスクで編集する。0 = Prefab の Renderer 設定を上書きしない。
    internal sealed class PeLightLayerRow : PdRow
    {
        private readonly SerializedObject _so;
        private readonly MaskField _field;
        private readonly Action _changed2;

        public PeLightLayerRow(SerializedObject so, SerializedObject defaultSo, FieldGuideEntry entry, Action changed)
            : base(so, defaultSo, new[] { entry }, entry.Label, entry.Hint, entry.Tier, false, Build(so, out var field), changed)
        {
            _so = so;
            _field = field;
            _changed2 = changed;
            _field.RegisterValueChangedCallback(e =>
            {
                var target = _so.targetObject;
                if (target == null)
                {
                    return;
                }

                Undo.RecordObject(target, "ライトレイヤーを変更");
                _so.Update();
                _so.FindProperty("LightLayerMask").uintValue = e.newValue == -1 ? uint.MaxValue : unchecked((uint)e.newValue);
                _so.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
                _changed2?.Invoke();
            });
        }

        private static VisualElement Build(SerializedObject so, out MaskField field)
        {
            var names = RenderingLayerMask.GetDefinedRenderingLayerNames();
            var choices = new List<string>(names.Length);
            for (var i = 0; i < names.Length; i++)
            {
                // MaskField の選択肢の index = ビット位置。未定義レイヤーも位置を保つため一意な名前を入れる。
                choices.Add(string.IsNullOrEmpty(names[i]) ? $"(未定義 {i})" : names[i]);
            }

            field = new MaskField(string.Empty, choices, ToInt(so.FindProperty("LightLayerMask").uintValue))
            {
                tooltip = "Rendering Layer Mask。Nothing(0) = Prefab の Renderer 設定を上書きしない",
            };
            return field;
        }

        private static int ToInt(uint v) => v == uint.MaxValue ? -1 : unchecked((int)v);

        public override void Sync()
        {
            var v = ToInt(_so.FindProperty("LightLayerMask").uintValue);
            if (_field.value != v)
            {
                _field.SetValueWithoutNotify(v);
            }
        }
    }

    // 複数同時再生(最大 8 スロット): 打撃 + 火花 + 煙のような重なりの確認。再生は実 Manager(SceneVfxPreviewDriver)。
    internal sealed class PeSlotsRow : PdRow
    {
        public const int MaxSlots = 8;

        private readonly Func<int, VfxData> _getData;
        private readonly Action<int, VfxData> _setData;
        private readonly Func<int, bool> _isPlaying;
        private readonly Action<int> _toggle;
        private readonly Button[] _buttons = new Button[MaxSlots];

        public PeSlotsRow(FieldGuideEntry entry, Func<int, VfxData> getData, Action<int, VfxData> setData, Func<int, bool> isPlaying, Action<int> toggle)
            : base(null, null, new[] { entry }, "同時に鳴らす VFX", entry.Hint, FieldTier.Advanced, true, new VisualElement(), null)
        {
            _getData = getData;
            _setData = setData;
            _isPlaying = isPlaying;
            _toggle = toggle;
            NoFold = true;
            var input = this.Q<VisualElement>(className: "pd-row__input");
            for (var i = 0; i < MaxSlots; i++)
            {
                var index = i;
                var line = new VisualElement();
                line.AddToClassList("pe-slot");
                var f = new ObjectField { objectType = typeof(VfxData), value = _getData(i) };
                f.AddToClassList("pe-slot__field");
                f.RegisterValueChangedCallback(e => _setData(index, e.newValue as VfxData));
                line.Add(f);
                var b = new Button(() =>
                {
                    _toggle(index);
                    Sync();
                }) { text = "▶", tooltip = "このスロットを再生 / 停止(スポーン先はメインと共通)" };
                b.AddToClassList("pd-btn");
                b.AddToClassList("pe-slot__btn");
                _buttons[i] = b;
                line.Add(b);
                input.Add(line);
            }
        }

        public override void Sync()
        {
            for (var i = 0; i < MaxSlots; i++)
            {
                if (_buttons[i] == null)
                {
                    continue;
                }

                var t = _isPlaying(i) ? "■" : "▶";
                if (_buttons[i].text != t)
                {
                    _buttons[i].text = t;
                }
            }
        }
    }
}
