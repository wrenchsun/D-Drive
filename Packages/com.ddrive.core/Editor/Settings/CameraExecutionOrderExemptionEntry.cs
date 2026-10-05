using System;
using UnityEngine;

namespace DDrive.Editor.Settings
{
    // [26_timeline.md] §4.6.5 契約 G-1 / [42_distribution.md] §5.14(2026-10-06、P-15 確認 Q-4) — 実行順の検査
    // (CameraExecutionOrderValidator)から外す型の 1 件分(`DDriveProjectSettings.CameraExecutionOrderExemptions` の要素)。
    // 用途は「カメラを読むだけ」で、実行順 1000 以上でも Cutscene のカメラ適用を邪魔しないスクリプト
    // (外部パッケージのスクリプトは、そのパッケージの Editor 側が `ICameraExecutionOrderExemptionProvider` で宣言するのが本筋。
    // これは手動の逃げ道)。シリアライズ形式は追加のみ(フィールドの削除・型変更は MAJOR、[42] §5.1)。
    [Serializable]
    public sealed class CameraExecutionOrderExemptionEntry
    {
        [Tooltip("除外する型の完全修飾名(例: MyGame.Cameras.CameraReader。名前空間付き。入れ子の型は Outer+Inner)。実在しない型名は無効になり、設定の検査で Warning が出る。")]
        public string TypeName = string.Empty;

        [Tooltip("除外する理由(必須。例: カメラを読むだけで、書き込みはしない)。空欄の要素は無効になり、設定の検査で Warning が出る。")]
        public string Reason = string.Empty;
    }
}
