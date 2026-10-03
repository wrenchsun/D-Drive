using System;
using DDrive.Editor.Cutscene;

namespace ExternalPackage.Fake
{
    // [docs/42 §5.14] 外部パッケージが書く ICutsceneImportListener のダミー(FC-5)。
    // TypeCache で常時発見されるため、普段は何もしない: `Sink` が設定されているテスト中だけ記録する。
    // テストアセンブリ名が `DDrive.Tests` で始まらない(= 発見される)ことが前提。
    public static class ExternalListenerProbe
    {
        // (リスナー名, 結果)。null のときはすべてのダミーが何もしない。
        public static Action<string, CutsceneImportResult> Sink;

        // true のとき ExternalThrowingListener が例外を投げる。
        public static bool ThrowEnabled;

        public static void Reset()
        {
            Sink = null;
            ThrowEnabled = false;
        }
    }

    public sealed class ExternalListenerB : ICutsceneImportListener
    {
        public int Order => 0;

        public void OnCutsceneShotImported(CutsceneImportResult result) => ExternalListenerProbe.Sink?.Invoke(nameof(ExternalListenerB), result);
    }

    // B と同じ Order(同値は型のフルネーム順 = A が先)。
    public sealed class ExternalListenerA : ICutsceneImportListener
    {
        public int Order => 0;

        public void OnCutsceneShotImported(CutsceneImportResult result) => ExternalListenerProbe.Sink?.Invoke(nameof(ExternalListenerA), result);
    }

    public sealed class ExternalListenerLast : ICutsceneImportListener
    {
        public int Order => 50;

        public void OnCutsceneShotImported(CutsceneImportResult result) => ExternalListenerProbe.Sink?.Invoke(nameof(ExternalListenerLast), result);
    }

    // internal な型は発見されない(docs/42 §5.14 E-19 の「public」。FX-R-14)。発見されると呼ばれて Sink に記録する(= テストが赤になる)。
    internal sealed class ExternalNonPublicListener : ICutsceneImportListener
    {
        public int Order => 1;

        public void OnCutsceneShotImported(CutsceneImportResult result) => ExternalListenerProbe.Sink?.Invoke(nameof(ExternalNonPublicListener), result);
    }

    // 一番先に呼ばれ、ThrowEnabled のとき例外を投げる(後続のリスナーと取り込みが止まらないことの確認用)。
    public sealed class ExternalThrowingListener : ICutsceneImportListener
    {
        public int Order => -10;

        public void OnCutsceneShotImported(CutsceneImportResult result)
        {
            if (!ExternalListenerProbe.ThrowEnabled)
            {
                return;
            }

            ExternalListenerProbe.Sink?.Invoke(nameof(ExternalThrowingListener), result);
            throw new InvalidOperationException("external listener failure (test)");
        }
    }
}
