using System.Collections.Generic;
using DDrive.Editor.Inspector;
using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Editor.Ux;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Manager;
using UnityEditor;
using UnityEngine;

namespace SampleExtension.Editor
{
    // 種別 "Sample" の専用エディター(docs/1014 §2.3)。D-Drive の専用エディターと同じ土台(UxEditorWindow<TData>)から作るので、
    // ヒーロー・目的別カード・検索・モード・固定バー(▶ ■ 速度 保存)・空状態・Light / Dark・Undo・自動保存は土台が持つ。
    // ここに書くのは「欄の表(FieldGuide)」「カードの説明」「再生のしかた」だけ。C# にインラインスタイルを書かない(見た目は USS)。
    [DataEditor(typeof(SampleData), "Sample Editor で開く")]
    public sealed class SampleEditor : UxEditorWindow<SampleData>
    {
        private SamplePreviewDriver _driver;

        [MenuItem(DDriveMenu.Editors + "Sample Editor")]
        public static void OpenFromMenu() => Open(Selection.activeObject as SampleData);

        // [DataEditor] が呼ぶ入口(public static・引数 1 つの Open)。
        public static void Open(SampleData target)
        {
            var window = OpenWindow<SampleEditor>("Sample Editor");
            if (target != null)
            {
                window.SetTarget(target);
            }
        }

        // ── 土台への指定 ──

        protected override string PrefsKey => "Sample";
        protected override FieldGuide<SampleData> Guide => SampleFieldGuide.Instance;

        // 「？」が開くマニュアルのページ名(SampleManual が宣言した key)。
        protected override string ManualPage => SampleManual.PageKey;

        protected override string CardDescription(string purpose)
        {
            switch (purpose)
            {
                case SampleFieldGuide.PurPlay: return "どう再生するか";
                case SampleFieldGuide.PurShape: return "強さの時間変化";
                default: return "一覧に出る名前・分類・メモ";
            }
        }

        // 空状態のサンプルは ISampleProvider(SampleSampleProvider)が担当する。確認用シーンも IPreviewSceneProvider(SamplePreviewScene)。

        // ── 再生(自分の Manager を実物で動かす) ──

        protected override bool SupportsPlayback => true;

        protected override bool IsPlaying => _driver != null && _driver.IsPlaying;

        protected override void OnWindowEnable() => _driver = new SamplePreviewDriver();

        protected override void OnWindowDisable()
        {
            _driver?.Dispose();
            _driver = null;
        }

        protected override void StartPlayback(bool focus)
        {
            if (Target != null)
            {
                _driver?.Play(Target);
            }
        }

        protected override void StopPlayback() => _driver?.Stop();
    }

    // 欄の表。AssetDataBase の共通欄(管理情報カード)は基底が足す。種別の欄を 1 つ残らず書く(書き漏れは検査で見つかる)。
    public sealed class SampleFieldGuide : FieldGuide<SampleData>
    {
        public const string PurPlay = "再生する";
        public const string PurShape = "形を変える";

        public static readonly SampleFieldGuide Instance = new SampleFieldGuide();

        private static readonly FieldGuideEntry[] Table =
        {
            E(nameof(SampleData.Intensity), "強さ", "効果の強さ。", FieldTier.Required, PurPlay, "強度", "パワー"),
            E(nameof(SampleData.Duration), "続く秒数", "Loop が OFF のとき、この秒数で終わる。", FieldTier.Common, PurPlay, "長さ", "時間"),
            E(nameof(SampleData.Loop), "ループ", "止めるまで続ける。", FieldTier.Common, PurPlay, "繰り返し"),
            E(nameof(SampleData.Fade), "強さの変化", "強さが時間でどう変わるか(形 + 尺)。", FieldTier.Advanced, PurShape, "カーブ", "イージング", "減衰"),
            FlagsEntry(PurPlay),
            // 「きっかけで鳴らす・出す(Events)」の欄は付けない: 外部 module の Manager は AssetEvent を発火しない(ctx.Events は組み込みの
            // AssetEventDispatcher に繋がっていない)ので、設定しても何も鳴らない。必要なら自分の Manager で AssetEventDispatcher を作って購読する(docs/1014 §2.2)。
        };

        protected override IReadOnlyList<FieldGuideEntry> SpecificEntries => Table;
        protected override IReadOnlyList<string> SpecificPurposes { get; } = new[] { PurPlay, PurShape };
    }

    // 確認用の再生: module が作る Manager(SampleModule)を EditorModuleHost で実物のまま Edit Mode で動かす(docs/1014 §2.3)。
    // UxPreviewDriverBase が、シーン切替・Play Mode 突入・再コンパイル・シーン保存の直前に自動で止めて片付ける。
    // Host は ▶ を初めて押したときに作る(窓を開いただけでは作らない)。Host は module の Bind / Unbind を呼ばないので、
    // Play 中のゲーム側の Samples ファサードには触れない(窓を開いたまま Play に入っても、Play 中に窓を閉じても安全)。
    public sealed class SamplePreviewDriver : UxPreviewDriverBase
    {
        private EditorModuleHost _host;
        private SampleManager _manager;
        private Handle<SampleMarker> _handle = Handle<SampleMarker>.Invalid;

        public SamplePreviewDriver() : base("Sample Preview")
        {
        }

        public bool IsPlaying => _manager != null && _manager.IsPlaying(_handle);

        public void Play(SampleData data)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return; // Play 中はゲーム側の Manager が動く。Edit 用の Host は作らない
            }

            EnsureHost();
            if (_manager != null)
            {
                _handle = _manager.PlayData(data);
            }
        }

        public void Stop()
        {
            _manager?.Stop(_handle);
            _handle = Handle<SampleMarker>.Invalid;
        }

        // EditorModuleHost が自分で EditorApplication.update から Tick するので、ここでは何もしない。
        protected override void OnTick(float scaledDt)
        {
        }

        // 止まるたびに Host ごと片付ける(次の ▶ で作り直す)。
        protected override void OnReset(PreviewResetReason reason)
        {
            _handle = Handle<SampleMarker>.Invalid;
            _manager = null;
            _host?.Dispose();
            _host = null;
        }

        private void EnsureHost()
        {
            if (_host != null)
            {
                return;
            }

            // Registry / Pool は省略(空の Registry・新規の Pool)。Data を直接再生するだけなら要らない。module は ModuleRegistry から実物が作られる。
            _host = new EditorModuleHost();
            foreach (var m in _host.Managers)
            {
                if (m is SampleManager sample)
                {
                    _manager = sample;
                }
            }
        }
    }
}
