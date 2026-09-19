using DDrive.Runtime.Cutscene.Tracks;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Cutscene
{
    // [26_timeline.md] §4.6.2/§5.2/§7.3(6-10c) — FBX から取り込んだ AnimationClip(カメラノードの
    // Transform + Camera プロパティのカーブ)を CutsceneCameraClip の焼かれたカーブへ写す。
    // AssetDatabase に依存しない(AnimationClip/GameObject を渡すだけの)純ロジックにして、実 FBX が無くても
    // コードで作った AnimationClip + GameObject(Camera 付き)でテストできるようにしてある(6-10c テスト方針)。
    //
    // [26] §7.3 要検証事項の実装メモ(2026-09-18、Unity Editor 未接続のため実 FBX での確認は未実施):
    // 画角(FOV)は Unity の Animation ウィンドウが Camera の Field of View を出す既知のプロパティ名
    // "field of view" で束縛される(長年の既定挙動)。焦点距離・ピント距離・絞り(Physical Camera 有効時の
    // m_FocalLength/m_FocusDistance/m_Aperture)は Maya の FBX 書き出しがどのプロパティに焼くかが機種依存で
    // 未確認のため、複数の候補プロパティ名を順に試し、どれも見つからなければ空カーブ(§4.6.4 の
    // 「取れなければ Focus=Off 相当」)にフォールバックする実装にしている。実 Maya 素材での確認は今後の課題。
    public static class CutsceneCameraCurveExtractor
    {
        private static readonly string[] FovNames = { "field of view" };
        private static readonly string[] FocalLengthNames = { "m_FocalLength", "focal length" };
        private static readonly string[] FocusDistanceNames = { "m_FocusDistance", "focus distance" };
        private static readonly string[] ApertureNames = { "m_Aperture", "aperture" };

        // cameraPath: AnimationClip 内でのカメラノードの相対パス(ルート直下なら ""。
        // AnimationUtility.CalculateTransformPath の結果をそのまま渡す)。
        // 戻り値: 位置・回転・FOV のいずれかのカーブが見つかれば true(Camera クリップを作る価値がある)。
        // 見つからなかったチャンネルは空カーブにする(「取れなければ書かない」を上書きのたびに再現するため、
        // 再取り込みで前回のカーブが残ってしまわないように毎回リセットしてから書き込む)。
        public static bool Extract(AnimationClip clip, string cameraPath, CutsceneCameraClip target)
        {
            if (clip == null || target == null)
            {
                return false;
            }

            var bindings = AnimationUtility.GetCurveBindings(clip);
            var path = cameraPath ?? string.Empty;

            var posX = FindCurve(clip, bindings, path, "m_LocalPosition.x");
            var posY = FindCurve(clip, bindings, path, "m_LocalPosition.y");
            var posZ = FindCurve(clip, bindings, path, "m_LocalPosition.z");

            var rotX = FindCurve(clip, bindings, path, "m_LocalRotation.x");
            var rotY = FindCurve(clip, bindings, path, "m_LocalRotation.y");
            var rotZ = FindCurve(clip, bindings, path, "m_LocalRotation.z");
            var rotW = FindCurve(clip, bindings, path, "m_LocalRotation.w");

            var fov = FindCurveAny(clip, bindings, path, FovNames);
            var focalLength = FindCurveAny(clip, bindings, path, FocalLengthNames);
            var focusDistance = FindCurveAny(clip, bindings, path, FocusDistanceNames);
            var aperture = FindCurveAny(clip, bindings, path, ApertureNames);

            target.PosX = posX ?? new AnimationCurve();
            target.PosY = posY ?? new AnimationCurve();
            target.PosZ = posZ ?? new AnimationCurve();

            var hasRotation = rotX != null && rotY != null && rotZ != null && rotW != null;
            target.RotX = hasRotation ? rotX : new AnimationCurve();
            target.RotY = hasRotation ? rotY : new AnimationCurve();
            target.RotZ = hasRotation ? rotZ : new AnimationCurve();
            target.RotW = hasRotation ? rotW : AnimationCurve.Constant(0f, 0f, 1f);

            target.FieldOfView = fov ?? AnimationCurve.Constant(0f, 0f, 60f);
            target.FocalLengthMm = focalLength ?? new AnimationCurve();
            target.FocusDistance = focusDistance ?? new AnimationCurve();
            target.Aperture = aperture ?? new AnimationCurve();

            return posX != null || posY != null || posZ != null || hasRotation || fov != null;
        }

        private static AnimationCurve FindCurve(AnimationClip clip, EditorCurveBinding[] bindings, string path, string propertyName)
        {
            for (var i = 0; i < bindings.Length; i++)
            {
                if (bindings[i].path == path && bindings[i].propertyName == propertyName)
                {
                    var curve = AnimationUtility.GetEditorCurve(clip, bindings[i]);
                    if (curve != null && curve.length > 0)
                    {
                        return curve;
                    }
                }
            }

            return null;
        }

        private static AnimationCurve FindCurveAny(AnimationClip clip, EditorCurveBinding[] bindings, string path, string[] candidateNames)
        {
            for (var n = 0; n < candidateNames.Length; n++)
            {
                var found = FindCurve(clip, bindings, path, candidateNames[n]);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
