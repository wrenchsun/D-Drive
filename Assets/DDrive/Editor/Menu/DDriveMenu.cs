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
    }
}
