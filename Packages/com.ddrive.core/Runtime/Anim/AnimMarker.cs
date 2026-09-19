namespace DDrive.Runtime.Anim
{
    // AnimData 本体は Phase 3(3-1)で実装する。ModelData.DefaultAnimation が「ID 参照」という
    // 最終設計([05_model_animation.md] A-2)を Phase 2 の時点から満たせるよう、タグ型だけ先出しする
    // ([[MaterialMarker]] と同じ考え方)。
    public readonly struct AnimMarker
    {
    }
}
