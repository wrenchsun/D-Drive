using System;
using UnityEngine;

namespace DDrive.Editor.Settings
{
    // [11_tasks.md] M-4(2026-10-05) — 禁止 API の検査(ForbiddenApiScanner)から外すファイル/フォルダの 1 件分
    // (`DDriveProjectSettings.ForbiddenApiAllowEntries` の要素)。用途は「自分で書き換えられない外部コード・
    // 生成コード」。自分のコードの個別の行は、行単位の許可コメント(行末の許可コメント。書式は docs/42 §5.9)を使う。
    // シリアライズ形式は追加のみ(フィールドの削除・型変更は MAJOR、[42] §5.1)。
    [Serializable]
    public sealed class ForbiddenApiAllowEntry
    {
        [Tooltip("除外するフォルダまたはファイルのパス(前方一致)。プロジェクトルートからの相対パス(例: Assets/Plugins/ThirdParty/)。区切りは / でも \\ でもよい。")]
        public string Path = string.Empty;

        [Tooltip("除外する規則名(Time / Instantiate / ResourcesLoad / AddressablesLoad / AudioSourcePlay。大文字小文字は区別しない)。空欄なら全ての規則を除外する。")]
        public string Rule = string.Empty;

        [Tooltip("除外する理由(必須)。空欄の要素は無効になり、設定の検査で Warning が出る。")]
        public string Reason = string.Empty;
    }
}
