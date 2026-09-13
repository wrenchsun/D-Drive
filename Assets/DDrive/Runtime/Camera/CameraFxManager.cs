using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Registry;
using DDrive.Foundation.Values;
using UnityEngine;
using ShakeId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Camera.ShakeMarker>;

namespace DDrive.Runtime.Camera
{
    // [16_camera_haptics.md] Part A — カメラシェイクの中核。Trauma 方式で合成する(多重発火で破綻しない)。
    //
    // 揺らす対象はカメラ本体ではなく、ランタイムで Camera.main の直上に挿入する専用ノード
    // (DDriveCameraShakeNode)。カメラ自身のローカル姿勢には触れないため、将来 CameraManager や
    // Cinemachine を導入してもこのノードの挿入方式だけ差し替えれば良い([01] ADR-4 / [16] 実装メモ)。
    //
    // Tick は GameLoop の共有 dt(TimeService.ScaledDeltaTime)ではなく、Unscaled dt で駆動する前提
    // (HitStop 中も揺れを止めない。[16] Part A 実装メモ)。dt 自体は呼び出し側が渡す値をそのまま使う
    // 純関数的な Tick(テストは任意の dt で駆動できる)。実配線は DDriveRuntimeBootstrap の
    // UnscaledCameraFxAdapter を参照。
    public sealed class CameraFxManager : IAssetManager
    {
        private sealed class ShakeInstance
        {
            public CameraShakeData Data;
            public float Elapsed;
            public float StrengthScale = 1f;
            public bool Paused;

            public bool Stopping;
            public float StopFadeDuration;
            public float StopFadeElapsed;
            public float StopWeightAtStop;

            public Vector3 SourcePos;
            public bool HasSource;

            // Perlin/Sine の軸ごとの位相ずれ(全軸が同じ波形になって不自然に見えないようにするため)。
            public float SeedX, SeedY, SeedZ, SeedRX, SeedRY, SeedRZ;
        }

        private readonly IAssetRegistry _registry;
        private readonly InstanceStore<ShakeMarker, ShakeInstance> _instances = new();
        private readonly List<Handle<ShakeMarker>> _active = new();

        private Transform _camera;
        private Transform _shakeNode;
        private Vector3 _baseLocalPos;
        private Quaternion _baseLocalRot = Quaternion.identity;
        private bool _cameraMissingWarned;

        private float _globalScale = 1f;

        public AssetType Type => AssetType.Shake;

        public CameraFxManager(IAssetRegistry registry)
        {
            _registry = registry;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RegisterPlaceholder()
        {
            PlaceholderProvider.Register(CreatePlaceholder);
        }

        // FR-1.4: 未登録/未ロードの Shake は「揺れない」プレースホルダで代替する。
        private static CameraShakeData CreatePlaceholder()
        {
            var data = ScriptableObject.CreateInstance<CameraShakeData>();
            data.DisplayName = "<Placeholder:SHAKE>";
            data.PosAmplitude = Vector3.zero;
            data.RotAmplitude = Vector3.zero;
            data.Envelope = ValueDef.Constant01(0f);
            data.MaxStack = 1;
            return data;
        }

        // ── Shake ──

        public Handle<ShakeMarker> Shake(ShakeId id)
            => ShakeData(_registry.ResolveOrPlaceholder<CameraShakeData>(id.Value));

        public Handle<ShakeMarker> Shake(ShakeId id, Vector3 sourcePos)
            => ShakeData(_registry.ResolveOrPlaceholder<CameraShakeData>(id.Value), sourcePos);

        public Handle<ShakeMarker> Shake(ShakeId id, float strengthScale)
            => ShakeData(_registry.ResolveOrPlaceholder<CameraShakeData>(id.Value), null, strengthScale);

        public Handle<ShakeMarker> ShakeData(CameraShakeData data, Vector3? sourcePos = null, float strengthScale = 1f)
        {
            if (data == null)
            {
                return Handle<ShakeMarker>.Invalid;
            }

            if (data.MaxStack > 0 && CountActive(data) >= data.MaxStack)
            {
                // 上限超過は「多重発火で破綻しない」ための意図的な無視(警告なし。連打は普通に起きる)。
                return Handle<ShakeMarker>.Invalid;
            }

            var instance = new ShakeInstance
            {
                Data = data,
                StrengthScale = Mathf.Max(0f, strengthScale),
                SourcePos = sourcePos ?? Vector3.zero,
                HasSource = sourcePos.HasValue,
                SeedX = Random.value * 1000f,
                SeedY = Random.value * 1000f,
                SeedZ = Random.value * 1000f,
                SeedRX = Random.value * 1000f,
                SeedRY = Random.value * 1000f,
                SeedRZ = Random.value * 1000f,
            };

            var handle = _instances.Add(instance);
            _active.Add(handle);
            return handle;
        }

        private int CountActive(CameraShakeData data)
        {
            var count = 0;
            for (var i = 0; i < _active.Count; i++)
            {
                if (_instances.TryGet(_active[i], out var instance) && instance.Data == data)
                {
                    count++;
                }
            }

            return count;
        }

        // ── Handle 操作 ──

        public void Stop(Handle<ShakeMarker> handle, float fade = 0.1f)
        {
            if (!_instances.TryGet(handle, out var instance) || instance.Stopping)
            {
                return;
            }

            instance.Stopping = true;
            instance.StopFadeDuration = Mathf.Max(0f, fade);
            instance.StopFadeElapsed = 0f;
            instance.StopWeightAtStop = Mathf.Max(0f, instance.Data.Envelope.EvaluateAt(instance.Elapsed));
        }

        public void SetStrength(Handle<ShakeMarker> handle, float strength)
        {
            if (_instances.TryGet(handle, out var instance))
            {
                instance.StrengthScale = Mathf.Max(0f, strength);
            }
        }

        public bool IsPlaying(Handle<ShakeMarker> handle) => _instances.IsValidSilent(handle);

        // テスト/エディタプレビュー用。現在合成に寄与しうる Instance 数(Stopping 含む)。
        public int ActiveCount => _active.Count;

        // デザイナー向け一斉停止(フェード付き)。IAssetManager.StopAll(StopReason) とは別 API。
        public void StopAllWithFade(float fadeOut = 0.1f)
        {
            for (var i = 0; i < _active.Count; i++)
            {
                Stop(_active[i], fadeOut);
            }
        }

        public void SetGlobalScale(float scale) => _globalScale = Mathf.Max(0f, scale);

        // ── Tick ──

        public void Tick(float dt)
        {
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                var handle = _active[i];
                if (!_instances.TryGet(handle, out var instance))
                {
                    _active.RemoveAt(i);
                    continue;
                }

                if (instance.Paused)
                {
                    continue;
                }

                instance.Elapsed += dt;

                if (instance.Stopping)
                {
                    instance.StopFadeElapsed += dt;
                    if (instance.StopFadeElapsed >= instance.StopFadeDuration)
                    {
                        Remove(handle);
                    }

                    continue;
                }

                if (IsExpired(instance))
                {
                    Remove(handle);
                }
            }

            EnsureCameraNode();
            ApplyOffsetToNode();
        }

        private static bool IsExpired(ShakeInstance instance)
        {
            var duration = instance.Data.Envelope.Duration;
            return duration <= 0f || instance.Elapsed >= duration;
        }

        private void Remove(Handle<ShakeMarker> handle)
        {
            _active.Remove(handle);
            _instances.Remove(handle);
        }

        public void OnPause(PauseChannel channel, bool paused) => ApplyPause(paused);

        private void ApplyPause(bool paused)
        {
            for (var i = 0; i < _active.Count; i++)
            {
                if (_instances.TryGet(_active[i], out var instance) && instance.Data.Flags.Pause == PauseMode.PauseWithGame)
                {
                    instance.Paused = paused;
                }
            }
        }

        public void StopAll(StopReason reason)
        {
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                Remove(_active[i]);
            }

            if (_shakeNode != null)
            {
                _shakeNode.localPosition = _baseLocalPos;
                _shakeNode.localRotation = _baseLocalRot;
            }
        }

        public void OnSceneUnload() => StopAll(StopReason.SceneUnload);

        // ── カメラノード ──

        private void EnsureCameraNode()
        {
            var cam = UnityEngine.Camera.main;
            if (cam == null)
            {
                if (!_cameraMissingWarned)
                {
                    _cameraMissingWarned = true;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                    Debug.LogWarning("[DDrive] CameraFx: Camera.main が見つからないため、シェイクは no-op です。");
#endif
                }

                _camera = null;
                _shakeNode = null;
                return;
            }

            _cameraMissingWarned = false;

            if (_camera == cam.transform && _shakeNode != null && cam.transform.parent == _shakeNode)
            {
                return;
            }

            AttachNode(cam.transform);
        }

        private void AttachNode(Transform camTransform)
        {
            var node = new GameObject("DDriveCameraShakeNode").transform;
            var origParent = camTransform.parent;
            node.SetParent(origParent, false);
            node.SetPositionAndRotation(camTransform.position, camTransform.rotation);

            camTransform.SetParent(node, false);
            camTransform.localPosition = Vector3.zero;
            camTransform.localRotation = Quaternion.identity;

            if (camTransform.gameObject.scene.name == "DontDestroyOnLoad")
            {
                Object.DontDestroyOnLoad(node.gameObject);
            }

            _camera = camTransform;
            _shakeNode = node;
            _baseLocalPos = node.localPosition;
            _baseLocalRot = node.localRotation;
        }

        // ── 合成・出力 ──

        private void ApplyOffsetToNode()
        {
            if (_shakeNode == null)
            {
                return;
            }

            ComputeOffsets(out var pos, out var rot);
            pos *= _globalScale;
            rot *= _globalScale;

            _shakeNode.localPosition = _baseLocalPos + pos;
            _shakeNode.localRotation = _baseLocalRot * Quaternion.Euler(rot);
        }

        // Trauma 方式: 各 Instance の重み(Envelope の減衰値 × TraumaWeight × StrengthScale)を合計し、
        // 0..1 にクランプしたものを二乗して最終振幅係数(shakeAmount)にする(二乗で「効き始めは弱く、
        // ピークは強く」ジャンプ感を出す。[16] Part A)。方向・波形は各 Instance の寄与を重みで加重平均する
        // ことで、「多重発火しても最大振幅を超えない」ことを保証する(重み付き平均は個々のベクトルの
        // 最大値を超えない)。
        private void ComputeOffsets(out Vector3 pos, out Vector3 rot)
        {
            var rawSum = 0f;
            var weightedPos = Vector3.zero;
            var weightedRot = Vector3.zero;

            for (var i = 0; i < _active.Count; i++)
            {
                if (!_instances.TryGet(_active[i], out var instance) || instance.Paused)
                {
                    continue;
                }

                var weight = InstanceWeight(instance);
                if (weight <= 0f)
                {
                    continue;
                }

                rawSum += weight;
                SampleWave(instance, out var wavePos, out var waveRot);
                weightedPos += weight * ResolvePosToNodeLocal(instance, wavePos);
                weightedRot += weight * waveRot;
            }

            var totalTrauma = Mathf.Clamp01(rawSum);
            var shakeAmount = totalTrauma * totalTrauma;

            if (rawSum > 1e-5f && shakeAmount > 0f)
            {
                pos = (weightedPos / rawSum) * shakeAmount;
                rot = (weightedRot / rawSum) * shakeAmount;
            }
            else
            {
                pos = Vector3.zero;
                rot = Vector3.zero;
            }
        }

        private static float InstanceWeight(ShakeInstance instance)
        {
            var envelopeValue = instance.Stopping
                ? (instance.StopFadeDuration > 0f
                    ? Mathf.Lerp(instance.StopWeightAtStop, 0f, Mathf.Clamp01(instance.StopFadeElapsed / instance.StopFadeDuration))
                    : 0f)
                : instance.Data.Envelope.EvaluateAt(instance.Elapsed);

            return Mathf.Max(0f, envelopeValue) * Mathf.Max(0f, instance.Data.TraumaWeight) * Mathf.Max(0f, instance.StrengthScale);
        }

        private void SampleWave(ShakeInstance instance, out Vector3 pos, out Vector3 rot)
        {
            var data = instance.Data;
            var freq = Mathf.Max(0f, data.Frequency.EvaluateAt(instance.Elapsed));

            switch (data.Pattern)
            {
                case ShakePattern.PerlinNoise:
                    pos = new Vector3(
                        Noise(instance.SeedX, instance.Elapsed, freq) * data.PosAmplitude.x,
                        Noise(instance.SeedY, instance.Elapsed, freq) * data.PosAmplitude.y,
                        Noise(instance.SeedZ, instance.Elapsed, freq) * data.PosAmplitude.z);
                    rot = new Vector3(
                        Noise(instance.SeedRX, instance.Elapsed, freq) * data.RotAmplitude.x,
                        Noise(instance.SeedRY, instance.Elapsed, freq) * data.RotAmplitude.y,
                        Noise(instance.SeedRZ, instance.Elapsed, freq) * data.RotAmplitude.z);
                    break;

                case ShakePattern.DecaySine:
                    pos = new Vector3(
                        Sine(instance.SeedX, instance.Elapsed, freq) * data.PosAmplitude.x,
                        Sine(instance.SeedY, instance.Elapsed, freq) * data.PosAmplitude.y,
                        Sine(instance.SeedZ, instance.Elapsed, freq) * data.PosAmplitude.z);
                    rot = new Vector3(
                        Sine(instance.SeedRX, instance.Elapsed, freq) * data.RotAmplitude.x,
                        Sine(instance.SeedRY, instance.Elapsed, freq) * data.RotAmplitude.y,
                        Sine(instance.SeedRZ, instance.Elapsed, freq) * data.RotAmplitude.z);
                    break;

                case ShakePattern.Impulse:
                case ShakePattern.CustomCurve:
                default:
                    pos = data.PosAmplitude;
                    rot = data.RotAmplitude;
                    break;
            }

            if (data.Space == ShakeSpace.FromSource && instance.HasSource)
            {
                var intensity = pos.magnitude;
                pos = ComputePushDirection(instance) * intensity;
            }
        }

        private static float Noise(float seed, float elapsed, float freq) => Mathf.PerlinNoise(seed, elapsed * freq) * 2f - 1f;

        private static float Sine(float seedPhase01, float elapsed, float freq) => Mathf.Sin((elapsed * freq + seedPhase01) * Mathf.PI * 2f);

        private Vector3 ComputePushDirection(ShakeInstance instance)
        {
            if (_camera == null)
            {
                return Vector3.forward;
            }

            var diff = _camera.position - instance.SourcePos;
            return diff.sqrMagnitude > 1e-6f ? diff.normalized : Vector3.forward;
        }

        // CameraLocal はノードのローカル軸にそのまま適用する。World/FromSource は「常に同じワールド方向」を
        // 保つため、ノードの親の回転を打ち消して変換する(親が無ければワールド=ローカルなのでそのまま)。
        private Vector3 ResolvePosToNodeLocal(ShakeInstance instance, Vector3 pos)
        {
            if (instance.Data.Space == ShakeSpace.CameraLocal || _shakeNode == null)
            {
                return pos;
            }

            var parent = _shakeNode.parent;
            return parent != null ? parent.InverseTransformVector(pos) : pos;
        }
    }
}
