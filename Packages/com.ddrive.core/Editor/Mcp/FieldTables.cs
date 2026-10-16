using System;
using System.Collections.Generic;
using System.Reflection;
using DDrive.Foundation.Extension;
using DDrive.Foundation.Identity;
using UnityEngine;

namespace DDrive.Editor.Mcp
{
    // [1002_ddrive_mcp.md] §4.5 MCP-3(2026-10-07) — AssetType ごとの「Data クラス」と「既定で返す欄」の表。
    // ddrive_asset_get の既定の fields、ddrive_asset_create の type → Data クラスの解決に使う。
    // 欄名はシリアライズ名そのまま(SerializedProperty のパス。m_ を付けない)。
    //
    // 既定の欄は 10 個以内 = 共通 4 欄(DisplayName / Category / Tags / Description)+ 種別の主要欄(6 個まで)。
    // 「デザイナーがまず触る欄」を選んだもの。全欄は fields:"*" で読む。表の欄名が実在することは FieldTablesTests が固定する。
    public static class FieldTables
    {
        public const int MaxDefaultFields = 10;

        // 共通欄の中で既定で返すもの(AssetDataBase)。
        public static readonly string[] CommonFields = { "DisplayName", "Category", "Tags", "Description" };

        public sealed class Entry
        {
            public AssetType Type { get; }
            public Type[] DataClasses { get; }
            // DataClasses と同じ並び(クラスごとに主要欄が違う種別 = ControlSkin のため、クラス単位で持つ)。
            public string[][] MainFields { get; }

            // X-5(2026-10-16): Type が Extension のときの種別キー(組み込み種別は空)。type 引数に KindKey("Sample" など)を渡すと、
            // IAssetKindProvider から作ったこの形の表になる(欄は Data クラスが宣言した公開欄の先頭 6 個)。
            public string KindKey { get; }

            public Entry(AssetType type, Type[] dataClasses, string[][] mainFields, string kindKey = "")
            {
                Type = type;
                DataClasses = dataClasses;
                MainFields = mainFields;
                KindKey = kindKey ?? string.Empty;
            }

            public bool HasMultipleClasses => DataClasses.Length > 1;
        }

        private static readonly Entry[] Entries =
        {
            Single(AssetType.Se, typeof(DDrive.Runtime.Audio.SeData),
                "Clips", "Volume", "PitchRange", "Loop", "Spatial", "MaxConcurrent"),
            Single(AssetType.Bgm, typeof(DDrive.Runtime.Audio.BgmData),
                "Intro", "LoopBody", "Volume", "FadeIn", "FadeOut", "Bpm"),
            Single(AssetType.Vfx, typeof(DDrive.Runtime.Vfx.VfxData),
                "Prefab", "AnchorId", "LifeMode", "Duration", "FadeOutSec", "Render"),
            Single(AssetType.Anim, typeof(DDrive.Runtime.Anim.AnimData),
                "Clip", "StateName", "Layer", "Loop", "DefaultCrossFade", "Mask"),
            Single(AssetType.Anim2D, typeof(DDrive.Runtime.Anim2D.Anim2DData),
                "Clip", "StateName", "Loop", "DefaultCrossFade", "Directions", "DirectionClips"),
            Single(AssetType.Material, typeof(DDrive.Runtime.Material.MaterialData),
                "Shader", "Common", "RenderQueueOffset", "RenderingLayerMask", "EnabledKeywords"),
            Single(AssetType.Texture, typeof(DDrive.Runtime.Material.TextureData),
                "Texture", "Usage", "Sprite", "AllowScale", "SliceBorder", "Channel"),
            Single(AssetType.Canvas, typeof(DDrive.Runtime.Ui.CanvasData),
                "Prefab", "Layer", "SortOffset", "CloseOnBack", "ModalBlocksInput", "PauseGameWhileOpen"),
            Single(AssetType.Prefab, typeof(DDrive.Runtime.Prefab.PrefabData),
                "Prefab", "Kind", "GameplayTags", "CollisionLayer", "Lod"),
            Single(AssetType.Presentation, typeof(DDrive.Runtime.Presentation.PresentationData),
                "TotalDuration", "Interruptible", "PredictLocal", "Tracks"),
            Single(AssetType.Shake, typeof(DDrive.Runtime.CameraShake.CameraShakeData),
                "Pattern", "PosAmplitude", "RotAmplitude", "Frequency", "Envelope", "Space"),
            Single(AssetType.Haptics, typeof(DDrive.Runtime.Haptics.HapticsData),
                "LowFreq", "HighFreq", "Priority", "LocalPlayerOnly"),
            Single(AssetType.UiTween, typeof(DDrive.Runtime.Ui.UiTweenData),
                "TotalDuration", "Tracks"),
            Single(AssetType.Model, typeof(DDrive.Runtime.Model.ModelData),
                "Prefab", "Slots", "DefaultAnimation", "Avatar", "RenderLayer", "Lod"),
            Single(AssetType.Anchor, typeof(DDrive.Runtime.Anchoring.AnchorData),
                "Parent", "Space", "Path", "LocalOffset", "LocalEuler", "LocalScale"),
            Single(AssetType.AnchorGroup, typeof(DDrive.Runtime.Anchoring.AnchorGroupData),
                "OriginAnchorId", "Layout", "GridCountX", "GridCountY", "CircleCount", "CircleRadius"),
            new Entry(
                AssetType.ControlSkin,
                new[] { typeof(DDrive.Runtime.Ui.ButtonSkinData), typeof(DDrive.Runtime.Ui.SliderSkinData) },
                new[]
                {
                    new[] { "HoverSe", "ClickSe", "LongPressSe", "DeniedSe" },
                    new[] { "GrabSe", "ReleaseSe", "NotchSe", "LimitSe", "DeniedSe" },
                }),
            Single(AssetType.Cutscene, typeof(DDrive.Runtime.Cutscene.CutsceneData),
                "Timeline", "Origin", "FrameRate", "Skip", "Wrap", "LockInput"),
        };

        private static Entry Single(AssetType type, Type dataClass, params string[] mainFields)
            => new Entry(type, new[] { dataClass }, new[] { mainFields });

        public static IReadOnlyList<Entry> All => Entries;

        public static bool TryGet(AssetType type, out Entry entry)
        {
            for (var i = 0; i < Entries.Length; i++)
            {
                if (Entries[i].Type == type)
                {
                    entry = Entries[i];
                    return true;
                }
            }

            entry = null;
            return false;
        }

        // 種別名(大小文字を区別しない)。数字・None・表に無い名前は false。
        public static bool TryParseType(string text, out AssetType type)
        {
            type = AssetType.None;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var trimmed = text.Trim();
            for (var i = 0; i < Entries.Length; i++)
            {
                if (string.Equals(Entries[i].Type.ToString(), trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    type = Entries[i].Type;
                    return true;
                }
            }

            return false;
        }

        // 種別名(組み込み)か種別キー(Extension。IAssetKindProvider.KindKey / DisplayName、大小文字無視)から表を引く。
        public static bool TryResolve(string text, out Entry entry)
        {
            if (TryParseType(text, out var type) && TryGet(type, out entry))
            {
                return true;
            }

            entry = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var trimmed = text.Trim();
            foreach (var provider in AssetKindRegistry.All)
            {
                if (provider.DataType == null)
                {
                    continue;
                }

                if (string.Equals(provider.KindKey, trimmed, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(provider.DisplayName, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    entry = new Entry(AssetType.Extension, new[] { provider.DataType }, new[] { MainFieldsOf(provider.DataType) }, provider.KindKey);
                    return true;
                }
            }

            return false;
        }

        // Extension の Data クラスが宣言した(AssetDataBase より下の)シリアライズされる公開欄の先頭 6 個。
        private static string[] MainFieldsOf(Type dataType)
        {
            var names = new List<string>();
            var chain = new List<Type>();
            for (var t = dataType; t != null && t != typeof(DDrive.Foundation.Data.AssetDataBase); t = t.BaseType)
            {
                chain.Insert(0, t);
            }

            foreach (var t in chain)
            {
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (f.IsNotSerialized || f.GetCustomAttribute<HideInInspector>() != null)
                    {
                        continue;
                    }

                    names.Add(f.Name);
                    if (names.Count >= 6)
                    {
                        return names.ToArray();
                    }
                }
            }

            return names.ToArray();
        }

        // 種別名の必須引数を解く。不正なら invalid_params(種別名の一覧つき)。
        public static Entry RequireType(string text)
        {
            if (!TryResolve(text, out var entry))
            {
                throw new McpToolError(
                    McpGuard.CodeInvalidParams,
                    $"type '{text}' が不正です({TypeNames()})");
            }

            return entry;
        }

        // 組み込みの種別名 + 発見された Extension の種別キー。
        public static string TypeNames()
        {
            var names = new List<string>(Entries.Length);
            for (var i = 0; i < Entries.Length; i++)
            {
                names.Add(Entries[i].Type.ToString());
            }

            foreach (var provider in AssetKindRegistry.All)
            {
                names.Add(provider.KindKey);
            }

            return string.Join(", ", names);
        }

        // asset が entry の種別(Extension なら KindKey も)に属するか。
        public static bool Matches(Entry entry, DDrive.Foundation.Data.AssetDataBase asset)
        {
            if (asset == null)
            {
                return false;
            }

            var attr = asset.GetType().GetCustomAttribute<AssetIdDefinitionAttribute>();
            if ((attr != null ? attr.Type : AssetType.None) != entry.Type)
            {
                return false;
            }

            return string.IsNullOrEmpty(entry.KindKey)
                || string.Equals(attr.KindKey, entry.KindKey, StringComparison.Ordinal);
        }

        // ddrive_asset_create 用。ControlSkin(Button / Slider の 2 クラス)だけ dataClass(クラス名。大小文字無視)が要る。
        public static Type ResolveDataClass(Entry entry, string dataClass)
        {
            if (string.IsNullOrWhiteSpace(dataClass))
            {
                if (entry.HasMultipleClasses)
                {
                    throw new McpToolError(
                        McpGuard.CodeInvalidParams,
                        $"{entry.Type} は data_class が必要です({ClassNames(entry)})");
                }

                return entry.DataClasses[0];
            }

            var trimmed = dataClass.Trim();
            foreach (var cls in entry.DataClasses)
            {
                if (string.Equals(cls.Name, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return cls;
                }
            }

            throw new McpToolError(
                McpGuard.CodeInvalidParams,
                $"data_class '{dataClass}' は {entry.Type} のクラスではありません({ClassNames(entry)})");
        }

        private static string ClassNames(Entry entry)
        {
            var names = new string[entry.DataClasses.Length];
            for (var i = 0; i < names.Length; i++)
            {
                names[i] = entry.DataClasses[i].Name;
            }

            return string.Join(" / ", names);
        }

        // Extension(KindKey 付き)の表にも使える既定の欄。組み込みは DefaultFields(type, dataClass) と同じ。
        public static IReadOnlyList<string> DefaultFields(Entry entry, Type dataClass = null)
        {
            if (entry == null)
            {
                return CommonFields;
            }

            if (string.IsNullOrEmpty(entry.KindKey))
            {
                return DefaultFields(entry.Type, dataClass);
            }

            var result = new List<string>(CommonFields);
            result.AddRange(entry.MainFields[0]);
            return result;
        }

        // 既定で返す欄 = 共通 4 欄 + そのクラスの主要欄。dataClass が表に無い(派生クラス等)ときは先頭クラスの表を使う。
        public static IReadOnlyList<string> DefaultFields(AssetType type, Type dataClass = null)
        {
            if (!TryGet(type, out var entry))
            {
                return CommonFields;
            }

            var index = 0;
            if (dataClass != null)
            {
                for (var i = 0; i < entry.DataClasses.Length; i++)
                {
                    if (entry.DataClasses[i] == dataClass)
                    {
                        index = i;
                        break;
                    }
                }
            }

            var result = new List<string>(CommonFields);
            result.AddRange(entry.MainFields[index]);
            return result;
        }
    }
}
