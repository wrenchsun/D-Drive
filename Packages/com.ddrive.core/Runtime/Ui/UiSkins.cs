using System;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Registry;

namespace DDrive.Runtime.Ui
{
    // [15_ui_interaction.md] / [18_ui_controls.md] Part A — UiInteractable.SkinId の遅延解決。
    // R3 は未導入のため Func のみの薄いファサード(DDriveRuntimeBootstrap が Bind/Unbind する)。
    public static class UiSkins
    {
        public static Func<AssetId<ControlSkinMarker>, ControlSkinData> Resolver { get; private set; }

        public static void Bind(IAssetRegistry registry)
        {
            Resolver = registry == null
                ? null
                : id => registry.TryResolveSync<ControlSkinData>(id.Value, out var skin) ? skin : null;
        }

        public static void Bind(Func<AssetId<ControlSkinMarker>, ControlSkinData> resolver) => Resolver = resolver;
    }
}
