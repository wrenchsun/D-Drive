using DDrive.Foundation.Identity;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Cutscene;
using DDrive.Runtime.Presentation;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [47_review_p_tickets_2026-09-20.md] P2-8(2026-09-20) — 消費側スキル
    // (Documentation~/skills/ddrive-consumer/SKILL.md「1. ID 経由の利用パターン」)が案内するサンプル
    // コードの「型・呼び出しの形」(Presentation.Play/Cutscene.Play が `in PlayContext` を取る 等)を
    // 実シグネチャに固定する。ドキュメント中の ID 定数(SEID.X 等)はプレースホルダで実際の生成物では
    // ないため、同じ型の形をしたダミー値に置き換えて再現する。このファイル自体がコンパイルできることが
    // 「ドキュメントの呼び出し方が現行 API と一致している」ことの保証になる(実際に何かを再生することは
    // 検証しない。SKILL.md を編集したら、このメソッドの本体も合わせて更新すること)。
    public class ConsumerSkillSampleCompileTests
    {
        // SKILL.md 「1. ID 経由の利用パターン」のコードブロックそのままの再現(ID 定数だけ引数に置き換え)。
        private static void SkillMd_IdUsagePattern_Sample(
            AssetId<SeMarker> seId,
            AssetId<VfxMarker> vfxId,
            AssetId<PresentationMarker> presId,
            AssetId<CutsceneMarker> cutId,
            Vector3 position,
            Quaternion rotation,
            Transform transform,
            Transform targetTransform)
        {
            // SE を鳴らす
            Audio.PlaySe(seId);

            // VFX を出す(位置・向きは呼び出し側の Transform 等から渡す)
            Vfx.Spawn(vfxId, position, rotation);

            // 演出(SE/VFX/揺れ/HitStop の束)を再生する。PlayContext は in 渡し
            // (呼び出し側の書き方は Presentation.Play(id, ctx) のままでよい。in は call site では省略できる)。
            var ctx = new PlayContext { Self = transform, Target = targetTransform };
            var handle = Presentation.Play(presId, in ctx);

            // Maya の FBX を取り込んだカットシーンを再生する(同じ PlayContext を渡す)
            var cutsceneHandle = Cutscene.Play(cutId, in ctx);

            // 未使用警告つぶし(このテストの目的はコンパイルが通ることそのもの)。
            _ = handle;
            _ = cutsceneHandle;
        }

        [Test]
        public void SkillMd_IdUsagePattern_Sample_Compiles()
        {
            Assert.DoesNotThrow(() => SkillMd_IdUsagePattern_Sample(
                default, default, default, default,
                Vector3.zero, Quaternion.identity, null, null));
        }

        // SKILL.md「1. ID 経由の利用パターン」は `Presentation.Play(id, ctx)` のように
        // in/ref キーワードを付けずに呼べることも明記している(in は call site で省略可能)。
        // こちらも同じ呼び出しが成立することを固定する。
        private static void SkillMd_IdUsagePattern_Sample_WithoutInKeyword(
            AssetId<PresentationMarker> presId, AssetId<CutsceneMarker> cutId, PlayContext ctx)
        {
            var handle = Presentation.Play(presId, ctx);
            var cutsceneHandle = Cutscene.Play(cutId, ctx);
            _ = handle;
            _ = cutsceneHandle;
        }

        [Test]
        public void SkillMd_IdUsagePattern_Sample_WithoutInKeyword_Compiles()
        {
            Assert.DoesNotThrow(() => SkillMd_IdUsagePattern_Sample_WithoutInKeyword(default, default, default));
        }
    }
}
