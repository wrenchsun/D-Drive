using DDrive.Foundation.Data;
using UnityEngine;

namespace DDrive.Runtime.Anchoring
{
    // AnchorDef を実際の Transform 階層に対して解決する共通ロジック。
    // Audio(1-3)・VFX(2-*) で共用する([03_audio.md] §2 / [04_vfx.md] §2)。
    // Data.Anchor が「どう付けるか(ボーン名/オフセット)」を持ち、contextRoot が「誰に対して」を渡す
    // ——両者を組み合わせて初めて解決できる(呼び出し側の明示引数は Anchor そのものを上書きする、
    // こちらは Data.Anchor をそのまま使う経路)。
    public static class AnchorResolver
    {
        // 戻り値が null の場合、呼び出し側は Anchor.LocalOffset をワールド座標として扱う(World 固定扱い)。
        public static Transform Resolve(AnchorDef anchor, Transform contextRoot)
        {
            switch (anchor.Space)
            {
                case AnchorSpace.BoneName:
                case AnchorSpace.NamedObject:
                    return contextRoot != null ? FindRecursive(contextRoot, anchor.Path) : null;

                case AnchorSpace.ContextTarget:
                    return contextRoot;

                case AnchorSpace.World:
                default:
                    return null;
            }
        }

        private static Transform FindRecursive(Transform root, string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            if (root.name == name)
            {
                return root;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindRecursive(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
