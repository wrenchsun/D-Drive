using System;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using UnityEngine;

namespace DDrive.Editor.Import
{
    // [51_tdrive_integration.md] §4.7(FC-6、2026-10-03) — 外部アセンブリが実装した IImportRuleHandler を
    // ImportRuleService に載せるときの包み(internal)。
    //  - 名乗り(TypeFolder / Target / DataType / Extensions / IdentifierFallback)は採用時に 1 度だけ読んでキャッシュする
    //    (定常の取り込みで外部コードのプロパティを何度も呼ばない。読めなかった / 不正なら警告して採用しない)。
    //  - LoadSource は例外を隔離して null を返す(= 作成スキップ。取り込み全体を止めない)。
    //  - Configure は例外をそのまま伝える(AssetCreationService.Create が CreateAsset の前に呼ぶので、例外のときアセットは
    //    作られない。ImportRuleService.ImportOne が捕まえてログに出す)。
    internal sealed class ExternalImportRuleHandler : IImportRuleHandler
    {
        internal readonly IImportRuleHandler Inner;

        private ExternalImportRuleHandler(IImportRuleHandler inner, string typeFolder, AssetType target, Type dataType, string[] extensions, string fallback)
        {
            Inner = inner;
            TypeFolder = typeFolder;
            Target = target;
            DataType = dataType;
            Extensions = extensions;
            IdentifierFallback = fallback;
        }

        public string TypeFolder { get; }

        public AssetType Target { get; }

        public Type DataType { get; }

        public string[] Extensions { get; }

        public string IdentifierFallback { get; }

        // 採用できなければ null。TypeFolder が空(= 今は名乗っていない)のときは黙って null、それ以外の不正は警告する。
        internal static ExternalImportRuleHandler TryCreate(IImportRuleHandler inner)
        {
            var typeName = inner.GetType().FullName;
            try
            {
                var folder = inner.TypeFolder?.Trim();
                if (string.IsNullOrEmpty(folder))
                {
                    return null;
                }

                if (folder.IndexOf('/') >= 0 || folder.IndexOf('\\') >= 0)
                {
                    Debug.LogWarning($"[DDrive] ImportRule: 外部ハンドラ {typeName} の TypeFolder '{folder}' に区切り文字が含まれているため無視します");
                    return null;
                }

                var dataType = inner.DataType;
                if (dataType == null || dataType.IsAbstract || !typeof(AssetDataBase).IsAssignableFrom(dataType))
                {
                    Debug.LogWarning($"[DDrive] ImportRule: 外部ハンドラ {typeName} の DataType が AssetDataBase を継承した具象型ではないため無視します");
                    return null;
                }

                var source = inner.Extensions;
                if (source == null || source.Length == 0)
                {
                    Debug.LogWarning($"[DDrive] ImportRule: 外部ハンドラ {typeName} の Extensions が空のため無視します");
                    return null;
                }

                var extensions = (string[])source.Clone();
                foreach (var ext in extensions)
                {
                    if (string.IsNullOrEmpty(ext) || ext[0] != '.' || !string.Equals(ext, ext.ToLowerInvariant(), StringComparison.Ordinal))
                    {
                        Debug.LogWarning($"[DDrive] ImportRule: 外部ハンドラ {typeName} の拡張子 '{ext}' は小文字・ドット付き(例 \".png\")で指定してください(一致しません)");
                    }
                }

                return new ExternalImportRuleHandler(inner, folder, inner.Target, dataType, extensions, inner.IdentifierFallback);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return null;
            }
        }

        public UnityEngine.Object LoadSource(string assetPath)
        {
            try
            {
                return Inner.LoadSource(assetPath);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return null;
            }
        }

        public void Configure(AssetDataBase data, UnityEngine.Object source, string assetPath)
            => Inner.Configure(data, source, assetPath);
    }
}
