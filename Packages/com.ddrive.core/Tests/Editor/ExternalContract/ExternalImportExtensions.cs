using System;
using System.Collections.Generic;
using DDrive.Editor.Import;
using DDrive.Editor.Materials;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Material;
using UnityEditor;
using UnityEngine;

namespace ExternalPackage.Fake
{
    // [docs/42 §5.14] 外部パッケージが書く取り込みまわりの拡張のダミー(FC-6 = E-21 / FC-14 = E-22)。
    // TypeCache で常時発見されるため、普段は何もしない: Probe のフラグが立っているテスト中だけ名乗る
    // (名乗らないときは TypeFolder 空 / FolderNames 空 / GetRules・GetTables 空 = D-Drive 側が無視する)。
    // テストアセンブリ名が `DDrive.Tests` で始まらない(= 発見される)ことが前提。普段の取り込み・Validation > Run All を汚さない。
    public static class ExternalImportProbe
    {
        public const string NotesFolder = "ExternalNotes";
        public const string OwnedFolder = "ExternalOwned";

        public static bool HandlerEnabled;          // ExternalNoteHandler が NotesFolder を名乗る
        public static bool DuplicateEnabled;        // ExternalNoteHandlerZDuplicate も NotesFolder を名乗る(型名が後ろ = 無視される側)
        public static bool ConflictEnabled;         // ExternalConflictingHandler が組み込みの "Se"、ExternalCutsceneHandler が "Cutscene" を名乗る
        public static bool ThrowInLoad;             // ExternalNoteHandler.LoadSource が例外を投げる
        public static string ThrowInConfigureOnName; // ファイル名にこれを含むとき Configure が例外を投げる(null で無効)
        public static bool OptOutEnabled;           // ExternalFolderOptOut が名乗る
        public static bool RuleProviderEnabled;     // ExternalToonMaskRuleProvider が規則を返す
        public static bool RuleProviderThrows;      // ExternalThrowingRuleProvider が例外を投げる
        public static IReadOnlyList<ShaderConversionTable> Tables; // ExternalTableProvider が返す表(null で空)
        public static bool TableProviderThrows;     // ExternalThrowingTableProvider が例外を投げる

        public static void Reset()
        {
            HandlerEnabled = false;
            DuplicateEnabled = false;
            ConflictEnabled = false;
            ThrowInLoad = false;
            ThrowInConfigureOnName = null;
            OptOutEnabled = false;
            RuleProviderEnabled = false;
            RuleProviderThrows = false;
            Tables = null;
            TableProviderThrows = false;
            ImportRuleService.ResetExtensionCacheForTests();
            TextureImportRuleProviders.ResetCacheForTests();
            ShaderConversionTables.ResetProviderCacheForTests();
        }
    }

    // 既存の AssetType(Se)の Data を作る外部ハンドラ。".txt" の TextAsset を元ファイルとして DisplayName に名前を入れる。
    public class ExternalNoteHandler : IImportRuleHandler
    {
        public virtual string TypeFolder => ExternalImportProbe.HandlerEnabled ? ExternalImportProbe.NotesFolder : string.Empty;
        public AssetType Target => AssetType.Se;
        public Type DataType => typeof(SeData);
        public string[] Extensions => new[] { ".txt" };
        public string IdentifierFallback => "Note";

        public UnityEngine.Object LoadSource(string assetPath)
        {
            if (ExternalImportProbe.ThrowInLoad)
            {
                throw new InvalidOperationException("external load failure (test)");
            }

            return AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
        }

        public void Configure(AssetDataBase data, UnityEngine.Object source, string assetPath)
        {
            var bad = ExternalImportProbe.ThrowInConfigureOnName;
            if (!string.IsNullOrEmpty(bad) && assetPath.Contains(bad))
            {
                throw new InvalidOperationException("external configure failure (test)");
            }

            data.DisplayName = "ext:" + source.name;
        }
    }

    // NotesFolder を ExternalNoteHandler と取り合う(型のフルネーム順で後ろ = 無視される)。
    public sealed class ExternalNoteHandlerZDuplicate : ExternalNoteHandler
    {
        public override string TypeFolder => ExternalImportProbe.DuplicateEnabled ? ExternalImportProbe.NotesFolder : string.Empty;
    }

    // 組み込みの種別フォルダ "Se" を名乗る(組み込み優先 = 無視 + 警告)。
    public sealed class ExternalConflictingHandler : ExternalNoteHandler
    {
        public override string TypeFolder => ExternalImportProbe.ConflictEnabled ? "Se" : string.Empty;
    }

    // D-Drive が別経路で使っているフォルダ "Cutscene" を名乗る(無視 + 警告)。
    public sealed class ExternalCutsceneHandler : ExternalNoteHandler
    {
        public override string TypeFolder => ExternalImportProbe.ConflictEnabled ? "Cutscene" : string.Empty;
    }

    // ハンドラ無しで「このフォルダは自分が管理する」と宣言する外部パッケージ。空文字・null・空白・重複・区切り入りは無視される。
    public sealed class ExternalFolderOptOut : IImportRuleFolderOptOut
    {
        public IEnumerable<string> FolderNames => ExternalImportProbe.OptOutEnabled
            ? new[] { ExternalImportProbe.OwnedFolder, ExternalImportProbe.OwnedFolder, string.Empty, null, "  ", "a/b", "Se" }
            : Array.Empty<string>();
    }

    // ── FC-14 ──

    // 接尾辞 _ToonMask → sRGB オフ、接頭辞 T_Toon → sRGB オフ(Profile の `T_` 接頭辞の規則より先に効くことの確認用)。
    public sealed class ExternalToonMaskRuleProvider : ITextureImportRuleProvider
    {
        public const string MaskRuleName = "ExternalToonMask";
        public const string PrefixRuleName = "ExternalToonPrefix";

        public IEnumerable<TextureImportProfile.Rule> GetRules()
        {
            if (!ExternalImportProbe.RuleProviderEnabled)
            {
                yield break;
            }

            yield return new TextureImportProfile.Rule
            {
                Name = MaskRuleName, Match = TextureImportProfile.MatchKind.Suffix, Pattern = "_ToonMask",
                Type = TextureImporterType.Default, SRgb = false, Mipmaps = true,
                Compression = TextureImporterCompression.CompressedHQ, Channel = TextureChannel.Mask, Usage = TextureUsage.Model,
            };
            yield return new TextureImportProfile.Rule
            {
                Name = PrefixRuleName, Match = TextureImportProfile.MatchKind.Prefix, Pattern = "T_Toon",
                Type = TextureImporterType.Default, SRgb = false, Mipmaps = true,
                Compression = TextureImporterCompression.Compressed, Channel = TextureChannel.Other, Usage = TextureUsage.Model,
            };
        }
    }

    public sealed class ExternalThrowingRuleProvider : ITextureImportRuleProvider
    {
        public IEnumerable<TextureImportProfile.Rule> GetRules()
        {
            if (ExternalImportProbe.RuleProviderThrows)
            {
                throw new InvalidOperationException("external rule provider failure (test)");
            }

            return Array.Empty<TextureImportProfile.Rule>();
        }
    }

    public sealed class ExternalTableProvider : IShaderConversionTableProvider
    {
        public IEnumerable<ShaderConversionTable> GetTables() => ExternalImportProbe.Tables ?? Array.Empty<ShaderConversionTable>();
    }

    public sealed class ExternalThrowingTableProvider : IShaderConversionTableProvider
    {
        public IEnumerable<ShaderConversionTable> GetTables()
        {
            if (ExternalImportProbe.TableProviderThrows)
            {
                throw new InvalidOperationException("external table provider failure (test)");
            }

            return Array.Empty<ShaderConversionTable>();
        }
    }
}
