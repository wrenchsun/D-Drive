using DDrive.Runtime.Material;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Runtime
{
    // [04_vfx.md] §7 — URP プロジェクトで Built-in RP 用シェーダーを使うと Scene/Game ビューで
    // 完全に透明になる落とし穴を、SubShader の RenderPipeline タグから機械的に検出できるか検証する。
    // このテストプロジェクトは URP なので、Detect(URPシェーダー)=Universal、
    // IsCompatibleWithActivePipeline(Built-in専用シェーダー)=false が期待値になる。
    public class ShaderPipelineAnalyzerTests
    {
        [Test]
        public void Detect_UrpShader_ReturnsUniversal()
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            Assert.IsNotNull(shader, "テスト前提: URP パッケージが導入されている");

            Assert.AreEqual(ShaderPipelineKind.Universal, ShaderPipelineAnalyzer.Detect(shader));
        }

        [Test]
        public void Detect_BuiltInShader_ReturnsBuiltIn()
        {
            var shader = Shader.Find("Standard");
            Assert.IsNotNull(shader, "テスト前提: Built-in の Standard シェーダーが存在する(常に同梱される)");

            Assert.AreEqual(ShaderPipelineKind.BuiltIn, ShaderPipelineAnalyzer.Detect(shader));
        }

        [Test]
        public void Detect_NullShader_ReturnsUnknown()
        {
            Assert.AreEqual(ShaderPipelineKind.Unknown, ShaderPipelineAnalyzer.Detect(null));
        }

        [Test]
        public void IsCompatibleWithActivePipeline_UrpShaderInUrpProject_ReturnsTrue()
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            Assert.IsTrue(ShaderPipelineAnalyzer.IsCompatibleWithActivePipeline(shader));
        }

        [Test]
        public void IsCompatibleWithActivePipeline_BuiltInShaderInUrpProject_ReturnsFalse()
        {
            var shader = Shader.Find("Standard");
            Assert.IsFalse(ShaderPipelineAnalyzer.IsCompatibleWithActivePipeline(shader),
                "Built-in RP 専用シェーダーは URP プロジェクトでは互換性なしと判定されるべき(実際に透明になる不具合の原因)");
        }

        [Test]
        public void ActivePipelineDisplayName_UrpProject_ReturnsUrp()
        {
            Assert.AreEqual("URP", ShaderPipelineAnalyzer.ActivePipelineDisplayName());
        }
    }
}
