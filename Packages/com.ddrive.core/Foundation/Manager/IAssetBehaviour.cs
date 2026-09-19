namespace DDrive.Foundation.Manager
{
    // AssetDataBase.CreateBehaviour() の差し込み口。派生 Data を追加するだけで
    // Manager を改修せずに特殊制御(ビート同期BGM等)を実装できる(FR-2.2)。
    public interface IAssetBehaviour
    {
        void OnSpawn(InstanceContext ctx);
        void OnTick(InstanceContext ctx, float dt);
        void OnDespawn(InstanceContext ctx);
    }
}
