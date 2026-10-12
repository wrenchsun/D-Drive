using System.Collections.Generic;
using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Editor.Ux;
using DDrive.Runtime.Audio;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Audio
{
    // BGM のエディター(docs/1010 UX-3。SeEditor の兄弟。旧 AudioEditorWindow は Archive メニューへ退避)。
    // 共通基盤だけで組み、ここに書くのは BGM 固有の部分だけ: 欄の表(BgmFieldGuide)・ループ範囲の波形・再生(実 BgmManager)。
    [DDrive.Editor.Inspector.DataEditor(typeof(BgmData), "Audio Editor で開く(波形・ループ編集)")]
    internal sealed class BgmEditor : UxEditorWindow<BgmData>
    {
        private const string Page = "audio-editor";

        private PreviewService _preview;

        // ── 入口 ──

        public static void Open(BgmData target)
        {
            var w = OpenWindow<BgmEditor>("Audio Editor");
            if (target != null)
            {
                w.SetTarget(target);
            }
        }

        // SE Editor から切り替えてきたとき: 選択中の BGM、なければ最近開いた BGM を開く。ウィンドウは元のウィンドウの隣に出す。
        public static void SwitchFrom(EditorWindow from)
        {
            var w = GetWindow<BgmEditor>("Audio Editor", true, from.GetType());
            w.minSize = new Vector2(500, 380);
            var target = Selection.activeObject as BgmData;
            if (target == null)
            {
                var recent = DataSwitcher.LoadRecent(typeof(BgmData));
                target = recent.Count > 0 ? recent[0] as BgmData : null;
            }

            if (target != null)
            {
                w.SetTarget(target);
            }
        }

        // ── 土台への指定 ──

        protected override string PrefsKey => "Bgm";
        protected override FieldGuide<BgmData> Guide => BgmFieldGuide.Instance;
        protected override string ManualPage => Page;

        protected override string ManualAnchor(string purpose)
        {
            switch (purpose)
            {
                case BgmFieldGuide.PurPlay: return "bgm-play";
                case BgmFieldGuide.PurLoop: return "bgm-loop";
                case BgmFieldGuide.PurHear: return "bgm-hear";
                case BgmFieldGuide.PurBeat: return "bgm-beat";
                case BgmFieldGuide.PurLink: return "link";
                case FieldGuide<BgmData>.AdminPurpose: return "admin";
                default: return null;
            }
        }

        protected override bool SupportsPlayback => true;
        protected override bool SupportsSpeed => false; // BGM は曲そのものを流すので、速度(ピッチ)は変えない
        protected override Texture DefaultIcon => EditorGUIUtility.IconContent("AudioClip Icon").image;
        protected override string SearchHint => "ラベル・説明・検索語に一致する設定だけ出す(例: ループ / フェード / ミキサー)";

        protected override string[] ModeTips => new[]
        {
            "必須の欄だけ。曲を入れて ▶ で聞くところまで",
            "よく使う欄まで。ループ範囲・フェード・ミキサー",
            "すべての欄",
        };

        protected override string CardDescription(string purpose)
        {
            switch (purpose)
            {
                case BgmFieldGuide.PurPlay: return "どの曲を、どんな大きさで流すか";
                case BgmFieldGuide.PurLoop: return "繰り返す範囲と、切り替わるときのフェード";
                case BgmFieldGuide.PurHear: return "出力先のミキサー";
                case BgmFieldGuide.PurBeat: return "拍に合わせた演出のためのテンポ";
                case BgmFieldGuide.PurLink: return "節目で別の音や演出を一緒に鳴らす";
                default: return "一覧に出る名前・分類・メモ";
            }
        }

        // ── ヒーロー(SE ⇄ BGM の切替) ──

        protected override void AddLeadingHeroActions(VisualElement actions) =>
            actions.Add(MakeButton("SE Editor へ", null, () => SeEditor.SwitchFrom(this), "SE を編集する Audio Editor に切り替える(いま開いている BGM はこのウィンドウに残ります)"));

        // ── ライフサイクル・再生(実 Manager) ──

        protected override void OnWindowEnable() => _preview = new PreviewService();

        protected override void OnWindowDisable()
        {
            _preview?.Dispose();
            _preview = null;
        }

        protected override bool IsPlaying => _preview != null && _preview.IsInitialized && _preview.BgmManager.IsPlaying;

        protected override void StartPlayback(bool focus)
        {
            if (Target == null || _preview == null)
            {
                return;
            }

            _preview.Initialize();
            _preview.PlayBgm(Target);
        }

        protected override void StopPlayback() => _preview?.StopAll();

        protected override void RestartPlayback()
        {
            StopPlayback();
            StartPlayback(false);
        }

        // 再生中に Clip やループ範囲を直したら、鳴らし直す(ループ範囲は再生の開始時に読まれる)。
        protected override void OnPropertyEdited(string path)
        {
            if (path == "LoopBody" || path == "Intro" || path.StartsWith("LoopStartSec") || path.StartsWith("LoopEndSec") || path == "Mixer")
            {
                RestartIfPlaying();
            }
        }

        // ── 欄 ──

        protected override UxRow CreateRow(FieldGuideEntry e, UxCard card, bool isAdmin)
        {
            var data = Target;
            switch (e.Field)
            {
                case nameof(BgmData.LoopEndSec):
                    return null; // ループの範囲の複合欄に含める

                case nameof(BgmData.LoopStartSec):
                {
                    var input = new BgmLoopInput(data, final =>
                    {
                        VisualEdited(final, false);
                        if (final)
                        {
                            RestartIfPlaying();
                        }
                    });
                    var entries = new[] { e, Guide.Find(nameof(BgmData.LoopEndSec)) };
                    return new UxVisualRow(So, DefaultSo, entries, "ループの範囲", "波形の端をドラッグして、繰り返す範囲を決めます。終了が 0 なら曲の最後まで。", FieldTier.Common, input, RowChanged) { NoFold = true };
                }

                default:
                    return base.CreateRow(e, card, isAdmin);
            }
        }
    }
}
