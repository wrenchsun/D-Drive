using System.Collections.Generic;
using System.Reflection;
using DDrive.Foundation.Data;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Inspector
{
    // [09_editor_tools.md] §8 — 全 Data アセット共通の Inspector。最上部に「〜で開く」ボタン(DataEditorRegistry)を出し、
    // その下は既定の描画。種別ごとに独自 Inspector を作る場合はこのクラスを継承し、先頭で DrawOpenEditorHeader() を呼ぶ
    // (SeDataEditor 参照)。editorForChildClasses=true なので、今後追加される Data 種別にも自動で適用される。
    [CustomEditor(typeof(AssetDataBase), true)]
    [CanEditMultipleObjects]
    public class AssetDataInspector : UnityEditor.Editor
    {
        // U-14(2026-09-17): 本文を IMGUI(DrawDefaultInspector)から UI Toolkit に切り替えた。
        //
        // 原因: ValueDefDrawer([17] §5)は 2026-07-27 に CreatePropertyGUI(UI Toolkit)専用へ書き直されていて
        // OnGUI を持たない。IMGUI の Inspector から描かれると Unity は PropertyDrawer.OnGUI の既定実装に落ち、
        // 「No GUI Implementation」というラベルだけを出す。BgmData の Fade In / Fade Out がまさにこれで
        // (同じ理由で CameraShakeData / HapticsData / Anim2DData / MaterialData / UiTweenData なども同症状)、
        // Inspector からフェードを編集できない状態だった。
        //
        // 直し方の選択: ValueDefDrawer に IMGUI 実装を足し直す案は採らない。手動 Rect + GetPropertyHeight の
        // IMGUI 版は AnimationCurve のカーブエディタを開くとクラッシュする既知の不具合があり、それが
        // UI Toolkit へ書き直した理由そのもの(ValueDefDrawer の冒頭コメント参照)。そこで、描く側である
        // この共通 Inspector を UI Toolkit にして CreatePropertyGUI が使われるようにする。
        // IMGUI の PropertyDrawer(AssetIdDrawer)は UI Toolkit の PropertyField が自動で IMGUIContainer に
        // 包んでくれるため、そのまま動く。
        //
        // 派生クラスが OnInspectorGUI を上書きしている場合(SeDataEditor)は null を返して従来の IMGUI 経路に戻す。
        // Unity は CreateInspectorGUI が null のとき OnInspectorGUI にフォールバックする。
        public override VisualElement CreateInspectorGUI()
        {
            if (OverridesOnInspectorGui(GetType()))
            {
                return null;
            }

            var root = new VisualElement();

            // ヘッダー(「〜で開く」/ バージョン / アイコン行 / 仕様書を開く)は IMGUI 実装のままなので包んで載せる。
            root.Add(new IMGUIContainer(DrawOpenEditorHeader));

            InspectorElement.FillDefaultInspector(root, serializedObject, this);
            ApplyReadOnlyFields(root, target as AssetDataBase);
            return root;
        }

        public override void OnInspectorGUI()
        {
            DrawOpenEditorHeader();

            // U-11(2026-09-17、[39_usability_fixes_2026-09-17.md]) — [InspectorReadOnly] を付けたフィールド
            // (Id/Version/Author/UpdatedAt 等)は DrawDefaultInspector() 相当の描画をそのまま使わず、
            // 1 プロパティずつ disabled 判定して描く(UI Toolkit 側の ApplyReadOnlyFields と同じ判定基準)。
            var readOnlyNames = GetReadOnlyFieldNames(target?.GetType());
            serializedObject.Update();

            var property = serializedObject.GetIterator();
            var enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;
                var disabled = property.propertyPath == "m_Script" || readOnlyNames.Contains(property.name);
                using (new EditorGUI.DisabledScope(disabled))
                {
                    EditorGUILayout.PropertyField(property, true);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        // U-11 — [InspectorReadOnly] を付けたフィールドを UI Toolkit の PropertyField 側で無効化する。
        // FillDefaultInspector が作った PropertyField は bindingPath == フィールド名になる(通常の public
        // フィールドはバッキングフィールド名を持たないため)。
        private static void ApplyReadOnlyFields(VisualElement root, AssetDataBase data)
        {
            if (data == null)
            {
                return;
            }

            var readOnlyNames = GetReadOnlyFieldNames(data.GetType());
            if (readOnlyNames.Count == 0)
            {
                return;
            }

            root.Query<PropertyField>().ForEach(field =>
            {
                if (!string.IsNullOrEmpty(field.bindingPath) && readOnlyNames.Contains(field.bindingPath))
                {
                    field.SetEnabled(false);
                }
            });
        }

        // dataType(派生を含む、AssetDataBase まで遡る)に [InspectorReadOnly] が付いたフィールド名の集合。
        // TypeCache 相当の使い切りではないので型ごとにキャッシュする(Inspector は選択が変わるたびに再構築されるため)。
        private static readonly Dictionary<System.Type, HashSet<string>> ReadOnlyFieldNamesByType = new();
        private static readonly HashSet<string> EmptyFieldNames = new();

        private static HashSet<string> GetReadOnlyFieldNames(System.Type dataType)
        {
            if (dataType == null || !typeof(AssetDataBase).IsAssignableFrom(dataType))
            {
                return EmptyFieldNames;
            }

            if (ReadOnlyFieldNamesByType.TryGetValue(dataType, out var cached))
            {
                return cached;
            }

            var names = new HashSet<string>();
            for (var t = dataType; t != null && typeof(AssetDataBase).IsAssignableFrom(t); t = t.BaseType)
            {
                foreach (var field in t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (field.GetCustomAttribute<InspectorReadOnlyAttribute>() != null)
                    {
                        names.Add(field.Name);
                    }
                }
            }

            ReadOnlyFieldNamesByType[dataType] = names;
            return names;
        }

        // 「〜で開く」ボタン列 + バージョン表示行(6-3) + アイコン行(フォルダから選択 / シーンから作成)。
        protected void DrawOpenEditorHeader()
        {
            if (targets.Length == 1)
            {
                DataEditorHeader.Draw(target as AssetDataBase);
                VersionStampGui.Draw(target as AssetDataBase);
                AssetIconGui.Draw(target as AssetDataBase);
                SpecUrlGui.Draw(target as AssetDataBase);
            }
        }

        // [09_editor_tools.md] §8.2 — Project ウィンドウのグリッド表示サムネイル(5-10)。
        // Icon が設定されていれば要求サイズに縮小して返す(全 Data 型共通。種別独自の Inspector も本クラスを
        // 継承していれば自動で効く。SeDataEditor 参照)。未設定なら既定の動作(スクリプトアイコン)に委ねる。
        public override Texture2D RenderStaticPreview(string assetPath, Object[] subAssets, int width, int height)
        {
            var icon = (target as AssetDataBase)?.Icon;
            return icon != null
                ? AssetIconService.ScaleForPreview(icon, width, height)
                : base.RenderStaticPreview(assetPath, subAssets, width, height);
        }

        // 派生クラスが独自の IMGUI 描画を持っているか(このクラス自身の実装かどうかで判定する)。
        private static bool OverridesOnInspectorGui(System.Type editorType)
        {
            // GetMethod は「最も派生した実装」の MethodInfo を返す。DeclaringType がこのクラスなら上書きなし。
            var method = editorType.GetMethod(nameof(OnInspectorGUI), BindingFlags.Public | BindingFlags.Instance);
            return method != null && method.DeclaringType != typeof(AssetDataInspector);
        }
    }
}
