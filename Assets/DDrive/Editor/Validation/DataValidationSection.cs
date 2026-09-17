using System;
using System.Collections.Generic;
using System.Reflection;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Validation
{
    // [09_editor_tools.md] §11(2026-09-17、[39] U-13) — 各専用エディタ共通の「個別検証」セクション。
    //
    // それまでは Vfx / Anchor / AnchorGroup / Anim / Anim2D / Canvas / Presentation が
    // 「Foldout を作って種別の Validator を new して HelpBox を積む」ほぼ同じコードを各自持ち、
    // Audio / Camera Fx / Material / Model / Prefab / Button Skin / Slider Skin には無い、という
    // ばらつきがあった(U-13)。この部品に一本化し、無かったエディタにも付ける。
    //
    // - 実行する Validator は「その Data の AssetType に一致するもの」+「1 アセット単位で意味がある
    //   IUniversalValidator」(ValueDef / Addressables 登録 / NetMode)。プロジェクト全体を対象にする
    //   Validator(仕様書差分・カタログ網羅)は Run All / CI 側の担当なので個別検証には出さない
    //   (DataValidationRunner.ProjectWideValidatorNames)
    // - FixAction 付きの結果には「修正」ボタンを出す(AssetBrowser の Validation 一覧と同じ)
    // - Validator が例外を投げてもセクション全体を落とさない(CLAUDE.md §0-4: 警告 + 継続)
    // - 横幅 500px でも見切れない([09] §7.1): HelpBox は折り返し、ボタン行は flexWrap で 2 段になる
    public sealed class DataValidationSection : VisualElement
    {
        private readonly Foldout _foldout;
        private readonly string _title;
        private readonly bool _includeSameTypeAssets;
        private readonly string _emptyTargetMessage;

        private AssetDataBase _target;

        /// <param name="title">Foldout の見出し。既定の「検証」で揃えること。</param>
        /// <param name="expanded">初期状態で開いておくか。</param>
        /// <param name="includeSameTypeAssets">
        /// true にすると ValidationContext に同じ Data 型のアセットをすべて載せる。
        /// Anchor / AnchorGroup のように「他のアセットとの関係(入れ子・循環)」を見る Validator 用。
        /// 既定(false)は対象 1 件だけの軽い文脈。
        /// </param>
        /// <param name="emptyTargetMessage">対象未選択のときに出す文言。</param>
        public DataValidationSection(
            string title = "検証",
            bool expanded = true,
            bool includeSameTypeAssets = false,
            string emptyTargetMessage = "対象アセットを選ぶと検証結果が出ます。")
        {
            _title = title;
            _includeSameTypeAssets = includeSameTypeAssets;
            _emptyTargetMessage = emptyTargetMessage;

            _foldout = new Foldout { text = title, value = expanded };
            Add(_foldout);
        }

        public AssetDataBase Target => _target;

        // 対象を差し替えて再検証する。null でも例外にせず「未選択」表示にする。
        public void Bind(AssetDataBase data)
        {
            _target = data;
            Refresh();
        }

        // 対象は変えずに再検証する(編集のたびに呼ぶ)。
        public void Refresh()
        {
            _foldout.Clear();

            if (_target == null)
            {
                _foldout.text = _title;
                _foldout.Add(new Label(_emptyTargetMessage) { style = { opacity = 0.6f, whiteSpace = WhiteSpace.Normal } });
                return;
            }

            var results = DataValidationRunner.Run(_target, _includeSameTypeAssets);

            var errors = 0;
            var warnings = 0;
            for (var i = 0; i < results.Count; i++)
            {
                switch (results[i].Severity)
                {
                    case ValidationSeverity.Error:
                        errors++;
                        break;
                    case ValidationSeverity.Warning:
                        warnings++;
                        break;
                }
            }

            _foldout.text = errors == 0 && warnings == 0
                ? $"{_title}: ✓ 問題なし"
                : $"{_title}: エラー {errors} / 警告 {warnings}";

            if (results.Count == 0)
            {
                _foldout.Add(new Label("Validation に問題はありません。") { style = { opacity = 0.6f, whiteSpace = WhiteSpace.Normal } });
                return;
            }

            for (var i = 0; i < results.Count; i++)
            {
                _foldout.Add(BuildResultRow(results[i]));
            }
        }

        private VisualElement BuildResultRow(ValidationResult result)
        {
            var type = result.Severity switch
            {
                ValidationSeverity.Error => HelpBoxMessageType.Error,
                ValidationSeverity.Warning => HelpBoxMessageType.Warning,
                _ => HelpBoxMessageType.Info,
            };

            var box = new HelpBox(result.Message, type);

            if (result.FixAction == null)
            {
                return box;
            }

            // 500px 幅でも収まるよう、HelpBox とボタンは横並びにせず縦に積む([09] §7.1)。
            var block = new VisualElement();
            block.Add(box);

            var buttonRow = new VisualElement
            {
                style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginBottom = 2 },
            };
            buttonRow.Add(new Button(() => InvokeFix(result.FixAction)) { text = "修正" });
            block.Add(buttonRow);
            return block;
        }

        private void InvokeFix(Action fix)
        {
            try
            {
                fix();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DDrive] 検証の「修正」に失敗しました: {e.Message}");
            }

            Refresh();
        }
    }

    // DataValidationSection の中身(検証の実行だけ)。テストから直接呼べるよう UI とは分けている。
    public static class DataValidationRunner
    {
        // プロジェクト全体を 1 回まとめて見る Validator。1 アセットの「個別検証」に出しても意味が無く、
        // ファイル I/O(Specs/*.json)やカタログ全走査を編集のたびに行うことになるため除外する。
        // Run All(CI.RunValidation)には従来どおり出る。
        private static readonly HashSet<string> ProjectWideValidatorNames = new(StringComparer.Ordinal)
        {
            "SpecDiffValidator",
            "ContentHashCatalogCoverageValidator",
            "CatalogAddressCoverageValidator",
        };

        private static List<IValidator> _validators;

        public static IReadOnlyList<IValidator> Validators => _validators ??= Discover();

        // 2026-09-17(docs/41_phase6_review_2026-09-17.md P2-6) — 「プロジェクト全体を
        // 1 回まとめて見る Validator か」の判定はここが唯一の定義。アセット単位の判定を行う他の経路
        // (`SpecWebSender` の isPlaceholder = `CI.RunValidation(includeProjectWideValidators: false)`)からも
        // 使う。全体結果は `ValidatorRegistry.RunAll` が「その時渡されたアセット」に紐付けてしまうため、
        // アセット単位の判定に混ぜると無関係なアセットの結果として現れる。
        public static bool IsProjectWide(IValidator validator)
            => validator != null && ProjectWideValidatorNames.Contains(validator.GetType().Name);

        public static List<ValidationResult> Run(AssetDataBase data, bool includeSameTypeAssets = false)
        {
            var results = new List<ValidationResult>();
            if (data == null)
            {
                return results;
            }

            var assetType = ResolveAssetType(data);
            var ctx = new ValidationContext(BuildContextAssets(data, includeSameTypeAssets));

            var validators = Validators;
            for (var i = 0; i < validators.Count; i++)
            {
                var validator = validators[i];
                var applies = validator is IUniversalValidator || validator.Target == assetType;
                if (!applies)
                {
                    continue;
                }

                // CLAUDE.md §0-4: 1 つの Validator が落ちても他の結果とセクション自体は生かす。
                try
                {
                    foreach (var result in validator.Validate(data, ctx))
                    {
                        results.Add(result);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[DDrive] 個別検証 '{validator.GetType().Name}' が失敗しました: {e.Message}");
                }
            }

            return results;
        }

        private static List<AssetDataBase> BuildContextAssets(AssetDataBase data, bool includeSameTypeAssets)
        {
            var all = new List<AssetDataBase>();

            if (includeSameTypeAssets)
            {
                foreach (var guid in AssetSearch.FindAssets("t:" + data.GetType().Name))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<AssetDataBase>(AssetDatabase.GUIDToAssetPath(guid));
                    if (asset != null)
                    {
                        all.Add(asset);
                    }
                }
            }

            if (!all.Contains(data))
            {
                all.Add(data);
            }

            return all;
        }

        private static List<IValidator> Discover()
        {
            var list = new List<IValidator>();
            foreach (var validator in CI.DiscoverValidators())
            {
                if (ProjectWideValidatorNames.Contains(validator.GetType().Name))
                {
                    continue;
                }

                list.Add(validator);
            }

            return list;
        }

        // ValidatorRegistry.ResolveAssetType と同じ規則(AssetIdDefinitionAttribute)。
        private static AssetType ResolveAssetType(AssetDataBase asset)
            => asset.GetType().GetCustomAttribute<AssetIdDefinitionAttribute>()?.Type ?? AssetType.None;
    }
}
