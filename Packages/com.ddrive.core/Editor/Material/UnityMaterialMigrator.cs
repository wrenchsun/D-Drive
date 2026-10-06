using System.Collections.Generic;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Menu;
using DDrive.Foundation.Data;
using DDrive.Runtime.Material;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DDrive.Editor.Materials
{
    // [06_material_texture.md] A-2 — Unity 標準シェーダー(URP Lit / Unlit、Built-in Standard / Unlit)を使っている既存の
    // Material アセットから、D-Drive 標準シェーダー(DDrive/Lit / DDrive/Unlit)の MaterialData を生成する(2026-09-11)。
    //   1. 変換先シェーダーを元シェーダーから決める(ResolveTargetShader)
    //   2. 共通チャンネルは MayaMaterialImporter.ImportMaterial(Unity Material → MaterialCommon + TextureData)で写す
    //   3. 変換先の固有(Specific)は既定値で登録したうえで、元 Material に同名プロパティがあれば値を引き継ぐ
    // 同じ Material を再実行すると SourceMaterial("UnityMaterial/<Material の GUID>/<名前>")で同定して Common だけ更新する
    // (固有調整は保持)。GUID を挟むのは、同名 Material が別フォルダにあると 1 つの MaterialData を奪い合うため(2026-09-11)。
    // Material アセットに加えて、Prefab / モデルを選んだ場合は Renderer の sharedMaterials も対象にする。
    public static class UnityMaterialMigrator
    {
        public const string SourceKey = "UnityMaterial";
        public const string LitShaderName = "DDrive/Lit";
        public const string UnlitShaderName = "DDrive/Unlit";

        // 元シェーダー名 → D-Drive 標準シェーダー名。未知のシェーダーは null(呼び出し側が Lit に倒すか決める)。
        private static readonly Dictionary<string, string> ShaderMap = new()
        {
            { "Universal Render Pipeline/Lit", LitShaderName },
            { "Universal Render Pipeline/Simple Lit", LitShaderName },
            { "Universal Render Pipeline/Complex Lit", LitShaderName },
            { "Universal Render Pipeline/Baked Lit", LitShaderName },
            { "Standard", LitShaderName },
            { "Standard (Specular setup)", LitShaderName },
            { "Universal Render Pipeline/Unlit", UnlitShaderName },
            { "Unlit/Texture", UnlitShaderName },
            { "Unlit/Color", UnlitShaderName },
            { "Unlit/Transparent", UnlitShaderName },
            { "Unlit/Transparent Cutout", UnlitShaderName },
            { LitShaderName, LitShaderName },
            { UnlitShaderName, UnlitShaderName },
        };

        public static bool IsSupported(Shader source) => source != null && ShaderMap.ContainsKey(source.name);

        // 変換先。未対応のシェーダーは null。
        public static Shader ResolveTargetShader(Shader source)
        {
            if (source == null || !ShaderMap.TryGetValue(source.name, out var targetName))
            {
                return null;
            }

            return Shader.Find(targetName);
        }

        // 1 Material 分。category は省略時にアセットの親フォルダ名。
        // 知らないシェーダーの扱いは Profile の UnknownShaderPolicy に従う(Ask は非対話なので従来どおり Lit に変換。FC-15)。
        public static MaterialData Migrate(UnityEngine.Material source, string category = null, MayaMaterialImporter.Report report = null,
            string gameDataRoot = AssetCreationService.DefaultGameDataRoot)
            => MigrateCore(source, category, report, gameDataRoot, null);

        // 呼び出し側が 1 操作分に解決した扱い(UnknownShaderGuard.TryResolve)を明示的に渡す版。
        public static MaterialData Migrate(UnityEngine.Material source, string category, MayaMaterialImporter.Report report,
            string gameDataRoot, UnknownShaderHandling unknownShader)
            => MigrateCore(source, category, report, gameDataRoot, unknownShader);

        private static MaterialData MigrateCore(UnityEngine.Material source, string category, MayaMaterialImporter.Report report,
            string gameDataRoot, UnknownShaderHandling? explicitHandling)
        {
            report ??= new MayaMaterialImporter.Report();
            if (source == null)
            {
                return null;
            }

            var target = ResolveTargetShader(source.shader);
            // 元の .mat が「知らないシェーダー」か / 欠けているか(FX-R-09 / FX-R-02)。既存 Data の扱いはこの 2 つで決める。
            var sourceUnknown = target == null && UnknownShaderGuard.IsUnknown(source.shader);
            var sourceMissing = UnknownShaderGuard.IsMissing(source.shader);
            // 元のシェーダーが欠けていて既存の Data があるときは、入口で何も書かずに警告して抜ける(FY-R-01)。
            // Shader / Specific だけでなく Common(色・テクスチャ等)も、欠けたシェーダーの Material から読み直した値では上書きしない。
            if (sourceMissing && MayaMaterialImporter.TryKeepExistingWhenShaderMissing(source, SourceKey, report, gameDataRoot, out var keptData))
            {
                return keptData;
            }

            // 知らないシェーダーを保つ(FC-15)。Profile の検索は知らないシェーダーのときだけ行う(一括変換での無駄な検索を避ける)。
            var keepSource = false;
            if (sourceUnknown)
            {
                var handling = explicitHandling ?? UnknownShaderGuard.HandlingFor(MayaImportProfile.FindOrDefault());
                if (handling == UnknownShaderHandling.Keep)
                {
                    target = source.shader;
                    keepSource = true;
                    report.Log($"元のシェーダーを保ちます: '{source.name}'({source.shader.name})");
                }
            }

            if (target == null)
            {
                target = Shader.Find(LitShaderName);
                if (UnknownShaderGuard.IsMissing(source.shader))
                {
                    // シェーダー参照が欠けている(FC-R-02)。保っても常にピンクなので Policy に関わらず、新規の Data は Lit にする。
                    // 既存の Data があるときはここへ来る前の入口で何も書かずに抜けている(FX-R-02 / FY-R-01)。
                    report.Warn($"'{source.name}' のシェーダーが見つかりません(パッケージ未導入・参照切れ)。新規の MaterialData は {LitShaderName} として作成します"
                               + "(既存の MaterialData があれば何も変更しません)");
                }
                else
                {
                    report.Log($"警告: '{source.name}' のシェーダー '{(source.shader != null ? source.shader.name : "null")}' は未対応のため {LitShaderName} として変換します"
                               + "(Profile の UnknownShaderPolicy を KeepSource にすると元のシェーダーを保てます)");
                }

                if (target == null)
                {
                    // [42_distribution.md] §2.3-1(P-4、2026-09-20) — シェーダーはパッケージ側(Packages/com.ddrive.core/Runtime/Shaders)へ移設済み。
                    report.Log($"エラー: {LitShaderName} が見つかりません(Packages/com.ddrive.core/Runtime/Shaders を確認)");
                    return null;
                }
            }

            var profile = ScriptableObject.CreateInstance<MayaImportProfile>();
            profile.hideFlags = HideFlags.HideAndDontSave;
            profile.TargetShader = target;
            profile.PreserveSpecificOnReimport = true;
            try
            {
                if (string.IsNullOrEmpty(category))
                {
                    category = profile.ResolveCategory(AssetDatabase.GetAssetPath(source));
                    if (string.IsNullOrEmpty(category))
                    {
                        category = "Migrated";
                    }
                }

                var unchangedBefore = report.Unchanged;
                var data = MayaMaterialImporter.ImportMaterial(source, category, SourceKey, profile, report, gameDataRoot);
                if (data == null)
                {
                    return null;
                }

                var countedUnchanged = report.Unchanged > unchangedBefore; // Common に差分が無く「変更なし」に数えられた
                var shaderChanged = false;

                // 知らないシェーダーを保つとき、既に有効なシェーダーが入っている既存 Data は上書きしない(FC-15)。
                // 既存 Data のシェーダーが知らないシェーダーで、呼び出しが「既存は上書きしない」Convert(Ask の非対話既定)なら寄せない(FC-R-01)。
                if (data.Shader != target && !(keepSource && data.Shader != null) && !KeepsExistingUnknownShader(data.Shader, explicitHandling, sourceUnknown))
                {
                    // 再実行時に既存 Data のシェーダーが違う(例: 以前 URP Lit のまま作った)場合も D-Drive 標準へ寄せる
                    Undo.RecordObject(data, "Migrate Unity Material");
                    data.Shader = target;
                    data.Specific = MaterialSpecificResolver.Merge(data.Specific, target);
                    EditorUtility.SetDirty(data);
                    shaderChanged = true;
                }

                var copied = CopySpecificValues(source, data);
                if (copied.Count > 0)
                {
                    report.Log($"固有を引き継ぎ: {data.name} ← {string.Join(", ", copied)}");
                }

                // Common は同じでも、シェーダーの寄せ・固有の引き継ぎで既存 Data を書き換えたなら「更新」に数え直す(レビュー [63] GE-R-03)。
                if (countedUnchanged && (shaderChanged || copied.Count > 0))
                {
                    report.Unchanged--;
                    report.Updated++;
                }

                AssetDatabase.SaveAssetIfDirty(data);
                return data;
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        // 既存 Data の知らないシェーダーを保つか。元の .mat 自身も知らないシェーダーのときだけ(FX-R-09)。元の .mat を
        // 標準シェーダー(URP Lit 等)に戻した場合は、従来どおり D-Drive 標準へ変換する。
        private static bool KeepsExistingUnknownShader(Shader existing, UnknownShaderHandling? explicitHandling, bool sourceUnknown)
        {
            if (!sourceUnknown || !UnknownShaderGuard.IsUnknown(existing))
            {
                return false;
            }

            var handling = explicitHandling ?? UnknownShaderGuard.HandlingFor(MayaImportProfile.FindOrDefault());
            return handling == UnknownShaderHandling.ConvertKeepingExisting;
        }

        // 変換先の固有(Specific に登録済み)のうち、元 Material に同名プロパティがあるものは値を引き継ぐ。戻り値は引き継いだ名前。
        // recordUndo=false は「まだアセットになっていない Data」用(AssetCreationService.Create の configure は CreateAsset の
        // 前に呼ばれるため、Undo.RecordObject が意味を持たない。2026-09-11 レビュー対応)。
        public static List<string> CopySpecificValues(UnityEngine.Material source, MaterialData data, bool recordUndo = true)
        {
            var copied = new List<string>();
            if (source == null || data == null || data.Specific == null || data.Shader == null)
            {
                return copied;
            }

            var changed = false;
            var specific = data.Specific;
            for (var i = 0; i < specific.Length; i++)
            {
                var name = specific[i].Property;
                if (string.IsNullOrEmpty(name) || !source.HasProperty(name))
                {
                    continue;
                }

                var index = source.shader.FindPropertyIndex(name);
                if (index < 0)
                {
                    continue;
                }

                var value = specific[i].Value;
                var type = source.shader.GetPropertyType(index);
                switch (type)
                {
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                        if (value.Type == ParamValueType.Float) { value.FloatValue = source.GetFloat(name); }
                        else if (value.Type == ParamValueType.Int) { value.IntValue = Mathf.RoundToInt(source.GetFloat(name)); }
                        else { continue; }
                        break;
                    case ShaderPropertyType.Int:
                        if (value.Type == ParamValueType.Int) { value.IntValue = source.GetInteger(name); }
                        else if (value.Type == ParamValueType.Float) { value.FloatValue = source.GetInteger(name); }
                        else { continue; }
                        break;
                    case ShaderPropertyType.Color:
                        if (value.Type != ParamValueType.Color) { continue; }
                        value.ColorValue = source.GetColor(name);
                        break;
                    case ShaderPropertyType.Vector:
                        if (value.Type != ParamValueType.Vector) { continue; }
                        value.VectorValue = source.GetVector(name);
                        break;
                    case ShaderPropertyType.Texture:
                        if (value.Type != ParamValueType.Object) { continue; }
                        value.ObjectValue = source.GetTexture(name);
                        break;
                    default:
                        continue;
                }

                // 値が同じなら書かない・引き継いだ名前にも入れない(変えていない .mat の再実行が「更新」に数えられないように。レビュー [63] GE-R-09)。
                if (SameValue(specific[i].Value, value))
                {
                    continue;
                }

                if (!changed)
                {
                    if (recordUndo)
                    {
                        Undo.RecordObject(data, "Migrate Unity Material Specific");
                    }

                    changed = true;
                }

                specific[i].Value = value;
                copied.Add(name);
            }

            if (changed)
            {
                data.Specific = specific;
                EditorUtility.SetDirty(data);
            }

            return copied;
        }

        // ParamValue の等価(型と、その型の値だけを比べる。Float は完全一致 = Material から読んだ値をそのまま書くため)。
        private static bool SameValue(in ParamValue a, in ParamValue b)
        {
            if (a.Type != b.Type)
            {
                return false;
            }

            switch (a.Type)
            {
                case ParamValueType.Float: return a.FloatValue == b.FloatValue;
                case ParamValueType.Int: return a.IntValue == b.IntValue;
                case ParamValueType.Color: return a.ColorValue == b.ColorValue;
                case ParamValueType.Vector: return a.VectorValue == b.VectorValue;
                case ParamValueType.Object: return a.ObjectValue == b.ObjectValue;
                default: return false;
            }
        }

        // 選択(Material アセット / Prefab・モデルの Renderer)から対象 Material を集める。重複なし。
        public static List<UnityEngine.Material> CollectFromSelection(IEnumerable<Object> selection)
        {
            var result = new List<UnityEngine.Material>();
            var seen = new HashSet<UnityEngine.Material>();
            void Add(UnityEngine.Material m)
            {
                if (m != null && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(m)) && seen.Add(m))
                {
                    result.Add(m);
                }
            }

            foreach (var obj in selection)
            {
                switch (obj)
                {
                    case UnityEngine.Material m:
                        Add(m);
                        break;
                    case GameObject go:
                        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                        {
                            foreach (var m in r.sharedMaterials)
                            {
                                Add(m);
                            }
                        }

                        break;
                }
            }

            return result;
        }

        [MenuItem(DDriveMenu.Generate + "選択した Material を D-Drive/Lit・Unlit の MaterialData に変換")]
        private static void MigrateSelection()
        {
            var materials = CollectFromSelection(Selection.objects);
            if (materials.Count == 0)
            {
                Debug.LogWarning("[DDrive] Material アセット、または Renderer を持つ Prefab / モデルを Project で選択してから実行してください。");
                return;
            }

            var report = new MayaMaterialImporter.Report();
            MaterialData last = null;
            // 知らないシェーダーの確認は 1 操作につき 1 回(先に全 Material を走査して判断を得てから変換する。FC-15)。
            if (!UnknownShaderGuard.TryResolve(MayaImportProfile.FindOrDefault(), materials, UnknownShaderGuard.IsInteractiveSession(), out var unknownShader))
            {
                Debug.Log("[DDrive] 知らないシェーダーの確認でキャンセルされたため、Material の変換を中断しました(何も変更していません)。");
                return;
            }

            // 一括の間は同定インデックス(SourceMaterial → MaterialData / Texture → TextureData)を作って使い回す([09] §9)。
            MayaMaterialImporter.BeginBatch();
            try
            {
                foreach (var m in materials)
                {
                    var data = Migrate(m, null, report, AssetCreationService.DefaultGameDataRoot, unknownShader);
                    if (data != null)
                    {
                        last = data;
                    }
                }
            }
            finally
            {
                MayaMaterialImporter.EndBatch();
            }

            Debug.Log($"[DDrive] Unity Material → D-Drive MaterialData: {materials.Count} 件\n{report}");
            if (last != null)
            {
                EditorGUIUtility.PingObject(last);
                Selection.activeObject = last;
            }
        }

        [MenuItem(DDriveMenu.Generate + "選択した Material を D-Drive/Lit・Unlit の MaterialData に変換", true)]
        private static bool ValidateMigrateSelection() => CollectFromSelection(Selection.objects).Count > 0;
    }
}
