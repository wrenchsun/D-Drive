using System;
using DDrive.Editor.Ux;
using UnityEngine.UIElements;

namespace DDrive.Editor.CameraFx
{
    // Shake / Haptics の新エディターが共有する部品(docs/1010 UX-10)。
    internal static class CameraFxUi
    {
        // 雛形 10 種の説明(ボタンのツールチップと、行の下の 1 行に出す)。数値は CameraFxPresets がたたき台として持つ。
        public static string PresetDescription(CameraFxPresetKind kind, bool shake)
        {
            switch (kind)
            {
                case CameraFxPresetKind.Pulse: return shake ? "軽い一撃。短くスッと収まる" : "軽く「トン」と鳴る短い振動";
                case CameraFxPresetKind.Rumble: return shake ? "持続する低い揺れ(ゴゴゴ)" : "持続する低い振動(ゴゴゴ)";
                case CameraFxPresetKind.Heartbeat: return shake ? "心拍のような二拍。繰り返す" : "心拍のような二拍の振動。繰り返す";
                case CameraFxPresetKind.Explosion: return shake ? "爆発。大きく激しく、素早く収まる" : "爆発。強い振動が素早く収まる";
                case CameraFxPresetKind.HitSmall: return shake ? "被弾(小)。短い一撃" : "被弾(小)。短くて軽い";
                case CameraFxPresetKind.HitLarge: return shake ? "被弾(大)。やや長く強い" : "被弾(大)。強くやや長い";
                case CameraFxPresetKind.Landing: return shake ? "着地。縦に一度だけ沈む" : "着地。重く短い";
                case CameraFxPresetKind.Earthquake: return shake ? "地震。止めるまで続く大きな揺れ" : "地震。止めるまで続く重い振動";
                case CameraFxPresetKind.Alarm: return shake ? "警報。一定のリズムで小さく震え続ける" : "警報。止めるまで続く弱い振動";
                case CameraFxPresetKind.Engine: return shake ? "エンジンのアイドリング。細かく小さく続く" : "エンジンのアイドリング。弱く一定に続く";
                default: return string.Empty;
            }
        }

        public static string PresetButtonText(CameraFxPresetKind kind) => CameraFxPresets.Label(kind);

        // 補助の行(Data の欄を持たない)を載せる。必須の段に置く行(雛形・波形)は「かんたん」でも見えるように段を必須にするが、バッジは出さない。
        public static UxRow AuxRow(FieldGuideEntry entry, VisualElement content, bool hideBadge)
        {
            var row = new UxVisualRow(null, null, new[] { entry }, entry.Label, entry.Hint, entry.Tier, content, null) { NoFold = true };
            if (hideBadge)
            {
                row.Q<Label>(className: "ux-badge")?.Show(false);
            }

            return row;
        }
    }

    // 雛形(プリセット)10 種のボタン列。押すと apply が Data を書き換える(CameraFxPresets。Undo は 1 回)。
    // ボタンにマウスを乗せると説明が出る。
    internal sealed class CameraFxPresetsRow : VisualElement
    {
        private readonly Label _note;
        private readonly string _defaultNote;
        private string _lastNote;

        public CameraFxPresetsRow(bool shake, Action<CameraFxPresetKind> apply)
        {
            AddToClassList("ux-fxp");
            var line = new VisualElement();
            line.AddToClassList("ux-fxp__line");
            Add(line);

            _defaultNote = "ボタンにマウスを乗せると、その雛形の説明が出ます";
            _lastNote = _defaultNote;
            _note = new Label(_defaultNote);
            _note.AddToClassList("ux-dim");
            _note.AddToClassList("ux-note");

            foreach (var kind in CameraFxPresets.All)
            {
                var captured = kind;
                var description = CameraFxUi.PresetDescription(kind, shake);
                var b = new Button(() =>
                {
                    apply(captured);
                    _lastNote = $"{CameraFxPresets.Label(captured)} を適用しました: {description}";
                    _note.text = _lastNote;
                })
                {
                    text = CameraFxUi.PresetButtonText(kind),
                    tooltip = description,
                };
                b.AddToClassList("ux-btn");
                b.AddToClassList("ux-fxp__btn");
                b.RegisterCallback<PointerEnterEvent>(e => _note.text = $"{CameraFxPresets.Label(captured)}: {description}");
                b.RegisterCallback<PointerLeaveEvent>(e => _note.text = _lastNote);
                line.Add(b);
            }

            Add(_note);
        }
    }
}
