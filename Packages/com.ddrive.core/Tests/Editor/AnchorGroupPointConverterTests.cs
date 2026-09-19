using DDrive.Editor.Anchor;
using DDrive.Runtime.Anchoring;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [22_anchor_group.md] §3.7（U-22）— 自動配置を手置きの点へ焼き付ける変換。
    // 「同じ計算を書き直さない」= AnchorLayout の結果と一致すること、点の番号が変わらないことを押さえる。
    public class AnchorGroupPointConverterTests
    {
        private AnchorGroupData _group;

        [SetUp]
        public void SetUp()
        {
            _group = ScriptableObject.CreateInstance<AnchorGroupData>();
            _group.name = "ANCG_Test_Convert";
        }

        [TearDown]
        public void TearDown()
        {
            if (_group != null)
            {
                Object.DestroyImmediate(_group);
            }
        }

        [Test]
        public void Convert_Grid3x3_BakesNinePointsAndSwitchesToManual()
        {
            _group.Layout = AnchorLayoutKind.Grid;
            _group.GridCountX = 3;
            _group.GridCountY = 1;
            _group.GridCountZ = 3;
            _group.GridSpacing = Vector3.one;
            _group.GridCentered = true;

            var before = Enumerate();
            var count = AnchorGroupPointConverter.Convert(_group);

            Assert.AreEqual(9, count);
            Assert.AreEqual(AnchorLayoutKind.Manual, _group.Layout);
            Assert.AreEqual(9, _group.Points.Length);
            Assert.Less(Vector3.Distance(new Vector3(-1f, 0f, -1f), _group.Points[0].LocalOffset), 1e-4f);

            // 変換の前後で各点の位置が変わらない（= 計算を書き直していない）。
            var after = Enumerate();
            Assert.AreEqual(before.Length, after.Length);
            for (var i = 0; i < before.Length; i++)
            {
                Assert.Less(Vector3.Distance(before[i], after[i]), 1e-4f, $"点 {i} の位置が変換でずれた");
            }
        }

        [Test]
        public void Convert_KeepsManualPointsAtTheEndSoIndicesStay()
        {
            _group.Layout = AnchorLayoutKind.Line;
            _group.LineCount = 2;
            _group.LineLength = 2f;
            _group.LineDirection = Vector3.forward;
            _group.LineCentered = false;
            _group.Points = new[]
            {
                new AnchorGroupPoint { Name = "手置き", LocalOffset = new Vector3(5f, 0f, 0f), LocalScale = Vector3.one },
            };

            var count = AnchorGroupPointConverter.Convert(_group);

            Assert.AreEqual(3, count);
            Assert.AreEqual("手置き", _group.Points[2].Name);
            Assert.Less(Vector3.Distance(new Vector3(5f, 0f, 0f), _group.Points[2].LocalOffset), 1e-4f);
            Assert.Less(Vector3.Distance(new Vector3(0f, 0f, 2f), _group.Points[1].LocalOffset), 1e-4f);
            Assert.Less(Vector3.Distance(Vector3.one, _group.Points[0].LocalScale), 1e-4f);
        }

        [Test]
        public void Convert_CircleFaceOutward_KeepsPerPointRotation()
        {
            _group.Layout = AnchorLayoutKind.Circle;
            _group.CircleCount = 4;
            _group.CircleRadius = 2f;
            _group.CircleArc = 360f;
            _group.CircleStartAngle = 0f;
            _group.CircleFaceOutward = true;

            Assert.AreEqual(4, AnchorGroupPointConverter.Convert(_group));
            Assert.Less(Mathf.DeltaAngle(90f, _group.Points[1].LocalEuler.y), 1e-3f);
        }

        [Test]
        public void Convert_Manual_IsNoOp()
        {
            _group.Layout = AnchorLayoutKind.Manual;
            _group.Points = new[] { new AnchorGroupPoint { LocalOffset = Vector3.one, LocalScale = Vector3.one } };

            Assert.IsFalse(AnchorGroupPointConverter.CanConvert(_group));
            Assert.AreEqual(0, AnchorGroupPointConverter.Convert(_group));
            Assert.AreEqual(1, _group.Points.Length);
        }

        // 原点込みの各点ワールドローカル位置（Registry 不要 = OriginAnchorId 未設定）。
        private Vector3[] Enumerate()
        {
            var specs = new AnchorSpawnSpec[AnchorGroupData.MaxPoints];
            var count = AnchorGroupPlanner.EnumeratePoints(null, _group, specs);
            var result = new Vector3[count];
            for (var i = 0; i < count; i++)
            {
                result[i] = specs[i].Def.LocalOffset;
            }

            return result;
        }
    }
}
