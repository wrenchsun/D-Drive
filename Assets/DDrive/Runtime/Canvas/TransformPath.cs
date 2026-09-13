using System.Text;
using UnityEngine;

namespace DDrive.Runtime.Ui
{
    // root から target までの相対パス("A/B/C"。Transform.Find にそのまま渡せる形)。target が root 自身なら空文字。
    // UiManager / CanvasDataValidator / 各エディタに同じ実装が 7 か所あったのを集約した(docs/24 整理項目 6、2026-09-14)。
    // target が root の子孫でないときは、辿れたところ(シーンのルート)までの名前を返す(従来の実装と同じ挙動)。
    public static class TransformPath
    {
        private static readonly StringBuilder Builder = new();

        public static string GetRelative(Transform root, Transform target)
        {
            if (target == null || target == root)
            {
                return string.Empty;
            }

            Builder.Clear();
            for (var cur = target; cur != null && cur != root; cur = cur.parent)
            {
                if (Builder.Length > 0)
                {
                    Builder.Insert(0, '/');
                }

                Builder.Insert(0, cur.name);
            }

            return Builder.ToString();
        }
    }
}
