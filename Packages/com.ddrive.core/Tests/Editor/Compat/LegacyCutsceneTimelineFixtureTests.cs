using System;
using System.IO;
using DDrive.Editor.Migration;
using DDrive.Runtime.Cutscene.Tracks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace DDrive.Tests.Editor.Compat
{
    // M-6(2026-10-06) — v1.3.1 以前の形式(D-Drive のトラック / マーカーが `m_Script: {fileID: 0}` +
    // `m_EditorClassIdentifier` で保存された .playable)が、今のコードで読めること・マイグレーションで GUID 参照に直ることを固定する。
    //
    // フィクスチャは v1.3.1 の時点で N-8 が作った Timeline(`git show 33b79f2:Assets/GameData/Cutscene/NetCheck/CUT_NetCheck_Markers_Timeline.playable`)の
    // コピー。`.playable` のままだと Packages 内の Timeline としてマイグレーションの対象になるため `.txt` で置き、
    // テストが一時フォルダへ `.playable` としてコピーして使う。
    public class LegacyCutsceneTimelineFixtureTests
    {
        private const string FixturePath = "Packages/com.ddrive.core/Tests/Editor/Compat/Fixtures/legacy_m6/Legacy_Cutscene_Timeline_v1_3_1.playable.txt";

        private string _tempDir;
        private string _tempAsset;

        [SetUp]
        public void SetUp()
        {
            _tempDir = "Assets/__M6LegacyFixture_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Directory.CreateDirectory(_tempDir);
            _tempAsset = _tempDir + "/Legacy.playable";
            File.Copy(Path.GetFullPath(FixturePath), _tempAsset); // 絶対パス(持ち込み先の PackageCache でも届く。GF-R-07)
            AssetDatabase.ImportAsset(_tempAsset, ImportAssetOptions.ForceUpdate);
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(_tempDir);
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }

            var meta = _tempDir + ".meta";
            if (File.Exists(meta))
            {
                File.Delete(meta);
            }

            AssetDatabase.Refresh();
        }

        [Test]
        public void Fixture_IsReallyLegacy_ScriptReferenceIsNull()
        {
            const string ident = "m_EditorClassIdentifier: DDrive.Runtime:DDrive.Runtime.Cutscene.Tracks:CutsceneSignalTrack";
            var text = File.ReadAllText(_tempAsset);
            var idx = text.IndexOf(ident, StringComparison.Ordinal);
            Assert.GreaterOrEqual(idx, 0);
            var before = text.Substring(Math.Max(0, idx - 80), 80);
            StringAssert.Contains("m_Script: {fileID: 0}", before);
        }

        [Test]
        public void Migration_RewritesReferences_Idempotent_AndKeepsContent()
        {
            var before = File.ReadAllText(_tempAsset);
            var ctx = new MigrationContext(false);
            CutsceneTimelineScriptReferenceMigration.MigratePaths(ctx, new[] { _tempAsset });
            Assert.AreEqual(1, ctx.Log.Count);

            var after = File.ReadAllText(_tempAsset);
            Assert.AreNotEqual(before, after);

            // 書き換え後は対象が残っていない / 差分は m_Script の 6 行だけ
            Assert.IsNull(CutsceneTimelineScriptReferenceMigration.Rewrite(after, (a, n) => "x", out _), "書き換え後にまだ対象が残っています");
            var bl = before.Split('\n');
            var al = after.Split('\n');
            Assert.AreEqual(bl.Length, al.Length);
            var diff = 0;
            for (var i = 0; i < bl.Length; i++)
            {
                if (bl[i] != al[i])
                {
                    diff++;
                    StringAssert.Contains("m_Script: {fileID: 0}", bl[i]);
                    StringAssert.Contains("m_Script: {fileID: 11500000, guid: ", al[i]);
                }
            }

            Assert.AreEqual(6, diff);

            // 2 回目は何もしない
            var ctx2 = new MigrationContext(false);
            CutsceneTimelineScriptReferenceMigration.MigratePaths(ctx2, new[] { _tempAsset });
            Assert.AreEqual(0, ctx2.Log.Count);
            Assert.AreEqual(after, File.ReadAllText(_tempAsset));

            // 直したあとは MonoScript が付く(Player で読める形)
            AssertTimelineLoads();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(_tempAsset))
            {
                if (o is TrackAsset || o is Marker)
                {
                    Assert.IsNotNull(MonoScript.FromScriptableObject((ScriptableObject)o), o.GetType().Name + " に MonoScript が付いていません");
                }
            }
        }

        [Test]
        public void Rewrite_OnlyTouchesDDriveTypesWithKnownScript()
        {
            const string yaml =
                "--- !u!114 &1\nMonoBehaviour:\n  m_Script: {fileID: 0}\n  m_Name: \n  m_EditorClassIdentifier: DDrive.Runtime:DDrive.Runtime.Cutscene.Tracks:CutsceneSignalTrack\n" +
                "--- !u!114 &2\nMonoBehaviour:\n  m_Script: {fileID: 0}\n  m_EditorClassIdentifier: Other.Asm:Ns:Foo\n" +
                "--- !u!114 &3\nMonoBehaviour:\n  m_Script: {fileID: 0}\n  m_EditorClassIdentifier: DDrive.Runtime:Ns:Unknown\n" +
                "--- !u!114 &4\nMonoBehaviour:\n  m_Script: {fileID: 11500000, guid: aaaa, type: 3}\n  m_EditorClassIdentifier: DDrive.Runtime::Ns.Known\n";
            var result = CutsceneTimelineScriptReferenceMigration.Rewrite(
                yaml,
                (asm, name) => asm == "DDrive.Runtime" && name == "DDrive.Runtime.Cutscene.Tracks.CutsceneSignalTrack" ? "0123456789abcdef0123456789abcdef" : null,
                out var count);
            Assert.AreEqual(1, count);
            StringAssert.Contains("m_Script: {fileID: 11500000, guid: 0123456789abcdef0123456789abcdef, type: 3}", result);
            Assert.AreEqual(2, CountOf(result, "m_Script: {fileID: 0}"));
        }

        [Test]
        public void Rewrite_PreservesCrLf()
        {
            var yaml = "--- !u!114 &1\r\nMonoBehaviour:\r\n  m_Script: {fileID: 0}\r\n  m_EditorClassIdentifier: DDrive.Runtime:A.B:C\r\n";
            var result = CutsceneTimelineScriptReferenceMigration.Rewrite(yaml, (a, n) => "ffffffffffffffffffffffffffffffff", out _);
            Assert.AreEqual("--- !u!114 &1\r\nMonoBehaviour:\r\n  m_Script: {fileID: 11500000, guid: ffffffffffffffffffffffffffffffff, type: 3}\r\n  m_EditorClassIdentifier: DDrive.Runtime:A.B:C\r\n", result);
        }

        private void AssertTimelineLoads()
        {
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(_tempAsset);
            Assert.IsNotNull(timeline);
            var signalTracks = 0;
            var signalMarkers = 0;
            var externalMarkers = 0;
            foreach (var track in timeline.GetOutputTracks())
            {
                Assert.IsNotNull(track);
                if (track is CutsceneSignalTrack)
                {
                    signalTracks++;
                }

                foreach (var marker in track.GetMarkers())
                {
                    Assert.IsNotNull(marker);
                    if (marker is CutsceneSignalNotification)
                    {
                        signalMarkers++;
                    }
                    else if (marker.GetType().Name == "NetCheckCutsceneMarker")
                    {
                        externalMarkers++;
                    }
                }
            }

            Assert.AreEqual(1, signalTracks);
            Assert.AreEqual(5, signalMarkers);
            Assert.AreEqual(5, externalMarkers);
        }

        private static int CountOf(string text, string needle)
        {
            var n = 0;
            var i = 0;
            while ((i = text.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
            {
                n++;
                i += needle.Length;
            }

            return n;
        }
    }
}
