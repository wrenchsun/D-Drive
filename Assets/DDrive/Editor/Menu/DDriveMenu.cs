namespace DDrive.Editor.Menu
{
    // [00_requirements.md] §5 / [09_editor_tools.md] §6 — メニューパスの文字列直書き禁止。
    // 全 [MenuItem] はこの定数経由で参照する。カテゴリは各チケットの実装に合わせて追加していく。
    // Root は Unity 標準の "Tools" メニュー配下に置き、新規のトップレベルメニューを増やさない。
    public static class DDriveMenu
    {
        public const string Root = "Tools/D-Drive/";
        public const string Editors = Root + "Editors/";
        public const string Validation = Root + "Validation/";
        public const string Generate = Root + "Generate/";
        public const string Debug = Root + "Debug/";
        // [11_tasks.md] 6-0(E) — 実機確認用ビルド一式。
        public const string Build = Root + "Build/";

        // [11_tasks.md] U-17(2026-09-17) — Project ウィンドウの右クリックメニュー(Unity 標準の "Assets/" 配下)。
        // 選択中のソースアセット(AudioClip / Texture / FBX / Prefab / Material …)から Data を作る入口。
        public const string AssetsRoot = "Assets/D-Drive/";
        public const string AssetsCreateData = AssetsRoot + "Data を作成/";

        // [11_tasks.md] U-18/U-19(2026-09-17) — Hierarchy の右クリックメニュー(Unity 標準の "GameObject/" 配下)。
        // D-Drive の基本オブジェクト(標準プレハブ・UI 部品・起動オブジェクト)をシーンに置く入口。
        public const string GameObjectRoot = "GameObject/D-Drive/";

        // GameObject メニューの priority。Unity 標準の作成系(Create Empty=0 / 3D Object=1 / UI=2 …)の並びに混ぜる。
        // 50 以上にすると Hierarchy の右クリックメニュー側で別グループへ落ちるため、小さい値を使う。
        public const int GameObjectPriority = 12;
    }
}
