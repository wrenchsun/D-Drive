using System;
using System.Collections.Generic;
using DDrive.Runtime.Cutscene;
using UnityEngine.Timeline;

namespace ExternalPackage.Fake
{
    // 外部パッケージ(T-Drive 等)を模した、ICutsceneMarker を実装するマーカー(FC-4 / E-20)。
    // 外部アセンブリから見える公開 API だけで書く(D-Drive の internal には触れない)。
    public sealed class ExternalFireMarker : Marker, ICutsceneMarker
    {
        public struct Call
        {
            public double MarkerTime;
            public double Elapsed;
            public bool HasDirector;
            public bool HasHandle;
            public bool IsEditPreview;
        }

        public static readonly List<Call> Calls = new();

        // true のマーカーは Fire で例外を投げる(例外隔離の確認用)。
        public bool Throw;

        // 注意: Reset という名前は Unity のマジックメソッド(ScriptableObject.Reset)と衝突するため ClearCalls。
        public static void ClearCalls() => Calls.Clear();

        public void Fire(in CutsceneMarkerContext context)
        {
            Calls.Add(new Call
            {
                MarkerTime = context.MarkerTime,
                Elapsed = context.Elapsed,
                HasDirector = context.Director != null,
                HasHandle = context.Handle.Index >= 0,
                IsEditPreview = context.IsEditPreview,
            });

            if (Throw)
            {
                throw new InvalidOperationException("ExternalFireMarker: 意図的な例外(テスト)");
            }
        }
    }
}
