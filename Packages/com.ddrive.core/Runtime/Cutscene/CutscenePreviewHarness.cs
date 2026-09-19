using DDrive.Runtime.Presentation;
using UnityEngine;

namespace DDrive.Runtime.Cutscene
{
    // [26_timeline.md] §4.4/§6(6-10d) — 確認用シーン(CutscenePreviewScene)に置く、Play Mode 中に
    // 任意の CutsceneData を手動再生するための最小ハーネス(公開ファサード Cutscene.PlayData を
    // 呼ぶだけの薄いラッパー。禁止 API・Manager の直接生成はしていない)。
    //
    // Editor asmdef の MonoBehaviour では GameObject.AddComponent が失敗する(Editor 専用プラットフォームの
    // asmdef からはコンポーネントを追加できない、Unity の既知の制約)ため、この確認用の薄いハーネスだけは
    // Runtime asmdef に置く。中身は公開静的ファサード(Cutscene/CutsceneHandle)しか使わず、Manager を
    // new したり UnityEditor を参照したりはしていないので Runtime に置いても既存規約(CLAUDE.md §0)に反しない。
    //
    // CutsceneManager は DDriveRuntimeBootstrap(Play Mode の起動時)/ テスト / Editor プレビュー以外では
    // 生成されない([02_core_framework.md] §14、CLAUDE.md §0-8)。この確認用シーンでは
    // DDriveRuntimeBootstrap をシーンに置いて Play Mode の起動時に生成させる方式を採る(専用の
    // Editor 駆動 CutsceneManager は作らない = ADR-4 の「Editor 専用の再生経路を作らない」を守る)。
    // そのため Edit Mode(非再生中)のスクラブでは Camera/SE/VFX 等の実 Manager 適用までは届かない
    // (§4.4 が想定した「Editor 用 Manager 群」の完全実装は本チケットの範囲外。TODO として
    // docs/26_timeline.md に記録した)。目視確認は Play Mode に入ってこのハーネスの Play を呼んだあと、
    // 標準 Timeline ウィンドウでスクラブする(Play Mode 中は実 Manager 経由でそのまま機能する)。
    public sealed class CutscenePreviewHarness : MonoBehaviour
    {
        [Tooltip("ctx.Self として使う Transform(未設定ならこのオブジェクト自身)。")]
        public Transform Self;

        [Tooltip("ctx.Target として使う Transform(任意)。")]
        public Transform Target;

        private CutsceneHandle _handle;

        public bool IsPlaying => _handle.IsPlaying;

        public CutsceneHandle Play(CutsceneData data)
        {
            if (data == null)
            {
                Debug.LogWarning("[DDrive] CutscenePreviewHarness.Play: CutsceneData が null です。");
                return CutsceneHandle.Invalid;
            }

            if (_handle.IsPlaying)
            {
                _handle.Cancel();
            }

            var ctx = new PlayContext { Self = Self != null ? Self : transform, Target = Target };
            _handle = Cutscene.PlayData(data, in ctx);
            return _handle;
        }

        public void Cancel()
        {
            if (_handle.IsPlaying)
            {
                _handle.Cancel();
            }
        }

        public void Skip()
        {
            if (_handle.IsPlaying)
            {
                _handle.Skip();
            }
        }
    }
}
