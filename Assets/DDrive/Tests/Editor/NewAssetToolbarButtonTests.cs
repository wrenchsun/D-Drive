using System;
using System.Collections.Generic;
using System.Reflection;
using DDrive.Editor.Audio;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Inspector;
using DDrive.Editor.Materials;
using DDrive.Editor.Vfx;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Material;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // 5-15 — 各専用エディタの「＋ 新規作成」共通ヘルパー(NewAssetToolbarButton)と、
    // NewAssetDialog の種別ロック付きオーバーロードのテスト。
    public class NewAssetToolbarButtonTests
    {
        private const string TestRoot = "Assets/DDrive/Tests/Editor/TempGameData_5_15";

        [TearDown]
        public void TearDown()
        {
            // 開いたままのテスト用ウィンドウを必ず閉じる(残骸を残さない)。
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (window is AudioEditorWindow or VfxEditorWindow or MaterialEditorWindow or NewAssetDialog)
                {
                    window.Close();
                }
            }

            if (AssetDatabase.IsValidFolder(TestRoot))
            {
                AddressablesSync.RemoveEntriesUnder(TestRoot);
                AssetDatabase.DeleteAsset(TestRoot);
                AssetDatabase.SaveAssets(); // Addressables 設定の dirty を後続テストに持ち越さない
            }
        }

        [Test]
        public void GetDataTypes_ReturnsDeclaredDataEditorTypes()
        {
            CollectionAssert.AreEquivalent(new[] { typeof(SeData), typeof(BgmData) }, NewAssetToolbarButton.GetDataTypes(typeof(AudioEditorWindow)));
            CollectionAssert.AreEquivalent(new[] { typeof(VfxData) }, NewAssetToolbarButton.GetDataTypes(typeof(VfxEditorWindow)));
            CollectionAssert.AreEquivalent(new[] { typeof(MaterialData), typeof(TextureData) }, NewAssetToolbarButton.GetDataTypes(typeof(MaterialEditorWindow)));
        }

        [Test]
        public void GetDataTypes_UnknownWindowType_ReturnsEmpty()
        {
            Assert.IsEmpty(NewAssetToolbarButton.GetDataTypes(typeof(NewAssetToolbarButtonTests)));
        }

        // [DataEditor] を持つ全エディタで、少なくとも 1 つの Data 型(いずれも AssetDataBase 派生)が解決できること。
        // 「＋ 新規作成」ボタンの種別ロックが空になって全種別表示にフォールバックしてしまう(=固定に失敗する)
        // ことが無いようにする回帰テスト。
        [Test]
        public void GetDataTypes_EveryDataEditorWindow_HasAtLeastOneAssetDataType()
        {
            var missing = new List<string>();
            foreach (var windowType in TypeCache.GetTypesWithAttribute<DataEditorAttribute>())
            {
                var dataTypes = NewAssetToolbarButton.GetDataTypes(windowType);
                if (dataTypes.Count == 0)
                {
                    missing.Add(windowType.Name);
                    continue;
                }

                foreach (var dataType in dataTypes)
                {
                    Assert.IsTrue(typeof(AssetDataBase).IsAssignableFrom(dataType),
                        $"{windowType.Name} の [DataEditor] Data 型 {dataType.Name} は AssetDataBase 派生であるべきです");
                }
            }

            Assert.IsEmpty(missing, "[DataEditor] を持つのに Data 型を 1 つも解決できないウィンドウ: " + string.Join(", ", missing));
        }

        [Test]
        public void SwitchToCreated_OpensOwnerWindow_AndSetsItAsTarget()
        {
            var asset = AssetCreationService.Create(typeof(SeData), AssetType.Se, "TestSe", "Category", "SwitchTarget5015", gameDataRoot: TestRoot);
            Assert.IsNotNull(asset);

            NewAssetToolbarButton.SwitchToCreated(typeof(AudioEditorWindow), asset);

            // AudioEditorWindow.Open(AssetDataBase) が呼ばれ、既存(または新規)ウィンドウの対象が
            // 作成したアセットに切り替わっているはず。
            var window = EditorWindow.GetWindow<AudioEditorWindow>();
            Assert.IsNotNull(window);

            var targetField = typeof(AudioEditorWindow).GetField("_target", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(targetField, "AudioEditorWindow._target が見つかりません(実装が変わった場合はテストを追従させてください)");
            Assert.AreSame(asset, targetField.GetValue(window));
        }

        [Test]
        public void SwitchToCreated_WindowTypeWithoutMatchingEntry_DoesNotThrow()
        {
            var asset = AssetCreationService.Create(typeof(SeData), AssetType.Se, "TestSe", "Category", "NoEntry5015", gameDataRoot: TestRoot);
            Assert.IsNotNull(asset);

            // NewAssetToolbarButtonTests 自身は [DataEditor] のエントリを持たないので、
            // 対応が見つからず何もしない(警告ログのみ、例外にはしない)。
            Assert.DoesNotThrow(() => NewAssetToolbarButton.SwitchToCreated(typeof(NewAssetToolbarButtonTests), asset));
        }

        [Test]
        public void SwitchToCreated_NullArguments_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => NewAssetToolbarButton.SwitchToCreated(null, null));
            Assert.DoesNotThrow(() => NewAssetToolbarButton.SwitchToCreated(typeof(AudioEditorWindow), null));
        }

        [Test]
        public void DialogOpen_WithLockedTypes_RestrictsSelectionToOwnerTypes()
        {
            // NewAssetDialog.Open(Type[], Action<AssetDataBase>) を実際に開き、種別ロックが内部状態に
            // 反映されていることを確認してから必ず閉じる(ウィンドウを開くテストの流儀)。
            AssetDataBase created = null;
            NewAssetDialog.Open(new[] { typeof(VfxData) }, a => created = a);

            var window = EditorWindow.GetWindow<NewAssetDialog>();
            Assert.IsNotNull(window);

            var lockedField = typeof(NewAssetDialog).GetField("_lockedTypes", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(lockedField);
            var locked = (Type[])lockedField.GetValue(window);
            CollectionAssert.AreEqual(new[] { typeof(VfxData) }, locked);

            // 作成ボタンはまだ押していないので onCreated は呼ばれていない。
            Assert.IsNull(created);
        }
    }
}
