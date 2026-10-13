using System.Collections.Generic;
using DDrive.Editor.Inspector;
using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Editor.Ux;
using DDrive.Foundation.Handle;
using DDrive.Runtime.Haptics;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace DDrive.Editor.CameraFx
{
    // Haptics(パッドの振動)のエディター(docs/1010 UX-10。旧 CameraFxEditorWindow は Archive メニューへ退避)。
    // 共通基盤(UxEditorWindow<HapticsData> / FieldGuide / UxRow / UxCard)で組み、ここに書くのは Haptics 固有の部分だけ:
    // 欄の表(HapticsFieldGuide)・雛形 10 種・波形グラフ(CameraFxGraphView。強さと長さをドラッグで編集)・
    // 接続中のパッドの振動(EditorHapticsPreviewDriver = 実 HapticsManager。ADR-4)。
    // Shake は別ウィンドウ(ShakeEditor)。ヒーローのボタンで行き来する。
    [DataEditor(typeof(HapticsData), "Haptics Editor で開く")]
    internal sealed class HapticsEditor : UxEditorWindow<HapticsData>
    {
        // 空状態の「サンプルを作る」(docs/1010 UX-19)
        protected override bool HasSample => true;

        protected override HapticsData CreateSample() => UxSamples.Haptics();

        private const string Page = "camera-haptics";

        private EditorHapticsPreviewDriver _driver;
        private Handle<HapticMarker> _handle = Handle<HapticMarker>.Invalid;
        private HelpBox _padHelp;
        private Label _status;

        // ── 入口 ──

        public static void Open(HapticsData target)
        {
            var w = OpenWindow<HapticsEditor>("Haptics Editor");
            if (target != null)
            {
                w.SetTarget(target);
            }
        }

        // Shake Editor から切り替えてきたとき: 選択中の Haptics、なければ最近開いた Haptics を開く。ウィンドウは元のウィンドウの隣に出す。
        public static void SwitchFrom(EditorWindow from)
        {
            var w = GetWindow<HapticsEditor>("Haptics Editor", true, from.GetType());
            w.minSize = new Vector2(500, 380);
            var target = Selection.activeObject as HapticsData;
            if (target == null)
            {
                var recent = DataSwitcher.LoadRecent(typeof(HapticsData));
                target = recent.Count > 0 ? recent[0] as HapticsData : null;
            }

            if (target != null)
            {
                w.SetTarget(target);
            }
        }

        // ── 土台への指定 ──

        protected override string PrefsKey => "Haptics";
        protected override FieldGuide<HapticsData> Guide => HapticsFieldGuide.Instance;
        protected override string ManualPage => Page;
        protected override string SearchHint => "ラベル・説明・検索語に一致する設定だけ出す(例: 低周波 / 長さ / パッド)";

        protected override string ManualAnchor(string purpose)
        {
            switch (purpose)
            {
                case HapticsFieldGuide.PurStart: return "haptics-preset";
                case HapticsFieldGuide.PurGraph: return "haptics-graph";
                case HapticsFieldGuide.PurMotor: return "haptics-motor";
                case HapticsFieldGuide.PurControl: return "haptics-control";
                case HapticsFieldGuide.PurCheck: return "haptics-check";
                case FieldGuide<HapticsData>.AdminPurpose: return "admin";
                default: return null;
            }
        }

        protected override string[] ModeTips => new[]
        {
            "雛形と波形グラフだけ。雛形を選んで ▶ でパッドを振動させる",
            "よく使う欄まで。低周波・高周波のモーター(強さと長さ)",
            "すべての欄(優先度・自分のときだけ・機種別の拡張・動作の取り決め・イベント)",
        };

        // 欄名の入っていない検査(「振動の尺が 2 秒を超えています」)をモーターの欄に結びつける。
        private static readonly KeyValuePair<string, string>[] MessageTokens =
        {
            new KeyValuePair<string, string>("振動の尺", "LowFreq"),
        };

        protected override IReadOnlyList<KeyValuePair<string, string>> ValidationTokens => MessageTokens;

        protected override string CardDescription(string purpose)
        {
            switch (purpose)
            {
                case HapticsFieldGuide.PurStart: return "10 種の雛形から選んで、2 つのモーターのカーブを一式入れる";
                case HapticsFieldGuide.PurGraph: return "低周波・高周波の強さを時間軸で見る。点と右端のドラッグで強さ・長さを変える";
                case HapticsFieldGuide.PurMotor: return "ドスン(低周波)とビリビリ(高周波)の強さと長さ";
                case HapticsFieldGuide.PurControl: return "同時に鳴ったときの扱い・自分だけに鳴らすか・機種別の拡張";
                case HapticsFieldGuide.PurCheck: return "接続中のパッドを実際に振動させて確かめる";
                default: return "一覧に出る名前・分類・メモ";
            }
        }

        // ── ヒーロー(Shake ⇄ Haptics の切替) ──

        protected override void AddLeadingHeroActions(VisualElement actions)
        {
            actions.Add(MakeButton("確認用シーン", null, () => CameraShakePreviewSceneSetup.OpenOrCreate(),
                "ライト・カメラ・床を備えた揺れ・振動の確認用シーンを開く(無ければ生成)"));
            actions.Add(MakeButton("Shake Editor へ", null, () => ShakeEditor.SwitchFrom(this),
                "カメラの揺れを編集する Shake Editor に切り替える(いま開いている Haptics はこのウィンドウに残ります)"));
        }

        protected override void AfterHero(VisualElement page)
        {
            _padHelp = new HelpBox("ゲームパッドが接続されていません(接続なし)。波形の確認だけ行えます。接続すると「▶」で実際に振動を確認できます。", HelpBoxMessageType.Info);
            _padHelp.AddToClassList("ux-scenehelp");
            _padHelp.Show(false);
            page.Add(_padHelp);
        }

        // ── ライフサイクル・再生(実 HapticsManager) ──

        protected override void OnWindowEnable()
        {
            _driver = new EditorHapticsPreviewDriver();
            _handle = Handle<HapticMarker>.Invalid;
        }

        protected override void OnWindowDisable()
        {
            _driver?.Dispose();
            _driver = null;
            _handle = Handle<HapticMarker>.Invalid;
        }

        protected override void OnBuildBodyStart()
        {
            _padHelp = null;
            _status = null;
        }

        protected override void OnTargetChanged() => _handle = Handle<HapticMarker>.Invalid;

        protected override void OnBodyBuilt(VisualElement page) => RefreshStatus();

        protected override void OnTick() => RefreshStatus();

        protected override bool SupportsPlayback => true;
        protected override bool SupportsSpeed => false;

        protected override bool IsPlaying => _driver != null && _driver.Manager.IsPlaying(_handle);

        protected override void StartPlayback(bool focus)
        {
            if (Target != null && _driver != null)
            {
                _handle = _driver.Play(Target);
            }
        }

        protected override void StopPlayback()
        {
            _driver?.ResetAndStop();
            _handle = Handle<HapticMarker>.Invalid;
        }

        protected override void OnPlaybackReset() => _handle = Handle<HapticMarker>.Invalid;

        private void RefreshStatus()
        {
            var pad = Gamepad.current;
            _padHelp?.Show(pad == null && Target != null);
            var text = pad != null ? $"パッド: 接続中({pad.displayName})" : "パッド: 接続なし";
            if (_status != null && _status.text != text)
            {
                _status.text = text;
            }
        }

        // ── 欄 ──

        protected override UxRow CreateRow(FieldGuideEntry e, UxCard card, bool isAdmin)
        {
            switch (e.Field)
            {
                case HapticsFieldGuide.RowPresets:
                {
                    var presets = new CameraFxPresetsRow(false, kind =>
                    {
                        CameraFxPresets.ApplyHapticsPreset(Target, kind);
                        RefreshAfterExternalWrite();
                        RestartIfPlaying();
                    });
                    return CameraFxUi.AuxRow(e, presets, true);
                }

                case HapticsFieldGuide.RowGraph:
                {
                    var graph = new CameraFxGraphView(new HapticsGraphSource(Target), final => VisualEdited(final, false));
                    return CameraFxUi.AuxRow(e, graph, true);
                }

                case HapticsFieldGuide.RowCheck:
                    return CameraFxUi.AuxRow(e, CreateCheckBox(), false);

                // モーターのカーブ(ValueDef)はこの Data の中心なので、最初から開いておく。
                case nameof(HapticsData.LowFreq):
                case nameof(HapticsData.HighFreq):
                {
                    var prop = So?.FindProperty(e.Field);
                    if (prop != null)
                    {
                        prop.isExpanded = true;
                    }

                    return base.CreateRow(e, card, isAdmin);
                }

                case nameof(HapticsData.Extensions):
                case nameof(HapticsData.Flags):
                case nameof(HapticsData.Events):
                    GuidedField.Collapse(So, e.Field);
                    return base.CreateRow(e, card, isAdmin);

                default:
                    return base.CreateRow(e, card, isAdmin);
            }
        }

        private VisualElement CreateCheckBox()
        {
            var box = new VisualElement();
            var line = new VisualElement();
            line.AddToClassList("ux-toolsline");
            line.Add(MakeButton("▶ パッドで振動(Test on Pad)", null, () => PlayMain(false), "接続中のゲームパッドを直接振動させる(Editor 再生外でも動く)"));
            line.Add(MakeButton("■ 停止", null, () => StopMain(), "振動を止めてモーターを 0 に戻す"));
            box.Add(line);
            _status = new Label();
            _status.AddToClassList("ux-dim");
            _status.AddToClassList("ux-note");
            box.Add(_status);
            return box;
        }
    }
}
