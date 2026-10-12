using UnityEngine;
using UnityEngine.UI;

namespace DDrive.Editor.Ui
{
    // 実要素を動かして確かめる前の状態(UiTweenManager.ApplyTrack が書き込み得る値: 位置・サイズ・スケール・回転・CanvasGroup.alpha・Graphic.color・Image.fillAmount)を
    // 保存して、止める・終わる・対象を替える・ウィンドウを閉じるときに必ず戻す(旧 UiTweenEditorWindow のレビュー対応 2026-09-14 と同じ規則)。
    // 元々無かった CanvasGroup は取り除く(差し引きでシーンに変更が残らないので Undo には積まない)。1 回戻したら保存は捨てる(二重に戻さない)。
    internal sealed class UiTweenSnapshot
    {
        private RectTransform _target;
        private Vector2 _anchoredPosition;
        private Vector2 _sizeDelta;
        private Vector3 _localScale;
        private Vector3 _localEuler;
        private bool _hadCanvasGroup;
        private float _alpha;
        private Graphic _graphic;
        private Color _color;
        private Image _image;
        private float _fillAmount;

        public bool HasSnapshot => _target != null;

        public RectTransform Target => _target;

        public void Take(RectTransform target)
        {
            _target = target;
            _anchoredPosition = target.anchoredPosition;
            _sizeDelta = target.sizeDelta;
            _localScale = target.localScale;
            _localEuler = target.localEulerAngles;

            var group = target.GetComponent<CanvasGroup>();
            _hadCanvasGroup = group != null;
            _alpha = group != null ? group.alpha : 1f;

            _graphic = target.GetComponent<Graphic>();
            _color = _graphic != null ? _graphic.color : Color.white;
            _image = target.GetComponent<Image>();
            _fillAmount = _image != null ? _image.fillAmount : 1f;
        }

        public void Restore()
        {
            var target = _target;
            _target = null;
            if (target == null)
            {
                _graphic = null;
                _image = null;
                return;
            }

            target.anchoredPosition = _anchoredPosition;
            target.sizeDelta = _sizeDelta;
            target.localScale = _localScale;
            target.localEulerAngles = _localEuler;

            var group = target.GetComponent<CanvasGroup>();
            if (group != null)
            {
                if (_hadCanvasGroup)
                {
                    group.alpha = _alpha;
                }
                else
                {
                    Object.DestroyImmediate(group); // UiTweenManager が Alpha Track のために追加したもの
                }
            }

            if (_graphic != null)
            {
                _graphic.color = _color;
            }

            if (_image != null)
            {
                _image.fillAmount = _fillAmount;
            }

            _graphic = null;
            _image = null;
        }
    }
}
