using System;
using System.Collections.Generic;
using System.IO;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Import;
using DDrive.Editor.Inspector;
using DDrive.Editor.Materials;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Creation
{
    // [11_tasks.md] U-17(2026-09-17) / [09_editor_tools.md] §1.2 —
    // 「Project で選んだソースアセットから、対応する Data を作る」の本体。
    //
    // 対応種別の洗い出しは新しく表を作らず、**既存の ImportRule(5-11、[09] §1.1)の `IImportRuleHandler` を
    // そのまま流用する**。あのインターフェースが既に「どの拡張子が、どの Data 型の、どのフィールドに入るか」を
    // 宣言しているため、SourceAssets/ のフォルダ監視と Project 右クリックで対応表が二重にならない
    // (Cutscene 等でハンドラが 1 つ増えれば、この右クリックメニューの中身も自動で増える)。
    // ImportRule には無いが「元アセットから作れる」ものだけを ExtraOptions に足す:
    //   - Material(.mat) → MaterialData(既存の UnityMaterialMigrator を呼ぶ。シェーダー変換とテクスチャ取り込みごと)
    //   - Sprite(画像)   → ButtonSkinData / SliderSkinData(Normal 状態の OverrideSprite に入れる)
    //
    // 作成そのものは AssetCreationService.Create(ファイル名・ID・カタログ・Addressables 登録まで 1 回で行う)
    // だけを通す。作成後は U-16 と同じ CreatedAssetOpener.Reveal で専用エディタを開く。
    public static class SourceDataCreation
    {
        // 1 種別ぶんの「ソース → Data」定義。
        public sealed class Option
        {
            public Type DataType;
            public AssetType Target;

            // 対象拡張子(小文字・ドット付き)。メニューの有効/無効判定はこれだけで行う(選択のたびに
            // 重いロードをしないため)。実際に読めるかは作成時に LoadSource が判定する。
            public string[] Extensions;

            // 識別子(ID 定数名)が作れないファイル名(数字始まり等)のときの前置語。
            public string IdentifierFallback;

            public Func<string, UnityEngine.Object> LoadSource;
            public Action<AssetDataBase, UnityEngine.Object, string> Configure;

            // 汎用経路(AssetCreationService.Create)ではなく専用の生成処理を持つ種別用(MaterialData)。
            // 設定されていればこちらが優先される。
            public Func<string, string, AssetDataBase> CreateOverride; // (assetPath, category) -> 作られた Data
        }

        private static List<Option> _options;

        public static IReadOnlyList<Option> Options
        {
            get
            {
                if (_options == null)
                {
                    _options = BuildOptions();
                }

                return _options;
            }
        }

        // ドメインリロード無しでの再収集(テスト用)。
        public static void Invalidate() => _options = null;

        public static Option Find(Type dataType)
        {
            for (var i = 0; i < Options.Count; i++)
            {
                if (Options[i].DataType == dataType)
                {
                    return Options[i];
                }
            }

            return null;
        }

        private static List<Option> BuildOptions()
        {
            var list = new List<Option>();

            // ① ImportRule の 9 ハンドラ(Se / Bgm / Texture / Model / Anim / Anim2D / Prefab / Canvas / Vfx)。
            foreach (var handler in ImportRuleService.Handlers)
            {
                var captured = handler;
                list.Add(new Option
                {
                    DataType = captured.DataType,
                    Target = captured.Target,
                    Extensions = captured.Extensions,
                    IdentifierFallback = captured.IdentifierFallback,
                    LoadSource = path => captured.LoadSource(path),
                    Configure = (data, source, path) => captured.Configure(data, source, path),
                });
            }

            // ② ImportRule に無い分。
            list.AddRange(ExtraOptions());
            return list;
        }

        private static readonly string[] ImageExtensions =
        {
            ".png", ".jpg", ".jpeg", ".tga", ".psd", ".tif", ".tiff", ".exr", ".bmp",
        };

        private static IEnumerable<Option> ExtraOptions()
        {
            // Material(.mat) → MaterialData。シェーダー変換・テクスチャの TextureData 化を含む既存経路
            // (UnityMaterialMigrator、[06] A 実装メモ)をそのまま呼ぶ。重複生成の判定もあちら側が持っている。
            yield return new Option
            {
                DataType = typeof(DDrive.Runtime.Material.MaterialData),
                Target = AssetType.Material,
                Extensions = new[] { ".mat" },
                IdentifierFallback = "Mat",
                LoadSource = path => AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(path),
                CreateOverride = (path, category) =>
                {
                    var material = AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(path);
                    return material == null ? null : UnityMaterialMigrator.Migrate(material, category);
                },
            };

            // Sprite(画像) → Skin。Normal 状態の見た目だけ入れて、他の状態はデザイナーが Skin Editor で足す。
            yield return new Option
            {
                DataType = typeof(ButtonSkinData),
                Target = AssetType.ControlSkin,
                Extensions = ImageExtensions,
                IdentifierFallback = "Skin",
                LoadSource = path => AssetDatabase.LoadAssetAtPath<Sprite>(path),
                Configure = (data, source, path) => ((ControlSkinData)data).Normal.OverrideSprite = (Sprite)source,
            };

            yield return new Option
            {
                DataType = typeof(SliderSkinData),
                Target = AssetType.ControlSkin,
                Extensions = ImageExtensions,
                IdentifierFallback = "SliderSkin",
                LoadSource = path => AssetDatabase.LoadAssetAtPath<Sprite>(path),
                Configure = (data, source, path) => ((ControlSkinData)data).Normal.OverrideSprite = (Sprite)source,
            };
        }

        // ── メニューからの呼び出し ──

        // [MenuItem(..., true)] の validate 用。選択中に「拡張子が合うプロジェクトアセット」が 1 つでもあるか。
        public static bool CanCreate(Type dataType)
        {
            var option = Find(dataType);
            if (option == null)
            {
                return false;
            }

            foreach (var path in SelectedAssetPaths())
            {
                if (Matches(option, path))
                {
                    return true;
                }
            }

            return false;
        }

        // 選択中のアセットすべてについて Data を作る。作成できた最後の 1 件を専用エディタで開く(U-16 と同じ経路)。
        public static void CreateFromSelection(Type dataType)
        {
            var option = Find(dataType);
            if (option == null)
            {
                Debug.LogWarning($"[DDrive] {dataType?.Name} は「ソースアセットから作成」に対応していません。");
                return;
            }

            AssetDataBase last = null;
            var created = 0;
            var skipped = 0;

            foreach (var path in SelectedAssetPaths())
            {
                if (!Matches(option, path))
                {
                    continue;
                }

                var result = CreateOne(option, path, out var wasExisting);
                if (result == null)
                {
                    continue;
                }

                last = result;
                if (wasExisting)
                {
                    skipped++;
                }
                else
                {
                    created++;
                }
            }

            if (last == null)
            {
                Debug.LogWarning($"[DDrive] 選択中のアセットから {dataType.Name} を作れませんでした(対応する拡張子 {string.Join(" / ", option.Extensions)} のアセットを Project で選んでください)。");
                return;
            }

            Debug.Log($"[DDrive] {dataType.Name}: 新規 {created} 件{(skipped > 0 ? $"(既存 {skipped} 件はそのまま開きます)" : string.Empty)}");
            CreatedAssetOpener.Reveal(last);
        }

        // 1 件ぶんの作成。既に同じソースから作られた Data があればそれを返す(wasExisting=true)。
        private static AssetDataBase CreateOne(Option option, string assetPath, out bool wasExisting)
        {
            wasExisting = false;

            var category = ResolveCategory(assetPath);

            if (option.CreateOverride != null)
            {
                // 専用経路(Material)。重複判定・カタログ登録はあちら側の責務。
                return option.CreateOverride(assetPath, category);
            }

            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid))
            {
                return null;
            }

            // ImportRule と同じ ImportSourceGuid で二重生成を防ぐ(同じ元ファイルから 2 つ目を作らない)。
            var existing = FindByImportSource(option.DataType, guid);
            if (existing != null)
            {
                wasExisting = true;
                Debug.LogWarning($"[DDrive] '{Path.GetFileName(assetPath)}' からの {option.DataType.Name} は既にあります: {existing.name}。既存のものを開きます。");
                return existing;
            }

            var source = option.LoadSource?.Invoke(assetPath);
            if (source == null)
            {
                Debug.LogWarning($"[DDrive] '{assetPath}' から {option.DataType.Name} の元データを読み込めませんでした(スキップ)。");
                return null;
            }

            var rawName = Path.GetFileNameWithoutExtension(assetPath);
            var identifier = AssetNamingService.ToIdentifier(rawName, option.IdentifierFallback);

            return AssetCreationService.Create(option.DataType, option.Target, rawName, category, identifier, data =>
            {
                data.ImportSourceGuid = guid;
                option.Configure?.Invoke(data, source, assetPath);
            });
        }

        private static bool Matches(Option option, string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || AssetDatabase.IsValidFolder(assetPath))
            {
                return false;
            }

            var ext = Path.GetExtension(assetPath).ToLowerInvariant();
            return Array.IndexOf(option.Extensions, ext) >= 0;
        }

        // 選択中のオブジェクトのアセットパス(重複除去)。サブアセット(Sprite 等)を選んだ場合も親ファイルのパスになる。
        public static IEnumerable<string> SelectedAssetPaths()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var objects = Selection.objects;
            if (objects == null)
            {
                yield break;
            }

            for (var i = 0; i < objects.Length; i++)
            {
                var path = AssetDatabase.GetAssetPath(objects[i]);
                if (!string.IsNullOrEmpty(path) && seen.Add(path))
                {
                    yield return path;
                }
            }
        }

        // カテゴリ(= GameData 配下のフォルダ階層、[10_workflow.md] §3.3)を元ファイルの置き場所から推測する。
        //  - SourceAssets/<種別>/<カテゴリ...>/ → ImportRule([09] §1.1)と同じ「種別フォルダから先」
        //  - それ以外 → 直上のフォルダ名 1 つ(例: Assets/Art/UI/Btn.png → "UI")
        // どちらも「推測」なので、後から AssetBrowser でカテゴリを変えれば整理メニューがフォルダごと追従する。
        public static string ResolveCategory(string assetPath)
        {
            var directory = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(directory))
            {
                return string.Empty;
            }

            var sourceRoot = ImportRuleService.DefaultSourceRoot + "/";
            if (directory.StartsWith(sourceRoot, StringComparison.Ordinal))
            {
                var rest = directory.Substring(sourceRoot.Length); // "<種別>/<カテゴリ...>" or "<種別>"
                var slash = rest.IndexOf('/');
                return slash < 0 ? string.Empty : rest.Substring(slash + 1);
            }

            var lastSlash = directory.LastIndexOf('/');
            var leaf = lastSlash < 0 ? directory : directory.Substring(lastSlash + 1);
            return string.Equals(leaf, "Assets", StringComparison.Ordinal) ? string.Empty : leaf;
        }

        // ImportSourceGuid が一致する既存 Data を探す(AssetSearch 経由、[09] §9)。
        private static AssetDataBase FindByImportSource(Type dataType, string sourceGuid)
        {
            if (!AssetDatabase.IsValidFolder(AssetCreationService.DefaultGameDataRoot))
            {
                return null;
            }

            foreach (var guid in AssetSearch.FindAssets("t:" + dataType.Name, new[] { AssetCreationService.DefaultGameDataRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.LoadAssetAtPath(path, dataType) is AssetDataBase data
                    && string.Equals(data.ImportSourceGuid, sourceGuid, StringComparison.Ordinal))
                {
                    return data;
                }
            }

            return null;
        }
    }
}
