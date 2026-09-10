using DDrive.Foundation.Identity;
using DDrive.Foundation.Values;
using UnityEngine;

namespace DDrive.Runtime.Anim2D
{
    // AssetId<Anim2DMarker> のタグ型。
    public readonly struct Anim2DMarker
    {
    }

    // BlendTree(2D Freeform Directional)に登録する方向数。
    public enum DirectionSet
    {
        None,  // 方向なし(単一 Clip)
        Four,  // 0 / 90 / 180 / 270
        Eight, // 45 度刻み
    }

    // [05_model_animation.md] C-3 — 2D スプライトアニメーションの再生単位(チケット 3-12、2026-09-10)。
    // AnimData を継承し、時間追跡・Frame/Time イベント・CrossFade は AnimManager をそのまま使う(実装共有)。
    // 追加分は方向 BlendTree(x, y パラメータ)とリタイミングの定義。Clip / DirectionClips は Anim2DEditor(3-11)が生成する。
    [CreateAssetMenu(menuName = "D-Drive/Anim/Anim2D Data", fileName = "ANIM2D_NewAnim")]
    [AssetIdDefinition(AssetType.Anim2D, typeof(Anim2DMarker), "ANIM2DID")]
    public class Anim2DData : Anim.AnimData
    {
        public const int EightDirections = 8;
        public const int FourDirections = 4;

        [Header("方向")]
        [Tooltip("方向付き再生か。Four / Eight のときは StateName の BlendTree(2D Freeform Directional)に DirectionClips が登録されている前提。")]
        public DirectionSet Directions = DirectionSet.None;

        [Tooltip("角度順(0 / 45 / … / 315、Four なら 0 / 90 / 180 / 270)の Clip。ツールが自動登録する。")]
        public AnimationClip[] DirectionClips;

        [Tooltip("BlendTree の X パラメータ名(既存 BlendTreeRegistrar の規約 \"x\")。")]
        public string ParamXName = "x";

        [Tooltip("BlendTree の Y パラメータ名(既存 BlendTreeRegistrar の規約 \"y\")。")]
        public string ParamYName = "y";

        [Header("リタイミング")]
        [Tooltip("フレーム配置カーブ([17] ValueDef)。Clip 生成時にツールがキー時刻へ焼き込む(ランタイムでは参照しない)。")]
        public ValueDef Retiming = ValueDef.Constant01(1f);

        public int RequiredDirectionClips => Directions switch
        {
            DirectionSet.Four => FourDirections,
            DirectionSet.Eight => EightDirections,
            _ => 0,
        };

        public bool HasDirections => Directions != DirectionSet.None;
    }
}
