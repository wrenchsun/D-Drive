using System.Collections.Generic;
using DDrive.Editor.Inspector;
using DDrive.Editor.Preview;
using DDrive.Editor.Ux;
using DDrive.Foundation.Data;
using DDrive.Foundation.Manager;
using ExternalPackage.Fake;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExternalContract.Tests
{
    // [1014_extension_platform.md] §2.3(X-3) / [42_distribution.md] §5.14 E-27 — 持ち込み先が書く専用エディターを模したテスト用の窓。
    // UxEditorWindow<T> / FieldGuide<T> / TuneKnobs + ITuneSource / DurationBar / UxPreviewDriverBase を public API だけで使う。
    // メニューには載せない(テストが直接開く)。

    public sealed class ExternalSampleFieldGuide : FieldGuide<ExternalSampleData>
    {
        public const string PurShow = "見せ方";
        public const string PurTune = "長さと調整";

        public static readonly ExternalSampleFieldGuide Instance = new ExternalSampleFieldGuide();

        private static readonly FieldGuideEntry[] Table =
        {
            E(nameof(ExternalSampleData.Intensity), "強さ", "効果の強さ。", FieldTier.Required, PurShow, "強度", "パワー"),
            E(nameof(ExternalSampleData.Loop), "ループ", "止めるまで続けるか。", FieldTier.Common, PurShow, "繰り返し"),
            E(nameof(ExternalSampleData.Duration), "続く秒数", "続く長さ。", FieldTier.Common, PurTune, "長さ", "時間"),
            E(nameof(ExternalSampleData.FadeOutSec), "余韻(秒)", "止めたあとの余韻。", FieldTier.Advanced, PurTune, "フェード"),
            E(nameof(ExternalSampleData.Knobs), "調整つまみ", "値を回して調整する。", FieldTier.Common, PurTune, "つまみ", "パラメータ"),
        };

        protected override IReadOnlyList<FieldGuideEntry> SpecificEntries => Table;
        protected override IReadOnlyList<string> SpecificPurposes { get; } = new[] { PurShow, PurTune };
    }

    public sealed class ExternalSampleTuneSource : ITuneSource
    {
        private static readonly string[] Fields = { "Name" };
        private static readonly string[] Kinds = { "つまみ" };

        private readonly ExternalSampleData _data;

        public ExternalSampleTuneSource(ExternalSampleData data)
        {
            _data = data;
        }

        public Object Owner => _data;
        public string OwnerKey => _data != null ? _data.name : string.Empty;
        public int Count => _data.Knobs.Count;
        public string Label(int index) => _data.Knobs[index].Name;
        public string TargetName(int index) => _data.Knobs[index].Name;
        public TuneValueKind KindOf(int index) => TuneValueKind.Float;
        public ParamValue Value(int index) => ParamValue.Of(_data.Knobs[index].Value);
        public void SetValue(int index, ParamValue value) => _data.Knobs[index].Value = value.FloatValue;

        public string Signature()
        {
            var s = _data.Knobs.Count.ToString();
            foreach (var k in _data.Knobs)
            {
                s += "|" + k.Name;
            }

            return s;
        }

        public int InferCategory(int index) => TuneCategories.Infer(TuneValueKind.Float, Label(index), TargetName(index));
        public string ArrayPath => "Knobs";
        public IReadOnlyList<string> DetailFields => Fields;
        public IReadOnlyList<string> AddKinds => Kinds;
        public TuneAddPlan PlanAdd(int kind) => new TuneAddPlan { Free = true };
        public void Append(int kind, TuneCandidate candidate) { }

        public void AppendFree()
        {
            Undo.RecordObject(_data, "Add Knob");
            _data.Knobs.Add(new ExternalSampleKnob { Name = "Knob " + (_data.Knobs.Count + 1) });
            EditorUtility.SetDirty(_data);
        }

        public void RemoveAt(int index)
        {
            Undo.RecordObject(_data, "Remove Knob");
            _data.Knobs.RemoveAt(index);
            EditorUtility.SetDirty(_data);
        }

        public void ApplyLive(string label, ParamValue value) { }
    }

    // UxPreviewDriverBase の最小の派生: 外部の Manager(ExternalCountingManager)を Edit Mode で Tick し、停止で台帳を空にする。
    public sealed class ExternalSamplePreviewDriver : UxPreviewDriverBase
    {
        public ExternalCountingManager Manager { get; } = new ExternalCountingManager();
        public bool Playing { get; private set; }
        public int ResetCount { get; private set; }
        public PreviewResetReason LastResetReason { get; private set; }

        public ExternalSamplePreviewDriver() : base("External Sample Preview")
        {
        }

        public void Play()
        {
            Playing = true;
            var _ = PreviewRoot; // ルートを作る
        }

        public void Stop() => Playing = false;

        protected override void OnTick(float scaledDt)
        {
            if (Playing)
            {
                Manager.Tick(scaledDt);
            }
        }

        protected override void OnReset(PreviewResetReason reason)
        {
            Playing = false;
            ResetCount++;
            LastResetReason = reason;
            Manager.StopAll(StopReason.Manual);
        }
    }

    [DataEditor(typeof(ExternalSampleData), "Sample Editor で開く")]
    public sealed class ExternalSampleEditorWindow : UxEditorWindow<ExternalSampleData>
    {
        private ExternalSamplePreviewDriver _driver;

        public static void Open(ExternalSampleData target)
        {
            var w = OpenWindow<ExternalSampleEditorWindow>("Sample Editor");
            if (target != null)
            {
                w.SetTarget(target);
            }
        }

        public ExternalSamplePreviewDriver Driver => _driver;

        protected override string PrefsKey => "ExternalSample";
        protected override FieldGuide<ExternalSampleData> Guide => ExternalSampleFieldGuide.Instance;
        protected override string ManualPage => "external-sample";

        protected override string CardDescription(string purpose)
        {
            switch (purpose)
            {
                case ExternalSampleFieldGuide.PurShow: return "どう見せるか";
                case ExternalSampleFieldGuide.PurTune: return "長さと調整つまみ";
                default: return "一覧に出る名前・分類・メモ";
            }
        }

        protected override bool SupportsPlayback => true;
        protected override bool IsPlaying => _driver != null && _driver.Playing;

        protected override void OnWindowEnable() => _driver = new ExternalSamplePreviewDriver { Speed = Speed };

        protected override void OnWindowDisable()
        {
            _driver?.Dispose();
            _driver = null;
        }

        protected override void StartPlayback(bool focus) => _driver?.Play();

        protected override void StopPlayback() => _driver?.Stop();

        protected override void OnSpeedChanged(float speed)
        {
            if (_driver != null)
            {
                _driver.Speed = speed;
            }
        }

        protected override UxRow CreateRow(FieldGuideEntry e, UxCard card, bool isAdmin)
        {
            var data = Target;
            switch (e.Field)
            {
                case nameof(ExternalSampleData.FadeOutSec):
                    return null; // 長さの欄に含める

                case nameof(ExternalSampleData.Duration):
                {
                    var bar = new DurationBar(
                        data,
                        () => new DurationBar.Values { Mode = data.Loop ? UxLifeMode.Loop : UxLifeMode.Duration, Duration = data.Duration, Fade = data.FadeOutSec },
                        v =>
                        {
                            data.Loop = v.Mode == UxLifeMode.Loop;
                            data.Duration = v.Duration;
                            data.FadeOutSec = v.Fade;
                        },
                        final => VisualEdited(final, false));
                    var entries = new[] { e, Guide.Find(nameof(ExternalSampleData.FadeOutSec)) };
                    return new UxVisualRow(So, DefaultSo, entries, "長さ・消え方", "続く秒数と余韻を 1 本のバーで。", FieldTier.Common, bar, RowChanged);
                }

                case nameof(ExternalSampleData.Knobs):
                {
                    var panel = new TuneKnobs(new ExternalSampleTuneSource(data), So, () => VisualEdited(false, false), () => { So?.Update(); RunValidation(); RefreshAll(false); });
                    return new TuneKnobsRow(So, DefaultSo, new[] { e }, "値を回して調整します。", panel, RowChanged);
                }

                default:
                    return base.CreateRow(e, card, isAdmin);
            }
        }
    }
}
