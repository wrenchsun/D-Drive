using System;

namespace DDrive.Editor.Tuning
{
    // M-2a(2026-09-27) — Enum 行の Popup(EditorGUILayout.Popup)⇔ TuningEntry.ValueString の対応を
    // UnityEditor API に依存しない純関数として切り出したもの(EditMode テストで検証するため)。
    public static class TuningEnumFieldLogic
    {
        // 現在値(ValueString)に一致する選択肢の添字を返す。一致しない/未設定なら 0(先頭)。
        public static int ResolveSelectedIndex(string[] options, string currentValue)
        {
            if (options == null)
            {
                return 0;
            }

            for (var i = 0; i < options.Length; i++)
            {
                if (string.Equals(options[i], currentValue, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return 0;
        }

        // Popup が返した添字を ValueString に書き込む値へ変換する。範囲外は空文字。
        public static string ResolveValueAt(string[] options, int index)
        {
            if (options == null || index < 0 || index >= options.Length)
            {
                return string.Empty;
            }

            return options[index];
        }
    }
}
