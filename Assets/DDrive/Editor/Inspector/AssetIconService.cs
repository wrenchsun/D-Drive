using System.IO;
using DDrive.Editor.AssetBrowser;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Inspector
{
    // [09_editor_tools.md] §8.1 — Data アセットのアイコン(AssetDataBase.Icon)を Unity 内で用意する(2026-09-10)。
    //  - フォルダから選択: 画像ファイルを選ぶ。プロジェクト外のファイルは Icons フォルダへコピーして取り込む
    //  - シーンから作成: SceneView(無ければ Main Camera)の見た目を丸ごと撮影 → IconCropWindow で正方形を切り出し → PNG 保存
    //    (撮影は GUI イベントの外(delayCall)で行う。OnInspectorGUI の中で Camera.Render すると URP の RenderPass 内で
    //    衝突して真っ黒になる — 2026-09-10 に実例)
    // 保存先は `Assets/GameData/Icons/<種別>/<アセット名>_Icon.png`(ツール管理、[10] §3)。
    // 取り込んだ画像は Editor 用途(ミップ無し・非圧縮・最大 512)に設定する。
    public static class AssetIconService
    {
        public const string DefaultIconRoot = "Assets/GameData/Icons";
        public const int DefaultSize = 256;
        public const int MaxShotWidth = 1600;
        private const string SizePrefKey = "DDrive.AssetIcon.Size";

        public static readonly int[] SizeChoices = { 128, 256, 512 };

        public static int PreferredSize
        {
            get => EditorPrefs.GetInt(SizePrefKey, DefaultSize);
            set => EditorPrefs.SetInt(SizePrefKey, value);
        }

        // 画像ファイルを選んで Icon に割り当てる。キャンセルなら null。
        public static Texture2D PickFromFolder(AssetDataBase asset, string iconRoot = DefaultIconRoot)
        {
            if (asset == null)
            {
                return null;
            }

            var start = asset.Icon != null ? Path.GetDirectoryName(AssetDatabase.GetAssetPath(asset.Icon)) : iconRoot;
            if (string.IsNullOrEmpty(start) || !Directory.Exists(start))
            {
                start = "Assets";
            }

            var picked = EditorUtility.OpenFilePanelWithFilters("アイコン画像を選択", start, new[] { "画像", "png,jpg,jpeg,tga,psd,bmp,gif", "すべて", "*" });
            if (string.IsNullOrEmpty(picked))
            {
                return null;
            }

            var assetPath = ToAssetPath(picked);
            if (assetPath == null)
            {
                // プロジェクト外 → Icons フォルダへコピーして取り込む
                var folder = IconFolderFor(asset, iconRoot);
                AssetCreationService.EnsureFolder(folder);
                assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{IconFileName(asset)}{Path.GetExtension(picked)}");
                File.Copy(picked, assetPath, overwrite: false);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            }

            ConfigureImporter(assetPath);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (texture == null)
            {
                Debug.LogWarning($"[DDrive] '{assetPath}' をテクスチャとして読み込めませんでした。");
                return null;
            }

            Assign(asset, texture);
            return texture;
        }

        // シーン全体を撮影して切り出しウィンドウを開く(GUI の外で撮る)。
        public static void CaptureFromScene(AssetDataBase asset, string iconRoot = DefaultIconRoot)
        {
            if (asset == null)
            {
                return;
            }

            EditorApplication.delayCall += () => IconCropWindow.Open(asset, iconRoot);
        }

        // 撮影元カメラ(SceneView → Main Camera の順)。無ければ null。
        public static Camera ResolveSourceCamera(out string sourceName)
        {
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null && sceneView.camera != null)
            {
                sourceName = "SceneView";
                return sceneView.camera;
            }

            if (Camera.main != null)
            {
                sourceName = "Main Camera";
                return Camera.main;
            }

            sourceName = null;
            return null;
        }

        // カメラの見た目を読み取り可能な Texture2D にする(縦横比はカメラのまま。呼び出し側が破棄する)。
        public static Texture2D RenderView(Camera source, int maxWidth = MaxShotWidth)
        {
            if (source == null)
            {
                return null;
            }

            var aspect = source.pixelWidth > 0 && source.pixelHeight > 0 ? (float)source.pixelWidth / source.pixelHeight : source.aspect;
            if (aspect <= 0f)
            {
                aspect = 16f / 9f;
            }

            var width = Mathf.Clamp(source.pixelWidth > 0 ? source.pixelWidth : maxWidth, 64, maxWidth);
            var height = Mathf.Max(64, Mathf.RoundToInt(width / aspect));

            // SceneView のカメラは直接 targetTexture を差し替えると表示が乱れるため、設定を写した一時カメラで描く。
            var go = new GameObject("[D-Drive] IconCapture") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            RenderTexture rt = null;
            try
            {
                cam.CopyFrom(source);
                // SceneView 用カメラ(CameraType.SceneView)のまま手動 Render すると URP が何も描かず真っ黒になる。
                // Game カメラとして描く(2026-09-10 に検証: SceneView のまま=0 ピクセル、Game=正常)。
                cam.cameraType = CameraType.Game;
                var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
                if (stage != null && stage.scene.IsValid())
                {
                    cam.scene = stage.scene; // プレハブモードの中身を撮る
                }

                cam.enabled = false;
                cam.aspect = aspect;
                rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                cam.targetTexture = rt;
                cam.Render();

                var previous = RenderTexture.active;
                RenderTexture.active = rt;
                var readback = new Texture2D(width, height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                readback.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                readback.Apply();
                RenderTexture.active = previous;
                return readback;
            }
            finally
            {
                cam.targetTexture = null;
                if (rt != null)
                {
                    rt.Release();
                    Object.DestroyImmediate(rt);
                }

                Object.DestroyImmediate(go);
            }
        }

        // 画像の一部(source 内のピクセル矩形、左上原点)を size×size に縮小して PNG 保存し、Icon に割り当てる。
        public static Texture2D CropAndSave(AssetDataBase asset, Texture2D source, RectInt crop, int size = -1, string iconRoot = DefaultIconRoot)
        {
            if (asset == null || source == null)
            {
                return null;
            }

            if (size <= 0)
            {
                size = PreferredSize;
            }

            crop.width = Mathf.Max(1, crop.width);
            crop.height = Mathf.Max(1, crop.height);
            crop.x = Mathf.Clamp(crop.x, 0, Mathf.Max(0, source.width - crop.width));
            crop.y = Mathf.Clamp(crop.y, 0, Mathf.Max(0, source.height - crop.height));

            // Graphics.Blit の scale/offset で切り出し + 縮小(offset は左下原点)。
            var scale = new Vector2((float)crop.width / source.width, (float)crop.height / source.height);
            var offset = new Vector2((float)crop.x / source.width, 1f - (float)(crop.y + crop.height) / source.height);
            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32);
            Texture2D result = null;
            try
            {
                Graphics.Blit(source, rt, scale, offset);
                var previous = RenderTexture.active;
                RenderTexture.active = rt;
                result = new Texture2D(size, size, TextureFormat.RGBA32, false);
                result.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                result.Apply();
                RenderTexture.active = previous;
                return SavePng(asset, result.EncodeToPNG(), iconRoot);
            }
            finally
            {
                RenderTexture.ReleaseTemporary(rt);
                if (result != null)
                {
                    Object.DestroyImmediate(result);
                }
            }
        }

        // PNG バイト列を規約の場所へ書き、インポートして Icon に割り当てる。
        public static Texture2D SavePng(AssetDataBase asset, byte[] png, string iconRoot = DefaultIconRoot)
        {
            var folder = IconFolderFor(asset, iconRoot);
            AssetCreationService.EnsureFolder(folder);
            var path = $"{folder}/{IconFileName(asset)}.png";
            File.WriteAllBytes(path, png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            ConfigureImporter(path);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }

            if (texture != null)
            {
                Assign(asset, texture);
            }

            return texture;
        }

        public static void Clear(AssetDataBase asset)
        {
            if (asset != null && asset.Icon != null)
            {
                Assign(asset, null);
            }
        }

        public static string IconFolderFor(AssetDataBase asset, string iconRoot = DefaultIconRoot)
        {
            var attr = asset != null ? System.Reflection.CustomAttributeExtensions.GetCustomAttribute<AssetIdDefinitionAttribute>(asset.GetType()) : null;
            var type = attr?.Type ?? AssetType.None;
            return $"{iconRoot}/{type}";
        }

        public static string IconFileName(AssetDataBase asset) => $"{asset.name}_Icon";

        private static void Assign(AssetDataBase asset, Texture2D texture)
        {
            Undo.RecordObject(asset, "Set Asset Icon");
            asset.Icon = texture;
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
        }

        // プロジェクト内の絶対パスなら "Assets/..." に変換。外なら null。
        private static string ToAssetPath(string absolute)
        {
            var full = Path.GetFullPath(absolute).Replace('\\', '/');
            var project = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            if (!full.StartsWith(project, System.StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return "Assets" + full.Substring(project.Length);
        }

        // Editor 表示用の設定(ミップ無し・非圧縮・最大 512・NPOT そのまま)。
        private static void ConfigureImporter(string assetPath)
        {
            if (AssetImporter.GetAtPath(assetPath) is not TextureImporter importer)
            {
                return;
            }

            var changed = false;
            if (importer.textureType != TextureImporterType.Default) { importer.textureType = TextureImporterType.Default; changed = true; }
            if (importer.mipmapEnabled) { importer.mipmapEnabled = false; changed = true; }
            if (importer.textureCompression != TextureImporterCompression.Uncompressed) { importer.textureCompression = TextureImporterCompression.Uncompressed; changed = true; }
            if (importer.maxTextureSize > 512) { importer.maxTextureSize = 512; changed = true; }
            if (importer.npotScale != TextureImporterNPOTScale.None) { importer.npotScale = TextureImporterNPOTScale.None; changed = true; }
            if (changed)
            {
                importer.SaveAndReimport();
            }
        }
    }

    // Inspector 上のアイコン行(サムネイル + 3 ボタン + サイズ)。DataEditorHeader と一緒に AssetDataInspector が描く。
    public static class AssetIconGui
    {
        private const float Thumb = 48f;

        public static void Draw(AssetDataBase target)
        {
            if (target == null)
            {
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                var rect = GUILayoutUtility.GetRect(Thumb, Thumb, GUILayout.Width(Thumb), GUILayout.Height(Thumb));
                EditorGUI.DrawRect(rect, new Color(0.2f, 0.2f, 0.2f));
                if (target.Icon != null)
                {
                    GUI.DrawTexture(rect, target.Icon, ScaleMode.ScaleToFit);
                }
                else
                {
                    GUI.Label(rect, "Icon\nなし", EditorStyles.centeredGreyMiniLabel);
                }

                using (new EditorGUILayout.VerticalScope())
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button(new GUIContent("フォルダから選択", "画像ファイルを選ぶ(プロジェクト外なら Icons フォルダへコピー)")))
                        {
                            AssetIconService.PickFromFolder(target);
                            GUIUtility.ExitGUI();
                        }

                        if (GUILayout.Button(new GUIContent("シーンから作成", "SceneView を丸ごと撮影し、切り出しウィンドウで正方形を選んで PNG にする")))
                        {
                            AssetIconService.CaptureFromScene(target);
                            GUIUtility.ExitGUI();
                        }

                        using (new EditorGUI.DisabledScope(target.Icon == null))
                        {
                            if (GUILayout.Button("クリア", GUILayout.Width(50)))
                            {
                                AssetIconService.Clear(target);
                                GUIUtility.ExitGUI();
                            }
                        }
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var labels = new string[AssetIconService.SizeChoices.Length];
                        var index = 1;
                        for (var i = 0; i < labels.Length; i++)
                        {
                            labels[i] = AssetIconService.SizeChoices[i] + " px";
                            if (AssetIconService.SizeChoices[i] == AssetIconService.PreferredSize)
                            {
                                index = i;
                            }
                        }

                        EditorGUILayout.LabelField("アイコンサイズ", GUILayout.Width(80));
                        var next = EditorGUILayout.Popup(index, labels, GUILayout.Width(70));
                        if (next != index)
                        {
                            AssetIconService.PreferredSize = AssetIconService.SizeChoices[next];
                        }

                        var path = target.Icon != null ? AssetDatabase.GetAssetPath(target.Icon) : "SceneView で対象を映してから「シーンから作成」";
                        EditorGUILayout.LabelField(path, EditorStyles.miniLabel);
                    }
                }
            }

            EditorGUILayout.Space(4f);
        }
    }
}
