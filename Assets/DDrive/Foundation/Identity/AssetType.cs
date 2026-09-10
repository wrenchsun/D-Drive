namespace DDrive.Foundation.Identity
{
    public enum AssetType : byte
    {
        None = 0,
        Se,
        Bgm,
        Vfx,
        Anim,
        Anim2D,
        Material,
        Texture,
        Canvas,
        Prefab,
        Presentation,
        Shake,
        Haptics,
        UiTween,

        // 末尾に追加すること(YAML には整数値で永続化されるため、既存の値の並び替え・挿入は
        // 既存アセットの種別を破壊する。新種別は必ず追記する)。
        Model,

        // [21_anchor_spec.md] §3.1: 生成位置定義(AnchorData)。2026-09-08 追加。
        Anchor,

        // [22_anchor_group.md]: 配置セット(AnchorGroupData)。2026-09-08 追加。
        AnchorGroup,

        // [15_ui_interaction.md] / [18_ui_controls.md]: UiInteractable 共通 Skin(ButtonSkinData 等)。2026-09-11 追加。
        ControlSkin,
    }
}
