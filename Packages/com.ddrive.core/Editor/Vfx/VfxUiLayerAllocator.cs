namespace DDrive.Editor.Vfx
{
    // Unity API(TagManager/SerializedObject)から切り離した純ロジック([04_vfx.md] §4 UI パーティクル)。
    // ユーザーレイヤー(8-31)の中から対象名を探し、無ければ最初の空きスロットに割り当てる。
    public static class VfxUiLayerAllocator
    {
        public const int FirstUserLayer = 8;
        public const int LastUserLayer = 31;

        // layers は index=0..31 の Unity レイヤー配列そのもの(未使用は空文字列)を想定。
        // 戻り値: 見つかった/確保できたレイヤー index。空きが無ければ -1(layers は変更しない)。
        public static int FindOrClaim(string[] layers, string layerName)
        {
            for (var i = FirstUserLayer; i <= LastUserLayer && i < layers.Length; i++)
            {
                if (layers[i] == layerName)
                {
                    return i;
                }
            }

            for (var i = FirstUserLayer; i <= LastUserLayer && i < layers.Length; i++)
            {
                if (string.IsNullOrEmpty(layers[i]))
                {
                    layers[i] = layerName;
                    return i;
                }
            }

            return -1;
        }
    }
}
