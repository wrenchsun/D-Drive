using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DDrive.Editor.Settings;
using DDrive.Editor.Validation;
using DDrive.Foundation.Validation;
using NUnit.Framework;

namespace DDrive.Tests.Editor
{
    // [11_tasks.md] M-4(2026-10-05) — 禁止 API の許可(行単位の許可コメント + 設定の許可リスト)。
    // 一時フォルダにだけ書く(実プロジェクトのソース・実 DDriveProjectSettings には触れない)。
    public class ForbiddenApiAllowanceTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            // 設定の許可リストのパスはプロジェクトルートからの相対パス(絶対パスは無効)なので、プロジェクト内の
            // Temp フォルダ(Unity の一時フォルダ。git 管理外)に作る。
            _tempDir = "Temp/ddrive_forbidden_allow_" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }

        private string WriteFile(string name, params string[] lines)
        {
            var path = Path.Combine(_tempDir, name);
            var dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, string.Join("\n", lines));
            return path;
        }

        private ForbiddenApiScanner.ScanReport Scan(IReadOnlyList<ForbiddenApiAllowEntry> entries = null)
            => ForbiddenApiScanner.ScanDetailed(_tempDir, entries);

        private static int Count(ForbiddenApiScanner.ScanReport r, string code) => r.Notices.Count(n => n.Code == code);

        [Test]
        public void SameLineComment_AllowsThatHit()
        {
            WriteFile("A.cs", "var t = Time.time; // ddrive-allow: Time(Host 引き継ぎのタイムアウトは実時間で測る)");
            var r = Scan();
            Assert.AreEqual(0, r.Violations.Count);
            Assert.AreEqual(1, r.Allowed.Count);
            Assert.AreEqual(ForbiddenApiScanner.AllowSource.Comment, r.Allowed[0].Source);
            Assert.AreEqual(1, Count(r, ForbiddenApiScanner.CodeAllowSummary));
        }

        [Test]
        public void PreviousLineComment_AllowsNextLineOnly()
        {
            WriteFile("A.cs",
                "// ddrive-allow: Instantiate(NGO の NetworkObject は Instantiate → Spawn が正規手順)",
                "var a = Instantiate(prefab);",
                "var b = Instantiate(prefab);");
            var r = Scan();
            Assert.AreEqual(1, r.Violations.Count);
            Assert.AreEqual(3, r.Violations[0].Line);
            Assert.AreEqual(1, r.Allowed.Count);
        }

        [Test]
        public void PreviousLineComment_DoesNotReachTwoLinesAhead()
        {
            WriteFile("A.cs",
                "// ddrive-allow: Time(理由)",
                "var x = 1;",
                "var t = Time.time;");
            var r = Scan();
            Assert.AreEqual(1, r.Violations.Count);
            Assert.AreEqual(1, Count(r, ForbiddenApiScanner.CodeAllowUnused));
        }

        [Test]
        public void OtherCommentLineBetween_BreaksTheAllow()
        {
            WriteFile("A.cs",
                "// ddrive-allow: Time(理由)",
                "// 補足",
                "var t = Time.time;");
            Assert.AreEqual(1, Scan().Violations.Count);
        }

        [Test]
        public void DifferentRuleName_DoesNotAllow()
        {
            WriteFile("A.cs", "var go = Instantiate(p); // ddrive-allow: Time(理由)");
            var r = Scan();
            Assert.AreEqual(1, r.Violations.Count);
            Assert.AreEqual(1, Count(r, ForbiddenApiScanner.CodeAllowUnused));
        }

        [Test]
        public void MissingReason_StaysViolation_WithMessage()
        {
            WriteFile("A.cs", "var t = Time.time; // ddrive-allow: Time");
            var r = Scan();
            Assert.AreEqual(1, r.Violations.Count);
            StringAssert.Contains("理由が必要", r.Violations[0].Message);
            Assert.AreEqual(0, r.Allowed.Count);
        }

        [Test]
        public void EmptyParentheses_AreInvalid()
        {
            WriteFile("A.cs", "var t = Time.time; // ddrive-allow: Time()");
            var r = Scan();
            Assert.AreEqual(1, r.Violations.Count);
            StringAssert.Contains("理由が必要", r.Violations[0].Message);
        }

        [Test]
        public void MissingReasonWithoutHit_GivesWarningNotice()
        {
            WriteFile("A.cs", "var x = 1; // ddrive-allow: Time");
            var r = Scan();
            Assert.AreEqual(0, r.Violations.Count);
            Assert.AreEqual(1, Count(r, ForbiddenApiScanner.CodeAllowNoReason));
            Assert.AreEqual(ValidationSeverity.Warning, r.Notices.First(n => n.Code == ForbiddenApiScanner.CodeAllowNoReason).Severity);
        }

        [Test]
        public void UnknownRuleName_IsInvalid_AndReported()
        {
            WriteFile("A.cs", "var t = Time.time; // ddrive-allow: Timez(理由)");
            var r = Scan();
            Assert.AreEqual(1, r.Violations.Count);
            Assert.AreEqual(1, Count(r, ForbiddenApiScanner.CodeAllowUnknownRule));
        }

        [Test]
        public void UnusedAllow_IsInfo()
        {
            WriteFile("A.cs", "var x = 1; // ddrive-allow: Time(もう要らない)");
            var r = Scan();
            var n = r.Notices.Single(x => x.Code == ForbiddenApiScanner.CodeAllowUnused);
            Assert.AreEqual(ValidationSeverity.Info, n.Severity);
            Assert.AreEqual(1, n.Line);
        }

        [Test]
        public void FullWidthParentheses_AndCaseInsensitiveRule_Work()
        {
            WriteFile("A.cs", "var t = Time.time; // DDRIVE-ALLOW: time（実時間で測る）");
            var r = Scan();
            Assert.AreEqual(0, r.Violations.Count);
            Assert.AreEqual(1, r.Allowed.Count);
            Assert.AreEqual("実時間で測る", r.Allowed[0].Reason);
        }

        [Test]
        public void ReasonMayContainParentheses()
        {
            WriteFile("A.cs", "var go = Instantiate(p); // ddrive-allow: Instantiate(NGO の Spawn(正規手順))");
            var r = Scan();
            Assert.AreEqual(0, r.Violations.Count);
            Assert.AreEqual("NGO の Spawn(正規手順)", r.Allowed[0].Reason);
        }

        [Test]
        public void TwoRulesOnOneLine_NeedTwoDirectives()
        {
            WriteFile("A.cs", "var t = Time.time + Instantiate(p).x; // ddrive-allow: Time(a) ddrive-allow: Instantiate(b)");
            var r = Scan();
            Assert.AreEqual(0, r.Violations.Count);
            Assert.AreEqual(2, r.Allowed.Count);
        }

        [Test]
        public void TwoRulesOnOneLine_OnlyOneDirective_LeavesTheOther()
        {
            WriteFile("A.cs", "var t = Time.time + Instantiate(p).x; // ddrive-allow: Time(a)");
            var r = Scan();
            Assert.AreEqual(1, r.Violations.Count);
            Assert.AreEqual(1, r.Allowed.Count);
        }

        [Test]
        public void DirectiveInsideStringLiteral_IsIgnored()
        {
            WriteFile("A.cs", "var s = \"// ddrive-allow: Time(x)\"; var t = Time.time;");
            var r = Scan();
            Assert.AreEqual(1, r.Violations.Count);
            Assert.AreEqual(0, r.Allowed.Count);
        }

        [Test]
        public void BlockCommentForm_IsNotRecognized()
        {
            WriteFile("A.cs", "var t = Time.time; /* ddrive-allow: Time(x) */");
            Assert.AreEqual(1, Scan().Violations.Count);
        }

        [Test]
        public void Settings_AllowsFolder()
        {
            WriteFile("ThirdParty/Lib.cs", "var t = Time.time;");
            WriteFile("Mine.cs", "var u = Time.time;");
            var entries = new List<ForbiddenApiAllowEntry>
            {
                new() { Path = _tempDir + "/ThirdParty/", Rule = "", Reason = "外部ライブラリ" },
            };
            var r = Scan(entries);
            Assert.AreEqual(1, r.Violations.Count);
            StringAssert.EndsWith("Mine.cs", r.Violations[0].FilePath);
            Assert.AreEqual(1, r.SettingsAllowedCount);
            Assert.AreEqual(0, r.CommentAllowedCount);
        }

        [Test]
        public void Settings_SingleFile_WithRule_OnlyThatRule()
        {
            var path = WriteFile("Gen.cs", "var t = Time.time; var go = Instantiate(p);");
            var entries = new List<ForbiddenApiAllowEntry>
            {
                new() { Path = _tempDir + "/Gen.cs", Rule = "time", Reason = "生成コード" },
            };
            var r = Scan(entries);
            Assert.AreEqual(1, r.Violations.Count);
            StringAssert.Contains("Instantiate", r.Violations[0].Message);
        }

        [Test]
        public void Settings_WithoutReasonOrWithUnknownRule_AreInvalid()
        {
            WriteFile("Lib.cs", "var t = Time.time;");
            var entries = new List<ForbiddenApiAllowEntry>
            {
                new() { Path = _tempDir + "/Lib.cs", Rule = "", Reason = "  " },
                new() { Path = _tempDir + "/Lib.cs", Rule = "Nope", Reason = "x" },
            };
            var r = Scan(entries);
            Assert.AreEqual(1, r.Violations.Count);
            Assert.AreEqual(2, Count(r, ForbiddenApiScanner.CodeAllowSettingsInvalid));
        }

        [Test]
        public void NoAllowances_ResultIsSameAsLegacyScan_NoNotices()
        {
            WriteFile("A.cs", "var t = Time.time;", "var go = Instantiate(p);");
            var detailed = Scan();
            var legacy = ForbiddenApiScanner.Scan(_tempDir);
            Assert.AreEqual(2, legacy.Count);
            Assert.AreEqual(legacy.Count, detailed.Violations.Count);
            Assert.AreEqual(0, detailed.Notices.Count);
            Assert.AreEqual(0, detailed.Allowed.Count);
            StringAssert.DoesNotContain("許可コメント", legacy[0].Message);
        }

        [Test]
        public void CommentAndSettings_AreCountedSeparately()
        {
            WriteFile("A.cs", "var t = Time.time; // ddrive-allow: Time(a)");
            WriteFile("Ext/B.cs", "var u = Time.time;");
            var entries = new List<ForbiddenApiAllowEntry>
            {
                new() { Path = _tempDir + "/Ext/", Reason = "外部" },
            };
            var r = Scan(entries);
            Assert.AreEqual(0, r.Violations.Count);
            Assert.AreEqual(1, r.CommentAllowedCount);
            Assert.AreEqual(1, r.SettingsAllowedCount);
            StringAssert.Contains("2 件(コメント 1、設定 1)", r.Notices.Single(n => n.Code == ForbiddenApiScanner.CodeAllowSummary).Message);
        }

        // D-Drive 自身のソースに、許可コメントの書式に見える誤認(説明文中の例など)が無いこと(読み取りのみ)。
        [Test]
        public void ThisPackage_HasNoAllowNotices()
        {
            // 持ち込み先では走査ルートが Assets(ゲームコード)になり、許可コメントの Info 等が正当に出る。
            if (!DDriveProjectSettings.instance.IsDevelopmentRepo)
            {
                Assert.Ignore("開発リポジトリ(D-Drive 自身を走査する環境)専用のテストです。");
            }

            var root = DDrive.Editor.CI.ResolveForbiddenApiScanRoot();
            if (!Directory.Exists(root))
            {
                Assert.Ignore("走査ルートがありません: " + root);
            }

            var r = ForbiddenApiScanner.ScanDetailed(root, null);
            // 許可の件数の要約(Info)は D-Drive 自身が許可コメントを持つので出る。それ以外(無効・未使用・誤認)が無いこと。
            var others = r.Notices.Where(n => n.Code != ForbiddenApiScanner.CodeAllowSummary).ToList();
            Assert.AreEqual(0, others.Count, string.Join(", ", others.Select(n => n.FilePath + ":" + n.Line + " " + n.Code)));
        }

        // 修正ラウンド 5(2026-10-05、docs/57 FZ-R-05) — D-Drive 自身(開発リポジトリの走査ルート = パッケージ)に、
        // 許可されていない禁止 API の当たりが 0 件であること。持ち込み先の走査ルートは Assets なので対象外。
        [Test]
        public void ThisPackage_HasNoForbiddenApiViolations()
        {
            if (!DDriveProjectSettings.instance.IsDevelopmentRepo)
            {
                Assert.Ignore("開発リポジトリ(D-Drive 自身を走査する環境)専用のテストです。");
            }

            var root = DDrive.Editor.CI.ResolveForbiddenApiScanRoot();
            if (!Directory.Exists(root))
            {
                Assert.Ignore("走査ルートがありません: " + root);
            }

            var r = ForbiddenApiScanner.ScanDetailed(root, null);
            Assert.AreEqual(0, r.Violations.Count, string.Join("\n", r.Violations.Select(v => v.FilePath + ":" + v.Line + " " + v.Message)));
        }

        // ── FZ-R-02: 設定の許可リストのパス一致は「区切り単位」 ──

        [Test]
        public void Settings_PathMatch_IsBySegment_NotStringPrefix()
        {
            WriteFile("Foo/In.cs", "var a = Time.time;");
            WriteFile("FooBar/Out.cs", "var b = Time.time;");
            var entries = new List<ForbiddenApiAllowEntry> { new() { Path = _tempDir + "/Foo", Reason = "外部" } };
            var r = Scan(entries);
            Assert.AreEqual(1, r.Violations.Count, "FooBar は Foo の配下ではない");
            StringAssert.Contains("FooBar", r.Violations[0].FilePath);
            Assert.AreEqual(1, r.SettingsAllowedCount);
        }

        [Test]
        public void Settings_PathMatch_IgnoresTrailingSlash_Backslash_DotPrefix_AndCase()
        {
            WriteFile("Foo/In.cs", "var a = Time.time;");
            foreach (var path in new[]
                     {
                         _tempDir + "/Foo/",
                         (_tempDir + "/Foo").Replace('/', '\\'),
                         "./" + _tempDir + "/Foo",
                         _tempDir.ToUpperInvariant() + "/FOO",
                     })
            {
                var r = Scan(new List<ForbiddenApiAllowEntry> { new() { Path = path, Reason = "外部" } });
                Assert.AreEqual(0, r.Violations.Count, "一致するはず: " + path);
            }
        }

        [Test]
        public void Settings_PathMatch_FileEntry_MatchesOnlyThatFile()
        {
            WriteFile("Gen.cs", "var a = Time.time;");
            WriteFile("Gen.cs.bak.cs", "var b = Time.time;");
            var r = Scan(new List<ForbiddenApiAllowEntry> { new() { Path = _tempDir + "/Gen.cs", Reason = "生成" } });
            Assert.AreEqual(1, r.Violations.Count);
            StringAssert.Contains("Gen.cs.bak.cs", r.Violations[0].FilePath);
        }

        [Test]
        public void Settings_TooBroadOrUnsafePaths_AreInvalid_AndDoNotAllowAnything()
        {
            WriteFile("A.cs", "var a = Time.time;");
            var entries = new List<ForbiddenApiAllowEntry>
            {
                new() { Path = "Temp", Reason = "x" },                                  // 1 階層(走査ルートの親でもある)
                new() { Path = "Temp/", Reason = "x" },
                new() { Path = _tempDir, Reason = "x" },                                // 走査ルートそのもの
                new() { Path = Path.GetFullPath(_tempDir + "/A.cs"), Reason = "x" },    // 絶対パス
                new() { Path = "/Temp/x/A.cs", Reason = "x" },                          // 絶対パス(ルート始まり)
                new() { Path = _tempDir + "/../" + Path.GetFileName(_tempDir) + "/A.cs", Reason = "x" }, // ..
            };
            var r = Scan(entries);
            Assert.AreEqual(1, r.Violations.Count, "どの無効な要素も許可として効かない");
            Assert.AreEqual(6, Count(r, ForbiddenApiScanner.CodeAllowSettingsInvalid));
            Assert.AreEqual(0, r.SettingsAllowedCount);
        }

        [Test]
        public void DescribeEntryProblem_ExplainsEachInvalidPath()
        {
            StringAssert.Contains("広すぎ", ForbiddenApiScanner.DescribeEntryProblem(new ForbiddenApiAllowEntry { Path = "Assets", Reason = "x" }));
            StringAssert.Contains("絶対パス", ForbiddenApiScanner.DescribeEntryProblem(new ForbiddenApiAllowEntry { Path = "C:/x/y", Reason = "x" }));
            StringAssert.Contains("'..'", ForbiddenApiScanner.DescribeEntryProblem(new ForbiddenApiAllowEntry { Path = "Assets/../x", Reason = "x" }));
            StringAssert.Contains("丸ごと", ForbiddenApiScanner.DescribeEntryProblem(new ForbiddenApiAllowEntry { Path = "Assets/Game", Reason = "x" }, "Assets/Game"));
            Assert.IsNull(ForbiddenApiScanner.DescribeEntryProblem(new ForbiddenApiAllowEntry { Path = "Assets/Plugins/ThirdParty", Reason = "x" }, "Assets"));
        }

        // ── FZ-R-06: 許可コメントの書式の契約 ──

        [Test]
        public void TextAfterTheClosingParenthesis_IsIgnored()
        {
            WriteFile("A.cs", "var t = Time.time; // ddrive-allow: Time(実時間) 5 秒で切る");
            var r = Scan();
            Assert.AreEqual(0, r.Violations.Count);
            Assert.AreEqual("実時間", r.Allowed[0].Reason);
        }

        // 将来の拡張は新しい接頭辞(ddrive-allow-<種類>:)で行う。今の版がそれを今の接頭辞と誤認して許可しない。
        [Test]
        public void FutureStylePrefixes_AreNotRecognizedAsAllowComments()
        {
            WriteFile("A.cs",
                "var a = Time.time; // ddrive-allow-file: Time(将来のファイル単位)",
                "var b = Time.time; // ddrive-allow-until: Time(理由) 2026-12-31",
                "var c = Time.time; // ddrive-allow-xxx: Time(x)");
            var r = Scan();
            Assert.AreEqual(3, r.Violations.Count, "どれも許可にならない(安全側)");
            Assert.AreEqual(0, r.Allowed.Count);
            Assert.AreEqual(0, r.Notices.Count, "許可コメントとして扱わないので Warning / Info も出ない");
        }

        [Test]
        public void PrefixAnywhereInTheComment_Works()
        {
            WriteFile("A.cs", "var t = Time.time; // TODO: 後で見直す ddrive-allow: Time(実時間)");
            var r = Scan();
            Assert.AreEqual(0, r.Violations.Count);
            Assert.AreEqual(1, r.Allowed.Count);
        }

        // 「直前の行」は当たりの出た物理行の 1 行上。複数行の文で当たりが 2 行目のとき、文の先頭の上に書いても効かない。
        [Test]
        public void PreviousLine_IsThePhysicalLineAboveTheHit()
        {
            WriteFile("A.cs",
                "// ddrive-allow: Time(理由)",
                "var t =",
                "    Time.time;");
            var r = Scan();
            Assert.AreEqual(1, r.Violations.Count);
            Assert.AreEqual(3, r.Violations[0].Line);
            Assert.AreEqual(1, Count(r, ForbiddenApiScanner.CodeAllowUnused));
        }

        [Test]
        public void Violation_CarriesRuleNameAndExcerpt()
        {
            WriteFile("A.cs", "    var go = Instantiate(prefab);  ");
            var v = Scan().Violations.Single();
            Assert.AreEqual("Instantiate", v.RuleName);
            Assert.AreEqual("var go = Instantiate(prefab);", v.Excerpt);
            Assert.AreEqual(1, v.Line);
        }

        [Test]
        public void RuleNames_AreStable()
        {
            CollectionAssert.AreEqual(
                new[] { "Time", "ResourcesLoad", "AddressablesLoad", "Instantiate", "AudioSourcePlay" },
                ForbiddenApiScanner.RuleNames);
        }
    }
}
