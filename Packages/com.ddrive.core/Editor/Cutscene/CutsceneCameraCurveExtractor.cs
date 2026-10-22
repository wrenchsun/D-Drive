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

        // 1017 TA-4(DD-7) — カメラのカット(Ubercam の区間の切り替わり)を検出してステップにする。
        // 毎フレームのキーで隣り合うキーの位置が positionThreshold を超えて飛ぶ、または回転が rotationThresholdDeg
        // を超えて変わる箇所を「カット」とみなし、その 2 キーの間の補間を Constant(ステップ)にする
        // (60 fps 再生で 2 台の間の絵が出ないように)。位置・回転・画角・焦点距離・ピント・絞りの全カーブに適用する
        // (該当時刻にキーが無いカーブは触らない)。0 以下の閾値はその判定を無効にする。戻り値 = ステップにしたカット数。
        public static int ApplyCutSteps(CutsceneCameraClip target, float positionThreshold, float rotationThresholdDeg)
        {
            if (target == null || (positionThreshold <= 0f && rotationThresholdDeg <= 0f))
            {
                return 0;
            }

            var timeBase = FirstNonEmpty(target.PosX, target.RotX, target.PosY, target.PosZ);
            if (timeBase == null || timeBase.length < 2)
            {
                return 0;
            }

            var hasPos = target.PosX.length > 0 && target.PosY.length > 0 && target.PosZ.length > 0;
            var hasRot = target.RotX.length > 0 && target.RotY.length > 0 && target.RotZ.length > 0 && target.RotW.length > 0;
            var curves = new[]
            {
                target.PosX, target.PosY, target.PosZ, target.RotX, target.RotY, target.RotZ, target.RotW,
                target.FieldOfView, target.FocalLengthMm, target.FocusDistance, target.Aperture,
            };

            var cuts = 0;
            for (var i = 0; i + 1 < timeBase.length; i++)
            {
                var t0 = timeBase[i].time;
                var t1 = timeBase[i + 1].time;
                var isCut = false;
                if (positionThreshold > 0f && hasPos)
                {
                    var p0 = new Vector3(target.PosX.Evaluate(t0), target.PosY.Evaluate(t0), target.PosZ.Evaluate(t0));
                    var p1 = new Vector3(target.PosX.Evaluate(t1), target.PosY.Evaluate(t1), target.PosZ.Evaluate(t1));
                    isCut = Vector3.Distance(p0, p1) > positionThreshold;
                }

                if (!isCut && rotationThresholdDeg > 0f && hasRot)
                {
                    var q0 = new Quaternion(target.RotX.Evaluate(t0), target.RotY.Evaluate(t0), target.RotZ.Evaluate(t0), target.RotW.Evaluate(t0));
                    var q1 = new Quaternion(target.RotX.Evaluate(t1), target.RotY.Evaluate(t1), target.RotZ.Evaluate(t1), target.RotW.Evaluate(t1));
                    isCut = Quaternion.Angle(q0, q1) > rotationThresholdDeg;
                }

                if (!isCut)
                {
                    continue;
                }

                cuts++;
                for (var c = 0; c < curves.Length; c++)
                {
                    var curve = curves[c];
                    var k0 = IndexAtTime(curve, t0);
                    var k1 = IndexAtTime(curve, t1);
                    if (k0 >= 0)
                    {
                        AnimationUtility.SetKeyRightTangentMode(curve, k0, AnimationUtility.TangentMode.Constant);
                    }

                    if (k1 >= 0)
                    {
                        AnimationUtility.SetKeyLeftTangentMode(curve, k1, AnimationUtility.TangentMode.Constant);
                    }
                }
            }

            return cuts;
        }

        private static AnimationCurve FirstNonEmpty(params AnimationCurve[] candidates)
        {
            for (var i = 0; i < candidates.Length; i++)
            {
                if (candidates[i] != null && candidates[i].length > 0)
                {
                    return candidates[i];
                }
            }

            return null;
        }

        private static int IndexAtTime(AnimationCurve curve, float time)
        {
            if (curve == null)
            {
                return -1;
            }

            for (var i = 0; i < curve.length; i++)
            {
                if (Mathf.Abs(curve[i].time - time) < 1e-5f)
                {
                    return i;
                }
            }

            return -1;
        }

        private static AnimationCurve FindCurve(AnimationClip clip,EditorCurveBinding[] bindings, string path, string propertyName)
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
