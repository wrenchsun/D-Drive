using System;
using DDrive.Foundation.Data;
using DDrive.Foundation.Easing;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Values;
using UnityEngine;

namespace DDrive.Runtime.Haptics
{
    public readonly struct HapticMarker
    {
    }

    // 同時再生時の優先度メタデータ。Max 合成(チャンネルごとに一番強い値を採用)自体が「強い方が勝つ」
    // 挙動を既に実現しているため、v1 時点では合成ロジックはこの値を参照しない(将来、同数上限超過時の
    // 淘汰基準などに使う想定の予約フィールド。[16] Part B 実装メモ参照)。
    public enum HapticPriority
    {
        Low,
        Normal,
        High,
    }

    // プラットフォーム別拡張(DualSense アダプティブトリガー等)の型だけを用意する([16] Part B)。
    // 実装は未対応。PlayData が Extensions を検出したら警告 1 回を出す。
    [Serializable]
    public struct HapticExt
    {
        [Tooltip("対象プラットフォーム名(自由入力。例: DualSense, SwitchHD)。実装は未対応。")]
        public string PlatformKey;
    }

    [CreateAssetMenu(menuName = "D-Drive/Haptics/Haptics Data", fileName = "HAPTIC_NewHaptic")]
    [AssetIdDefinition(AssetType.Haptics, typeof(HapticMarker), "HAPTICID")]
    public class HapticsData : AssetDataBase
    {
        private static ValueDef DefaultMotorCurve() => new ValueDef
        {
            Mode = ValueMode.Parametric,
            Parametric = EaseDef.Named(Ease.OutQuad),
            From = 1f,
            To = 0f,
            Time = new TimeDef { Mode = TimeMode.Duration, Value = 0.2f, SpeedScale = 1f },
            Loop = LoopMode.Once,
        };

        [Header("モーター")]
        [Tooltip("低周波モーター(ドスン系)。形+尺は ValueDef。既定 0.2s で減衰。")]
        public ValueDef LowFreq = DefaultMotorCurve();

        [Tooltip("高周波モーター(ビリビリ系)。形+尺は ValueDef。既定 0.2s で減衰。")]
        public ValueDef HighFreq = DefaultMotorCurve();

        [Header("制御")]
        [Tooltip("同時再生時、値が大きい方が優先される目安(Max 合成そのものが実質的な優先度になるため、現状は参照専用)。")]
        public HapticPriority Priority = HapticPriority.Normal;

        [Tooltip("自分に起きた事象のみ振動させる(既定)。false は Cosmetic 扱いで全員に配送する想定([16] Part B)。" +
                 "NGO 統合前の v1 は判定先が無いため常にローカル再生(要判断: [16] 実装メモ参照)。")]
        public bool LocalPlayerOnly = true;

        [Header("拡張")]
        [Tooltip("DualSense アダプティブトリガー等、プラットフォーム別拡張(未実装。設定すると警告が出る)。")]
        public HapticExt[] Extensions;
    }
}
