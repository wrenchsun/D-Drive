using DDrive.Editor;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Cutscene
{
    // [26_timeline.md] §4.1/§5.3/§7.2-6(6-10c) — Maya FBX 取り込みのプロジェクト既定値。
    // MayaImportProfile/Anim2DImportProfile と同じ FindOrDefault() パターン(プロジェクトに無ければ
    // メモリ上の組み込み既定を使う)。**新規生成にのみ効く**: 既存の CutsceneData / Camera クリップの値は
    // ここを変えても変わらない([11_tasks.md] 6-10c AC「DefaultFrameRate を 30↔60 に変えても既存 CutsceneData
    // は変わらない」)。
    [CreateAssetMenu(menuName = "D-Drive/Cutscene/Cutscene Import Profile", fileName = "CutsceneImportProfile")]
    public sealed class CutsceneImportProfile : ScriptableObject
    {
        [Tooltip("OFF なら FBX 配置での自動生成を行わない(Tools/D-Drive/Generate の手動再実行は可)。")]
        public bool AutoImport = true;

        [Tooltip("新規 CutsceneData のプロジェクト既定 fps(30 または 60、[26] §5.3「プロジェクトの既定 fps」)。" +
                 "FBX から fps を検出できたときはそちらを優先する(検出できないときのみ使う既定値)。")]
        public float DefaultFrameRate = 30f;

        [Tooltip("新規生成する D-Drive Camera クリップの既定 StepFps(0 = 量子化なし。[26] §4.6.3/§7.2-6 の決定どおり既定 0)。")]
        public float DefaultCameraStepFps;

        [Tooltip("新規生成する D-Drive Camera クリップの既定ブレンド秒(BlendIn/BlendOut 共通)。")]
        public float DefaultBlendSeconds = 0.25f;

        [Tooltip("識別子が生成できない(数字始まり等)ときの前置語。")]
        public string IdentifierFallback = "Cutscene";

        private static CutsceneImportProfile _builtIn;

        public static CutsceneImportProfile FindOrDefault()
        {
            foreach (var guid in AssetSearch.FindAssets("t:" + nameof(CutsceneImportProfile)))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/Tests/"))
                {
                    continue;
                }

                var profile = AssetDatabase.LoadAssetAtPath<CutsceneImportProfile>(path);
                if (profile != null)
                {
                    return profile;
                }
            }

            if (_builtIn == null)
            {
                _builtIn = CreateInstance<CutsceneImportProfile>();
                _builtIn.name = "CutsceneImportProfile (built-in default)";
                _builtIn.hideFlags = HideFlags.HideAndDontSave;
            }

            return _builtIn;
        }
    }
}
