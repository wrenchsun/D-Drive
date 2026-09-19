using DDrive.Foundation.Identity;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Ui;
using UnityEditor;

namespace DDrive.Editor.Ui
{
    // Editor では Audio が未 Bind のため UiSlider 自身の SE は鳴らない。プレビューのスライダーのイベントから、
    // 実行時と同じ SE を試聴側で鳴らすための対応表(掴む・離す・目盛り・端・拒否)。
    public enum SliderSeEvent
    {
        Grab,
        Release,
        Notch,
        Limit,
        Denied,
    }

    // (レビュー対応 2026-09-14) SliderSkinEditorWindow と SliderEditorWindow がイベント → SE の対応と目盛り SE の
    // 間引きをそれぞれ持っていたため共通化した。SeData の検索は DataIdLookup(Id ごとにキャッシュ)。
    // 間引きの状態を持つので、スライダーを見るウィンドウごとに 1 つ持つ。
    public sealed class SliderSePreview
    {
        // SliderSkinData.NotchSeMinIntervalSec の既定値と同じ(Skin が無いとき用)。
        public const float DefaultNotchSeMinIntervalSec = 0.04f;

        private double _lastNotchSeTime = double.NegativeInfinity;

        public static string PropertyName(SliderSeEvent e)
        {
            switch (e)
            {
                case SliderSeEvent.Grab: return nameof(SliderSkinData.GrabSe);
                case SliderSeEvent.Release: return nameof(SliderSkinData.ReleaseSe);
                case SliderSeEvent.Notch: return nameof(SliderSkinData.NotchSe);
                case SliderSeEvent.Limit: return nameof(SliderSkinData.LimitSe);
                default: return nameof(SliderSkinData.DeniedSe);
            }
        }

        public static AssetId<SeMarker> GetId(SliderSkinData skin, SliderSeEvent e)
        {
            if (skin == null)
            {
                return default;
            }

            switch (e)
            {
                case SliderSeEvent.Grab: return skin.GrabSe;
                case SliderSeEvent.Release: return skin.ReleaseSe;
                case SliderSeEvent.Notch: return skin.NotchSe;
                case SliderSeEvent.Limit: return skin.LimitSe;
                default: return skin.DeniedSe;
            }
        }

        // 目盛り SE は高速ドラッグで詰まらないよう NotchSeMinIntervalSec 未満の連続を鳴らさない(実行時と同じ)。
        // それ以外のイベントは常に true。
        public bool ShouldPlay(SliderSkinData skin, SliderSeEvent e)
        {
            if (e != SliderSeEvent.Notch)
            {
                return true;
            }

            var now = EditorApplication.timeSinceStartup;
            var minInterval = skin != null ? skin.NotchSeMinIntervalSec : DefaultNotchSeMinIntervalSec;
            if (now - _lastNotchSeTime < minInterval)
            {
                return false;
            }

            _lastNotchSeTime = now;
            return true;
        }

        // 鳴らす SeData。未設定・見つからない・間引き中は null。
        public SeData Resolve(SliderSkinData skin, SliderSeEvent e)
        {
            var id = GetId(skin, e);
            if (!id.IsValid || !ShouldPlay(skin, e))
            {
                return null;
            }

            return DataIdLookup.Find<SeData>(id.Value);
        }
    }
}
