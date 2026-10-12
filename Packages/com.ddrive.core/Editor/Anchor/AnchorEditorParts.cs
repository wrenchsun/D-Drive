using System;
using DDrive.Editor.Ux;
using DDrive.Foundation.Data;
using DDrive.Runtime.Anchoring;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Anchor
{
    // Anchor / Anchor Group の新エディターが共有する部品(docs/1010 UX-11)。
    internal static class AnchorUi
    {
        // 「今の設定でどこに出るか」の 1 行説明。prefix は「基準:」「ルート 'x' の基準で」「原点:」など。
        // 旧 AnchorEditorWindow.RefreshStatus / AnchorGroupEditorWindow.RefreshStatus と同じ文言(スポーン先 = 検索の起点)。
        public static string ResolutionText(string prefix, in AnchorDef def, Transform attach)
        {
            switch (def.Space)
            {
                case AnchorSpace.World:
                    return $"{prefix} World 固定(オフセット = ワールド座標 {def.LocalOffset})";

                case AnchorSpace.ContextTarget:
                    return attach != null ? $"{prefix} ✓ スポーン先 '{attach.name}' そのもの" : $"{prefix} ⚠ スポーン先が未指定のためワールド固定扱い";

                default:
                    if (attach == null)
                    {
                        return $"{prefix} ⚠ スポーン先が未指定のため Path を検索できません(ワールド固定扱い)";
                    }

                    if (string.IsNullOrEmpty(def.Path))
                    {
                        return $"{prefix} ⚠ Path が空です。「Path を一覧から選ぶ」でボーンか ★AnchorPoint を選んでください";
                    }

                    var resolved = AnchorResolver.Resolve(def, attach);
                    if (resolved == null)
                    {
                        return $"{prefix} ⚠ '{def.Path}' がスポーン先 '{attach.name}' の階層に見つかりません";
                    }

                    var isPoint = resolved.TryGetComponent<AnchorPoint>(out _);
                    return $"{prefix} ✓ '{resolved.name}'{(isPoint ? "(★AnchorPoint: SpawnOffset / ランダムが追加適用)" : string.Empty)}";
            }
        }

        public static Label Note(string text)
        {
            var l = new Label(text);
            l.AddToClassList("ux-dim");
            l.AddToClassList("ux-note");
            return l;
        }

        public static VisualElement ToolsLine()
        {
            var line = new VisualElement();
            line.AddToClassList("ux-toolsline");
            return line;
        }

        public static string ShortVector(Vector3 v) => $"({v.x:0.##}, {v.y:0.##}, {v.z:0.##})";
    }

    // Data の欄を持たない補助の行(試し出し・連鎖・ヒエラルキーから作る・点の一覧など)。検索・表示モード・目的別カードの対象にはなるが、
    // 変更の件数・検証の結びつけには入らない。sync = 外部変更を取り込む処理(無ければ null)。hideBadge = 「必須」の段でもバッジを出さない。
    internal sealed class AnchorAuxRow : UxRow
    {
        private readonly Action _sync;

        public AnchorAuxRow(FieldGuideEntry entry, VisualElement input, Action sync, bool hideBadge = false, string label = null, string hint = null)
            : base(null, null, new[] { entry }, label ?? entry.Label, hint ?? entry.Hint, entry.Tier, true, input, null)
        {
            _sync = sync;
            NoFold = true;
            if (hideBadge)
            {
                this.Q<Label>(className: "ux-badge")?.Show(false);
            }
        }

        public override void Sync() => _sync?.Invoke();
    }
}
