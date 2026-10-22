using System.Collections.Generic;
using UnityEngine;

namespace DDrive.Editor.Cutscene
{
    // 1017 TA-3(DD-2 + DD-9) — イベント用ロケーターのアニメ付きユーザープロパティ(ddEvent)のカーブから、
    // 「値が閾値(0.5)以上になったフレーム」を時刻(秒)で列挙する純ロジック(AssetDatabase 非依存)。
    // 読み方: 最初に 1 になったフレーム(先頭キーが既に 1 なら先頭 = DD-9)、その後 0 に戻ってから再び 1 になったフレーム。
    // 整数のステップキーを想定するが、補間されていても閾値で同じ結果になるよう frameRate 刻みで走査する。
    public static class CutsceneEventCurveReader
    {
        public const float Threshold = 0.5f;

        public static List<float> FindRisingEdges(AnimationCurve curve, float frameRate)
        {
            var edges = new List<float>();
            if (curve == null || curve.length == 0)
            {
                return edges;
            }

            var first = curve[0].time;
            var last = curve[curve.length - 1].time;
            var wasHigh = false;

            if (frameRate <= 0f || last <= first)
            {
                // フレーム刻みが分からない / キーが 1 つだけ: キーの時刻だけで判定する。
                for (var i = 0; i < curve.length; i++)
                {
                    var high = curve[i].value >= Threshold;
                    if (high && !wasHigh)
                    {
                        edges.Add(curve[i].time);
                    }

                    wasHigh = high;
                }

                return edges;
            }

            var frameCount = Mathf.RoundToInt((last - first) * frameRate);
            for (var n = 0; n <= frameCount; n++)
            {
                var t = first + n / frameRate;
                var high = curve.Evaluate(t) >= Threshold;
                if (high && !wasHigh)
                {
                    edges.Add(t);
                }

                wasHigh = high;
            }

            return edges;
        }
    }
}
