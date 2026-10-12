using System;
using System.Collections.Generic;
using DDrive.Editor.Ux;
using DDrive.Runtime.Presentation;
using UnityEditor;
using UnityEngine.UIElements;

namespace DDrive.Editor.Presentation
{
    // Presentation 固有の行(docs/1010 UX-5)。いずれも UxRow を継承し、カードに普通の欄として並ぶ。

    // Data の欄を持たない補助の行(タイムライン・Signal・確認用のモデル・SceneView・ログ・環境)。検索・表示モード・目的別カードの対象にはなるが、
    // 変更の件数・検証の結びつけには入らない。sync = 外部変更を取り込む処理(無ければ null)。
    internal sealed class PresentationAuxRow : UxRow
    {
        private readonly Action _sync;

        public PresentationAuxRow(FieldGuideEntry entry, string label, string hint, FieldTier tier, VisualElement input, Action sync)
            : base(null, null, new[] { entry }, label, hint, tier, true, input, null)
        {
            _sync = sync;
            NoFold = true;
        }

        public override void Sync() => _sync?.Invoke();
    }

    // データの署名(「変わったか」だけを割り当て無しで見る)が変わったときだけ中身を作り直す入れ物。
    // 署名が同じ間は tick(行の外から変わる状態の取り込み)だけ呼ぶ。毎 150ms の Sync は署名の比較だけ。
    internal sealed class PresentationSyncBox : VisualElement, IUxSyncable
    {
        private readonly Func<int> _signature;
        private readonly Action _rebuild;
        private readonly Action _tick;
        private int _last = int.MinValue;

        public PresentationSyncBox(Func<int> signature, Action rebuild, Action tick)
        {
            _signature = signature;
            _rebuild = rebuild;
            _tick = tick;
            AddToClassList("ux-sec");
        }

        // 一覧を作り直す(署名も取り直す)。
        public void Rebuild()
        {
            _last = _signature();
            _rebuild();
        }

        public void Sync()
        {
            if (_signature() != _last)
            {
                Rebuild();
            }

            _tick?.Invoke();
        }
    }

    // 全体の長さの入力: 数値 + 「トラックの最後に合わせる」+ 尺が 0 のときの注記(OnSignal だけの演出は最初の Tick で即完了する)。
    internal sealed class PresentationDurationInput : VisualElement, IUxSyncable
    {
        private readonly PresentationData _data;
        private readonly Label _note;

        public PresentationDurationInput(SerializedObject so, PresentationData data, Action fit)
        {
            _data = data;
            Add(GuidedField.MakeInput(so, nameof(PresentationData.TotalDuration)));
            var line = new VisualElement();
            line.AddToClassList("ux-toolsline");
            var button = new Button(fit) { text = "トラックの最後に合わせる", tooltip = "TotalDuration を各トラックの終了時刻(分かる場合はアセットの長さを加味、分からなければ最大時刻 + 0.5 秒)の最大値に設定する" };
            button.AddToClassList("ux-btn");
            line.Add(button);
            Add(line);
            _note = new Label("尺が 0 です。OnSignal のみで構成された演出は全体の長さを明示しないと、最初の Tick で即完了します。");
            _note.AddToClassList("ux-dim");
            _note.AddToClassList("ux-note");
            Add(_note);
            Sync();
        }

        public void Sync()
        {
            if (_data == null)
            {
                return;
            }

            _note.Show(PresentationTiming.EffectiveDuration(_data) <= 0f);
        }
    }
}
