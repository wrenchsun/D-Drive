using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Ux
{
    // 専用エディターのスクリーンショット(テスト・エージェントの自己確認用。Editor 専用、docs/1010 §2)。
    // OS の画面キャプチャは他のアプリが前面だと使えないため、UnityEditor 内部の GUIView.GrabPixels でウィンドウの描画結果を直接読む
    // (内部 API なのでリフレクション。見つからなければ null / false を返し、例外で止めない)。
    // Light / Dark の切替(SwitchTheme)も含む。窓は「前の描画フレーム」が撮れるので、開いた直後・テーマを替えた直後は
    // 数フレーム(Repaint → 待つ)置いてから Capture すること。
    internal static class UxScreenshot
    {
        // ウィンドウの内容(rootVisualElement の範囲)を画像にする。撮れなければ null。
        // 戻り値の Texture2D は呼び出し側が Object.DestroyImmediate で破棄する(破棄しないとテストの繰り返しでメモリが増え続ける)。
        public static Texture2D Capture(EditorWindow window)
        {
            if (window == null)
            {
                return null;
            }

            try
            {
                var parentField = typeof(EditorWindow).GetField("m_Parent", BindingFlags.Instance | BindingFlags.NonPublic);
                var view = parentField?.GetValue(window);
                if (view == null)
                {
                    return null;
                }

                var guiViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GUIView");
                var grab = guiViewType?.GetMethod("GrabPixels", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (grab == null)
                {
                    return null;
                }

                var ppp = EditorGUIUtility.pixelsPerPoint;
                var area = window.rootVisualElement.worldBound; // ウィンドウ内容の左上を原点とする(タブ・ツールバーは含まない)
                // GrabPixels の矩形はホストビュー(タブ見出しを含む)の左下が原点(GPU 座標)。内容(root)の矩形から求める。
                var hostHeight = (view.GetType().GetProperty("position", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(view) as Rect?)?.height ?? (area.y + area.height);
                var grabY = hostHeight - (area.y + area.height);
                var w = Mathf.Max(1, Mathf.RoundToInt(area.width * ppp));
                var h = Mathf.Max(1, Mathf.RoundToInt(area.height * ppp));
                var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var previous = RenderTexture.active;
                try
                {
                    grab.Invoke(view, new object[] { rt, new Rect(area.x, grabY, area.width, area.height) });
                    RenderTexture.active = rt;
                    var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                    tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    FlipVertically(tex); // GrabPixels の結果は上下が逆(GPU 座標)
                    return tex;
                }
                finally
                {
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(rt);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[D-Drive] ウィンドウの画像化に失敗しました: {e.Message}");
                return null;
            }
        }

        private static void FlipVertically(Texture2D tex)
        {
            var w = tex.width;
            var h = tex.height;
            var src = tex.GetPixels32();
            var dst = new Color32[src.Length];
            for (var y = 0; y < h; y++)
            {
                Array.Copy(src, y * w, dst, (h - 1 - y) * w, w);
            }

            tex.SetPixels32(dst);
            tex.Apply();
        }

        // PNG で保存する。成功したら true。
        public static bool Save(EditorWindow window, string path)
        {
            var tex = Capture(window);
            if (tex == null)
            {
                return false;
            }

            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllBytes(path, tex.EncodeToPNG());
                return true;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        // エディターの配色(Dark / Light)を切り替える。すでにその配色なら何もしない。切り替えたら true。
        // 切替のあと窓がテーマを読み直すまでは数フレームかかる(UxEditorWindow は 150ms の定期更新で追従して作り直す)。
        public static bool SwitchTheme(bool dark)
        {
            if (EditorGUIUtility.isProSkin == dark)
            {
                return false;
            }

            InternalEditorUtility.SwitchSkinAndRepaintAllViews();
            return true;
        }
    }
}
