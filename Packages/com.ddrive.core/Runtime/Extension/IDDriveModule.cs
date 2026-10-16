using System.Collections.Generic;
using DDrive.Foundation.Manager;

namespace DDrive.Runtime.Extension
{
    // [1014_extension_platform.md] §2.2(X-2、2026-10-16) — 持ち込み先が D-Drive を変えずに Manager と静的ファサードを
    // 起動配線へ足すための拡張点。DDriveRuntimeBootstrap が組み込み Manager を登録して Bind した後に、
    // Order 昇順(同値は型のフルネーム順)で CreateManagers → GameLoop.Register → Bind() を呼び、
    // 終了時は StopAll の後に逆順で Unbind() する。例外は module 単位で隔離される(警告 1 件 + その module を飛ばす)。
    // 発見規則は [42_distribution.md] §5.14(public・非 abstract・ジェネリック定義でない・public 引数なしコンストラクタ)。
    // 呼び出しは起動と終了だけで、Tick の定常経路には何も足さない。
    // X-R-06: IL2CPP のマネージドコード除去(Managed Stripping)で実装型が消えないよう RequireImplementors を付ける。
    // X-R-01: Bind / Unbind は Play の DDriveRuntimeBootstrap(BindFacades = true)からだけ呼ばれる。Editor プレビュー(EditorModuleHost)と
    // BindFacades = false の Bootstrap は CreateManagers だけ呼び、Bind / Unbind は呼ばない。module のインスタンスは起動ごとに新しく作られる。
    [UnityEngine.Scripting.RequireImplementors]
    public interface IDDriveModule
    {
        int Order { get; }

        // 返した Manager は GameLoop に登録される(null 要素は警告して飛ばす)。Manager を持たない module は空/null でよい。
        IReadOnlyList<IAssetManager> CreateManagers(in DDriveBootstrapContext ctx);

        // Manager 登録後に 1 回(Play の Bootstrap だけ。Editor プレビューでは呼ばれない)。静的ファサードの Bind などをここで行う。
        void Bind();

        // 終了時に 1 回(Bind まで成功した module だけ。StopAll の後、組み込みファサードの Unbind より前)。Bind の逆を行う。
        void Unbind();
    }
}
