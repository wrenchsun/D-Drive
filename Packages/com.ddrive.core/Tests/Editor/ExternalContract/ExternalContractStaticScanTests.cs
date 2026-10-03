using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor.PackageManager;

namespace ExternalContract.Tests
{
    // [docs/42 §5.14] 外部拡張の契約(静的確認)。E-14(D-Drive が Renderer Feature に関与しない)・
    // E-17(静的部分: D-Drive の Editor / Runtime が FBX の取り込み設定・メッシュ・ボーン・スケールを触らない)。
    // D-Drive 本体のソース(Foundation / Runtime / Editor。Tests と Samples~ は除く)を走査する。コメント行は対象外。
    public class ExternalContractStaticScanTests
    {
        private static string PackageRoot()
        {
            var info = PackageInfo.FindForAssembly(typeof(DDrive.Runtime.DDriveVersion).Assembly);
            Assert.IsNotNull(info, "D-Drive パッケージの場所を解決できない");
            return info.resolvedPath;
        }

        private static IEnumerable<(string File, int Line, string Text)> SourceLines(params string[] folders)
        {
            var root = PackageRoot();
            foreach (var folder in folders)
            {
                var dir = Path.Combine(root, folder);
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                foreach (var file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    var lines = File.ReadAllLines(file);
                    for (var i = 0; i < lines.Length; i++)
                    {
                        var trimmed = lines[i].TrimStart();
                        if (trimmed.StartsWith("//", System.StringComparison.Ordinal) || trimmed.StartsWith("*", System.StringComparison.Ordinal))
                        {
                            continue;
                        }

                        var commentAt = lines[i].IndexOf("//", System.StringComparison.Ordinal);
                        yield return (file, i + 1, commentAt >= 0 ? lines[i].Substring(0, commentAt) : lines[i]);
                    }
                }
            }
        }

        private static List<string> Find(Regex pattern, params string[] folders)
        {
            var hits = new List<string>();
            foreach (var (file, line, text) in SourceLines(folders))
            {
                if (pattern.IsMatch(text))
                {
                    hits.Add($"{Path.GetFileName(file)}:{line}: {text.Trim()}");
                }
            }

            return hits;
        }

        // ModelImporter(FBX)固有の取り込み設定への書き込み。TextureImporter の isReadable 等とは混ざらない名前だけを挙げる。
        private static readonly Regex ForbiddenModelImporterWrite = new(
            @"\.\s*(importBlendShapes|importVisibility|importCameras|importLights|optimizeGameObjects|meshCompression|globalScale|useFileScale|useFileUnits|extraExposedTransformPaths|weldVertices|importNormals|importTangents|keepQuads|swapUVChannels|generateSecondaryUV|skinWeights|maxBonesPerVertex|bakeAxisConversion|preserveHierarchy|resampleCurves|materialImportMode|materialLocation|importAnimation)\b\s*=[^=]");

        // 走査の正規表現が空振りしていないことの確認(検出すべきものを検出し、無関係な名前は拾わない)。
        [Test]
        public void E17_Control_ForbiddenWritePattern_MatchesExpectedLines()
        {
            Assert.IsTrue(ForbiddenModelImporterWrite.IsMatch("importer.meshCompression = ModelImporterMeshCompression.High;"));
            Assert.IsTrue(ForbiddenModelImporterWrite.IsMatch("modelImporter.importBlendShapes = false;"));
            Assert.IsFalse(ForbiddenModelImporterWrite.IsMatch("_globalScale = 1f;"));
            Assert.IsFalse(ForbiddenModelImporterWrite.IsMatch("camera.SetGlobalScale(2f);"));
            Assert.IsFalse(ForbiddenModelImporterWrite.IsMatch("if (importer.importBlendShapes == true) {}"));
        }

        [Test]
        public void E14_NoRendererFeatureReferences_InRuntimeOrEditor()
        {
            var hits = Find(new Regex(@"\b(ScriptableRendererFeature|ScriptableRendererData|ScriptableRenderPass)\b"), "Foundation", "Runtime", "Editor");
            Assert.IsEmpty(hits, "D-Drive は URP の Renderer / Renderer Feature に関与しない: " + string.Join(" | ", hits));
        }

        // E-17: ModelImporter への書き込みは Cutscene のキャラ FBX の animationType / avatarSetup だけ。
        [Test]
        public void E17_OnlyAnimationTypeAndAvatarSetup_AreWrittenToModelImporter()
        {
            var hits = Find(ForbiddenModelImporterWrite, "Foundation", "Runtime", "Editor");
            Assert.IsEmpty(hits, "FBX の取り込み設定を書いてはならない(animationType / avatarSetup を除く): " + string.Join(" | ", hits));

            // 書き込んでいるのは CutsceneFbxPostprocessor の animationType / avatarSetup(CopyFromOther 時の sourceAvatar を含む)だけ(ModelImporter 型の変数への代入を全部拾う)。
            var assign = new Regex(@"\b[mM]odelImporter\s*\.\s*(\w+)\s*=[^=]");
            var written = new HashSet<string>();
            foreach (var (_, _, text) in SourceLines("Foundation", "Runtime", "Editor"))
            {
                foreach (Match m in assign.Matches(text))
                {
                    written.Add(m.Groups[1].Value);
                }
            }

            CollectionAssert.IsSubsetOf(written, new[] { "animationType", "avatarSetup", "sourceAvatar" }, "ModelImporter に書くプロパティは animationType / avatarSetup(+ 参照先 sourceAvatar)だけ");
        }

        // E-17: ボーン・メッシュ・スケールを D-Drive が書き換えない(Spawn は位置・回転・親だけ)。
        [Test]
        public void E17_RuntimeNeverWritesBonesMeshOrScale_OnModelAndCutscene()
        {
            var bonesOrMesh = Find(new Regex(@"\.\s*(bones|rootBone|sharedMesh|bindposes|boneWeights)\s*=[^=]"), "Foundation", "Runtime");
            Assert.IsEmpty(bonesOrMesh, "Runtime はボーン・メッシュを書き換えない: " + string.Join(" | ", bonesOrMesh));

            var scale = Find(new Regex(@"\.\s*(localScale|lossyScale)\s*=[^=]"), "Runtime/Model", "Runtime/Cutscene");
            Assert.IsEmpty(scale, "Model / Cutscene はスケールを書かない: " + string.Join(" | ", scale));
        }
    }
}
