using DDrive.Foundation.Handle;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Anim;
using UnityEngine;
using Anim2DId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Anim2D.Anim2DMarker>;

namespace DDrive.Runtime.Anim2D
{
    // [05_model_animation.md] C-4 — 2D スプライトアニメの静的ファサード(チケット 3-12)。
    // 再生実体は AnimManager(Anim2DData は AnimData の派生)。ここは方向 BlendTree のパラメータ設定を足すだけ。
    // Handle は 3D と共通(Handle<AnimMarker>): 設計書の Anim2DHandle は AnimManager 共有のため同じ型にした。
    public static class Anim2D
    {
        private static AnimManager _anim;
        private static IAssetRegistry _registry;

        public static void Bind(AnimManager anim, IAssetRegistry registry)
        {
            _anim = anim;
            _registry = registry;
        }

        public static bool IsBound => _anim != null && _registry != null;

        public static Handle<AnimMarker> Play(Anim2DId id, Animator target)
            => PlayData(Resolve(id), target);

        // dir: BlendTree の x, y(正規化しなくてよい。0 ベクトルなら方向を変えない)。
        public static Handle<AnimMarker> Play(Anim2DId id, Animator target, Vector2 dir)
        {
            var data = Resolve(id);
            if (data is Anim2DData anim2D && anim2D.HasDirections)
            {
                SetDirection(target, dir, anim2D.ParamXName, anim2D.ParamYName);
            }

            return PlayData(data, target);
        }

        public static Handle<AnimMarker> PlayData(AnimData data, Animator target)
            => _anim != null ? _anim.PlayData(data, target) : Handle<AnimMarker>.Invalid;

        // 再生中でも方向だけ変える(BlendTree の x, y)。
        // 毎フレーム呼ぶ経路では animator.parameters(配列 alloc)を走査するのでハッシュ版を使うこと。
        public static void SetDirection(Animator target, Vector2 dir, string paramX = "x", string paramY = "y")
        {
            if (target == null || target.runtimeAnimatorController == null || dir.sqrMagnitude <= 0f)
            {
                return;
            }

            var n = dir.normalized;
            if (HasParameter(target, paramX))
            {
                target.SetFloat(paramX, n.x);
            }

            if (HasParameter(target, paramY))
            {
                target.SetFloat(paramY, n.y);
            }
        }

        /// <summary>
        /// 方向パラメータをハッシュ指定で設定する(定常経路用。alloc なし)。
        /// ハッシュは <see cref="Animator.StringToHash"/> で事前に求め、存在しないパラメータには 0 を渡す(その軸は書かない)。
        /// </summary>
        public static void SetDirection(Animator target, Vector2 dir, int paramXHash, int paramYHash)
        {
            if (target == null || target.runtimeAnimatorController == null || dir.sqrMagnitude <= 0f)
            {
                return;
            }

            var n = dir.normalized;
            if (paramXHash != 0)
            {
                target.SetFloat(paramXHash, n.x);
            }

            if (paramYHash != 0)
            {
                target.SetFloat(paramYHash, n.y);
            }
        }

        /// <summary>
        /// Float パラメータが存在すればそのハッシュを、無ければ 0 を返す(<see cref="SetDirection(Animator, Vector2, int, int)"/> 用のキャッシュ作成)。
        /// animator.parameters を走査するので、対象が決まった時に 1 回だけ呼ぶこと。
        /// </summary>
        public static int ResolveFloatParameterHash(Animator target, string name)
            => target != null && HasParameter(target, name) ? Animator.StringToHash(name) : 0;

        public static void SetSpeed(Handle<AnimMarker> h, float speed) => _anim?.SetSpeed(h, speed);

        // 先頭フレームで止めて保持する(チャージ中の構え等。OH_CASE2026_ITAMI の DirectionalSpriteAnimator.FreezeAtFirstFrame 相当、2026-09-11)。
        // 再生中の Handle に対して呼ぶ。Unfreeze で speed を戻す。
        public static void FreezeAtFirstFrame(Handle<AnimMarker> h)
        {
            if (_anim == null)
            {
                return;
            }

            _anim.Seek(h, 0f);
            _anim.SetSpeed(h, 0f);
        }

        public static void Unfreeze(Handle<AnimMarker> h, float speed = 1f) => _anim?.SetSpeed(h, speed);

        public static void Stop(Handle<AnimMarker> h) => _anim?.Stop(h);

        public static bool IsPlaying(Handle<AnimMarker> h) => _anim?.IsPlaying(h) ?? false;

        private static AnimData Resolve(Anim2DId id)
            => _registry != null ? _registry.ResolveOrPlaceholder<AnimData>(id.Value) : null;

        private static bool HasParameter(Animator animator, string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            var parameters = animator.parameters;
            for (var i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == name && parameters[i].type == AnimatorControllerParameterType.Float)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
