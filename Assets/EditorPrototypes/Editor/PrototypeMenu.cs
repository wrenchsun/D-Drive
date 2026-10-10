using DDrive.Editor.Menu;
using UnityEditor;

namespace DDrive.EditorPrototypes
{
    // docs/1008 §3 — サンプルエディター 3 本の入口。[DataEditor] は付けない(既存 VFX Editor の「〜で開く」を奪わない)。
    public static class PrototypeMenu
    {
        public const string Prototypes = DDriveMenu.Root + "Prototypes/";

        [MenuItem(Prototypes + "A 段階表示")]
        public static void OpenA() => PrototypeAWindow.Open();

        [MenuItem(Prototypes + "B ステップ型")]
        public static void OpenB() => PrototypeBWindow.Open();

        [MenuItem(Prototypes + "C 目的別カード")]
        public static void OpenC() => PrototypeCWindow.Open();
    }
}
