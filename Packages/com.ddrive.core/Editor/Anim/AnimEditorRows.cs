using System;
using DDrive.Editor.Ux;
using UnityEngine.UIElements;

namespace DDrive.Editor.Anim
{
    // Anim 固有の行(docs/1010 UX-6)。いずれも UxRow を継承し、カードに普通の欄として並ぶ。

    // Data の欄を持たない補助の行(ステート一覧の補助・タイムライン・動かす相手・モデル・ブレンド確認・ログなど)。検索・表示モード・目的別カードの対象にはなるが、
    // 変更の件数・検証の結びつけには入らない。sync = 外部変更を取り込む処理(無ければ null)。
    internal sealed class AnimAuxRow : UxRow
    {
        private readonly Action _sync;

        public AnimAuxRow(FieldGuideEntry entry, string label, string hint, FieldTier tier, VisualElement input, Action sync)
            : base(null, null, new[] { entry }, label, hint, tier, true, input, null)
        {
            _sync = sync;
            NoFold = true;
        }

        public override void Sync() => _sync?.Invoke();
    }

    // データの署名(「変わったか」だけを割り当て無しで見る)が変わったときだけ中身を作り直す入れ物。
    // 署名が同じ間は tick(行の外から変わる状態の取り込み)だけ呼ぶ。毎 150ms の Sync は署名の比較だけ。
    internal sealed class AnimSyncBox : VisualElement, IUxSyncable
    {
        private readonly Func<int> _signature;
        private readonly Action _rebuild;
        private readonly Action _tick;
        private int _last = int.MinValue;

        public AnimSyncBox(Func<int> signature, Action rebuild, Action tick)
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
}
