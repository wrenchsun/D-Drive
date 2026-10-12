using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Net;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Audio;
using DDrive.Runtime.CameraShake;
using DDrive.Runtime.Haptics;
using DDrive.Runtime.Presentation;
using DDrive.Runtime.Vfx;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Presentation
{
    // 実運用規模の確認用サンプル(docs/1010 D-10。「剣の一撃」= トラック 13 本・Signal / Marker あり・Anchor / Params 設定済みと未設定が混在・Asset 未設定のエラー 1 本)。
    // 指定のフォルダ(Assets/ 配下)に、参照先の Data(SE 3・VFX 2・Anim・CameraShake・Haptics)と PresentationData を 1 つ作る。呼び出し側が使い終わったら
    // フォルダごと消す(実データ Assets/GameData には作らない。Addressables にも登録しない)。スクリーンショット・テスト・見通しの確認用。
    internal static class PresentationSample
    {
        public const ulong BaseId = 0xDD500000;

        public sealed class Result
        {
            public PresentationData Presentation;
            public string Folder;
            public int TrackCount;
        }

        public static Result Build(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                var parent = System.IO.Path.GetDirectoryName(folder).Replace((char)92, (char)47);
                AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
            }

            var anim = Create<AnimData>(folder, "ANIM_SwordSlash", BaseId + 1, "剣の一撃(モーション)");
            var seLight = Create<SeData>(folder, "SE_SwordWhoosh", BaseId + 2, "剣を振る音");
            var seHit = Create<SeData>(folder, "SE_SwordHit", BaseId + 3, "斬撃のヒット音");
            var seVoice = Create<SeData>(folder, "SE_SlashVoice", BaseId + 4, "気合の声");
            var vfxTrail = Create<VfxData>(folder, "VFX_SlashTrail", BaseId + 5, "斬撃の軌跡");
            vfxTrail.Params = new[]
            {
                new VfxParam { Label = "MainColor", Type = VfxParamType.Color, TargetProperty = "_Color", Default = ParamValue.Of(Color.white) },
                new VfxParam { Label = "Size", Type = VfxParamType.Float, TargetProperty = "_Size", Default = ParamValue.Of(1f) },
            };
            var vfxSpark = Create<VfxData>(folder, "VFX_HitSpark", BaseId + 6, "ヒット火花");
            var shake = Create<CameraShakeData>(folder, "SHAKE_HitMedium", BaseId + 7, "ヒット時の揺れ");
            var haptic = Create<HapticsData>(folder, "HAPTIC_HitMedium", BaseId + 8, "ヒット時の振動");

            var data = ScriptableObject.CreateInstance<PresentationData>();
            data.Id = BaseId + 100;
            data.DisplayName = "剣の一撃(サンプル)";
            data.Category = "Combat";
            data.Flags.Net = NetMode.Cosmetic;
            data.TotalDuration = 1.2f;
            data.Interruptible = true;
            data.Tracks = new[]
            {
                Track(TrackKind.Anim, 0f, anim.Id, AssetType.Anim),
                Track(TrackKind.Se, 0.05f, seLight.Id, AssetType.Se),
                Track(TrackKind.Se, 0.08f, seVoice.Id, AssetType.Se),
                WithAnchor(Track(TrackKind.Vfx, 0.12f, vfxTrail.Id, AssetType.Vfx), TrackTargetMode.Self, new Vector3(0f, 1.2f, 0.6f),
                    new[] { ParamValue.Of(new Color(0.4f, 0.8f, 1f)), ParamValue.Of(1.4f) }),
                WithAnchor(Track(TrackKind.Vfx, 0.32f, vfxSpark.Id, AssetType.Vfx), TrackTargetMode.World, new Vector3(1.5f, 0.8f, 0f), null),
                Track(TrackKind.Se, 0.32f, seHit.Id, AssetType.Se),
                Scoped(Track(TrackKind.CameraShake, 0.32f, shake.Id, AssetType.Shake)),
                Track(TrackKind.Haptic, 0.32f, haptic.Id, AssetType.Haptics),
                Params(Track(TrackKind.HitStop, 0.34f, 0, AssetType.None), new[] { ParamValue.Of(0.06f) }),
                Named(TrackKind.Signal, 0.4f, "hit_confirm"),
                Named(TrackKind.Marker, 0.8f, "recovery"),
                Signal(Track(TrackKind.Se, 0f, seHit.Id, AssetType.Se), "parry"),
                Signal(Track(TrackKind.Vfx, 0f, 0, AssetType.Vfx), "parry"), // Asset 未設定(検証がエラーを出す例)
            };
            AssetDatabase.CreateAsset(data, $"{folder}/PRES_SwordSlash_Sample.asset");
            EditorUtility.SetDirty(data);
            DDrive.Editor.Versioning.DDriveAssetSave.SaveAllSuppressed();
            return new Result { Presentation = data, Folder = folder, TrackCount = data.Tracks.Length };
        }

        private static T Create<T>(string folder, string name, ulong id, string display) where T : AssetDataBase
        {
            var d = ScriptableObject.CreateInstance<T>();
            d.Id = id;
            d.DisplayName = display;
            AssetDatabase.CreateAsset(d, $"{folder}/{name}.asset");
            return d;
        }

        private static PresentationTrack Track(TrackKind kind, float time, ulong id, AssetType type)
        {
            var t = PresentationTrackEditOps.CreateTrack(kind, time, null);
            if (id != 0)
            {
                t.Asset = new AssetRef { Type = type, Id = id };
            }

            return t;
        }

        private static PresentationTrack WithAnchor(PresentationTrack t, TrackTargetMode target, Vector3 offset, ParamValue[] values)
        {
            t.Target = target;
            t.Anchor.LocalOffset = offset;
            t.Params = values;
            return t;
        }

        private static PresentationTrack Scoped(PresentationTrack t)
        {
            t.Scope = PresentationEffectScope.ParticipantsOnly;
            return t;
        }

        private static PresentationTrack Params(PresentationTrack t, ParamValue[] values)
        {
            t.Params = values;
            return t;
        }

        private static PresentationTrack Named(TrackKind kind, float time, string key)
        {
            var t = PresentationTrackEditOps.CreateTrack(kind, time, null);
            t.SignalKey = key;
            return t;
        }

        private static PresentationTrack Signal(PresentationTrack t, string key)
        {
            t.Trigger = TrackTrigger.OnSignal;
            t.SignalKey = key;
            return t;
        }
    }
}
