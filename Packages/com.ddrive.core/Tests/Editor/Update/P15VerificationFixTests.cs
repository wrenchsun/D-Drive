using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using DDrive.Editor;
using DDrive.Editor.Update;
using DDrive.Editor.Validation;
using DDrive.Foundation.Validation;
using NUnit.Framework;

namespace DDrive.Tests.Editor.Update
{
    // 2026-10-06 P-15 の人による確認(docs/43 §15)で見つかった BUG-1 と、仕様の判断 Q-1〜Q-3 への対応を固定する。
    //   BUG-1: git の標準出力 / エラーを UTF-8 で読む(日本語 Windows の既定は Shift_JIS)+ 読めなかった package.json を「宣言なし」にしない
    //   Q-1  : 同じ URL の再追加のメッセージ(過去形。ウィンドウに出す)
    //   Q-2  : 一覧の行の依存の表示(宣言した側 = 主表示、相手側 = 原因の手がかり)
    //   Q-3  : 依存の警告はアセットに紐付けない(プロジェクト全体の指摘)
    public class P15VerificationFixTests
    {
        private const string ToonUrl = "git+https://github.com/example/T-Drive.git?path=unity/com.tdrive.toon#v0.5.0";

        private sealed class FakeFetcher : IRemotePackageJsonFetcher
        {
            public string Json;

            public string FetchPackageJson(string cloneUrl, string subPath, string reference, out string warningMessage)
            {
                warningMessage = Json == null ? "failed" : null;
                return Json;
            }
        }

        private static PackageState State(string id, string version, string packageJson = null, string displayName = null)
            => new(id, displayName ?? id, version, "C:/pkg/" + id, DdriveUpdateDeclaration.Parse(packageJson));

        // ── BUG-1: GitProcess の文字コード ──

        [Test]
        public void BuildStartInfo_ReadsStdoutAndStderrAsUtf8_WithoutBom()
        {
            var info = GitProcess.BuildStartInfo(new[] { "show", "HEAD:package.json" }, null);

            Assert.AreEqual(65001, info.StandardOutputEncoding.CodePage, "標準出力は UTF-8");
            Assert.AreEqual(65001, info.StandardErrorEncoding.CodePage, "標準エラーも UTF-8");
            Assert.AreEqual(0, info.StandardOutputEncoding.GetPreamble().Length, "BOM なし");
            Assert.IsTrue(info.RedirectStandardOutput && info.RedirectStandardError && info.RedirectStandardInput);
            Assert.AreEqual("0", info.Environment["GIT_TERMINAL_PROMPT"]);
            CollectionAssert.AreEqual(new[] { "show", "HEAD:package.json" }, info.ArgumentList.ToArray());
        }

        [Test]
        public void Run_ReadsUtf8Output_OfRealGit_InTemporaryRepository()
        {
            // 日本語 Windows(既定コードページ Shift_JIS)で意味がある確認。一時リポジトリ(ローカル・ネットワークなし)に
            // 日本語入りの package.json をコミットし、`git show` の出力が文字化けせずに JSON として読めること。
            var probe = GitProcess.Run(new[] { "--version" }, null, 10000, CancellationToken.None);
            Assume.That(probe.Success, "git が使えること: " + probe.Error);

            var dir = Path.Combine(Path.GetTempPath(), "ddrive_gitutf8_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                const string json = "{\n  \"name\": \"com.test.jp\",\n  \"version\": \"1.0.0\",\n  \"displayName\": \"日本語パッケージ\",\n  \"description\": \"表情コントローラー\",\n  \"ddriveUpdate\": { \"requires\": { \"com.ddrive.core\": \"9.0.0\" } }\n}\n";
                File.WriteAllBytes(Path.Combine(dir, "package.json"), new UTF8Encoding(false).GetBytes(json));

                var common = new[] { "-c", "user.name=t", "-c", "user.email=t@example.com", "-c", "commit.gpgsign=false", "-c", "core.autocrlf=false" };
                Assert.IsTrue(GitProcess.Run(new[] { "init", "-q" }, dir, 20000, CancellationToken.None).Success);
                Assert.IsTrue(GitProcess.Run(common.Concat(new[] { "add", "package.json" }).ToArray(), dir, 20000, CancellationToken.None).Success);
                Assert.IsTrue(GitProcess.Run(common.Concat(new[] { "commit", "-q", "-m", "jp" }).ToArray(), dir, 20000, CancellationToken.None).Success);

                var show = GitProcess.Run(GitArguments.ShowPackageJson(null), dir, 20000, CancellationToken.None);

                Assert.IsTrue(show.Success, show.Error);
                StringAssert.Contains("日本語パッケージ", show.Stdout);
                StringAssert.Contains("表情コントローラー", show.Stdout);
                Assert.IsTrue(DdriveUpdateDeclaration.TryParse(show.Stdout, out var declaration), "JSON として読める");
                Assert.AreEqual("9.0.0", declaration.Requires["com.ddrive.core"]);
            }
            finally
            {
                try
                {
                    foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }

                    Directory.Delete(dir, true);
                }
                catch (Exception)
                {
                    // 一時フォルダの掃除失敗は無視する。
                }
            }
        }

        // ── BUG-1: 「宣言なし」と「読めなかった」の区別 ──

        [Test]
        public void TryParse_DistinguishesNoDeclaration_FromUnreadable()
        {
            Assert.IsTrue(DdriveUpdateDeclaration.TryParse("{ \"name\": \"com.x\", \"version\": \"1.0.0\" }", out var none));
            Assert.IsTrue(none.IsEmpty);
            Assert.IsFalse(none.IsUnreadable, "ddriveUpdate が無い = 宣言なし(読めた)");

            Assert.IsTrue(DdriveUpdateDeclaration.TryParse("{ \"ddriveUpdate\": null }", out _));

            foreach (var bad in new[] { "", "   ", "{ \"description\": \"日本語パ\u0081?\" ", "not json", "[1,2]", "{ \"ddriveUpdate\": \"x\" }", "{ \"ddriveUpdate\": [1] }" })
            {
                Assert.IsFalse(DdriveUpdateDeclaration.TryParse(bad, out var declaration), $"読めない: {bad}");
                Assert.IsTrue(declaration.IsUnreadable);
                Assert.IsTrue(declaration.IsEmpty);
            }
        }

        [Test]
        public void Parse_KeepsReturningEmptyForUnreadable_ForBackwardCompatibility()
        {
            Assert.AreSame(DdriveUpdateDeclaration.Empty, DdriveUpdateDeclaration.Parse("not json"));
            Assert.AreSame(DdriveUpdateDeclaration.Empty, DdriveUpdateDeclaration.Parse("{ \"ddriveUpdate\": \"x\" }"));
            Assert.AreSame(DdriveUpdateDeclaration.Empty, DdriveUpdateDeclaration.Parse(null));
        }

        [Test]
        public void Preflight_JapanesePackageJson_ReadsDeclarationAndWarns()
        {
            var installed = new[] { State("com.ddrive.core", "1.3.1"), State("com.tdrive.toon", "0.5.0") };
            var fetcher = new FakeFetcher
            {
                Json = "{\n  \"name\": \"com.tdrive.toon\",\n  \"version\": \"0.6.0\",\n  \"displayName\": \"トゥーン\",\n  \"description\": \"表情と確認用の日本語の説明です\",\n" +
                       "  \"ddriveUpdate\": { \"requires\": { \"com.ddrive.core\": \"1.4.0\", \"com.missing\": \"1.0.0\" } }\n}\n",
            };

            var result = UpdatePreflight.Run(fetcher, GitPackageUrl.Parse(ToonUrl), "com.tdrive.toon", "v0.6.0", installed);

            Assert.IsTrue(result.Fetched);
            StringAssert.Contains("確認しました", result.Message);
            Assert.AreEqual(2, result.Issues.Count, "満たせない requires を見逃さない");
            Assert.IsTrue(PackageDependencyChecker.HasAtLeast(result.Issues, DependencyIssueSeverity.Error));
        }

        [Test]
        public void Preflight_GarbledOrBrokenJson_SaysCouldNotCheck_AndDoesNotClaimChecked()
        {
            var installed = new[] { State("com.tdrive.toon", "0.5.0") };
            // 文字化け(Shift_JIS で読まれた UTF-8)で閉じ引用符が飲まれた JSON を模す。
            var fetcher = new FakeFetcher { Json = "{\n  \"version\": \"0.6.0\",\n  \"description\": \"日本語パ\u0081?\n  \"ddriveUpdate\": { \"requires\": { \"com.missing\": \"1.0.0\" } }\n}\n" };

            var result = UpdatePreflight.Run(fetcher, GitPackageUrl.Parse(ToonUrl), "com.tdrive.toon", "v0.6.0", installed);

            Assert.IsFalse(result.Fetched, "読めなかったので「確認しました」にしない");
            StringAssert.Contains("事前確認できませんでした", result.Message);
            StringAssert.Contains("読めませんでした", result.Message);
            StringAssert.Contains("更新後に確認します", result.Message);
            StringAssert.DoesNotContain("確認しました", result.Message);
        }

        [Test]
        public void Preflight_ReadablePackageJsonWithoutDeclaration_StaysChecked()
        {
            var installed = new[] { State("com.tdrive.toon", "0.5.0") };
            var fetcher = new FakeFetcher { Json = "{ \"version\": \"0.6.0\", \"description\": \"宣言なしの日本語\" }" };

            var result = UpdatePreflight.Run(fetcher, GitPackageUrl.Parse(ToonUrl), "com.tdrive.toon", "v0.6.0", installed);

            Assert.IsTrue(result.Fetched, "読めて宣言が無い = 従来どおり「確認しました」");
            StringAssert.Contains("確認しました", result.Message);
            Assert.IsEmpty(result.Issues);
        }

        [Test]
        public void Preflight_UnreadableDeclaration_StillChecksOthersAgainstNewVersion()
        {
            // D-Drive 以外が D-Drive の版に依存している状況で、上げ先(toon。0.4.0 へ下げる)の package.json が壊れていても、
            // 他のパッケージの宣言との照合(版だけ)は行う。
            var installed = new[]
            {
                State("com.ddrive.core", "1.4.0"),
                State("com.tdrive.toon", "0.5.0"),
                State("com.other", "1.0.0", "{ \"ddriveUpdate\": { \"requires\": { \"com.tdrive.toon\": \"0.5.0\" } } }"),
            };
            var fetcher = new FakeFetcher { Json = "{ broken" };

            var result = UpdatePreflight.Run(fetcher, GitPackageUrl.Parse(ToonUrl), "com.tdrive.toon", "v0.4.0", installed);

            Assert.IsFalse(result.Fetched);
            Assert.AreEqual(1, result.Issues.Count);
            Assert.AreEqual(PackageDependencyIssue.CodeRequiresOld, result.Issues[0].Code);
        }

        [Test]
        public void Check_UnreadableDeclaration_IsWarningBadDeclaration_NotSilentlyEmpty()
        {
            var unreadable = new PackageState("com.tdrive.toon", "Toon", "0.6.0", null, DdriveUpdateDeclaration.Unreadable);

            var issues = PackageDependencyChecker.Check(new[] { unreadable, State("com.ddrive.core", "1.4.0") });

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(PackageDependencyIssue.CodeBadDeclaration, issues[0].Code, "新しいコードは足さない(BAD-DECLARATION の範囲)");
            Assert.AreEqual(DependencyIssueSeverity.Warning, issues[0].Severity);
            Assert.AreEqual("com.tdrive.toon", issues[0].PackageId);
            StringAssert.Contains("package.json を読めませんでした", issues[0].Message);
        }

        [Test]
        public void CheckPlanned_UnreadableBefore_DoesNotReportAsNew()
        {
            var current = new[] { new PackageState("com.tdrive.toon", "Toon", "0.5.0", null, DdriveUpdateDeclaration.Unreadable) };

            var planned = PackageDependencyChecker.CheckPlanned(current, "com.tdrive.toon", "0.6.0", null);

            Assert.IsEmpty(planned, "今も読めていない件は「更新で新しく出た問題」ではない");
        }

        // ── Q-1: 再追加のメッセージ ──

        [Test]
        public void AddPlanner_RegisterExisting_MessageIsPastTense()
        {
            var manifest = Newtonsoft.Json.Linq.JObject.Parse(
                "{ \"dependencies\": { \"com.tdrive.toon\": \"" + ToonUrl + "\" } }");
            var planner = new PackageAddPlanner(null);

            var byUrl = planner.Plan("https://github.com/example/T-Drive.git?path=unity/com.tdrive.toon", manifest);
            var byId = planner.Plan("com.tdrive.toon", manifest);

            Assert.AreEqual(PackageAddOutcome.RegisterExisting, byUrl.Outcome);
            Assert.AreEqual("manifest に同じ URL の com.tdrive.toon があります。管理対象に登録しました。", byUrl.Message);
            Assert.AreEqual(PackageAddOutcome.RegisterExisting, byId.Outcome);
            Assert.AreEqual("com.tdrive.toon を管理対象に登録しました。", byId.Message);
        }

        // ── Q-2: 行の依存の表示 ──

        private static List<PackageDependencyIssue> IssuesFor(string toonJson, string ddriveVersion = "1.3.1", string toonVersion = "0.6.0")
            => PackageDependencyChecker.Check(new[]
            {
                State("com.ddrive.core", ddriveVersion, null, "D-Drive"),
                State("com.tdrive.facial", toonVersion, toonJson, "T-Drive Facial"),
            });

        [Test]
        public void Describe_NoIssues_IsOk()
            => Assert.AreEqual("依存 OK", ManagedPackageRows.DescribeDependency("com.ddrive.core", new List<PackageDependencyIssue>()));

        [Test]
        public void Describe_CompatibleOld_DeclarerGetsWarning_CounterpartGetsWeakerHint()
        {
            var issues = IssuesFor("{ \"ddriveUpdate\": { \"compatibleWith\": { \"com.ddrive.core\": \"1.4.0\" } } }");

            var declarer = ManagedPackageRows.DescribeDependency("com.tdrive.facial", issues);
            var counterpart = ManagedPackageRows.DescribeDependency("com.ddrive.core", issues);

            Assert.AreEqual("⚠ 依存に注意(D-Drive v1.4.0 以降に対応、現在 v1.3.1)", declarer, "宣言した側の行に主表示");
            Assert.AreEqual("ℹ T-Drive Facial が v1.4.0 以降を想定しています(現在 v1.3.1)", counterpart, "相手側は警告にしない");
            StringAssert.DoesNotContain("⚠", counterpart);
            StringAssert.DoesNotContain("依存に注意", counterpart);
        }

        [Test]
        public void Describe_RequiresOld_CounterpartSaysRequires()
        {
            var issues = IssuesFor("{ \"ddriveUpdate\": { \"requires\": { \"com.ddrive.core\": \"1.4.0\" } } }");

            Assert.AreEqual("✗ 依存を満たしていません(D-Drive v1.4.0 以降が必要、現在 v1.3.1)", ManagedPackageRows.DescribeDependency("com.tdrive.facial", issues));
            Assert.AreEqual("ℹ T-Drive Facial が v1.4.0 以降を要求しています(現在 v1.3.1)", ManagedPackageRows.DescribeDependency("com.ddrive.core", issues));
        }

        [Test]
        public void Describe_RequiresMissing_OnlyDeclarerRow()
        {
            var issues = IssuesFor("{ \"ddriveUpdate\": { \"requires\": { \"com.missing\": \"1.0.0\" } } }");

            Assert.AreEqual("✗ 依存を満たしていません(com.missing v1.0.0 以降が必要、未導入)", ManagedPackageRows.DescribeDependency("com.tdrive.facial", issues));
            Assert.AreEqual("依存 OK", ManagedPackageRows.DescribeDependency("com.ddrive.core", issues), "相手は居ないので他の行は変わらない");
        }

        [Test]
        public void Describe_UnreadableDeclaration_DeclarerRowSaysPackageJsonUnreadable()
        {
            var issues = PackageDependencyChecker.Check(new[]
            {
                new PackageState("com.tdrive.facial", "T-Drive Facial", "0.6.0", null, DdriveUpdateDeclaration.Unreadable),
            });

            Assert.AreEqual("⚠ 依存に注意(package.json を読めません)", ManagedPackageRows.DescribeDependency("com.tdrive.facial", issues));
        }

        [Test]
        public void Describe_MajorAhead_InfoOnBothRows()
        {
            var issues = IssuesFor("{ \"ddriveUpdate\": { \"compatibleWith\": { \"com.ddrive.core\": \"1.0.0\" } } }", "2.0.0");

            StringAssert.StartsWith("ℹ 確認事項あり", ManagedPackageRows.DescribeDependency("com.tdrive.facial", issues));
            StringAssert.StartsWith("ℹ T-Drive Facial が宣言しているのは v1.0.0 以降です", ManagedPackageRows.DescribeDependency("com.ddrive.core", issues));
        }

        [Test]
        public void Describe_MultipleCounterparts_ShowsFirstAndCount()
        {
            var issues = PackageDependencyChecker.Check(new[]
            {
                State("com.ddrive.core", "1.3.1", null, "D-Drive"),
                State("com.a", "1.0.0", "{ \"ddriveUpdate\": { \"compatibleWith\": { \"com.ddrive.core\": \"1.4.0\" } } }", "A"),
                State("com.b", "1.0.0", "{ \"ddriveUpdate\": { \"compatibleWith\": { \"com.ddrive.core\": \"1.5.0\" } } }", "B"),
            });

            var text = ManagedPackageRows.DescribeDependency("com.ddrive.core", issues);

            StringAssert.StartsWith("ℹ A が v1.4.0 以降を想定しています(現在 v1.3.1)", text);
            StringAssert.EndsWith("(ほか 1 件)", text);
        }

        [Test]
        public void Describe_OwnAndCounterpart_AreJoined()
        {
            // D-Drive 自身も ddriveUpdate を宣言する(Toon に依存)+ Toon も D-Drive に依存、のような両方向。
            var issues = PackageDependencyChecker.Check(new[]
            {
                State("com.ddrive.core", "1.3.1", "{ \"ddriveUpdate\": { \"compatibleWith\": { \"com.tdrive.facial\": \"9.0.0\" } } }", "D-Drive"),
                State("com.tdrive.facial", "0.6.0", "{ \"ddriveUpdate\": { \"compatibleWith\": { \"com.ddrive.core\": \"1.4.0\" } } }", "T-Drive Facial"),
            });

            var text = ManagedPackageRows.DescribeDependency("com.ddrive.core", issues);

            Assert.AreEqual("⚠ 依存に注意(T-Drive Facial v9.0.0 以降に対応、現在 v0.6.0) / ℹ T-Drive Facial が v1.4.0 以降を想定しています(現在 v1.3.1)", text);
        }

        // ── Q-3: 依存の警告をアセットに紐付けない ──

        [Test]
        public void RunValidation_PackageDependencyFindings_AreProjectWide_NotAttachedToAnyAsset_AndReportedOnce()
        {
            var original = PackageDependencyValidator.PackagesProvider;
            try
            {
                PackageDependencyValidator.PackagesProvider = () => new List<PackageState>
                {
                    State("com.ddrive.core", "1.3.1", null, "D-Drive"),
                    State("com.tdrive.facial", "0.6.0", "{ \"ddriveUpdate\": { \"compatibleWith\": { \"com.ddrive.core\": \"1.4.0\" } } }", "T-Drive Facial"),
                };

                var reports = CI.RunValidation();

                var found = reports.Where(r => r.Result.Code == PackageDependencyIssue.CodeCompatibleOld).ToList();
                Assert.AreEqual(1, found.Count, "Run All 1 回につき 1 回だけ");
                Assert.IsNull(found[0].Asset, "どのアセットにも紐付けない(プロジェクト全体の指摘)");
                Assert.AreEqual(ValidationSeverity.Warning, found[0].Result.Severity);
                Assert.IsFalse(
                    reports.Any(r => r.Asset != null && (r.Result.Code ?? string.Empty).StartsWith("DD-PKGDEP-", StringComparison.Ordinal)),
                    "依存の警告が無関係なアセットのパス付きで出ない");
            }
            finally
            {
                PackageDependencyValidator.PackagesProvider = original;
            }
        }

        [Test]
        public void PerAssetValidation_DoesNotIncludePackageDependencyValidator()
        {
            Assert.IsFalse(
                DataValidationRunner.Validators.Any(v => v is PackageDependencyValidator),
                "1 アセットの個別検証には出さない(全アセットの結果として現れてしまう)");
            Assert.IsTrue(DataValidationRunner.IsProjectWide(new PackageDependencyValidator()));
        }
    }
}
