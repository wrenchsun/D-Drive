using DDrive.Foundation.Handle;
using UnityEngine;

namespace DDrive.Runtime.Model
{
    // FC-12([docs/51] §4.13) — モデルのスポーン / 返却の通知で渡す、その Instance の情報。
    public readonly struct ModelInstanceContext
    {
        public readonly Handle<ModelMarker> Handle;
        public readonly ModelData Data;
        public readonly GameObject Root;

        public ModelInstanceContext(Handle<ModelMarker> handle, ModelData data, GameObject root)
        {
            Handle = handle;
            Data = data;
            Root = root;
        }
    }

    // FC-12 — Prefab(ルート / 子)のコンポーネントが実装すると、ModelsManager がスポーン / 返却を通知する。
    // 実装は生成時(その GameObject が初めて Spawn されるとき)に 1 回だけ集めてキャッシュするため、
    // 実行時に後から足したコンポーネントは対象外(Prefab に付けておく)。呼び出しは Listener ごとに
    // try/catch で隔離し、例外は Debug.LogException で出して他の Listener と処理を継続する。
    public interface IModelInstanceListener
    {
        // スロットの Material 適用・DefaultAnimation の開始の後(Spawn の戻り値の直前)。
        void OnModelSpawned(in ModelInstanceContext context);

        // プールへ戻す(Discard 含む)直前。Despawn / 強制回収の全経路で、アニメ停止・台帳の掃除より前。
        // ブレンドシェイプの重みの復元(FC-2)はこの通知の後に行う。
        void OnModelReturning(in ModelInstanceContext context);
    }
}
