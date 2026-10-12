using System.Collections.Generic;
using DDrive.Editor.Inspector;
using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Editor.Ux;
using DDrive.Runtime.CameraShake;
using DDrive.Runtime.Haptics;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.CameraFx
{
    // Shake(カメラシェイク)のエディター(docs/1010 UX-10。旧 CameraFxEditorWindow は Archive メニューへ退避)。
    // 共通基盤(UxEditorWindow<CameraShakeData> / FieldGuide / UxRow / UxCard)で組み、ここに書くのは Shake 固有の部分だけ:
    // 欄の表(ShakeFieldGuide)・雛形 10 種・波形グラフ(CameraFxGraphView)・実カメラの再生(SceneCameraShakePreviewDriver)。
    // 波形そのものの編集は ValueDefDrawer の PropertyField(Frequency / Envelope)とグラフのドラッグ(長さ)。
    // 再生は実 CameraFxManager を Editor から駆動する(ADR-4)。ウィンドウ自身は SceneView / Game ビューに何も描かない。
    // Haptics は別ウィンドウ(HapticsEditor)。ヒーローのボタンで行き来する。
    [DataEditor(typeof(CameraShakeData), "Shake Editor で開く")]
    internal sealed class ShakeEditor : UxEditorWindow<CameraShakeData>
    {
        private const string Page = "camera-haptics";

        private SceneCameraShakePreviewDriver _driver;
        private HelpBox _cameraHelp;
        private Label _status;

        // ── 入口 ──

        // メニューは Shake / Haptics 共通の入口: Haptics が選ばれていれば Haptics Editor、それ以外は Shake Editor を開く。
        [MenuItem(DDriveMenu.Editors + "Shake / Haptics")]
        public static void OpenFromMenu()
        {
            if (Selection.activeObject is HapticsData haptics)
            {
                HapticsEditor.Open(haptics);
                return;
            }

            Open(Selection.activeObject as CameraShakeData);
        }

        public static void Open(CameraShakeData target)
        {
            var w = OpenWindow<ShakeEditor>("Shake Editor");
            if (target != null)
            {
                w.SetTarget(target);
            }
        }

        // Haptics Editor から切り替えてきたとき: 選択中の Shake、なければ最近開いた Shake を開く。ウィンドウは元のウィンドウの隣に出す。
        public static void SwitchFrom(EditorWindow from)
        {
            var w = GetWindow<ShakeEditor>("Shake Editor", true, from.GetType());
            w.minSize = new Vector2(500, 380);
            var target = Selection.activeObject as CameraShakeData;
            if (target == null)
            {
                var recent = DataSwitcher.LoadRecent(typeof(CameraShakeData));
                target = recent.Count > 0 ? recent[0] as CameraShakeData : null;
            }

            if (target != null)
            {
                w.SetTarget(target);
            }
        }

        // ── 土台への指定 ──

        protected override string PrefsKey => "Shake";
        protected override FieldGuide<CameraShakeData> Guide => ShakeFieldGuide.Instance;
        protected override string ManualPage => Page;
        protected override Texture DefaultIcon => EditorGUIUtility.IconContent("Camera Icon").image;
        protected override string SearchHint => "ラベル・説明・検索語に一致する設定だけ出す(例: 減衰 / 揺れ幅 / 連打)";

        protected override string ManualAnchor(string purpose)
        {
            switch (purpose)
            {
                case ShakeFieldGuide.PurStart: return "shake-preset";
                case ShakeFieldGuide.PurGraph: return "shake-graph";
                case ShakeFieldGuide.PurShape: return "shake-shape";
                case ShakeFieldGuide.PurPower: return "shake-power";
                case ShakeFieldGuide.PurStack: return "shake-stack";
                case ShakeFieldGuide.PurCheck: return "shake-check";
                case FieldGuide<CameraShakeData>.AdminPurpose: return "admin";
                default: return null;
            }
        }

        protected override string[] ModeTips => new[]
        {
            "雛形と波形グラフだけ。雛形を選んで ▶ で揺らす",
            "よく使う欄まで。波形の種類・速さ・減衰と長さ・揺れ幅・重なり方",
            "すべての欄(揺れる向きの基準・動作の取り決め・イベント)",
        };

        protected override string CardDescription(string purpose)
        {
            switch (purpose)
            {
                case ShakeFieldGuide.PurStart: return "10 種の雛形から選んで、波形・揺れ幅・減衰を一式入れる";
                case ShakeFieldGuide.PurGraph: return "減衰と位置・回転の揺れを時間軸で見る。右端のドラッグで長さを変える";
                case ShakeFieldGuide.PurShape: return "どんな波形で、どれくらいの速さ・長さで揺れるか";
                case ShakeFieldGuide.PurPower: return "位置と回転の揺れ幅";
                case ShakeFieldGuide.PurStack: return "連打・同時発生のときの合成のしかた";
                case ShakeFieldGuide.PurCheck: return "実際のカメラを揺らして確かめる";
                default: return "一覧に出る名前・分類・メモ";
            }
        }

        // ── ヒーロー(Shake ⇄ Haptics の切替と確認用シーン) ──

        protected override void AddLeadingHeroActions(VisualElement actions)
        {
            actions.Add(MakeButton("確認用シーン", null, () => CameraShakePreviewSceneSetup.OpenOrCreate(),
                "ライト・カメラ・床を備えた揺れ・振動の確認用シーンを開く(無ければ生成)"));
            actions.Add(MakeButton("Haptics Editor へ", null, () => HapticsEditor.SwitchFrom(this),
                "パッドの振動を編集する Haptics Editor に切り替える(いま開いている Shake はこのウィンドウに残ります)"));
        }

        protected override void AfterHero(VisualElement page)
        {
            _cameraHelp = new HelpBox("開いているシーンに Camera.main がありません。「確認用シーン」を開いてカメラを用意してください。", HelpBoxMessageType.Warning);
            _cameraHelp.AddToClassList("ux-scenehelp");
            _cameraHelp.Show(false);
            page.Add(_cameraHelp);
        }

        // ── ライフサイクル・再生(実 CameraFxManager) ──

        protected override void OnWindowEnable() => _driver = new SceneCameraShakePreviewDriver();

        protected override void OnWindowDisable()
        {
            _driver?.Dispose();
            _driver = null;
        }

        protected override void OnBuildBodyStart()
        {
            _cameraHelp = null;
            _status = null;
        }

        protected override void OnBodyBuilt(VisualElement page) => RefreshStatus();

        protected override void OnTick() => RefreshStatus();

        protected override bool SupportsPlayback => true;
        protected override bool SupportsSpeed => false;

        // 実カメラの揺れは Instance の合成が続いている間が「再生中」(連打で重ねると ActiveCount が増える)。
        protected override bool IsPlaying => _driver != null && _driver.ActiveCount > 0;

        protected override void StartPlayback(bool focus)
        {
            if (Target != null)
            {
                _driver?.Play(Target);
            }
        }

        protected override void StopPlayback() => _driver?.StopAll();

        // 連打: いまの揺れを止めずに、もう 1 つ重ねる(MaxStack まで合成される)。
        private void PlayStacked()
        {
            if (Target != null)
            {
                _driver?.Play(Target);
            }
        }

        private void RefreshStatus()
        {
            if (_driver == null)
            {
                return;
            }

            var hasCamera = _driver.HasCamera;
            _cameraHelp?.Show(!hasCamera && Target != null);
            var text = Target == null
                ? string.Empty
                : $"合成中の揺れ: {_driver.ActiveCount}" + (hasCamera ? string.Empty : "(Camera.main が見つかりません)");
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
                case ShakeFieldGuide.RowPresets:
                {
                    var presets = new CameraFxPresetsRow(true, kind =>
                    {
                        CameraFxPresets.ApplyShakePreset(Target, kind);
                        RefreshAfterExternalWrite();
                        RestartIfPlaying();
                    });
                    return CameraFxUi.AuxRow(e, presets, true);
                }

                case ShakeFieldGuide.RowGraph:
                {
                    var graph = new CameraFxGraphView(new ShakeGraphSource(Target), final => VisualEdited(final, false));
                    return CameraFxUi.AuxRow(e, graph, true);
                }

                case ShakeFieldGuide.RowCheck:
                    return CameraFxUi.AuxRow(e, CreateCheckBox(), false);

                // 減衰のカーブ(ValueDef)は波形グラフと対になる欄なので、最初から開いておく(速さは固定値のことが多いので閉じたまま)。
                case nameof(CameraShakeData.Envelope):
                    ExpandValueDef(e.Field);
                    return base.CreateRow(e, card, isAdmin);

                case nameof(CameraShakeData.Frequency):
                    GuidedField.Collapse(So, e.Field);
                    return base.CreateRow(e, card, isAdmin);

                case nameof(CameraShakeData.Flags):
                case nameof(CameraShakeData.Events):
                    GuidedField.Collapse(So, e.Field);
                    return base.CreateRow(e, card, isAdmin);

                default:
                    return base.CreateRow(e, card, isAdmin);
            }
        }

        private void ExpandValueDef(string field)
        {
            var prop = So?.FindProperty(field);
            if (prop != null)
            {
                prop.isExpanded = true;
            }
        }

        private VisualElement CreateCheckBox()
        {
            var box = new VisualElement();
            var line = new VisualElement();
            line.AddToClassList("ux-toolsline");
            line.Add(MakeButton("▶ 重ねて再生(連打可)", null, PlayStacked, "いまの揺れを止めずに、もう 1 つ重ねる(連打すると Trauma 合成で多重発火の挙動を確認できる)"));
            line.Add(MakeButton("■ 全停止(フェード)", null, () => StopMain(), "揺れをすべて 0.1 秒でフェードアウトして止める"));
            line.Add(MakeButton("確認用シーン", null, () => CameraShakePreviewSceneSetup.OpenOrCreate(), "ライト・カメラ・床を備えた確認用シーンを開く(無ければ生成)"));
            box.Add(line);
            _status = new Label();
            _status.AddToClassList("ux-dim");
            _status.AddToClassList("ux-note");
            box.Add(_status);
            return box;
        }
    }
}
