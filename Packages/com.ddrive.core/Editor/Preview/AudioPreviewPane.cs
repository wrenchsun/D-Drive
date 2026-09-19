using DDrive.Editor.Common;
using DDrive.Foundation.Data;
using DDrive.Runtime.Audio;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Preview
{
    // [09_editor_tools.md] §2 の共通プレビュー UI(再生/停止/ループ/速度)。
    // AssetBrowser(1-5)と AudioEditor(1-7)で共用する。PreviewService の所有は呼び出し側。
    public sealed class AudioPreviewPane : VisualElement
    {
        private readonly PreviewService _service;
        private AssetDataBase _bound;

        private readonly Button _playButton;
        private readonly Button _stopButton;
        private readonly Toggle _loopToggle;
        private readonly Slider _speedSlider;
        private readonly Label _titleLabel;

        public AudioPreviewPane(PreviewService service)
        {
            _service = service;

            // U-12(2026-09-17): 「下部のサウンドのプレビューバーの UI が崩れている」の修正。
            // 原因は 2 つ:
            //   1. Toggle / Slider は BaseField で、ラベル部に USS 既定の min-width 120px が付く。
            //      「ループ」「速度」のような 2〜3 文字のラベルでも 120px を占め、チェックボックスと
            //      つまみがバーの右端へ押し出されていた
            //   2. 行が flex-wrap: nowrap のうえ速度スライダーが固定幅 180px だったため、バーが狭いと
            //      折り返さずに右側が見切れていた([09] §7.1 の横幅 500px 下限を満たしていなかった)
            // → ラベル幅を内容なりにし、行を折り返し可能にし、固定幅をやめる。
            style.flexDirection = FlexDirection.Row;
            style.flexWrap = Wrap.Wrap;
            style.alignItems = Align.Center;
            style.paddingLeft = 6;
            style.paddingRight = 6;
            style.paddingTop = 3;
            style.paddingBottom = 3;
            style.borderTopWidth = 1;
            style.borderTopColor = new Color(0f, 0f, 0f, 0.3f);

            _titleLabel = new Label("(プレビュー対象なし)");
            _titleLabel.style.flexGrow = 1f;
            _titleLabel.style.flexShrink = 1f;
            _titleLabel.style.minWidth = 0; // 既定だと縮まず、右側のコントロールを押し出す
            _titleLabel.style.overflow = Overflow.Hidden;
            _titleLabel.style.textOverflow = TextOverflow.Ellipsis;
            _titleLabel.style.whiteSpace = WhiteSpace.NoWrap;
            _titleLabel.style.marginRight = 6;
            _titleLabel.style.opacity = 0.7f;
            Add(_titleLabel);

            _playButton = new Button(Play) { text = "▶ 再生", tooltip = "実 AudioManager 経由で試聴(実行時と同じコードパス)" };
            _playButton.style.flexShrink = 0f;
            Add(_playButton);

            _stopButton = new Button(Stop) { text = "■ 停止" };
            _stopButton.style.flexShrink = 0f;
            Add(_stopButton);

            _loopToggle = new Toggle("ループ") { tooltip = "SE をループで試聴(アセット本体は変更しない)" };
            _loopToggle.style.marginLeft = 8;
            _loopToggle.style.flexShrink = 0f;
            CompactFieldLayout.ShrinkLabel(_loopToggle.labelElement);
            Add(_loopToggle);

            _speedSlider = new Slider("速度", 0.1f, 2f) { value = 1f, tooltip = "0.1x–2x。Audio ではピッチとして適用" };
            _speedSlider.style.marginLeft = 8;
            _speedSlider.style.minWidth = 120;
            _speedSlider.style.flexGrow = 1f;
            _speedSlider.style.flexShrink = 1f;
            CompactFieldLayout.ShrinkLabel(_speedSlider.labelElement);
            _speedSlider.RegisterValueChangedCallback(evt => _service.Speed = evt.newValue);
            Add(_speedSlider);

            SetEnabledState(false);
        }

        public void Bind(AssetDataBase asset)
        {
            _bound = asset is SeData or BgmData ? asset : null;
            _titleLabel.text = _bound != null
                ? $"プレビュー: {(string.IsNullOrEmpty(_bound.DisplayName) ? _bound.name : _bound.DisplayName)}"
                : "(プレビュー対象なし)";
            SetEnabledState(_bound != null);
        }

        private void SetEnabledState(bool enabled)
        {
            _playButton.SetEnabled(enabled);
            _stopButton.SetEnabled(enabled);
            _loopToggle.SetEnabled(enabled);
            _speedSlider.SetEnabled(enabled);
        }

        private void Play()
        {
            if (_bound == null)
            {
                return;
            }

            _service.Initialize();
            _service.Speed = _speedSlider.value;

            switch (_bound)
            {
                case SeData se:
                    _service.PlaySe(se, forceLoop: _loopToggle.value);
                    break;

                case BgmData bgm:
                    _service.PlayBgm(bgm);
                    break;
            }
        }

        private void Stop() => _service.StopAll();
    }
}
