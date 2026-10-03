namespace DDrive.Runtime.Viewing
{
    // [51_tdrive_integration.md] §4.4(FC-3) — ViewPose がどこから来たか。値は末尾追加のみ(互換面。docs/42 §5)。
    public enum ViewSource
    {
        None = 0,
        MainCamera = 1,
        Cutscene = 2,
        // IViewProvider が返す視点の出どころ(分割画面・独自カメラ制御など。D-Drive 自身は使わない)。
        // プロバイダは ViewPose を作るときに Override を入れる(FC-R-16)。
        Override = 3,
    }
}
