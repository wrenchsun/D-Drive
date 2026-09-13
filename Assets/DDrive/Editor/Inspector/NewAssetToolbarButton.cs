using System;
using System.Collections.Generic;
using DDrive.Editor.AssetBrowser;
using DDrive.Foundation.Data;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Inspector
{
    // 5-15 — 各専用エディタのツールバーに置く共通の「＋ 新規作成」ボタン。
    // [DataEditor] 属性の既存の反射処理(DataEditorAttribute / DataEditorRegistry。§8「エディターで開く」と同じ仕組み)を
    // 再利用し、「このエディタ(windowType)が対応する Data 型」を求めて NewAssetDialog をその種別に固定して開く。
    // 作成完了後は DataEditorRegistry.GetEntries(作成物の型) から windowType 自身のエントリを探し、
    // その Open(created) を呼んでこのエディタへ自動で切り替える(= Inspector の「エディターで開く」ボタンと同じ経路)。
    public static class NewAssetToolbarButton
    {
        private const string ButtonText = "＋ 新規作成";
        private const string Tooltip = "命名規則どおりの新規アセットを作成し、このエディタへ切り替える(AssetBrowser を開く必要はない)";

        // windowType に宣言されている [DataEditor] の Data 型一覧(重複除去、宣言順)。
        // DataEditorRegistry は Data 型 → エディタの向きで索引しているため、ここでは windowType 自身の
        // 属性を直接読む(逆引きの索引を別に持たず、既存の属性宣言をそのまま再利用する)。
        public static IReadOnlyList<Type> GetDataTypes(Type windowType)
        {
            var list = new List<Type>();
            if (windowType == null)
            {
                return list;
            }

            foreach (var obj in windowType.GetCustomAttributes(typeof(DataEditorAttribute), false))
            {
                if (obj is DataEditorAttribute attribute && attribute.DataType != null && !list.Contains(attribute.DataType))
                {
                    list.Add(attribute.DataType);
                }
            }

            return list;
        }

        // 作成された Data を、windowType 自身の [DataEditor] エントリ経由で開く(= そのエディタへ切り替える)。
        // 呼び出し元のウィンドウが既に閉じていても DataEditorRegistry.Entry.Open が例外を警告に変換して吸収する。
        public static void SwitchToCreated(Type windowType, AssetDataBase created)
        {
            if (windowType == null || created == null)
            {
                return;
            }

            foreach (var entry in DataEditorRegistry.GetEntries(created.GetType()))
            {
                if (entry.WindowType == windowType)
                {
                    entry.Open(created);
                    return;
                }
            }

            Debug.LogWarning($"[DDrive] {windowType.Name} に対応する [DataEditor] エントリが見つからず、作成した {created.GetType().Name} への切り替えをスキップしました。");
        }

        private static void OpenDialog(Type windowType)
        {
            var dataTypes = GetDataTypes(windowType);
            if (dataTypes.Count == 0)
            {
                Debug.LogWarning($"[DDrive] {windowType?.Name} に [DataEditor] の宣言が見つかりません。「＋ 新規作成」を無視します。");
                return;
            }

            var lockedTypes = new Type[dataTypes.Count];
            for (var i = 0; i < dataTypes.Count; i++)
            {
                lockedTypes[i] = dataTypes[i];
            }

            NewAssetDialog.Open(lockedTypes, created => SwitchToCreated(windowType, created));
        }

        // Toolbar(UnityEditor.UIElements.Toolbar)の子として置く用。既存の各エディタのツールバーはこの見た目に揃えている。
        public static ToolbarButton CreateToolbarButton(Type windowType) =>
            new(() => OpenDialog(windowType)) { text = ButtonText, tooltip = Tooltip };

        // Toolbar を持たないウィンドウで、通常の VisualElement 列に直接置く用。
        public static Button CreateButton(Type windowType) =>
            new(() => OpenDialog(windowType)) { text = ButtonText, tooltip = Tooltip };
    }
}
