using System;
using DDrive.Editor.Materials;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Anim2D;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Material;
using DDrive.Runtime.Model;
using DDrive.Runtime.Prefab;
using DDrive.Runtime.Ui;
using DDrive.Runtime.Vfx;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Import
{
    // [11_tasks.md] 5-11 実装メモ(2026-09-14) — 各種別の「元ファイル → Data」対応表。
    // 元ファイルの参照フィールド: Se=Clips[0] / Bgm=LoopBody / Texture=Texture / Model・Prefab・Canvas・Vfx=Prefab(GameObject) /
    // Anim・Anim2D=Clip(AnimationClip)。Anim2D は AnimData を継承しているため Anim と同じ Clip 割り当てのみ行い、
    // DirectionClips(方向別スプライトアニメ)は既存の Anim2DEditor(3-11/3-12)に委ねる(要判断は docs/28 参照)。
    internal sealed class SeImportHandler : IImportRuleHandler
    {
        public string TypeFolder => "Se";
        public AssetType Target => AssetType.Se;
        public Type DataType => typeof(SeData);
        public string[] Extensions => new[] { ".wav", ".mp3", ".ogg", ".aiff", ".aif" };
        public string IdentifierFallback => "Se";

        public UnityEngine.Object LoadSource(string assetPath) => AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);

        public void Configure(AssetDataBase data, UnityEngine.Object source, string assetPath)
        {
            ((SeData)data).Clips = new[] { (AudioClip)source };
        }

    }

    internal sealed class BgmImportHandler : IImportRuleHandler
    {
        public string TypeFolder => "Bgm";
        public AssetType Target => AssetType.Bgm;
        public Type DataType => typeof(BgmData);
        public string[] Extensions => new[] { ".wav", ".mp3", ".ogg", ".aiff", ".aif" };
        public string IdentifierFallback => "Bgm";

        public UnityEngine.Object LoadSource(string assetPath) => AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);

        public void Configure(AssetDataBase data, UnityEngine.Object source, string assetPath)
        {
            ((BgmData)data).LoopBody = (AudioClip)source;
        }

    }

    internal sealed class TextureImportHandler : IImportRuleHandler
    {
        public string TypeFolder => "Texture";
        public AssetType Target => AssetType.Texture;
        public Type DataType => typeof(TextureData);
        public string[] Extensions => new[] { ".png", ".jpg", ".jpeg", ".tga", ".psd", ".tif", ".tiff", ".exr", ".bmp" };
        public string IdentifierFallback => "Tex";

        public UnityEngine.Object LoadSource(string assetPath) => AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);

        public void Configure(AssetDataBase data, UnityEngine.Object source, string assetPath)
        {
            var tex = (TextureData)data;
            tex.Texture = (Texture2D)source;

            // [06_material_texture.md] B-3 の命名規約(TextureImportProfile)を Usage/Channel の既定値として再利用する
            // (3-8 の既存規約と食い違わないように。規約に該当しなければ TextureData の既定値 = Model/Albedo のまま)。
            var profile = TextureImportProfile.FindOrDefault();
            if (profile != null && profile.TryMatch(assetPath, out var rule))
            {
                tex.Channel = rule.Channel;
                tex.Usage = rule.Usage;
            }
        }

    }

    internal sealed class ModelImportHandler : IImportRuleHandler
    {
        public string TypeFolder => "Model";
        public AssetType Target => AssetType.Model;
        public Type DataType => typeof(ModelData);
        public string[] Extensions => new[] { ".fbx" };
        public string IdentifierFallback => "Model";

        public UnityEngine.Object LoadSource(string assetPath) => AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);

        public void Configure(AssetDataBase data, UnityEngine.Object source, string assetPath)
        {
            var model = (ModelData)data;
            model.Prefab = (GameObject)source;

            // Slots も同時に埋める(U-2、2026-09-17)。FBX の MaterialData は MayaModelPostprocessor が
            // 先に(同じ delayCall 列の手前で)作っているので、ここでは探して結び付けるだけ = 新しいアセットは作らない
            // (configure はまだ AssetDatabase.CreateAsset の前なので、ここでアセットを作らない)。
            // 未解決のスロットは None のまま残り、ModelEditor の「元ファイルを再読み込み」で作り直せる。
            var report = new DDrive.Editor.Model.ModelSlotBinder.Report();
            model.Slots = DDrive.Editor.Model.ModelSlotBinder.BuildSlots(model.Prefab, null, report);
            if (report.Unresolved > 0)
            {
                Debug.LogWarning($"[DDrive] ImportRule: {assetPath} の Material スロット {report.Unresolved}/{report.Slots} 件は MaterialData が見つかりませんでした"
                                 + "(Model Editor の「元ファイルを再読み込み」で生成できます)。");
            }
        }

    }

    // Anim / Anim2D 共通: .anim はそのまま、.fbx は埋め込みの AnimationClip サブアセットの先頭 1 本
    // (Unity が自動生成する "__preview__" は除く)を使う。複数テイクを含む FBX は最初の 1 本のみを取り込む
    // (要判断。docs/28 参照)。
    internal static class AnimSourceLoader
    {
        public static AnimationClip Load(string assetPath)
        {
            var ext = System.IO.Path.GetExtension(assetPath).ToLowerInvariant();
            if (ext == ".anim")
            {
                return AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            }

            foreach (var sub in AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath))
            {
                if (sub is AnimationClip clip && !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                {
                    return clip;
                }
            }

            return null;
        }
    }

    internal sealed class AnimImportHandler : IImportRuleHandler
    {
        public string TypeFolder => "Anim";
        public AssetType Target => AssetType.Anim;
        public Type DataType => typeof(AnimData);
        public string[] Extensions => new[] { ".anim", ".fbx" };
        public string IdentifierFallback => "Anim";

        public UnityEngine.Object LoadSource(string assetPath) => AnimSourceLoader.Load(assetPath);

        public void Configure(AssetDataBase data, UnityEngine.Object source, string assetPath)
        {
            ((AnimData)data).Clip = (AnimationClip)source;
        }

    }

    internal sealed class Anim2DImportHandler : IImportRuleHandler
    {
        public string TypeFolder => "Anim2D";
        public AssetType Target => AssetType.Anim2D;
        public Type DataType => typeof(Anim2DData);
        public string[] Extensions => new[] { ".anim", ".fbx" };
        public string IdentifierFallback => "Anim2D";

        public UnityEngine.Object LoadSource(string assetPath) => AnimSourceLoader.Load(assetPath);

        public void Configure(AssetDataBase data, UnityEngine.Object source, string assetPath)
        {
            // Anim2DData : AnimData。Clip は基底の実体フィールド、Directions/DirectionClips は
            // Anim2DEditor(3-11/3-12)側でスプライトから組み立てる運用のため触らない。
            ((AnimData)data).Clip = (AnimationClip)source;
        }

    }

    internal sealed class PrefabImportHandler : IImportRuleHandler
    {
        public string TypeFolder => "Prefab";
        public AssetType Target => AssetType.Prefab;
        public Type DataType => typeof(PrefabData);
        public string[] Extensions => new[] { ".prefab" };
        public string IdentifierFallback => "Prefab";

        public UnityEngine.Object LoadSource(string assetPath) => AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);

        public void Configure(AssetDataBase data, UnityEngine.Object source, string assetPath)
        {
            ((PrefabData)data).Prefab = (GameObject)source;
        }

    }

    internal sealed class CanvasImportHandler : IImportRuleHandler
    {
        public string TypeFolder => "Canvas";
        public AssetType Target => AssetType.Canvas;
        public Type DataType => typeof(CanvasData);
        public string[] Extensions => new[] { ".prefab" };
        public string IdentifierFallback => "Canvas";

        public UnityEngine.Object LoadSource(string assetPath) => AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);

        public void Configure(AssetDataBase data, UnityEngine.Object source, string assetPath)
        {
            ((CanvasData)data).Prefab = (GameObject)source;
        }

    }

    internal sealed class VfxImportHandler : IImportRuleHandler
    {
        public string TypeFolder => "Vfx";
        public AssetType Target => AssetType.Vfx;
        public Type DataType => typeof(VfxData);
        public string[] Extensions => new[] { ".prefab" };
        public string IdentifierFallback => "Vfx";

        public UnityEngine.Object LoadSource(string assetPath) => AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);

        public void Configure(AssetDataBase data, UnityEngine.Object source, string assetPath)
        {
            ((VfxData)data).Prefab = (GameObject)source;
        }

    }
}
