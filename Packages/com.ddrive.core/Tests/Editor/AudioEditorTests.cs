using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DDrive.Foundation.Data;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Audio;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Tests.Editor
{
    // docs/1010 UX-3: 新 SeEditor / BgmEditor(Editor/Audio)。Ux の型は internal なので、型の中身はリフレクションで見る。
    public class AudioEditorTests
    {
        private static Assembly EditorAssembly => typeof(DDrive.Editor.Menu.DDriveMenu).Assembly;

        private static IReadOnlyList<object> GuideEntries(string guideTypeName)
        {
            var guideType = EditorAssembly.GetType(guideTypeName);
            Assert.IsNotNull(guideType, guideTypeName + " が見つかりません");
            var instance = guideType.GetField("Instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var list = new List<object>();
            foreach (var e in (IEnumerable)guideType.BaseType.GetProperty("Entries").GetValue(instance))
            {
                list.Add(e);
            }

            return list;
        }

        private static void AssertGuideCoversEveryEditableField<T>(string guideTypeName) where T : ScriptableObject
        {
            var inGuide = new HashSet<string>();
            foreach (var e in GuideEntries(guideTypeName))
            {
                inGuide.Add((string)e.GetType().GetField("Field").GetValue(e));
            }

            var data = ScriptableObject.CreateInstance<T>();
            try
            {
                var so = new SerializedObject(data);
                var readOnly = new HashSet<string> { "Id", "Version", "Author", "UpdatedAt", "m_Script" };
                var missing = new List<string>();
                var it = so.GetIterator();
                var enter = true;
                while (it.NextVisible(enter))
                {
                    enter = false;
                    if (!readOnly.Contains(it.name) && !inGuide.Contains(it.name))
                    {
                        missing.Add(it.name);
                    }
                }

                Assert.IsEmpty(missing, guideTypeName + " に無い欄: " + string.Join(", ", missing));
                foreach (var field in inGuide)
                {
                    Assert.IsNotNull(so.FindProperty(field), "表の欄 " + field + " が " + typeof(T).Name + " にありません");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void SeFieldGuide_CoversEveryEditableField_AndOnlyExistingFields() =>
            AssertGuideCoversEveryEditableField<SeData>("DDrive.Editor.Audio.SeFieldGuide");

        [Test]
        public void BgmFieldGuide_CoversEveryEditableField_AndOnlyExistingFields() =>
            AssertGuideCoversEveryEditableField<BgmData>("DDrive.Editor.Audio.BgmFieldGuide");

        // 必須の段 = Validator が Error にする欄(Clips / LoopBody)。
        [Test]
        public void RequiredTier_IsTheFieldTheValidatorErrorsOn()
        {
            AssertRequired("DDrive.Editor.Audio.SeFieldGuide", "Clips");
            AssertRequired("DDrive.Editor.Audio.BgmFieldGuide", "LoopBody");
        }

        private static void AssertRequired(string guide, string field)
        {
            var required = new List<string>();
            foreach (var e in GuideEntries(guide))
            {
                if (Convert.ToInt32(e.GetType().GetField("Tier").GetValue(e)) == 0)
                {
                    required.Add((string)e.GetType().GetField("Field").GetValue(e));
                }
            }

            CollectionAssert.AreEqual(new[] { field }, required, guide);
        }

        [Test]
        public void SeDataValidator_Results_AreBoundToTheirFields()
        {
            var se = ScriptableObject.CreateInstance<SeData>();
            try
            {
                se.MaxConcurrent = 0;
                var results = new List<ValidationResult>(new SeDataValidator().Validate(se, new ValidationContext(new List<AssetDataBase>())));
                Assert.AreEqual("Clips", results.Find(r => r.Severity == ValidationSeverity.Error && r.Message.StartsWith("Clip")).Field);
                Assert.AreEqual("Mixer", results.Find(r => r.Message.StartsWith("Mixer")).Field);
                Assert.AreEqual("MaxConcurrent", results.Find(r => r.Message.StartsWith("MaxConcurrent")).Field);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(se);
            }
        }

        [Test]
        public void BgmDataValidator_Results_AreBoundToTheirFields()
        {
            var bgm = ScriptableObject.CreateInstance<BgmData>();
            try
            {
                var results = new List<ValidationResult>(new BgmDataValidator().Validate(bgm, new ValidationContext(new List<AssetDataBase>())));
                Assert.AreEqual("LoopBody", results.Find(r => r.Severity == ValidationSeverity.Error).Field);
                Assert.AreEqual("Mixer", results.Find(r => r.Message.StartsWith("Mixer")).Field);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(bgm);
            }
        }

        // 波形の列データ: 読めるクリップは最大 / 最小振幅の表になり、同じクリップはキャッシュされる。
        [Test]
        public void WavePeaks_BuildsColumnsForReadableClip_AndCachesThem()
        {
            var type = EditorAssembly.GetType("DDrive.Editor.Audio.WavePeaks");
            Assert.IsNotNull(type);
            var get = type.GetMethod("Get", BindingFlags.Public | BindingFlags.Static);
            var clip = AudioClip.Create("peaks", 4410, 1, 44100, false);
            try
            {
                var samples = new float[4410];
                for (var i = 0; i < samples.Length; i++)
                {
                    samples[i] = Mathf.Sin(i * 0.05f);
                }

                clip.SetData(samples, 0);
                var peaks = (Array)get.Invoke(null, new object[] { clip });
                Assert.IsNotNull(peaks);
                Assert.AreEqual(2048, peaks.Length);
                Assert.AreSame(peaks, get.Invoke(null, new object[] { clip }), "同じクリップはキャッシュを返す");
                Assert.IsNull(get.Invoke(null, new object[] { null }));
            }
            finally
            {
                type.GetMethod("Clear", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
                UnityEngine.Object.DestroyImmediate(clip);
            }
        }

        // 新エディターが共通基盤の部品でヒーロー・固定バー・目的別カードを組む(再生を持たない種別ではないので ▶ もある)。
        [Test]
        public void SeEditor_BuildsHeroCardsAndFooter()
        {
            var se = ScriptableObject.CreateInstance<SeData>();
            se.name = "AudioSmokeSe";
            AssertBuilds("DDrive.Editor.Audio.SeEditor", se, cardCount: 7, speed: true); // 鳴らす / 切る / 聞こえ方 / 同時再生 / 連携 / 管理情報 / その他の検証
        }

        [Test]
        public void BgmEditor_BuildsHeroCardsAndFooter_WithoutSpeed()
        {
            var bgm = ScriptableObject.CreateInstance<BgmData>();
            bgm.name = "AudioSmokeBgm";
            AssertBuilds("DDrive.Editor.Audio.BgmEditor", bgm, cardCount: 7, speed: false); // 鳴らす / 切る / 聞こえ方 / 拍 / 連携 / 管理情報 / その他の検証
        }

        private static void AssertBuilds(string typeName, ScriptableObject data, int cardCount, bool speed)
        {
            var type = EditorAssembly.GetType(typeName);
            Assert.IsNotNull(type, typeName);
            EditorWindow window = null;
            try
            {
                window = (EditorWindow)ScriptableObject.CreateInstance(type);
                var baseType = type.BaseType;
                baseType.GetField("_target", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, data);
                baseType.GetMethod("CreateGUI", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);

                var root = window.rootVisualElement;
                Assert.IsNotNull(root.Q(className: "ux-hero"), "ヒーロー");
                Assert.IsNotNull(root.Q(className: "ux-bar"), "固定バー");
                Assert.IsNotNull(root.Q(className: "ux-seg"), "セグメント");
                Assert.AreEqual(cardCount, root.Query(className: "ux-card").ToList().Count, "カード数");
                Assert.IsNotNull(root.Q(className: "ux-wave"), "波形");
                Assert.AreEqual(speed, root.Q(className: "ux-bar__speed") != null, "速度スライダーの有無");
            }
            finally
            {
                if (window != null)
                {
                    UnityEngine.Object.DestroyImmediate(window);
                }

                UnityEngine.Object.DestroyImmediate(data);
            }
        }
    }
}
