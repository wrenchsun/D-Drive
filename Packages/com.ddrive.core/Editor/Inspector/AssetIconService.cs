using System;
using System.Collections.Generic;
using System.IO;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Settings;
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
    // 保存先は `Assets/GameData/Icons/<種別>/<アセット名>_<GUID 先頭 8 桁>_Icon.png`(ツール管理、[10] §3。
    // 同名 Data どうしがファイルを奪い合わないよう GUID を混ぜる — 2026-09-11 レビュー対応)。
    // 取り込んだ画像は Editor 用途(ミップ無し・非圧縮・最大 512)に設定する。
    public static class AssetIconService
    {
        public const string DefaultIconRoot = "Assets/GameData/Icons";
        public const int DefaultSize = 256;
        public const int MaxShotWidth = 1600;
        private const string SizePrefKey = "DDrive.AssetIcon.Size";

        // [42_distribution.md] §3.4/§7 B-6(P-5) — Icons は GameData 配下の固定サブフォルダなので、
        // DDriveProjectSettings に専用フィールドは持たず GameDataRoot から導出する。
        private static string ResolveIconRoot(string requested) =>
            string.IsNullOrEmpty(requested) || requested == DefaultIconRoot
                ? $"{DDriveProjectSettings.instance.GameDataRoot}/Icons"
                : requested;

        public static readonly int[] SizeChoices = { 128, 256, 512 };

        public static int PreferredSize
        {
            get => EditorPrefs.GetInt(SizePrefKey, DefaultSize);
            set => EditorPrefs.SetInt(SizePrefKey, value);
        }

        // ── 初期アイコンの自動生成(2026-09-11) ──
        // Data 種別ごとに「元アセット(Prefab / Texture / Sprite)」か「描画(Material 等、元アセットが無くても表現できるもの)」の
        // 提供元を登録し、Icon が未設定なら自動で PNG を作って割り当てる。登録は DefaultIconProviders(元アセット)と
        // MaterialIconProvider(描画)が [InitializeOnLoadMethod] で行う。
        //   元アセット: Texture2D / Sprite はそのまま縮小、GameObject(Prefab)は AssetPreview の描画結果(非同期なので完了を待つ)
        //   描画      : Func<AssetDataBase, int, Texture2D>(size×size の読める Texture2D を返す。呼び出し側が破棄する)

        private static readonly Dictionary<Type, Func<AssetDataBase, UnityEngine.Object>> SourceProviders = new();
        private static readonly Dictionary<Type, Func<AssetDataBase, int, Texture2D>> RenderProviders = new();

        public static void RegisterSource<T>(Func<T, UnityEngine.Object> resolve) where T : AssetDataBase
            => SourceProviders[typeof(T)] = data => resolve((T)data);

        public static void RegisterRenderer<T>(Func<T, int, Texture2D> render) where T : AssetDataBase
            => RenderProviders[typeof(T)] = (data, size) => render((T)data, size);

        // その Data に自動生成の手段があるか(元アセットが実際に入っているか、描画できる種別か)。
        public static bool CanCreateDefaultIcon(AssetDataBase asset)
        {
            if (asset == null)
            {
                return false;
            }

            if (FindProvider(RenderProviders, asset.GetType()) != null)
            {
                return true;
            }

            var source = FindProvider(SourceProviders, asset.GetType());
            return source != null && source(asset) != null;
        }

        public static string DescribeDefaultIconSource(AssetDataBase asset)
        {
            if (asset == null)
            {
                return "";
            }

            if (FindProvider(RenderProviders, asset.GetType()) != null)
            {
                return "描画から生成";
            }

            var source = FindProvider(SourceProviders, asset.GetType());
            var obj = source?.Invoke(asset);
            return obj != null ? $"元アセット({obj.name})から生成" : "元アセット未設定";
        }

        // 自動生成して割り当てる。Prefab のプレビューは非同期なので完了時に onDone(null なら失敗)を呼ぶ。
        // 戻り値: 開始できたか(提供元が無い / 元アセット未設定なら false)。
        public static bool TryCreateDefaultIcon(AssetDataBase asset, Action<Texture2D> onDone = null, int size = -1, string iconRoot = DefaultIconRoot, bool recordUndo = true)
        {
            if (asset == null)
            {
                return false;
            }

            if (size <= 0)
            {
                size = PreferredSize;
            }

            var render = FindProvider(RenderProviders, asset.GetType());
            if (render != null)
            {
                Texture2D rendered = null;
                try
                {
                    rendered = render(asset, size);
                    var icon = rendered != null ? CropAndSave(asset, rendered, new RectInt(0, 0, rendered.width, rendered.height), size, iconRoot, recordUndo) : null;
                    onDone?.Invoke(icon);
                    // 一括生成中(_batching)は PNG を書くだけで割り当ては EndBatch なので、icon は null でも「開始した」。
                    return icon != null || (_batching && rendered != null);
                }
                finally
                {
                    if (rendered != null)
                    {
                        UnityEngine.Object.DestroyImmediate(rendered);
                    }
                }
            }

            // 注意: onDone?.Invoke(CropAndSave(...)) と書くと onDone が null のとき引数(= 生成処理)ごと評価されない。
            // 生成は必ず別の文で行ってからコールバックする(2026-09-11 に一括生成で実害)。
            var resolve = FindProvider(SourceProviders, asset.GetType());
            var source = resolve?.Invoke(asset);
            switch (source)
            {
                case Sprite sprite when sprite.texture != null:
                {
                    var r = sprite.textureRect;
                    var crop = new RectInt(Mathf.RoundToInt(r.x), Mathf.RoundToInt(sprite.texture.height - r.yMax), Mathf.RoundToInt(r.width), Mathf.RoundToInt(r.height));
                    var icon = CropAndSave(asset, sprite.texture, crop, size, iconRoot, recordUndo);
                    onDone?.Invoke(icon);
                    return true;
                }
                case Texture2D texture:
                {
                    var icon = CropAndSave(asset, texture, new RectInt(0, 0, texture.width, texture.height), size, iconRoot, recordUndo);
                    onDone?.Invoke(icon);
                    return true;
                }
                case GameObject prefab:
                    WaitForAssetPreview(prefab, preview =>
                    {
                        Texture2D icon = null;
                        if (asset != null && preview != null)
                        {
                            icon = CropAndSave(asset, preview, new RectInt(0, 0, preview.width, preview.height), size, iconRoot, recordUndo);
                        }
                        else if (asset != null)
                        {
                            Debug.LogWarning($"[DDrive] '{asset.name}' の初期アイコン: Prefab '{prefab.name}' のプレビューが時間内に生成されませんでした。Inspector の「自動生成」で再試行してください。");
                        }

                        onDone?.Invoke(icon);
                    });
                    return true;
                default:
                    return false;
            }
        }

        // Icon 未設定で自動生成できる Data すべてに生成する(メニュー / 一括用)。戻り値は開始した件数。
        // 2026-09-11 レビュー対応: 1 件ごとに ImportAsset / SaveAndReimport すると、そのたびに projectChanged と
        // OnPostprocessAllAssets が飛んで AssetSearch のキャッシュと MaterialIconProvider の Registry が捨てられ、
        // 件数分の FindAssets が走っていた。PNG の書き出しだけ先に済ませ、インポートと割り当てを最後にまとめて行う。
        public static int CreateDefaultIconsForAll(bool onlyMissing = true)
        {
            var targets = new List<AssetDataBase>();
            foreach (var guid in AssetSearch.FindAssets("t:" + nameof(AssetDataBase)))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/Tests/"))
                {
                    continue;
                }

                var data = AssetDatabase.LoadAssetAtPath<AssetDataBase>(path);
                if (data == null || (onlyMissing && data.Icon != null) || !CanCreateDefaultIcon(data))
                {
                    continue;
                }

                targets.Add(data);
            }

            var started = 0;
            BeginBatch();
            try
            {
                for (var i = 0; i < targets.Count; i++)
                {
                    if (TryCreateDefaultIcon(targets[i]))
                    {
                        started++;
                    }
                }
            }
            finally
            {
                EndBatch();
            }

            return started;
        }

        // ── 一括書き出し(PNG だけ先に書き、インポート / 割り当ては最後にまとめる) ──
        private static bool _batching;
        private static readonly List<(AssetDataBase asset, string path, bool recordUndo)> PendingBatch = new();

        private static void BeginBatch()
        {
            _batching = true;
            PendingBatch.Clear();
        }

        // 書き出した PNG をまとめてインポートし、Importer 設定と Icon 割り当てを行う。
        private static void EndBatch()
        {
            _batching = false;
            if (PendingBatch.Count == 0)
            {
                return;
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            for (var i = 0; i < PendingBatch.Count; i++)
            {
                var (asset, path, recordUndo) = PendingBatch[i];
                if (asset == null)
                {
                    continue;
                }

                ConfigureImporter(path);
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture == null)
                {
                    Debug.LogWarning($"[DDrive] '{asset.name}' の初期アイコン: '{path}' をテクスチャとして読み込めませんでした。");
                    continue;
                }

                Assign(asset, texture, recordUndo);
            }

            PendingBatch.Clear();
        }

        // AssetPreview は初回 null(バックグラウンド生成)なので、生成が終わるまで EditorApplication.update で待つ(最大 30 秒。
        // エディタが非フォーカスだと生成が遅いので長め)。
        // 2026-09-11 レビュー対応: 一括生成で全 Prefab 分の待ちを同時に走らせると AssetPreview のキャッシュが溢れて
        // どれも生成されないまま 30 秒待つことがあったため、同時に待つのは MaxConcurrentPreviewWaits 件までにして順番待ちさせる。
        private const double PreviewTimeoutSec = 30.0;
        private const int MaxConcurrentPreviewWaits = 4;

        private static readonly Queue<(UnityEngine.Object target, Action<Texture2D> onReady)> PreviewQueue = new();
        private static int _activePreviewWaits;

        private static void WaitForAssetPreview(UnityEngine.Object target, Action<Texture2D> onReady)
        {
            PreviewQueue.Enqueue((target, onReady));
            PumpPreviewQueue();
        }

        private static void PumpPreviewQueue()
        {
            while (_activePreviewWaits < MaxConcurrentPreviewWaits && PreviewQueue.Count > 0)
            {
                var (target, onReady) = PreviewQueue.Dequeue();
                BeginPreviewWait(target, onReady);
            }
        }

        private static void BeginPreviewWait(UnityEngine.Object target, Action<Texture2D> onReady)
        {
            _activePreviewWaits++;
            var deadline = EditorApplication.timeSinceStartup + PreviewTimeoutSec;
            void Poll()
            {
                var preview = target != null ? AssetPreview.GetAssetPreview(target) : null;
                if (preview == null && target != null && EditorApplication.timeSinceStartup <= deadline)
                {
                    return;
                }

                EditorApplication.update -= Poll;
                _activePreviewWaits--;
                try
                {
                    onReady(preview);
                }
                finally
                {
                    PumpPreviewQueue();
                }
            }

            EditorApplication.update += Poll;
        }

        private static TFunc FindProvider<TFunc>(Dictionary<Type, TFunc> table, Type type) where TFunc : class
        {
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                if (table.TryGetValue(t, out var f))
                {
                    return f;
                }
            }

            return null;
        }

        [MenuItem(DDrive.Editor.Menu.DDriveMenu.Generate + "初期アイコンを生成(未設定の Data のみ)")]
        private static void CreateDefaultIconsMenu()
        {
            var n = CreateDefaultIconsForAll(onlyMissing: true);
            Debug.Log($"[DDrive] 初期アイコンの生成を {n} 件開始しました(Prefab のプレビューは数秒後に反映)。");
        }

        // 画像ファイルを選んで Icon に割り当てる。キャンセルなら null。
        public static Texture2D PickFromFolder(AssetDataBase asset, string iconRoot = DefaultIconRoot)
        {
            iconRoot = ResolveIconRoot(iconRoot);
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
                    UnityEngine.Object.DestroyImmediate(rt);
                }

                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // [09_editor_tools.md] §8.2 — Project ウィンドウのグリッド表示サムネイル(5-10)用に、Icon(128/256/512px 想定)を
        // 要求された width×height にそのまま縮小する(クロップ無し)。RenderStaticPreview はズームレベルごとに違う
        // サイズを要求してくるため、都度この関数で作り直す(呼び出し側が破棄する。null なら生成不可)。
        public static Texture2D ScaleForPreview(Texture2D source, int width, int height)
        {
            if (source == null || width <= 0 || height <= 0)
            {
                return null;
            }

            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            try
            {
                Graphics.Blit(source, rt);
                var previous = RenderTexture.active;
                RenderTexture.active = rt;
                var result = new Texture2D(width, height, TextureFormat.RGBA32, false);
                result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                result.Apply();
                RenderTexture.active = previous;
                return result;
            }
            finally
            {
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        // 画像の一部(source 内のピクセル矩形、左上原点)を size×size に縮小して PNG 保存し、Icon に割り当てる。
        public static Texture2D CropAndSave(AssetDataBase asset, Texture2D source, RectInt crop, int size = -1, string iconRoot = DefaultIconRoot, bool recordUndo = true)
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
                return SavePng(asset, result.EncodeToPNG(), iconRoot, recordUndo);
            }
            finally
            {
                RenderTexture.ReleaseTemporary(rt);
                if (result != null)
                {
                    UnityEngine.Object.DestroyImmediate(result);
                }
            }
        }

        // PNG バイト列を規約の場所へ書き、インポートして Icon に割り当てる。
        // 一括生成中(BeginBatch〜EndBatch)は書き出しだけ行い、インポートと割り当ては EndBatch でまとめて行う(戻り値は null)。
        public static Texture2D SavePng(AssetDataBase asset, byte[] png, string iconRoot = DefaultIconRoot, bool recordUndo = true)
        {
            iconRoot = ResolveIconRoot(iconRoot);
            var folder = IconFolderFor(asset, iconRoot);
            AssetCreationService.EnsureFolder(folder);
            var path = IconAssetPath(asset, folder, iconRoot);
            File.WriteAllBytes(path, png);
            if (_batching)
            {
                PendingBatch.Add((asset, path, recordUndo));
                return null;
            }

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
                Assign(asset, texture, recordUndo);
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
            iconRoot = ResolveIconRoot(iconRoot);
            var attr = asset != null ? System.Reflection.CustomAttributeExtensions.GetCustomAttribute<AssetIdDefinitionAttribute>(asset.GetType()) : null;
            var type = attr?.Type ?? AssetType.None;
            return $"{iconRoot}/{type}";
        }

        // 2026-09-11 レビュー対応: 種別が同じで名前も同じ Data(別カテゴリの同名など)が同じ PNG を上書きし合っていたので、
        // アセットの GUID 先頭 8 桁をファイル名に混ぜて一意にする。GUID が取れない(まだアセットでない)ときは名前だけ。
        public static string IconFileName(AssetDataBase asset)
        {
            if (asset == null)
            {
                return "Icon";
            }

            var assetPath = AssetDatabase.GetAssetPath(asset);
            var guid = string.IsNullOrEmpty(assetPath) ? null : AssetDatabase.AssetPathToGUID(assetPath);
            return string.IsNullOrEmpty(guid) ? $"{asset.name}_Icon" : $"{asset.name}_{guid.Substring(0, 8)}_Icon";
        }

        // 書き出し先の PNG パス。既にアイコン置き場の PNG が割り当たっているなら、そのファイルを上書きする
        // (アイコンを作り直してもファイルが増えない。リネーム前の名前で作られた既存アイコンもそのまま使い続ける)。
        private static string IconAssetPath(AssetDataBase asset, string folder, string iconRoot)
        {
            var current = asset != null && asset.Icon != null ? AssetDatabase.GetAssetPath(asset.Icon) : null;
            if (!string.IsNullOrEmpty(current) && current.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                && current.StartsWith(iconRoot + "/", StringComparison.Ordinal))
            {
                return current;
            }

            return $"{folder}/{IconFileName(asset)}.png";
        }

        private static void Assign(AssetDataBase asset, Texture2D texture, bool recordUndo = true)
        {
            // 2026-09-11 レビュー対応: アセット作成直後の自動生成は recordUndo=false。
            // 作成自体が Undo 対象でないため、ここで Undo を積むと Ctrl+Z が「アイコン割り当てだけ」を取り消して紛らわしい。
            if (recordUndo)
            {
                Undo.RecordObject(asset, "Set Asset Icon");
            }

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

                        using (new EditorGUI.DisabledScope(!AssetIconService.CanCreateDefaultIcon(target)))
                        {
                            if (GUILayout.Button(new GUIContent("自動生成", AssetIconService.DescribeDefaultIconSource(target) + "。Prefab / Texture / Sprite は元アセットの見た目、Material は描画から作る")))
                            {
                                AssetIconService.TryCreateDefaultIcon(target);
                                GUIUtility.ExitGUI();
                            }
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

                        var path = target.Icon != null
                            ? AssetDatabase.GetAssetPath(target.Icon)
                            : AssetIconService.CanCreateDefaultIcon(target) ? "「自動生成」で " + AssetIconService.DescribeDefaultIconSource(target) : "SceneView で対象を映してから「シーンから作成」";
                        EditorGUILayout.LabelField(path, EditorStyles.miniLabel);
                    }
                }
            }

            EditorGUILayout.Space(4f);
        }
    }
}
