using System;
using DDrive.Foundation.Data;
using UnityEditor;

namespace DDrive.Editor.Versioning
{
    // [11_tasks.md] 6-3 / [09_editor_tools.md] §4 — 保存時に AssetDataBase.Version/Author/UpdatedAt を自動更新する。
    // ユーザー決定(2026-09-15): 保存時に自動更新・今の値だけ表示(過去履歴は git に任せる)。
    // データ形式は変えない(既存フィールドを書き換えるだけ。フィールド追加・型変更はしない)。
    //
    // - 新規作成: Version の初期値(int 既定 0)のまま初回保存されると、下記フックが 0→1 にする
    //   (AssetCreationService.Create は特別扱いしない。「新規作成時は Version=1」を自然に満たす)。
    // - 既存アセットの designer 編集: 保存ごとに Version+1・Author=Environment.UserName・UpdatedAt=保存時刻。
    // - 一括処理(インポート検知の自動生成・カタログ/Addressables 同期・仕様書同期の適用・ID 再生成・
    //   関連テスト)は VersionStampSuppression.Scope() で囲むことで、機械的な書き換えを版数に乗せない
    //   (大量アセットの版数が一括で上がってノイズになるのを防ぐ、[11_tasks.md] 6-3 の要求)。
    //   スコープは入れ子安全(参照カウント)。抑止中は OnWillSaveAssets に渡された全パスをまとめて素通しする
    //   ため、同じ SaveAssets() 呼び出しに乗った(スコープ外の)他の dirty アセットも一時的に対象外になる —
    //   これは Unity の AssetDatabase.SaveAssets() がパス単位の呼び出し元を区別できないことに由来する制約で、
    //   実運用では影響が小さい(該当 API は基本的に単発 or 同種アセットの一括処理でのみ使う)。
    public static class VersionStampSuppression
    {
        private static int _depth;

        public static bool IsActive => _depth > 0;

        public static IDisposable Scope() => new Handle();

        // テスト([SetUp]/[TearDown] で使いたい場合)用に、Scope を作らず直接カウントを操作したいケースは
        // 無い想定(既存の *.Suppress = bool 方式と違い、必ず using で対にする運用にする)。
        private sealed class Handle : IDisposable
        {
            private bool _disposed;

            public Handle() => _depth++;

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _depth--;
            }
        }
    }

    // Unity が名前とシグネチャで拾う AssetModificationProcessor コールバック
    // (UnityEditor.AssetModificationProcessor を継承するだけで、明示的な override は無い)。
    public sealed class VersionStampProcessor : UnityEditor.AssetModificationProcessor
    {
        // 保存直前に呼ばれる。ここでフィールドを書き換えれば、この直後の物理書き込みにそのまま乗る
        // (再度 AssetDatabase.SaveAssets() 等を呼ぶ必要はない = 無限ループ・二重加算にならない)。
        public static string[] OnWillSaveAssets(string[] paths)
        {
            if (paths == null || paths.Length == 0 || VersionStampSuppression.IsActive)
            {
                return paths;
            }

            string nowIso = null;
            string userName = null;

            foreach (var path in paths)
            {
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                var asset = AssetDatabase.LoadMainAssetAtPath(path) as AssetDataBase;
                // 実際に変更された(dirty な)ものだけを対象にする([11_tasks.md] 6-3)。
                if (asset == null || !EditorUtility.IsDirty(asset))
                {
                    continue;
                }

                // Undo で戻せるようにする(このアセットの直前の内容 = 今回の編集そのものを含む状態を記録してから
                // Version/Author/UpdatedAt を書き換える。AssetIconService の自動生成と同じ考え方だが、
                // ここは常に記録する — デザイナー編集の保存に必ず伴う操作であり、自動生成専用の recordUndo=false
                // パターンとは違う)。
                Undo.RecordObject(asset, "Version Stamp");

                nowIso ??= FormatTimestamp(DateTime.Now);
                userName ??= Environment.UserName;

                asset.Version++;
                asset.Author = userName;
                asset.UpdatedAt = nowIso;
                EditorUtility.SetDirty(asset);
            }

            return paths;
        }

        // ISO 8601(秒まで、タイムゾーン無し = ローカル時刻)。VersionStampGui.FormatForDisplay と対応。
        public static string FormatTimestamp(DateTime time) => time.ToString("yyyy-MM-ddTHH:mm:ss");
    }
}
