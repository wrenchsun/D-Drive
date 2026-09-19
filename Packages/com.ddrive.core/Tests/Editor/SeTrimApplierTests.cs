using DDrive.Editor.Audio;
using DDrive.Runtime.Audio;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    public class SeTrimApplierTests
    {
        private const string TempDir = "Packages/com.ddrive.core/Tests/Editor/Temp";
        private const string AssetPath = TempDir + "/TrimApplier_Se.asset";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.LoadAssetAtPath<SeData>(AssetPath) != null)
            {
                AssetDatabase.DeleteAsset(AssetPath);
            }

            for (var i = 0; i < 4; i++)
            {
                var wav = $"{TempDir}/TrimApplier_Se_Trimmed_{i}.wav";
                if (AssetDatabase.LoadAssetAtPath<AudioClip>(wav) != null)
                {
                    AssetDatabase.DeleteAsset(wav);
                }
            }
        }

        private static SeData CreateSavedSeData(params SeClipSource[] sources)
        {
            if (!AssetDatabase.IsValidFolder(TempDir))
            {
                AssetDatabase.CreateFolder("Packages/com.ddrive.core/Tests/Editor", "Temp");
            }

            var data = ScriptableObject.CreateInstance<SeData>();
            data.Sources = sources;
            AssetDatabase.CreateAsset(data, AssetPath);
            using (DDrive.Editor.Versioning.VersionStampSuppression.Scope()) { AssetDatabase.SaveAssets(); }
            return data;
        }

        private static AudioClip CreateSourceClip(float seconds)
        {
            var sampleCount = (int)(44100 * seconds);
            var clip = AudioClip.Create("src", sampleCount, 1, 44100, false);
            var samples = new float[sampleCount];
            for (var i = 0; i < sampleCount; i++)
            {
                samples[i] = Mathf.Sin(i * 0.01f) * 0.5f;
            }

            clip.SetData(samples, 0);
            return clip;
        }

        [Test]
        public void Apply_WritesTrimmedWavAssets_AndKeepsSourceUntouched()
        {
            var source = CreateSourceClip(2f);
            var data = CreateSavedSeData(new SeClipSource { Source = source, TrimStartSec = 0.5f, TrimEndSec = 1.5f });

            var applied = SeTrimApplier.Apply(data);

            Assert.IsTrue(applied);
            Assert.AreEqual(1, data.Clips.Length);
            Assert.IsNotNull(data.Clips[0]);

            // インポート済みの実 .wav アセットとして永続化されている(サブアセットではない)。
            var clipPath = AssetDatabase.GetAssetPath(data.Clips[0]);
            StringAssert.EndsWith("TrimApplier_Se_Trimmed_0.wav", clipPath);

            // 1 秒分(±数サンプルの丸め許容)に切り出されている。
            Assert.AreEqual(44100, data.Clips[0].samples, 10);
            Assert.AreEqual(2f * 44100, source.samples, "元クリップは不変(非破壊)");
        }

        [Test]
        public void Apply_RunTwice_OverwritesSameWavFileInsteadOfAccumulating()
        {
            var source = CreateSourceClip(2f);
            var data = CreateSavedSeData(new SeClipSource { Source = source, TrimStartSec = 0.5f, TrimEndSec = 1.5f });

            SeTrimApplier.Apply(data);
            var firstPath = AssetDatabase.GetAssetPath(data.Clips[0]);

            data.Sources[0].TrimStartSec = 0f;
            SeTrimApplier.Apply(data);
            var secondPath = AssetDatabase.GetAssetPath(data.Clips[0]);

            Assert.AreEqual(firstPath, secondPath, "同じ .wav ファイルを上書きする(蓄積しない)");
            Assert.AreEqual(1.5f * 44100, data.Clips[0].samples, 10, "新しいトリム範囲(1.5秒)が反映されている");
        }

        [Test]
        public void Apply_UnsavedAsset_ReturnsFalseWithWarning()
        {
            var data = ScriptableObject.CreateInstance<SeData>();
            data.Sources = new[] { new SeClipSource { Source = CreateSourceClip(0.1f) } };

            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*"));
            Assert.IsFalse(SeTrimApplier.Apply(data));

            Object.DestroyImmediate(data);
        }
    }
}
