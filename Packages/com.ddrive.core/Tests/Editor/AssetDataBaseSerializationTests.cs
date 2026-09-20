using DDrive.Editor.Versioning;
using DDrive.Foundation.Data;
using DDrive.Foundation.Event;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Net;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    public class AssetDataBaseSerializationTests
    {
        private const string TempDir = TestTempFolder.Root + "/Temp";
        private const string TempAssetPath = TempDir + "/RoundTrip_TestAssetData.asset";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.LoadAssetAtPath<TestAssetData>(TempAssetPath) != null)
            {
                AssetDatabase.DeleteAsset(TempAssetPath);
            }
        }

        [Test]
        public void SerializationRoundTrip_PreservesAllCommonFields()
        {
            if (!AssetDatabase.IsValidFolder(TempDir))
            {
                TestTempFolder.CreateFolder("Temp");
            }

            var data = ScriptableObject.CreateInstance<TestAssetData>();
            data.Id = 0x1234_5678_9ABC_DEF0;
            data.DisplayName = "RoundTrip";
            data.Description = "desc";
            data.Category = "Audio/SE";
            data.Tags = new[] { "Enemy", "Boss" };
            data.Version = 3;
            data.Author = "wrench";
            data.UpdatedAt = "2026-07-26";
            data.ChangeNote = "note";
            data.Assignee = "yoshida";
            data.SpecUrl = "https://docs.google.com/spreadsheets/d/EXAMPLE/edit#gid=0";
            data.Flags = new AssetFlags
            {
                Pause = PauseMode.IgnorePause,
                Load = LoadMode.Preload,
                Pool = PoolPolicy.Pooled(4, 32),
                Priority = 7,
                Persistent = true,
                Domain = AssetDomain.UI,
                Net = NetMode.Cosmetic,
            };
            data.Events = new[]
            {
                new AssetEvent
                {
                    Trigger = EventTrigger.Frame,
                    Time = 15f,
                    Action = EventAction.PlayAsset,
                    Target = AssetRef.From(new AssetId<TestAssetData>(0xAA, AssetType.Se)),
                    Param = ParamValue.Of(0.5f),
                },
            };

            // [11_tasks.md] 6-3: 保存フック(VersionStampProcessor)が Version/Author/UpdatedAt を書き換えて
            // しまうと、このテストが検証したい「設定した値がそのまま往復するか」が壊れるため抑止する。
            using (VersionStampSuppression.Scope())
            {
                AssetDatabase.CreateAsset(data, TempAssetPath);
                using (DDrive.Editor.Versioning.VersionStampSuppression.Scope()) { AssetDatabase.SaveAssets(); }
            }

            AssetDatabase.Refresh();

            var reloaded = AssetDatabase.LoadAssetAtPath<TestAssetData>(TempAssetPath);

            Assert.IsNotNull(reloaded);
            Assert.AreEqual(data.Id, reloaded.Id);
            Assert.AreEqual(data.DisplayName, reloaded.DisplayName);
            Assert.AreEqual(data.Description, reloaded.Description);
            Assert.AreEqual(data.Category, reloaded.Category);
            CollectionAssert.AreEqual(data.Tags, reloaded.Tags);
            Assert.AreEqual(data.Version, reloaded.Version);
            Assert.AreEqual(data.Author, reloaded.Author);
            Assert.AreEqual(data.UpdatedAt, reloaded.UpdatedAt);
            Assert.AreEqual(data.ChangeNote, reloaded.ChangeNote);
            Assert.AreEqual(data.Assignee, reloaded.Assignee);
            Assert.AreEqual(data.SpecUrl, reloaded.SpecUrl);

            Assert.AreEqual(data.Flags.Pause, reloaded.Flags.Pause);
            Assert.AreEqual(data.Flags.Load, reloaded.Flags.Load);
            Assert.AreEqual(data.Flags.Pool.Kind, reloaded.Flags.Pool.Kind);
            Assert.AreEqual(data.Flags.Pool.InitialCount, reloaded.Flags.Pool.InitialCount);
            Assert.AreEqual(data.Flags.Pool.MaxCount, reloaded.Flags.Pool.MaxCount);
            Assert.AreEqual(data.Flags.Priority, reloaded.Flags.Priority);
            Assert.AreEqual(data.Flags.Persistent, reloaded.Flags.Persistent);
            Assert.AreEqual(data.Flags.Domain, reloaded.Flags.Domain);
            Assert.AreEqual(data.Flags.Net, reloaded.Flags.Net);

            Assert.AreEqual(1, reloaded.Events.Length);
            Assert.AreEqual(EventTrigger.Frame, reloaded.Events[0].Trigger);
            Assert.AreEqual(15f, reloaded.Events[0].Time);
            Assert.AreEqual(EventAction.PlayAsset, reloaded.Events[0].Action);
            Assert.AreEqual(AssetType.Se, reloaded.Events[0].Target.Type);
            Assert.AreEqual((ulong)0xAA, reloaded.Events[0].Target.Id);
            Assert.AreEqual(ParamValueType.Float, reloaded.Events[0].Param.Type);
            Assert.AreEqual(0.5f, reloaded.Events[0].Param.FloatValue);
        }
    }
}
