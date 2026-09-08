using System;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using UnityEngine;

namespace DDrive.Runtime.Vfx
{
    public readonly struct VfxMarker
    {
    }

    public enum VfxLifeMode
    {
        OneShot,
        Loop,
        Duration,
    }

    // UnityEngine.RenderMode(Canvas 用)と名前が衝突するため VfxRenderMode とする。
    public enum VfxRenderMode
    {
        World3D,
        UIOverlay,
    }

    public enum VfxParamType
    {
        Float,
        Int,
        Color,
        Curve,
        Gradient,
        Texture,
        Vector,
    }

    // デザイナーが命名する公開引数。TargetProperty は MaterialPropertyBlock のシェーダープロパティ名、
    // または VisualEffect(VFX Graph) の Exposed 名のどちらかとして解決を試みる([04_vfx.md] §2)。
    [Serializable]
    public struct VfxParam
    {
        [Tooltip("SetParam から参照する名前(例: MainColor, Size)。")]
        public string Label;

        [Tooltip("値の型。TargetProperty の実体と一致させること。")]
        public VfxParamType Type;

        [Tooltip("シェーダープロパティ名、または VFX Graph の Exposed プロパティ名。")]
        public string TargetProperty;

        [Tooltip("初期値。")]
        public ParamValue Default;

        [Tooltip("任意。時間で変化させる場合の形+スピード(Float 系のみ有効)。")]
        public DDrive.Foundation.Values.ValueDef Anim;
    }

    [CreateAssetMenu(menuName = "D-Drive/Vfx/Vfx Data", fileName = "VFX_NewEffect")]
    [AssetIdDefinition(AssetType.Vfx, typeof(VfxMarker), "VFXID")]
    public class VfxData : AssetDataBase
    {
        [Header("Prefab")]
        [Tooltip("再生する実体。ParticleSystem / VFX Graph のどちらでも可。")]
        public GameObject Prefab;

        [Header("Anchor")]
        [Tooltip("アタッチ位置定義(Audio と共通)。")]
        public AnchorDef Anchor;

        [Header("Lifetime")]
        [Tooltip("OneShot=自然終了で自動返却 / Loop=Stop/Killまで継続 / Duration=Durationで強制終了。")]
        public VfxLifeMode LifeMode;

        [Tooltip("LifeMode=Duration の継続秒数。OneShot でも ParticleSystem が無い(VFX Graph 等)場合の終了判定フォールバックに使う。")]
        public float Duration = 1f;

        [Tooltip("Stop時、放出を止めてから実際に返却するまでの待ち秒数(既存パーティクルの消失待ち)。")]
        public float FadeOutSec;

        [Header("Render")]
        [Tooltip("World3D=通常シーン内 / UIOverlay=UI上のパーティクル(専用カメラで合成)。")]
        public VfxRenderMode Render;

        [Tooltip("Sorting/RenderingLayerMask に使う値。")]
        public int RenderLayer;

        [Tooltip("Light Layer のビットマスク。")]
        public uint LightLayerMask;

        [Header("Parameters")]
        [Tooltip("デザイナーが公開する調整項目。ラベル経由で SetParam から反映する。")]
        public VfxParam[] Params;
    }
}
