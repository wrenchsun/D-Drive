using System;
using System.IO;
using System.Text;
using DDrive.Editor.AssetBrowser;
using DDrive.Foundation.Data;
using DDrive.Foundation.Extension;
using DDrive.Foundation.Identity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DDrive.Editor.Extension
{
    // [1014_extension_platform.md] §2.4(X-4、2026-10-16) — ISampleProvider / IPreviewSceneProvider の呼び出しの実体(副作用側)。
    // 例外で止めない: provider の例外・欠けたシーンは警告 + no-op。
    public static class ExtensionProviderActions
    {
        public const string SampleCategory = "Sample";

        // 空状態の「サンプルを作る」: 同じ DisplayName のサンプル(カテゴリ Sample)が既にあればそれを返し、無ければ provider に作らせる。
        // 保存していない Data が返れば AssetCreationService.Create で保存・カタログ・Addressables 登録、保存済みなら RegisterExisting で登録だけ。
        // gameDataRoot の既定は DDriveProjectSettings.GameDataRoot(テストは一時フォルダを渡す)。作れなければ null。
        public static AssetDataBase CreateOrGetSample(ISampleProvider provider, Type dataType, string gameDataRoot = AssetCreationService.DefaultGameDataRoot)
        {
            if (provider == null || dataType == null)
            {
                return null;
            }

            var display = string.IsNullOrEmpty(provider.DisplayName) ? provider.KindKey : provider.DisplayName;
            var existing = FindSample(dataType, display);
            if (existing != null)
            {
                return existing;
            }

            var root = AssetCreationService.ResolveGameDataRoot(gameDataRoot);
            var kindFolder = AssetKindRegistry.TryGet(provider.KindKey, out var kind) && !string.IsNullOrEmpty(kind.Folder) ? kind.Folder : provider.KindKey;
            var folder = $"{root}/{kindFolder}/{SampleCategory}";
            AssetCreationService.EnsureFolder(folder);

            AssetDataBase made;
            try
            {
                made = provider.CreateSample(folder);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DDrive] ISampleProvider '{provider.GetType().FullName}'.CreateSample が例外を出しました(サンプルは作りません): {e.Message}");
                return null;
            }

            if (made == null)
            {
                return null;
            }

            if (EditorUtility.IsPersistent(made))
            {
                AssetCreationService.RegisterExisting(made, AssetType.Extension, root);
                return made;
            }

            var template = made;
            var created = AssetCreationService.Create(
                template.GetType(), AssetType.Extension, display, SampleCategory, IdentifierFor(display),
                a =>
                {
                    var flags = a.Flags; // 種別の既定 Load(Preload など)は作成側が決めた値を残す
                    EditorUtility.CopySerialized(template, a);
                    a.Flags = flags;
                    a.DisplayName = display;
                    a.Category = SampleCategory;
                },
                root);
            UnityEngine.Object.DestroyImmediate(template);
            return created;
        }

        private static AssetDataBase FindSample(Type dataType, string display)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:" + dataType.Name))
            {
                var d = AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guid), dataType) as AssetDataBase;
                if (d != null && d.GetType() == dataType && d.Category == SampleCategory && d.DisplayName == display)
                {
                    return d;
                }
            }

            return null;
        }

        // 表示名から PascalCase 英数字の識別子を作る(使える文字が無ければ "Sample")。
        private static string IdentifierFor(string display)
        {
            var sb = new StringBuilder();
            var upperNext = true;
            foreach (var c in display ?? string.Empty)
            {
                if (c < 128 && char.IsLetterOrDigit(c))
                {
                    sb.Append(upperNext ? char.ToUpperInvariant(c) : c);
                    upperNext = false;
                }
                else
                {
                    upperNext = true;
                }
            }

            var id = sb.ToString();
            if (id.Length == 0 || char.IsDigit(id[0]))
            {
                id = "Sample" + id;
            }

            return id;
        }

        // ヒーローの「確認用シーン」: provider のシーンを開く。未保存のシーンがあれば先に確認する(キャンセルで false)。
        // 既にそのシーンが開いていれば開き直さない。開いた直後に provider.Setup(scene)。シーンが無ければ警告して false。
        public static bool OpenPreviewScene(IPreviewSceneProvider provider)
        {
            if (provider == null)
            {
                return false;
            }

            var path = provider.ScenePath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Debug.LogWarning($"[DDrive] IPreviewSceneProvider '{provider.GetType().FullName}': 確認用シーンが見つかりません: {path}");
                return false;
            }

            if (SceneManager.GetActiveScene().path == path)
            {
                return true;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return false;
            }

            Scene scene;
            try
            {
                scene = EditorSceneManager.OpenScene(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DDrive] 確認用シーンを開けませんでした({path}): {e.Message}");
                return false;
            }

            try
            {
                provider.Setup(scene);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DDrive] IPreviewSceneProvider '{provider.GetType().FullName}'.Setup が例外を出しました(続行します): {e.Message}");
            }

            return true;
        }
    }
}
