namespace DDrive.Runtime.Viewing
{
    // [51_tdrive_integration.md] §4.4(FC-3) — ViewPose がどこから来たか。値は末尾追加のみ(互換面。docs/42 §5)。
    public enum ViewSource
    {
        None = 0,
        MainCamera = 1,
        Cutscene = 2,
        Override = 3,
    }
}
