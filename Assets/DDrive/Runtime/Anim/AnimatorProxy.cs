using System.Collections.Generic;
using UnityEngine;

namespace DDrive.Runtime.Anim
{
    // [05_model_animation.md] B-3 — 対象 Animator に AnimManager が自動アタッチする代行コンポーネント。
    // IK コールバック(OnAnimatorIK)と BlendShape の適用を担当する。状態(再生中の AnimData と正規化時間)は
    // AnimManager が毎 Tick 書き込む。ゲームコードはこのコンポーネントを直接触らない(IK ターゲットの指定だけ)。
    //
    // 状態は Animator の Layer ごとに保持する(2026-09-09): AnimManager は異なる Layer の同時再生を許すため、
    // 後から Tick された Layer が他 Layer の IK を上書きしないよう、OnAnimatorIK(layerIndex) はその Layer の
    // AnimData だけを見る。BlendShape は各再生が自分のトラックを書くため、同じ ShapeName を複数 Layer で
    // 同時に使うと後勝ちになる(Validator が警告する)。
    [DisallowMultipleComponent]
    public sealed class AnimatorProxy : MonoBehaviour
    {
        private struct LayerState
        {
            public int Layer;
            public AnimData Data;
            public float NormalizedTime;
        }

        [Tooltip("IK のターゲット。AnimData.Ik で有効にした手足だけが使われる。未設定の手足は IK を掛けない。")]
        public Transform LeftHandTarget;
        public Transform RightHandTarget;
        public Transform LeftFootTarget;
        public Transform RightFootTarget;

        // 最後に SetActive された Data / 時間(単一 Layer 運用向けの互換 API)。Layer 別は GetActiveData / GetNormalizedTime。
        public AnimData ActiveData { get; private set; }
        public float NormalizedTime { get; private set; }

        private readonly List<LayerState> _layers = new(2);
        private Animator _animator;
        private SkinnedMeshRenderer[] _skinned;

        public Animator Animator => _animator != null ? _animator : (_animator = GetComponent<Animator>());

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _skinned = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        }

        public void SetActive(AnimData data, float normalizedTime)
        {
            ActiveData = data;
            NormalizedTime = normalizedTime;
            if (data == null)
            {
                return;
            }

            for (var i = 0; i < _layers.Count; i++)
            {
                if (_layers[i].Layer == data.Layer)
                {
                    _layers[i] = new LayerState { Layer = data.Layer, Data = data, NormalizedTime = normalizedTime };
                    return;
                }
            }

            _layers.Add(new LayerState { Layer = data.Layer, Data = data, NormalizedTime = normalizedTime });
        }

        // その Data がまだその Layer の現行なら外す(別の Data に置き換わっていれば触らない)。
        public void ClearActive(AnimData data)
        {
            if (ReferenceEquals(ActiveData, data))
            {
                ActiveData = null;
            }

            if (data == null)
            {
                return;
            }

            for (var i = 0; i < _layers.Count; i++)
            {
                if (_layers[i].Layer == data.Layer && ReferenceEquals(_layers[i].Data, data))
                {
                    _layers.RemoveAt(i);
                    return;
                }
            }
        }

        public AnimData GetActiveData(int layer)
        {
            for (var i = 0; i < _layers.Count; i++)
            {
                if (_layers[i].Layer == layer)
                {
                    return _layers[i].Data;
                }
            }

            return null;
        }

        public float GetNormalizedTime(int layer)
        {
            for (var i = 0; i < _layers.Count; i++)
            {
                if (_layers[i].Layer == layer)
                {
                    return _layers[i].NormalizedTime;
                }
            }

            return -1f;
        }

        public int ActiveLayerCount => _layers.Count;

        // BlendShapeTrack をカーブどおりに反映する(AnimManager の Tick から)。
        public void ApplyBlendShapes(AnimData data, float normalizedTime)
        {
            if (data == null || data.BlendShapes == null || data.BlendShapes.Length == 0)
            {
                return;
            }

            _skinned ??= GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var track in data.BlendShapes)
            {
                if (string.IsNullOrEmpty(track.ShapeName) || track.Weight == null)
                {
                    continue;
                }

                var weight = track.Weight.Evaluate(normalizedTime);
                foreach (var smr in _skinned)
                {
                    if (smr == null || smr.sharedMesh == null)
                    {
                        continue;
                    }

                    var index = smr.sharedMesh.GetBlendShapeIndex(track.ShapeName);
                    if (index >= 0)
                    {
                        smr.SetBlendShapeWeight(index, weight);
                    }
                }
            }
        }

        // 対象モデルに存在するブレンドシェイプ名か(Validator / エディタ用)。
        public bool HasBlendShape(string shapeName)
        {
            _skinned ??= GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var smr in _skinned)
            {
                if (smr != null && smr.sharedMesh != null && smr.sharedMesh.GetBlendShapeIndex(shapeName) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void OnAnimatorIK(int layerIndex)
        {
            var data = GetActiveData(layerIndex);
            if (data == null || !data.Ik.Any || Animator == null)
            {
                return;
            }

            var weight = data.Ik.EvaluateWeight(GetNormalizedTime(layerIndex));
            ApplyGoal(AvatarIKGoal.LeftHand, data.Ik.LeftHand, LeftHandTarget, weight);
            ApplyGoal(AvatarIKGoal.RightHand, data.Ik.RightHand, RightHandTarget, weight);
            ApplyGoal(AvatarIKGoal.LeftFoot, data.Ik.LeftFoot, LeftFootTarget, weight);
            ApplyGoal(AvatarIKGoal.RightFoot, data.Ik.RightFoot, RightFootTarget, weight);
        }

        private void ApplyGoal(AvatarIKGoal goal, bool enabled, Transform target, float weight)
        {
            if (!enabled || target == null)
            {
                _animator.SetIKPositionWeight(goal, 0f);
                _animator.SetIKRotationWeight(goal, 0f);
                return;
            }

            _animator.SetIKPositionWeight(goal, weight);
            _animator.SetIKRotationWeight(goal, weight);
            _animator.SetIKPosition(goal, target.position);
            _animator.SetIKRotation(goal, target.rotation);
        }
    }
}
