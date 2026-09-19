using System;
using System.Collections.Generic;
using DDrive.Foundation.Data;
using UnityEditor;

namespace DDrive.Editor.Dependencies
{
    // [11_tasks.md] 5-6 / [10_workflow.md] §3「ID の削除は Archived タグ→1リリース後に削除の2段階」。
    // TagCatalog(選択制の辞書)はまだ無いため、[27_spec_sheet.md] の SpecStatusTag と同じ流儀で
    // AssetDataBase.Tags(string[])に予約タグを載せる最小実装にした(要判断: docs/28 参照)。
    // "State/仮" 等の仕様書の状態タグとは別の軸(ライフサイクル)なので、プレフィックスは共有しない。
    public static class ArchiveTagService
    {
        public const string Tag = "Archived";

        public static bool IsArchived(AssetDataBase asset)
        {
            if (asset?.Tags == null)
            {
                return false;
            }

            foreach (var tag in asset.Tags)
            {
                if (string.Equals(tag, Tag, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        // Undo.RecordObject + SetDirty(CLAUDE.md §0-5)。既に同じ状態なら何もしない(Undo に無駄な段を積まない)。
        public static void SetArchived(AssetDataBase asset, bool archived)
        {
            if (asset == null || IsArchived(asset) == archived)
            {
                return;
            }

            Undo.RecordObject(asset, archived ? "D-Drive: アセットをアーカイブ" : "D-Drive: アーカイブを解除");

            var next = new List<string>();
            if (asset.Tags != null)
            {
                foreach (var tag in asset.Tags)
                {
                    if (!string.IsNullOrEmpty(tag) && !string.Equals(tag, Tag, StringComparison.Ordinal))
                    {
                        next.Add(tag);
                    }
                }
            }

            if (archived)
            {
                next.Add(Tag);
            }

            asset.Tags = next.ToArray();
            EditorUtility.SetDirty(asset);
        }
    }
}
