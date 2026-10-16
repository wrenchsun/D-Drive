using System.Collections.Generic;
using DDrive.Editor.Inspector;
using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Editor.Ux;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Pool;
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
            EventsEntry(PurShape),
        };

        protected override IReadOnlyList<FieldGuideEntry> SpecificEntries => Table;
        protected override IReadOnlyList<string> SpecificPurposes { get; } = new[] { PurPlay, PurShape };
    }

    // 確認用の再生: module が作る Manager(SampleModule)を EditorModuleHost で実物のまま Edit Mode で動かす(docs/1014 §2.3)。
    // UxPreviewDriverBase が、シーン切替・Play Mode 突入・再コンパイル・シーン保存の直前に自動で止めて片付ける。
    public sealed class SamplePreviewDriver : UxPreviewDriverBase
    {
        private readonly EditorModuleHost _host;
        private readonly SampleManager _manager;
        private Handle<SampleMarker> _handle = Handle<SampleMarker>.Invalid;

        public SamplePreviewDriver() : base("Sample Preview")
        {
            // Registry / Pool は自分のものを渡す(Data を直接再生するだけなら空でよい)。module は ModuleRegistry から実物が作られる。
            _host = new EditorModuleHost(EditorAnchorRegistry.Build(), new PoolService());
            foreach (var m in _host.Managers)
            {
                if (m is SampleManager sample)
                {
                    _manager = sample;
                }
            }
        }

        public bool IsPlaying => _manager != null && _manager.IsPlaying(_handle);

        public void Play(SampleData data)
        {
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

        protected override void OnReset(PreviewResetReason reason)
        {
            _host.Loop.StopAll(StopReason.Manual);
            _handle = Handle<SampleMarker>.Invalid;
            if (reason == PreviewResetReason.Dispose)
            {
                _host.Dispose();
            }
        }
    }
}
