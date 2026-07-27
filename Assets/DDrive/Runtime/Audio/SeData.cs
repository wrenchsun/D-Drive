using System;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using UnityEngine;
using UnityEngine.Audio;

namespace DDrive.Runtime.Audio
{
    public readonly struct SeMarker
    {
    }

    public enum ClipSelectMode
    {
        Random,
        RoundRobin,
        First,
    }

    public enum SpatialMode
    {
        None,
        Anchor,
        AtPosition,
    }

    // 非破壊トリミング用の元データ。Source は常にインポートしたままの状態を保持し、
    // 上書きしない。実際に再生される SeData.Clips[i] は Source を TrimStart/TrimEnd で
    // 切り出して都度生成したコピー(Editor の「トリミングを適用」操作でのみ再生成される)。
    [Serializable]
    public struct SeClipSource
    {
        [Tooltip("元の(未加工の)AudioClip。トリミングを何度適用してもここは書き換わらない。")]
        public AudioClip Source;

        [Tooltip("トリミング開始位置(秒)。0 ならクリップの先頭から。")]
        [Min(0f)] public float TrimStartSec;

        [Tooltip("トリミング終了位置(秒)。TrimStartSec 以下の値は「クリップの終端まで」を意味する。")]
        public float TrimEndSec;
    }

    [CreateAssetMenu(menuName = "D-Drive/Audio/SE Data", fileName = "SE_NewSound")]
    [AssetIdDefinition(AssetType.Se, typeof(SeMarker), "SEID")]
    public class SeData : AssetDataBase
    {
        [Header("Clips")]
        [Tooltip("実際に再生される AudioClip。複数=SelectMode に応じてランダム/ラウンドロビン選択。")]
        public AudioClip[] Clips;

        [Tooltip("非破壊トリミング編集用の元データ+トリム範囲(Editor専用。要素数は Clips と揃える)。" +
                 "ここを編集しても Clips は自動更新されない。Inspector の「トリミングを適用」ボタンで反映する。")]
        public SeClipSource[] Sources;

        [Tooltip("再生開始位置(秒)。トリミングとは別に、再生の都度この位置から鳴らす(元データは変更しない)。")]
        [Min(0f)] public float StartOffsetSec;

        [Tooltip("Clips が複数ある場合の選択方法。Random=毎回ランダム / RoundRobin=順番に一巡 / First=常に先頭。")]
        public ClipSelectMode SelectMode;

        [Tooltip("出力先の AudioMixerGroup。未設定だと Validation で警告になる。")]
        public AudioMixerGroup Mixer;

        [Tooltip("基準音量(0〜1)。")]
        [Range(0f, 1f)] public float Volume = 1f;

        [Tooltip("再生ごとのランダムピッチ範囲。x=最小, y=最大(両方 1 で無効)。")]
        public Vector2 PitchRange = new(1f, 1f);

        [Tooltip("ループ再生するか。false の場合は再生完了で自動的にプールへ返却される。")]
        public bool Loop;

        [Header("3D")]
        [Tooltip("None=2D再生 / Anchor=Anchor定義に従って追従 / AtPosition=呼び出し側の座標指定が必須。")]
        public SpatialMode Spatial;

        [Tooltip("Spatial=Anchor 時のアタッチ位置定義。VFX と共通の AnchorDef。")]
        public AnchorDef Anchor;

        [Tooltip("この距離までは減衰なし(フル音量)。")]
        public float MinDistance = 1f;

        [Tooltip("この距離を超えると聞こえなくなる。")]
        public float MaxDistance = 30f;

        [Tooltip("距離→減衰カーブ(任意)。横軸が時間ではないため ValueDef の対象外。")]
        public AnimationCurve Rolloff;

        [Tooltip("音源の指向性。0=点音源(近くで定位がはっきり) / 180=無指向。")]
        [Range(0f, 180f)] public float Spread;

        [Tooltip("ドップラー効果を有効にするか。既定 false(演出音での違和感防止)。")]
        public bool DopplerEnabled;

        [Header("制御")]
        [Tooltip("同一SEの同時再生数上限。超過時は最も古い再生を停止する。")]
        public int MaxConcurrent = 8;

        [Tooltip("連打防止のクールダウン秒数。この時間内の再生要求は無視される。")]
        public float CooldownSec = 0.03f;
    }
}
