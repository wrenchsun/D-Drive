using UnityEngine;

namespace DDrive.Editor.Audio
{
    // 3D サウンド確認パッド(AudioEditor)の座標計算。
    // パッド平面は上から見た XZ 平面(+Y が前方/奥)。音源は常に中央(原点)、リスナーが動く。
    //
    // 実際のオーディオへの適用は「リスナーを動かす」のではなく「リスナーから見た相対位置に
    // 音源を動かす」ことで行う(シーン上のどの AudioListener がアクティブでも正しく鳴らすため)。
    public static class ListenerPadMath
    {
        // listenerPadPos: パッド上のリスナー位置(メートル、音源が原点、+y=前方)。
        // listenerAngleDeg: リスナーの向き(0=前方/上向き、時計回りが正)。
        // 戻り値: リスナーのローカル空間で見た音源のオフセット(x=右, z=前方)。
        public static Vector3 SourceOffsetInListenerLocal(Vector2 listenerPadPos, float listenerAngleDeg)
        {
            var toSource = -listenerPadPos;

            var rad = listenerAngleDeg * Mathf.Deg2Rad;
            var forward = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
            var right = new Vector2(Mathf.Cos(rad), -Mathf.Sin(rad));

            return new Vector3(Vector2.Dot(toSource, right), 0f, Vector2.Dot(toSource, forward));
        }

        public static float DistanceToSource(Vector2 listenerPadPos) => listenerPadPos.magnitude;

        // パッド矩形(px)↔メートルの相互変換。rangeX/rangeY は矩形の半分に対応するメートル数(±range)。
        public static Vector2 PixelToMeters(Vector2 pixel, Rect padRect, float rangeX, float rangeY)
        {
            var nx = (pixel.x - padRect.center.x) / (padRect.width * 0.5f);
            var ny = (padRect.center.y - pixel.y) / (padRect.height * 0.5f); // GUI 座標は下が+のため反転
            return new Vector2(nx * rangeX, ny * rangeY);
        }

        public static Vector2 MetersToPixel(Vector2 meters, Rect padRect, float rangeX, float rangeY)
        {
            var px = padRect.center.x + meters.x / rangeX * (padRect.width * 0.5f);
            var py = padRect.center.y - meters.y / rangeY * (padRect.height * 0.5f);
            return new Vector2(px, py);
        }

        public static Vector2 ClampToRange(Vector2 meters, float rangeX, float rangeY)
            => new(Mathf.Clamp(meters.x, -rangeX, rangeX), Mathf.Clamp(meters.y, -rangeY, rangeY));
    }
}
