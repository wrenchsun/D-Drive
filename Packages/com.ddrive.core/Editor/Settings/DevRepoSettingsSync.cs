using UnityEditor;

namespace DDrive.Editor.Settings
{
    // [42_distribution.md] §4.5/§7 A-9(P-6、2026-09-20) — 開発リポジトリ(このリポジトリ)でだけ
    // `DDriveProjectSettings.IsDevelopmentRepo` を true にする。`DDRIVE_DEV_REPO`(このリポジトリの
    // ProjectSettings の Scripting Define Symbols にのみ追加されている。`Tests/Editor/DevRepoOnlyGuard.cs`
    // と同じシンボル)が定義されているときだけ動く静的コンストラクタで、人手で ProjectSettings/*.asset を
    // 編集する必要が無い(CLAUDE.md §0-1「.asset をテキスト編集しない」。ScriptableSingleton.Save 経由の
    // 保存のみ)。持ち込み先には `DDRIVE_DEV_REPO` が無いため、このクラスは何もしない
    // (`IsDevelopmentRepo` は既定 false のまま = `ProjectSetupValidator` の「改造している可能性」判定が働く)。
    [InitializeOnLoad]
    internal static class DevRepoSettingsSync
    {
        static DevRepoSettingsSync()
        {
#if DDRIVE_DEV_REPO
            if (!DDriveProjectSettings.instance.IsDevelopmentRepo)
            {
                DDriveProjectSettings.instance.IsDevelopmentRepo = true;

                // このリポジトリの Assets/Generated/ には asmdef が無く(Assembly-CSharp 所属のまま
                // 運用してきた)、`EmitGeneratedAsmdef` の既定 ON(A-8。持ち込み先向けの既定値)を
                // このリポジトリ自身に適用すると、次回の Regenerate Asset IDs で
                // Assets/Generated/DDrive.Generated.asmdef が新規に作られ、意図せず構成が変わって
                // しまう。開発リポジトリ初回検出時だけ明示的に OFF にする(持ち込み先の既定値には
                // 影響しない。DDriveProjectSettings のインスタンスはプロジェクトごとに別)。
                DDriveProjectSettings.instance.EmitGeneratedAsmdef = false;
            }
#endif
        }
    }
}
