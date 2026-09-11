using System;
using UnityEngine;

namespace DDrive.Runtime.Ui
{
    // [18_ui_controls.md] B-4 — 標準オプション(音量・アクセシビリティ・UI 速度)の格納先。
    // Canvas 側は Open 時に現在値でスライダーを初期化するだけでよく、「設定画面がスクリプト0行で
    // 完成する」ことを狙う([18] B-4 実装メモ参照)。
    public enum OptionKey
    {
        None,
        MasterVolume,
        BgmVolume,
        SeVolume,
        VoiceVolume,
        ShakeScale,
        HapticScale,
        UiSpeedScale,
    }

    // セーブデータ抽象。OptionStore はこのインタフェースだけに依存する(PlayerPrefs 以外の
    // 永続化先にも差し替え可能にするため)。
    public interface IOptionStorage
    {
        void Write(string key, float v);
        bool TryRead(string key, out float v);
    }

    // 既定の永続化先。キーは "DDrive.Option." + OptionKey 名。
    public sealed class PlayerPrefsOptionStorage : IOptionStorage
    {
        private const string Prefix = "DDrive.Option.";

        public void Write(string key, float v)
        {
            PlayerPrefs.SetFloat(Prefix + key, v);
            PlayerPrefs.Save();
        }

        public bool TryRead(string key, out float v)
        {
            var full = Prefix + key;
            if (PlayerPrefs.HasKey(full))
            {
                v = PlayerPrefs.GetFloat(full);
                return true;
            }

            v = default;
            return false;
        }
    }

    // [18_ui_controls.md] B-4 — 音量・アクセシビリティ・UI 速度の軽量ストア。R3 未導入のため
    // 通知は素の event(Action)。適用先が未実装(Audio バス等、Phase 5 スコープ)の OptionKey は
    // 値を保持するだけに留め、警告を 1 回だけ出す(実装メモ参照)。
    public sealed class OptionStore
    {
        private static readonly int Count = Enum.GetValues(typeof(OptionKey)).Length;
        private readonly float[] _values = new float[Count];
        private bool _busWarned;

        public event Action<OptionKey, float> OnChanged;

        // Bgm/Se/Voice の実バス反映フック(未設定なら値の保持のみ。Audio.SetBusVolume 実装[03]で差し替える想定)。
        public Action<OptionKey, float> ExternalApplier;

        // UiSpeedScale の反映先(未設定なら値の保持のみ)。
        public UiTweenManager UiTweens;

        // Codex レビュー対応(2026-09-11): Set() のたびに毎回 Write すると PlayerPrefs I/O が頻発するため、
        // 変更があったことだけ記録して SaveIfDirty() でまとめて書く(Bootstrap の OnDestroy/OnApplicationQuit から呼ぶ)。
        public IOptionStorage Storage { get; set; }
        private bool _dirty;

        public OptionStore()
        {
            for (var i = 0; i < _values.Length; i++)
            {
                _values[i] = 1f; // 既定値(音量/速度=100%)
            }
        }

        public float Get(OptionKey key) => _values[(int)key];

        public void Set(OptionKey key, float value)
        {
            if (key == OptionKey.None)
            {
                return;
            }

            value = Mathf.Clamp01(value);
            _values[(int)key] = value;
            Apply(key, value);
            _dirty = true;
            OnChanged?.Invoke(key, value);
        }

        // Storage が設定されていて、かつ Set() 以降に変更があるときだけ保存する(Bootstrap の
        // OnDestroy/OnApplicationQuit から呼ぶ想定。未設定/未変更なら no-op)。
        public void SaveIfDirty()
        {
            if (!_dirty || Storage == null)
            {
                return;
            }

            Save(Storage);
            _dirty = false;
        }

        private void Apply(OptionKey key, float value)
        {
            switch (key)
            {
                case OptionKey.MasterVolume:
                    AudioListener.volume = value;
                    break;

                case OptionKey.BgmVolume:
                case OptionKey.SeVolume:
                case OptionKey.VoiceVolume:
                    if (ExternalApplier != null)
                    {
                        ExternalApplier(key, value);
                    }
                    else if (!_busWarned)
                    {
                        _busWarned = true;
                        Debug.LogWarning("[DDrive] OptionStore: Audio バス別音量([03] Audio.SetBusVolume)は Phase 5 で実装予定のため、値は保持のみです");
                    }

                    break;

                case OptionKey.UiSpeedScale:
                    if (UiTweens != null)
                    {
                        UiTweens.GlobalSpeed = Mathf.Max(0.01f, value);
                    }

                    break;

                case OptionKey.ShakeScale:
                case OptionKey.HapticScale:
                    // Phase 6([16] Part B)の消費先が未実装のため値の保持のみ。
                    break;
            }
        }

        public void Save(IOptionStorage storage)
        {
            if (storage == null)
            {
                return;
            }

            var values = (OptionKey[])Enum.GetValues(typeof(OptionKey));
            for (var i = 0; i < values.Length; i++)
            {
                if (values[i] == OptionKey.None)
                {
                    continue;
                }

                storage.Write(values[i].ToString(), Get(values[i]));
            }
        }

        public void Load(IOptionStorage storage)
        {
            if (storage == null)
            {
                return;
            }

            var values = (OptionKey[])Enum.GetValues(typeof(OptionKey));
            for (var i = 0; i < values.Length; i++)
            {
                if (values[i] == OptionKey.None)
                {
                    continue;
                }

                if (storage.TryRead(values[i].ToString(), out var v))
                {
                    Set(values[i], v);
                }
            }

            _dirty = false; // Load 直後は「未変更」扱い(Set() が立てた dirty フラグを打ち消す)
        }
    }

    // デザイナー/プログラマー向けの薄い静的ファサード(Ui.cs / UiSkins.cs と同じ設計)。
    public static class Options
    {
        private static OptionStore _instance;

        public static void Bind(OptionStore instance) => _instance = instance;

        public static bool IsBound => _instance != null;

        public static float Get(OptionKey key) => _instance?.Get(key) ?? 1f;

        public static void Set(OptionKey key, float value) => _instance?.Set(key, value);

        public static event Action<OptionKey, float> OnChanged
        {
            add { if (_instance != null) _instance.OnChanged += value; }
            remove { if (_instance != null) _instance.OnChanged -= value; }
        }
    }
}
