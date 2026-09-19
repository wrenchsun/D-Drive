namespace DDrive.Runtime.Material
{
    // MaterialData 本体は Phase 3(3-5)で実装する。ModelData.Slots が「ID 参照(直参照しない)」という
    // 最終設計([05_model_animation.md] A-2)を Phase 2 の時点から満たせるよう、タグ型だけ先出しする
    // (中身が空なので Phase 3 が MaterialData を追加してもここは変更不要)。
    public readonly struct MaterialMarker
    {
    }
}
