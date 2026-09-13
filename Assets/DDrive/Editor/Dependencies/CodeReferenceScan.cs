using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DDrive.Editor.Codegen;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Dependencies
{
    // [11_tasks.md] 5-6 — 「削除しようとしている ID の生成済み定数(SEID.PlayerSlash 等)がコードから
    // 使われているか」の簡易チェック。DependencyGraphService はデータ側(Data/Prefab/Scene)の参照しか
    // 追わないため、コード側の参照はこの grep ベースの best-effort な補助でしか警告できない
    // (要判断: docs/28 参照。誤検知/見逃しがあり得るので、削除を止めるのではなく確認ダイアログの文言で注意喚起するだけに留める)。
    internal static class CodeReferenceScan
    {
        private const int MaxHits = 5;

        // 見つからない/判定できない場合は null。見つかった場合は確認ダイアログにそのまま載せられる文言を返す。
        public static string FindPossibleReferences(AssetDataBase asset, string assetPath)
        {
            try
            {
                var pattern = BuildConstantReference(asset, assetPath);
                if (string.IsNullOrEmpty(pattern))
                {
                    return null;
                }

                var hits = new List<string>();
                var dataPath = Application.dataPath.Replace('\\', '/');
                foreach (var file in Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories))
                {
                    var normalized = file.Replace('\\', '/');
                    if (normalized.EndsWith("/Generated/AssetIds.g.cs", StringComparison.Ordinal))
                    {
                        continue; // 生成ファイル自身に定数が並ぶのは当然なので除外
                    }

                    string text;
                    try
                    {
                        text = File.ReadAllText(normalized);
                    }
                    catch (Exception)
                    {
                        continue; // 読めないファイルはスキップ(CLAUDE.md §0-4)
                    }

                    if (text.IndexOf(pattern, StringComparison.Ordinal) >= 0)
                    {
                        var relative = "Assets" + normalized.Substring(dataPath.Length);
                        hits.Add(relative);
                        if (hits.Count >= MaxHits)
                        {
                            break;
                        }
                    }
                }

                if (hits.Count == 0)
                {
                    return null;
                }

                return $"生成された ID 定数 '{pattern}' を参照しているコードが見つかりました({string.Join(", ", hits)} 等)。" +
                       "削除するとコンパイルエラーになる可能性があります(簡易チェックのため見逃し/誤検知の余地あり)。";
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DDrive] CodeReferenceScan: 走査に失敗しました: {e.Message}");
                return null;
            }
        }

        // "SEID.PlayerSlash" のような文字列を、AssetIdGenerator と同じ規則(ファイル名→定数名)で組み立てる。
        private static string BuildConstantReference(AssetDataBase asset, string assetPath)
        {
            var dataType = asset.GetType();
            AssetIdDefinitionAttribute attr = null;
            while (dataType != null && attr == null)
            {
                attr = dataType.GetCustomAttribute<AssetIdDefinitionAttribute>();
                dataType = dataType.BaseType;
            }

            if (attr == null || string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            var fileName = Path.GetFileNameWithoutExtension(assetPath);
            var constName = AssetIdGenerator.ToConstantName(fileName);
            return $"{attr.ConstantsClassName}.{constName}";
        }
    }
}
