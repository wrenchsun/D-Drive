using System;
using DDrive.Foundation.Pool;
using UnityEngine;

namespace DDrive.Runtime.Model
{
    // 2026-09-17 レビュー対応(P1-1 恒久策) — Pool から強制回収(上限超過による Priority 回収)された
    // ときに ModelsManager の内部台帳を追従させるための橋渡し。VfxInstancePoolable /
    // SeSourcePoolable と同じ作り([02_core_framework.md] §6)。Spawn 時に Instance のルートへ
    // 自動で付与される。
    //
    // 2026-10-03(FC-2 / FC-12、[51] §4.3・§4.13) — 生成時に 1 回だけ、ルート配下の SkinnedMeshRenderer と
    // その BlendShape 既定重み、IModelInstanceListener を集めてキャッシュする(定常経路の Spawn / Return は
    // 配列を for で回すだけ = 割り当てなし)。返却時は台帳掃除(OnReturnedToPool)の後に重みを既定へ戻す。
    internal sealed class ModelInstancePoolable : MonoBehaviour, IPoolable
    {
        public Action OnReturnedToPool;

        private bool _captured;
        private SkinnedMeshRenderer[] _renderers;
        private float[][] _defaultWeights;
        private IModelInstanceListener[] _listeners;

        // 冪等。Rent 直後(DefaultAnimation 再生前)に呼ぶ。2 回目以降(プール再利用)は何もしない
        // (返却のたびに既定へ戻すので、最初に控えた値が常に Prefab 生成時の値)。
        public void Capture()
        {
            if (_captured)
            {
                return;
            }

            _captured = true;

            _renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            _defaultWeights = new float[_renderers.Length][];
            for (var i = 0; i < _renderers.Length; i++)
            {
                var smr = _renderers[i];
                var mesh = smr != null ? smr.sharedMesh : null;
                if (mesh == null)
                {
                    continue;
                }

                var count = mesh.blendShapeCount;
                var weights = new float[count];
                for (var j = 0; j < count; j++)
                {
                    weights[j] = smr.GetBlendShapeWeight(j);
                }

                _defaultWeights[i] = weights;
            }

            _listeners = GetComponentsInChildren<IModelInstanceListener>(true);
        }

        public void NotifySpawned(in ModelInstanceContext context)
        {
            var listeners = _listeners;
            if (listeners == null)
            {
                return;
            }

            for (var i = 0; i < listeners.Length; i++)
            {
                var listener = listeners[i];
                if (listener is UnityEngine.Object unityObject && unityObject == null)
                {
                    continue;
                }

                try
                {
                    listener.OnModelSpawned(in context);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        public void NotifyReturning(in ModelInstanceContext context)
        {
            var listeners = _listeners;
            if (listeners == null)
            {
                return;
            }

            for (var i = 0; i < listeners.Length; i++)
            {
                var listener = listeners[i];
                if (listener is UnityEngine.Object unityObject && unityObject == null)
                {
                    continue;
                }

                try
                {
                    listener.OnModelReturning(in context);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        public void OnReturn()
        {
            OnReturnedToPool?.Invoke();
            OnReturnedToPool = null;
            ResetBlendShapes();
        }

        // FC_* / fcs_* を含む全シェイプを、生成時の値へ戻す(変化のあるものだけ書く)。
        // sharedMesh が無い・シェイプ数が変わった Renderer は飛ばす(例外にしない)。
        private void ResetBlendShapes()
        {
            var renderers = _renderers;
            if (renderers == null)
            {
                return;
            }

            for (var i = 0; i < renderers.Length; i++)
            {
                var smr = renderers[i];
                var weights = _defaultWeights[i];
                if (smr == null || weights == null)
                {
                    continue;
                }

                var mesh = smr.sharedMesh;
                if (mesh == null || mesh.blendShapeCount != weights.Length)
                {
                    continue;
                }

                for (var j = 0; j < weights.Length; j++)
                {
                    if (smr.GetBlendShapeWeight(j) != weights[j])
                    {
                        smr.SetBlendShapeWeight(j, weights[j]);
                    }
                }
            }
        }
    }
}
