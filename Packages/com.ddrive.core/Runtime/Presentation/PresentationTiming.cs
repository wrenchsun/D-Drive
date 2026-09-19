namespace DDrive.Runtime.Presentation
{
    // PresentationManager と PresentationDataValidator の両方が使う、TotalDuration の自動算出式([08] §2)。
    // 純粋関数(0 alloc)。
    public static class PresentationTiming
    {
        public static float EffectiveDuration(PresentationData data)
        {
            if (data == null)
            {
                return 0f;
            }

            if (data.TotalDuration > 0f)
            {
                return data.TotalDuration;
            }

            var tracks = data.Tracks;
            if (tracks == null)
            {
                return 0f;
            }

            var max = 0f;
            for (var i = 0; i < tracks.Length; i++)
            {
                if (tracks[i].Trigger == TrackTrigger.AtTime && tracks[i].Time > max)
                {
                    max = tracks[i].Time;
                }
            }

            return max;
        }
    }
}
