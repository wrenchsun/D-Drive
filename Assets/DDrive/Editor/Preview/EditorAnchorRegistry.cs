using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Loader;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Anchoring;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Preview
{
    // エディタのプレビュー用 Registry に、プロジェクト内の全 AnchorData を登録して同期解決できるようにする
    // ([21_anchor_spec.md] §3.6)。プレビューは実 Manager を駆動する(ADR-4)ため、VfxData.AnchorId /
    // SeData.AnchorId や AnchorEditor の試し出し(anchorOverride)が、ランタイムと同じ AnchorChain で解決される。
    // 未保存の編集も同じアセットインスタンスを見るためそのまま反映される。
    public static class EditorAnchorRegistry
    {
        private sealed class AssetDatabaseLoader : IAssetLoader
        {
            public UniTask<T> LoadAsync<T>(string address, CancellationToken ct) where T : UnityEngine.Object
                => UniTask.FromResult(AssetDatabase.LoadAssetAtPath<T>(address));

            public void Release(string address)
            {
            }

            public UniTask PreloadAsync(IEnumerable<string> addresses, IProgress<float> progress) => UniTask.CompletedTask;
        }

        // 全 AnchorData を登録・ロード済みにした Registry を作る(EditMode 専用。ロードは同期完了する)。
        public static AssetRegistry Build()
        {
            var registry = new AssetRegistry(new AssetDatabaseLoader());
            Refresh(registry);
            return registry;
        }

        // アセットの追加・削除後に呼び直す(既知 ID は上書きされるだけなので重複しない)。
        // AnchorData に加え、配置セット(AnchorGroupData)とそこから参照される VfxData / SeData も登録する
        // ([22] §3.5: AnchorGroupPlanner が ID から VFX/SE を引くため)。
        public static void Refresh(AssetRegistry registry)
        {
            if (registry == null)
            {
                return;
            }

            var entries = new List<CatalogEntry>();
            var loads = new List<(ulong id, System.Type type)>();
            Collect<AnchorData>(AssetType.Anchor, entries, loads);
            Collect<AnchorGroupData>(AssetType.AnchorGroup, entries, loads);
            Collect<DDrive.Runtime.Vfx.VfxData>(AssetType.Vfx, entries, loads);
            Collect<DDrive.Runtime.Audio.SeData>(AssetType.Se, entries, loads);
            Collect<DDrive.Runtime.Audio.BgmData>(AssetType.Bgm, entries, loads);
            Collect<DDrive.Runtime.Anim.AnimData>(AssetType.Anim, entries, loads);
            Collect<DDrive.Runtime.Model.ModelData>(AssetType.Model, entries, loads);
            // 2026-09-14(5-4): PresentationEditor の統合プレビューが CameraShake/Haptic トラックを ID 解決
            // (ResolveOrPlaceholder)できるように登録を追加(ShakeEditor/HapticsEditor 単体は ID を経由しないため
            // 未登録でも動いていたが、Presentation 経由では登録が無いと必ず Placeholder になっていた)。
            Collect<DDrive.Runtime.CameraShake.CameraShakeData>(AssetType.Shake, entries, loads);
            Collect<DDrive.Runtime.Haptics.HapticsData>(AssetType.Haptics, entries, loads);
            Collect<DDrive.Runtime.Material.MaterialData>(AssetType.Material, entries, loads);
            Collect<DDrive.Runtime.Material.TextureData>(AssetType.Texture, entries, loads);
            Collect<DDrive.Runtime.Prefab.PrefabData>(AssetType.Prefab, entries, loads);
            Collect<DDrive.Runtime.Ui.CanvasData>(AssetType.Canvas, entries, loads);
            Collect<DDrive.Runtime.Ui.ButtonSkinData>(AssetType.ControlSkin, entries, loads);
            Collect<DDrive.Runtime.Ui.UiTweenData>(AssetType.UiTween, entries, loads);
            // 2026-09-18(6-10a): PresentationEditor の統合プレビューが TrackKind.Timeline から CutsceneData を
            // ID 解決できるように登録する(Shake/Haptics と同じ理由。未登録だと Presentation 経由で常に Placeholder になる)。
            Collect<DDrive.Runtime.Cutscene.CutsceneData>(AssetType.Cutscene, entries, loads);

            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.hideFlags = HideFlags.HideAndDontSave;
            catalog.SetEntries(entries);
            registry.RegisterCatalogAsync(catalog).GetAwaiter().GetResult();
            foreach (var (id, type) in loads)
            {
                if (type == typeof(AnchorData)) registry.ResolveAsync<AnchorData>(id).GetAwaiter().GetResult();
                else if (type == typeof(AnchorGroupData)) registry.ResolveAsync<AnchorGroupData>(id).GetAwaiter().GetResult();
                else if (type == typeof(DDrive.Runtime.Vfx.VfxData)) registry.ResolveAsync<DDrive.Runtime.Vfx.VfxData>(id).GetAwaiter().GetResult();
                else if (type == typeof(DDrive.Runtime.Audio.BgmData)) registry.ResolveAsync<DDrive.Runtime.Audio.BgmData>(id).GetAwaiter().GetResult();
                else if (type == typeof(DDrive.Runtime.Anim.AnimData)) registry.ResolveAsync<DDrive.Runtime.Anim.AnimData>(id).GetAwaiter().GetResult();
                else if (type == typeof(DDrive.Runtime.Model.ModelData)) registry.ResolveAsync<DDrive.Runtime.Model.ModelData>(id).GetAwaiter().GetResult();
                else if (type == typeof(DDrive.Runtime.CameraShake.CameraShakeData)) registry.ResolveAsync<DDrive.Runtime.CameraShake.CameraShakeData>(id).GetAwaiter().GetResult();
                else if (type == typeof(DDrive.Runtime.Haptics.HapticsData)) registry.ResolveAsync<DDrive.Runtime.Haptics.HapticsData>(id).GetAwaiter().GetResult();
                else if (type == typeof(DDrive.Runtime.Material.MaterialData)) registry.ResolveAsync<DDrive.Runtime.Material.MaterialData>(id).GetAwaiter().GetResult();
                else if (type == typeof(DDrive.Runtime.Material.TextureData)) registry.ResolveAsync<DDrive.Runtime.Material.TextureData>(id).GetAwaiter().GetResult();
                else if (type == typeof(DDrive.Runtime.Prefab.PrefabData)) registry.ResolveAsync<DDrive.Runtime.Prefab.PrefabData>(id).GetAwaiter().GetResult();
                else if (type == typeof(DDrive.Runtime.Ui.CanvasData)) registry.ResolveAsync<DDrive.Runtime.Ui.CanvasData>(id).GetAwaiter().GetResult();
                else if (type == typeof(DDrive.Runtime.Ui.ButtonSkinData)) registry.ResolveAsync<DDrive.Runtime.Ui.ButtonSkinData>(id).GetAwaiter().GetResult();
                else if (type == typeof(DDrive.Runtime.Ui.UiTweenData)) registry.ResolveAsync<DDrive.Runtime.Ui.UiTweenData>(id).GetAwaiter().GetResult();
                else if (type == typeof(DDrive.Runtime.Cutscene.CutsceneData)) registry.ResolveAsync<DDrive.Runtime.Cutscene.CutsceneData>(id).GetAwaiter().GetResult();
                else registry.ResolveAsync<DDrive.Runtime.Audio.SeData>(id).GetAwaiter().GetResult();
            }

            UnityEngine.Object.DestroyImmediate(catalog);
        }

        private static void Collect<T>(AssetType type, List<CatalogEntry> entries, List<(ulong, System.Type)> loads) where T : DDrive.Foundation.Data.AssetDataBase
        {
            foreach (var guid in AssetSearch.FindAssets("t:" + typeof(T).Name))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset == null || asset.Id == 0)
                {
                    continue;
                }

                entries.Add(new CatalogEntry { Id = asset.Id, Type = type, Address = path });
                loads.Add((asset.Id, typeof(T)));
            }
        }

        public static AnchorGroupData FindGroup(ulong id)
        {
            if (id == 0)
            {
                return null;
            }

            foreach (var guid in AssetSearch.FindAssets("t:" + nameof(AnchorGroupData)))
            {
                var asset = AssetDatabase.LoadAssetAtPath<AnchorGroupData>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null && asset.Id == id)
                {
                    return asset;
                }
            }

            return null;
        }

        // ID → アセット(エディタ表示用。Registry を持たない場所から使う)。
        public static AnchorData Find(ulong id)
        {
            if (id == 0)
            {
                return null;
            }

            foreach (var guid in AssetSearch.FindAssets("t:" + nameof(AnchorData)))
            {
                var asset = AssetDatabase.LoadAssetAtPath<AnchorData>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null && asset.Id == id)
                {
                    return asset;
                }
            }

            return null;
        }
    }
}
