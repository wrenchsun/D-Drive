using System;
using DDrive.Runtime.Anim2D;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Anim2D
{
    // 2D スプライトアニメーション生成ツールの命名規則・既定値。
    // 移植元: Katsuya.Tools.SpriteAnimation.SpriteAnimationNameData の AssetData 化([05] C-2)。
    // 固定パスの ScriptableObject 参照を廃止し、TextureImportProfile と同じ FindOrDefault パターンで解決する。
    [CreateAssetMenu(menuName = "D-Drive/Anim/Anim2D Import Profile", fileName = "Anim2DImportProfile")]
    public sealed class Anim2DImportProfile : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("命名規則で使う名称(例: Player)。")]
            public string Name;

            [Tooltip("この名称で使用可能なステート一覧(例: Idle, Run, Attack)。")]
            public string[] States = Array.Empty<string>();
        }

        [Tooltip("名称とステートの全組み合わせ。")]
        public Entry[] Entries = Array.Empty<Entry>();

        [Tooltip("生成される AnimationClip の基準フレームレート。")]
        public int DefaultFrameRate = 12;

        [Tooltip("方向の既定セット(BlendTree 登録の有無・角度数)。")]
        public DirectionSet DefaultDirections = DirectionSet.None;

        [Tooltip("生成した AnimationClip の既定保存先フォルダ。")]
        public string DefaultClipFolder = "Assets/GameData/Anim2D/Clips";

        // 名称 → Anim2DData の Category に使う既定ルール(そのまま Name を使う。必要ならプロジェクトごとに上書き)。
        public string ResolveCategory(string name) => string.IsNullOrEmpty(name) ? "Anim2D" : name;

        // プロジェクト内の Profile(Tests 配下は除外)。無ければ組み込み既定(メモリ上、保存しない)。
        private static Anim2DImportProfile _builtIn;

        public static Anim2DImportProfile FindOrDefault()
        {
            foreach (var guid in AssetSearch.FindAssets("t:" + nameof(Anim2DImportProfile)))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/Tests/"))
                {
                    continue;
                }

                var profile = AssetDatabase.LoadAssetAtPath<Anim2DImportProfile>(path);
                if (profile != null)
                {
                    return profile;
                }
            }

            if (_builtIn == null)
            {
                _builtIn = CreateInstance<Anim2DImportProfile>();
                _builtIn.name = "Anim2DImportProfile (built-in default)";
                _builtIn.hideFlags = HideFlags.HideAndDontSave;
            }

            return _builtIn;
        }
    }
}
