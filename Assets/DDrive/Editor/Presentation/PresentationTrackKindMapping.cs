using System;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Anim2D;
using DDrive.Runtime.Audio;
using DDrive.Runtime.CameraShake;
using DDrive.Runtime.Haptics;
using DDrive.Runtime.Presentation;
using DDrive.Runtime.Ui;
using DDrive.Runtime.Vfx;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Presentation
{
    // [08_presentation.md] §4(5-4) — PresentationEditor のトラック行が使う Kind ⇔ 具象 Data 型の対応表。
    // D&D で落とされた Data の型から Kind を自動判定する(AssetBrowser/Project ウィンドウ→トラックレーン)のと、
    // トラックの「Asset」欄の ObjectField の objectType を Kind から決めるのに使う(どちらも同じ対応表を経由し、
    // 別々に定義してズレないようにする)。
    public static class PresentationTrackKindMapping
    {
        // トラックの「Asset」欄に受け付ける具象型(HitStop/Timeline/Marker/Signal は null = Asset 欄を出さない)。
        public static Type AssetTypeFor(TrackKind kind) => kind switch
        {
            TrackKind.Anim => typeof(AnimData),
            TrackKind.Anim2D => typeof(Anim2DData),
            TrackKind.Se => typeof(SeData),
            TrackKind.Bgm => typeof(BgmData),
            TrackKind.Vfx => typeof(VfxData),
            TrackKind.CameraShake => typeof(CameraShakeData),
            TrackKind.Haptic => typeof(HapticsData),
            TrackKind.Canvas => typeof(CanvasData),
            TrackKind.UiTween => typeof(UiTweenData),
            _ => null,
        };

        // トラックの Asset(AssetRef)欄を書き込むときの AssetType([08_presentation.md] 実装メモの委譲表と対応)。
        public static AssetType AssetKindFor(TrackKind kind) => kind switch
        {
            TrackKind.Anim => AssetType.Anim,
            TrackKind.Anim2D => AssetType.Anim2D,
            TrackKind.Se => AssetType.Se,
            TrackKind.Bgm => AssetType.Bgm,
            TrackKind.Vfx => AssetType.Vfx,
            TrackKind.CameraShake => AssetType.Shake,
            TrackKind.Haptic => AssetType.Haptics,
            TrackKind.Canvas => AssetType.Canvas,
            TrackKind.UiTween => AssetType.UiTween,
            _ => AssetType.None,
        };

        // D&D: Project ウィンドウ / AssetBrowser から落とされた Data の具象型から Kind を判定する。
        // Anim2DData : AnimData なので Anim2D を先に判定すること。
        public static bool TryKindFor(AssetDataBase asset, out TrackKind kind)
        {
            switch (asset)
            {
                case Anim2DData:
                    kind = TrackKind.Anim2D;
                    return true;
                case AnimData:
                    kind = TrackKind.Anim;
                    return true;
                case SeData:
                    kind = TrackKind.Se;
                    return true;
                case BgmData:
                    kind = TrackKind.Bgm;
                    return true;
                case VfxData:
                    kind = TrackKind.Vfx;
                    return true;
                case CameraShakeData:
                    kind = TrackKind.CameraShake;
                    return true;
                case HapticsData:
                    kind = TrackKind.Haptic;
                    return true;
                case CanvasData:
                    kind = TrackKind.Canvas;
                    return true;
                case UiTweenData:
                    kind = TrackKind.UiTween;
                    return true;
                default:
                    kind = default;
                    return false;
            }
        }

        // タイムライン上のレーン色(Kind ごと)。パラメータ上書きの見た目とは無関係の識別用。
        public static Color LaneColor(TrackKind kind) => kind switch
        {
            TrackKind.Anim or TrackKind.Anim2D => new Color(0.55f, 0.7f, 1f),
            TrackKind.Se or TrackKind.Bgm => new Color(0.5f, 0.9f, 0.6f),
            TrackKind.Vfx => new Color(1f, 0.7f, 0.3f),
            TrackKind.CameraShake => new Color(1f, 0.4f, 0.4f),
            TrackKind.Haptic => new Color(0.8f, 0.5f, 1f),
            TrackKind.HitStop => new Color(1f, 1f, 1f),
            TrackKind.Canvas or TrackKind.UiTween => new Color(0.4f, 0.85f, 0.85f),
            TrackKind.Marker => new Color(0.75f, 0.75f, 0.75f),
            TrackKind.Signal => new Color(1f, 0.85f, 0.2f),
            TrackKind.Timeline => new Color(0.6f, 0.6f, 0.6f),
            _ => Color.gray,
        };

        // トラックの Asset(AssetRef.Id)から実際の AssetDataBase を解決する(Inspector 表示 / 5-4 追補の
        // 「トラックの最後に合わせる」の長さ見積りで共用。PresentationEditorWindow.Tracks.cs から移設 — 2 箇所に
        // 同じ検索コードを置かない、[12_review.md] のコピペ禁止に対応)。
        public static AssetDataBase FindAssetById(Type dataType, ulong id)
        {
            if (id == 0 || dataType == null)
            {
                return null;
            }

            foreach (var guid in DDrive.Editor.AssetSearch.FindAssets("t:" + dataType.Name))
            {
                var asset = AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guid), dataType) as AssetDataBase;
                if (asset != null && asset.Id == id)
                {
                    return asset;
                }
            }

            return null;
        }
    }
}
