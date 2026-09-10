using System;
using UnityEngine;

namespace DDrive.Runtime.Ui
{
    // [15_ui_interaction.md] B-3.5 — プロジェクト独自プリセットの登録(チケット 4-11 残り)。
    // これは AssetDataBase ではない単なる ScriptableObject(AssetId を持たない)。
    // ランタイムは常に UiTweenId(UiTweenData.Id)で Tween を再生するため、カタログは
    // 「エディタ上でデザイナーが選ぶための選択肢リスト」以上の意味を持たない
    // (実行時にこのアセット自体を参照することはない)。
    [CreateAssetMenu(menuName = "D-Drive/Ui/Ui Preset Catalog", fileName = "UIPRESETCATALOG_New")]
    public class UiPresetCatalog : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            [Tooltip("デザイナーの選択肢に出る表示名")]
            public string Name;

            [Tooltip("実体の UiTweenData(再生は常に UiTweenId 経由。このカタログはこの Tween を選ぶための一覧に過ぎない)")]
            public UiTweenData Tween;

            [Tooltip("エディタ側のグルーピング用(任意)")]
            public string Category;
        }

        public Entry[] Entries;
    }
}
