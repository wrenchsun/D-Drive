using DDrive.Editor.CanvasTool;
using DDrive.Editor.Ui;
using DDrive.Foundation.Easing;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Values;
using DDrive.Runtime.Ui;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [15_ui_interaction.md] B-3.5/B-6 — 4-11 残作業(UiPresetCatalog)+ 4-10(カーブ一覧 / スプラインハンドル /
    // CanvasEditor 割当 UI)の純ロジック部分(UI 組み立て自体は EditorWindow 依存のため対象外。
    // AssetDatabase に依存する UiPresetCatalogUtility.Collect() もエディタ限定のため未検証、
    // Validate はメモリ上の ScriptableObject のみで完結するのでここで検証する)。
    public class UiTweenEditorTests
    {
        // ── UiPresetCatalogUtility.Validate ──

        [Test]
        public void Validate_FlagsEmptyName_NullTween_And_UnassignedId()
        {
            var tweenWithId = ScriptableObject.CreateInstance<UiTweenData>();
            tweenWithId.Id = 123;

            var tweenNoId = ScriptableObject.CreateInstance<UiTweenData>();
            tweenNoId.Id = 0;

            var catalog = ScriptableObject.CreateInstance<UiPresetCatalog>();
            catalog.Entries = new[]
            {
                new UiPresetCatalog.Entry { Name = string.Empty, Tween = tweenWithId }, // 名前空
                new UiPresetCatalog.Entry { Name = "Null Tween", Tween = null }, // Tween 未設定
                new UiPresetCatalog.Entry { Name = "No Id", Tween = tweenNoId }, // Id 未採番
                new UiPresetCatalog.Entry { Name = "OK", Tween = tweenWithId }, // 問題なし
            };

            var issues = UiPresetCatalogUtility.Validate(catalog);

            Assert.AreEqual(3, issues.Count);
            Object.DestroyImmediate(tweenWithId);
            Object.DestroyImmediate(tweenNoId);
            Object.DestroyImmediate(catalog);
        }

        [Test]
        public void Validate_NullOrEmptyCatalog_ReturnsNoIssues()
        {
            Assert.AreEqual(0, UiPresetCatalogUtility.Validate(null).Count);

            var empty = ScriptableObject.CreateInstance<UiPresetCatalog>();
            Assert.AreEqual(0, UiPresetCatalogUtility.Validate(empty).Count);
            Object.DestroyImmediate(empty);
        }

        // ── TweenTrackSummary.Describe ──

        [Test]
        public void Describe_ParametricTrack_FormatsPropertyModeDurationLoopDelay()
        {
            var track = new TweenTrack
            {
                Property = TweenProperty.Alpha,
                Delay = 0.5f,
                Motion = new ValueDef
                {
                    Mode = ValueMode.Parametric,
                    Parametric = EaseDef.Named(Ease.OutCubic),
                    Time = TimeDef.Duration(0.25f),
                    Loop = LoopMode.Once,
                },
            };

            var text = TweenTrackSummary.Describe(in track);

            StringAssert.Contains("Alpha", text);
            StringAssert.Contains("Ease:OutCubic", text);
            StringAssert.Contains("0.25s", text);
            StringAssert.Contains("Once", text);
            StringAssert.Contains("delay 0.5s", text);
        }

        [Test]
        public void Describe_CurveTrack_ReportsCurveMode_And_LoopCount()
        {
            var track = new TweenTrack
            {
                Property = TweenProperty.Scale,
                Motion = new ValueDef
                {
                    Mode = ValueMode.Curve,
                    Curve = AnimationCurve.Linear(0f, 0f, 1f, 1f),
                    Time = TimeDef.Duration(1.5f),
                    Loop = LoopMode.PingPong,
                    LoopCount = 3,
                },
            };

            var text = TweenTrackSummary.Describe(in track);

            StringAssert.Contains("Scale", text);
            StringAssert.Contains("Curve", text);
            StringAssert.Contains("1.5s", text);
            StringAssert.Contains("PingPong x3", text);
        }

        // ── SplineHandleMath ──

        [Test]
        public void WorldLocal_RoundTrips_ForOffsetScaledRectTransform()
        {
            var go = new GameObject("RT", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.position = new Vector3(10f, 5f, -3f);
            rt.rotation = Quaternion.Euler(0f, 45f, 0f);
            rt.localScale = new Vector3(2f, 2f, 2f);

            var local = new Vector3(12.3f, -4.5f, 0f);
            var world = SplineHandleMath.LocalToWorld(rt, local);
            var roundTripped = SplineHandleMath.WorldToLocal(rt, world);

            Assert.That(roundTripped.x, Is.EqualTo(local.x).Within(0.001f));
            Assert.That(roundTripped.y, Is.EqualTo(local.y).Within(0.001f));
            Assert.That(roundTripped.z, Is.EqualTo(local.z).Within(0.001f));

            Object.DestroyImmediate(go);
        }

        // ── CanvasElementFxCollector.CopyPhases ──

        [Test]
        public void CopyPhases_CopiesAppearIdleDisappear_ToAllOtherRows()
        {
            var source = new ElementFx
            {
                ElementPath = "Source",
                AppearPreset = new UiPresetRef { Preset = UiPreset.PopIn },
                IdlePreset = new UiPresetRef { Preset = UiPreset.Pulse },
                DisappearPreset = new UiPresetRef { Preset = UiPreset.PopOut },
                Appear = new AssetId<UiTweenMarker>(111, AssetType.UiTween),
                Idle = new AssetId<UiTweenMarker>(222, AssetType.UiTween),
                Disappear = new AssetId<UiTweenMarker>(333, AssetType.UiTween),
            };

            var rows = new[]
            {
                source,
                new ElementFx { ElementPath = "Other1" },
                new ElementFx { ElementPath = "Other2", AppearPreset = new UiPresetRef { Preset = UiPreset.FadeIn } },
            };

            CanvasElementFxCollector.CopyPhases(ref rows, 0);

            // コピー元自身は変わらない、パス(ElementPath)は変わらない。
            Assert.AreEqual("Source", rows[0].ElementPath);
            Assert.AreEqual(UiPreset.PopIn, rows[0].AppearPreset.Preset);

            for (var i = 1; i < rows.Length; i++)
            {
                Assert.AreEqual(UiPreset.PopIn, rows[i].AppearPreset.Preset, $"rows[{i}].AppearPreset");
                Assert.AreEqual(UiPreset.Pulse, rows[i].IdlePreset.Preset, $"rows[{i}].IdlePreset");
                Assert.AreEqual(UiPreset.PopOut, rows[i].DisappearPreset.Preset, $"rows[{i}].DisappearPreset");
                Assert.AreEqual(111ul, rows[i].Appear.Value, $"rows[{i}].Appear");
                Assert.AreEqual(222ul, rows[i].Idle.Value, $"rows[{i}].Idle");
                Assert.AreEqual(333ul, rows[i].Disappear.Value, $"rows[{i}].Disappear");
            }
        }

        [Test]
        public void CopyPhases_InvalidFromIndex_DoesNothing()
        {
            var rows = new[] { new ElementFx { ElementPath = "A" }, new ElementFx { ElementPath = "B" } };
            var before = rows[1];

            CanvasElementFxCollector.CopyPhases(ref rows, 5);

            Assert.AreEqual(before.ElementPath, rows[1].ElementPath);
        }
    }
}
