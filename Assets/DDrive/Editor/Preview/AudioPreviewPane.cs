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

            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.paddingLeft = 6;
            style.paddingRight = 6;
            style.paddingTop = 3;
            style.paddingBottom = 3;
            style.borderTopWidth = 1;
            style.borderTopColor = new Color(0f, 0f, 0f, 0.3f);

            _titleLabel = new Label("(プレビュー対象なし)");
            _titleLabel.style.flexGrow = 1f;
            _titleLabel.style.opacity = 0.7f;
            Add(_titleLabel);

            _playButton = new Button(Play) { text = "▶ 再生", tooltip = "実 AudioManager 経由で試聴(実行時と同じコードパス)" };
            Add(_playButton);

            _stopButton = new Button(Stop) { text = "■ 停止" };
            Add(_stopButton);

            _loopToggle = new Toggle("ループ") { tooltip = "SE をループで試聴(アセット本体は変更しない)" };
            _loopToggle.style.marginLeft = 8;
            Add(_loopToggle);

            _speedSlider = new Slider("速度", 0.1f, 2f) { value = 1f, tooltip = "0.1x–2x。Audio ではピッチとして適用" };
            _speedSlider.style.width = 180;
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
