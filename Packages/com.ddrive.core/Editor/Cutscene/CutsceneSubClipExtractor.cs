using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Cutscene
{
    // 1017 TA-5(DD-15) — 小物ごとに「そのノード以下のカーブだけ」を切り出したサブクリップを作る。
    // 小物のトラックに FBX 全体のクリップを渡すと、クリップ内の道筋(PRP_Mic/...)が小物のインスタンス
    // (ルート = 小物自身)に当たらないため、道筋をそのノード起点に付け替える(PRP_Mic → ""、PRP_Mic/Lid → Lid)。
    // AssetDatabase に依存しない純ロジック(コードで組んだ AnimationClip でテストできる)。
    public static class CutsceneSubClipExtractor
    {
        // 戻り値: 新しい(非永続の)AnimationClip。nodePath またはその子孫の束縛が 1 本も無ければ null
        // (呼び元は従来どおり全体クリップを渡す)。名前の前方一致ではなく "/" 区切りで判定する(PRP_Micro は入らない)。
        public static AnimationClip ExtractForNode(AnimationClip source, string nodePath)
        {
            if (source == null || string.IsNullOrEmpty(nodePath))
            {
                return null;
            }

            var prefix = nodePath + "/";
            var result = new AnimationClip
            {
                frameRate = source.frameRate,
                wrapMode = source.wrapMode,
                legacy = source.legacy,
            };

            var count = 0;
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
            {
                string newPath;
                if (binding.path == nodePath)
                {
                    newPath = string.Empty;
                }
                else if (binding.path.StartsWith(prefix, System.StringComparison.Ordinal))
                {
                    newPath = binding.path.Substring(prefix.Length);
                }
                else
                {
                    continue;
                }

                var curve = AnimationUtility.GetEditorCurve(source, binding);
                if (curve == null)
                {
                    continue;
                }

                var moved = binding;
                moved.path = newPath;
                AnimationUtility.SetEditorCurve(result, moved, curve);
                count++;
            }

            if (count == 0)
            {
                Object.DestroyImmediate(result);
                return null;
            }

            return result;
        }
    }
}
