using System.Collections.Generic;
using DDrive.Foundation.Manager;
using DDrive.Runtime.Extension;

namespace SampleExtension
{
    // 起動配線の口(docs/1014 §2.2)。DDriveRuntimeBootstrap が組み込みの Manager を登録した後に Order 順で呼ぶ。
    // CreateManagers で作った Manager は GameLoop に登録される。Bind / Unbind で静的ファサードを結ぶ。
    // Editor プレビュー(EditorModuleHost)も同じ module を使う(ctx.IsEditorPreview = true)が、Host は Bind / Unbind を呼ばない。
    // [Preserve] と同じフォルダの link.xml は、IL2CPP のコード除去(Managed Stripping)で module が消えないようにするため(型がどこからも静的に参照されないので)。
    [UnityEngine.Scripting.Preserve]
    public sealed class SampleModule : IDDriveModule
    {
        private SampleManager _manager;

        public int Order => 1000;

        public IReadOnlyList<IAssetManager> CreateManagers(in DDriveBootstrapContext ctx)
        {
            _manager = new SampleManager(ctx.Registry);
            return new IAssetManager[] { _manager };
        }

        public void Bind() => Samples.Bind(_manager);

        public void Unbind() => Samples.Bind(null);

        // エディターの ▶ が module の Manager を取り出すための口。
        public SampleManager Manager => _manager;
    }
}
