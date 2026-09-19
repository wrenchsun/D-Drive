namespace DDrive.Foundation.Pause
{
    // Presentation からの HitStop(スロー/静止)を担当。Time.timeScale には触れず、
    // GameLoop が Tick(unscaledDeltaTime) してから ScaledDeltaTime を Manager に配るモデル。
    public sealed class TimeService
    {
        public float TimeScale { get; private set; } = 1f;

        private float _hitStopRemaining;

        public void HitStop(float duration, float scale = 0f)
        {
            // duration<=0 を許すと Tick の早期リターン条件と噛み合って TimeScale が永久に戻らなくなる。
            if (duration <= 0f)
            {
                return;
            }

            _hitStopRemaining = duration;
            TimeScale = scale;
        }

        public void Tick(float unscaledDeltaTime)
        {
            if (_hitStopRemaining <= 0f)
            {
                return;
            }

            _hitStopRemaining -= unscaledDeltaTime;
            if (_hitStopRemaining <= 0f)
            {
                _hitStopRemaining = 0f;
                TimeScale = 1f;
            }
        }

        public float ScaledDeltaTime(float unscaledDeltaTime) => unscaledDeltaTime * TimeScale;
    }
}
