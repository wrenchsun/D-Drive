using System;
using DDrive.Editor.AssetBrowser;
using DDrive.Foundation.Identity;

namespace DDrive.Editor.Spec
{
    // [27_spec_sheet.md] §4.3 — 既存アセットを「種別+識別子」で仕様書の行と結び付けるための逆変換。
    // AssetDataBase には識別子そのものを持つフィールドが無い(表示名は日本語可、カテゴリは自由記述のため
    // どちらも識別子の代わりに使えない)。代わりに AssetNamingService.BuildFileName が組み立てる
    // "{prefix}_{categorySegment}_{identifier}" は、categorySegment・identifier のどちらも '_' を
    // 含み得ない(識別子は PascalCase、カテゴリセグメントは英数字のみに正規化される。[27] §3.1)ため、
    // ファイル名を '_' で分割した最後のトークンは常に元の識別子と一致する(新しいフィールドを追加せずに
    // 済ませるための決定。docs/28 の「要判断」に記載)。
    public static class SpecIdentifierCodec
    {
        public static bool TryExtractIdentifier(string fileNameWithoutExt, AssetType type, out string identifier)
            => TryExtractIdentifier(fileNameWithoutExt, type, string.Empty, out identifier);

        // X-5(2026-10-16): Extension 種別(KindKey 付き)。接頭辞は IAssetKindProvider.Prefix(AssetNamingService.GetTypePrefix(type, kindKey))。
        // 組み込み種別(kindKey が空)は上のオーバーロードと同じ結果。仕様書シートへの Extension の配線(SpecDiffService)は別チケット(docs/1004 X-5)。
        public static bool TryExtractIdentifier(string fileNameWithoutExt, AssetType type, string kindKey, out string identifier)
        {
            identifier = null;
            if (string.IsNullOrEmpty(fileNameWithoutExt))
            {
                return false;
            }

            var expectedPrefix = AssetNamingService.GetTypePrefix(type, kindKey) + "_";
            if (!fileNameWithoutExt.StartsWith(expectedPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            var lastUnderscore = fileNameWithoutExt.LastIndexOf('_');
            if (lastUnderscore < 0 || lastUnderscore == fileNameWithoutExt.Length - 1)
            {
                return false;
            }

            var candidate = fileNameWithoutExt.Substring(lastUnderscore + 1);
            if (!AssetNamingService.IsValidIdentifier(candidate))
            {
                return false;
            }

            identifier = candidate;
            return true;
        }
    }
}
