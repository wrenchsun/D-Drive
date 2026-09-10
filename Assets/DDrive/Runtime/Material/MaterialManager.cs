using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Registry;
using DDrive.Foundation.Values;
using UnityEngine;
using MaterialId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Material.MaterialMarker>;

namespace DDrive.Runtime.Material
{
    // [06_material_texture.md] A-3 — マテリアルの Manager(チケット 3-5、2026-09-10)。
    //
    // - Get(id): MaterialData から Unity Material を 1 つ生成して共有する(Data ごとに 1 実体。Data は書き換えない)
    // - Apply(renderer, slot, id): 共有 Material をスロットへ割り当てる(ModelsManager.SetMaterial / Slots の実処理)
    // - Replace(from, to): Apply 済みの Renderer と、シーン内で from を使っている Renderer をまとめて差し替える
    // - FadeTo(renderer, to, sec): 溶け替え。一時 Material(from のコピー)を Material.Lerp で to へ寄せ、終了時に共有 to へ戻す
    // - SetGlobalParam: Shader.SetGlobal*
    // - MaterialAnim(UV スクロール等)は Tick が共有 Material に対して一括で駆動する(Update を持つ MonoBehaviour を量産しない)
    //
    // テクスチャは MaterialCommon の TextureData ID を Registry で解決する。未登録なら null(そのチャンネル無し)で継続する。
    public sealed class MaterialManager : IAssetManager
    {
        private sealed class Built
        {
            public MaterialData Data;
            public UnityEngine.Material Material;
            public float Elapsed;
            public bool Paused;
            public Vector2[] BaseOffsets; // Anims の OffsetU/V 用: 生成時のオフセット(Anims と同じ並び)
        }

        private sealed class FadeInstance
        {
            public Renderer Renderer;
            public int Slot;
            public UnityEngine.Material From;
            public UnityEngine.Material Temp;
            public Built To;
            public float Elapsed;
            public float Duration;
            public bool Paused;
        }

        private readonly IAssetRegistry _registry;
        private readonly Dictionary<MaterialData, Built> _built = new();
        private readonly List<Built> _animated = new();
        private readonly InstanceStore<MaterialMarker, FadeInstance> _fades = new();
        private readonly List<Handle<MaterialMarker>> _activeFades = new();
        private readonly List<(Renderer renderer, int slot, Built built)> _applied = new();
        private readonly List<UnityEngine.Material> _materialScratch = new();
        private Shader _defaultShader;

        public AssetType Type => AssetType.Material;

        public MaterialManager(IAssetRegistry registry, Shader defaultShader = null)
        {
            _registry = registry;
            _defaultShader = defaultShader;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RegisterPlaceholder()
        {
            PlaceholderProvider.Register(CreatePlaceholder);
        }

        // FR-1.4: 未登録 ID は「マゼンタの Placeholder マテリアル」。
        private static MaterialData CreatePlaceholder()
        {
            var data = ScriptableObject.CreateInstance<MaterialData>();
            data.DisplayName = "<Placeholder:MAT>";
            data.Common = MaterialCommon.Default;
            data.Common.AlbedoTint = Color.magenta;
            return data;
        }

        // プロジェクトの既定シェーダー(URP Lit → Standard の順)。
        public Shader DefaultShader
        {
            get
            {
                if (_defaultShader == null)
                {
                    _defaultShader = Shader.Find("Universal Render Pipeline/Lit");
                    if (_defaultShader == null)
                    {
                        _defaultShader = Shader.Find("Standard");
                    }
                }

                return _defaultShader;
            }
        }

        // ── Get / Apply / Replace ──

        public UnityEngine.Material Get(MaterialId id)
            => GetData(_registry.ResolveOrPlaceholder<MaterialData>(id.Value));

        public UnityEngine.Material GetData(MaterialData data) => data == null ? null : GetOrBuild(data).Material;

        // 生成済みの共有 Material(未生成なら生成しない)。エディタ表示用。
        public bool TryGetBuilt(MaterialData data, out UnityEngine.Material material)
        {
            if (data != null && _built.TryGetValue(data, out var built) && built.Material != null)
            {
                material = built.Material;
                return true;
            }

            material = null;
            return false;
        }

        public int BuiltCount => _built.Count;

        public void Apply(Renderer renderer, int slot, MaterialId id)
            => ApplyData(renderer, slot, _registry.ResolveOrPlaceholder<MaterialData>(id.Value));

        public void ApplyData(Renderer renderer, int slot, MaterialData data)
        {
            if (renderer == null || data == null || slot < 0)
            {
                return;
            }

            var built = GetOrBuild(data);
            var materials = renderer.sharedMaterials;
            if (slot >= materials.Length)
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                Debug.LogWarning($"[DDrive] Mats.Apply: '{renderer.name}' has {materials.Length} slots; slot {slot} ignored.");
#endif
                return;
            }

            materials[slot] = built.Material;
            renderer.sharedMaterials = materials;
            Track(renderer, slot, built);
        }

        // from を使っている Renderer(Apply 済み + シーン内の全 Renderer)を to に差し替える。明示呼び出し限定(シーン走査あり)。
        public int Replace(MaterialId from, MaterialId to)
        {
            var fromData = _registry.ResolveOrPlaceholder<MaterialData>(from.Value);
            var toData = _registry.ResolveOrPlaceholder<MaterialData>(to.Value);
            return ReplaceData(fromData, toData);
        }

        public int ReplaceData(MaterialData fromData, MaterialData toData)
        {
            if (fromData == null || toData == null || !_built.TryGetValue(fromData, out var fromBuilt) || fromBuilt.Material == null)
            {
                return 0;
            }

            var toBuilt = GetOrBuild(toData);
            var replaced = 0;
            var renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var r = 0; r < renderers.Length; r++)
            {
                var renderer = renderers[r];
                renderer.GetSharedMaterials(_materialScratch);
                var changed = false;
                for (var i = 0; i < _materialScratch.Count; i++)
                {
                    if (_materialScratch[i] == fromBuilt.Material)
                    {
                        _materialScratch[i] = toBuilt.Material;
                        changed = true;
                        replaced++;
                        Track(renderer, i, toBuilt);
                    }
                }

                if (changed)
                {
                    renderer.SetSharedMaterials(_materialScratch);
                }
            }

            return replaced;
        }

        // ── FadeTo ──

        public Handle<MaterialMarker> FadeTo(Renderer renderer, MaterialId to, float seconds) => FadeTo(renderer, 0, to, seconds);

        public Handle<MaterialMarker> FadeTo(Renderer renderer, int slot, MaterialId to, float seconds)
            => FadeToData(renderer, slot, _registry.ResolveOrPlaceholder<MaterialData>(to.Value), seconds);

        public Handle<MaterialMarker> FadeToData(Renderer renderer, int slot, MaterialData toData, float seconds)
        {
            if (renderer == null || toData == null || slot < 0)
            {
                return Handle<MaterialMarker>.Invalid;
            }

            var materials = renderer.sharedMaterials;
            if (slot >= materials.Length)
            {
                return Handle<MaterialMarker>.Invalid;
            }

            var toBuilt = GetOrBuild(toData);
            if (seconds <= 0f)
            {
                ApplyData(renderer, slot, toData);
                return Handle<MaterialMarker>.Invalid;
            }

            // 同じスロットのフェードが進行中なら打ち切る(完了扱いで to を確定)。
            for (var i = _activeFades.Count - 1; i >= 0; i--)
            {
                if (_fades.TryGet(_activeFades[i], out var running) && running.Renderer == renderer && running.Slot == slot)
                {
                    Finish(_activeFades[i], running);
                }
            }

            var from = renderer.sharedMaterials[slot];
            var temp = from != null ? new UnityEngine.Material(from) : new UnityEngine.Material(toBuilt.Material);
            temp.name = (from != null ? from.name : "None") + "->" + toBuilt.Material.name + " (fade)";
            temp.hideFlags = HideFlags.DontSave;

            materials = renderer.sharedMaterials;
            materials[slot] = temp;
            renderer.sharedMaterials = materials;

            var instance = new FadeInstance
            {
                Renderer = renderer,
                Slot = slot,
                From = from,
                Temp = temp,
                To = toBuilt,
                Duration = seconds,
            };
            var handle = _fades.Add(instance);
            _activeFades.Add(handle);
            return handle;
        }

        public bool IsFading(Handle<MaterialMarker> handle) => _fades.IsValidSilent(handle);

        public int ActiveFadeCount => _activeFades.Count;

        // 途中で止める: その時点の見た目のまま残さず、to を確定する(中断 = 完了扱い)。
        public void Stop(Handle<MaterialMarker> handle)
        {
            if (_fades.TryGet(handle, out var instance))
            {
                Finish(handle, instance);
            }
        }

        // ── Global ──

        public void SetGlobalParam(string property, ParamValue value)
        {
            if (string.IsNullOrEmpty(property))
            {
                return;
            }

            var id = Shader.PropertyToID(property);
            switch (value.Type)
            {
                case ParamValueType.Float: Shader.SetGlobalFloat(id, value.FloatValue); break;
                case ParamValueType.Int: Shader.SetGlobalInteger(id, value.IntValue); break;
                case ParamValueType.Bool: Shader.SetGlobalFloat(id, value.BoolValue ? 1f : 0f); break;
                case ParamValueType.Color: Shader.SetGlobalColor(id, value.ColorValue); break;
                case ParamValueType.Vector: Shader.SetGlobalVector(id, value.VectorValue); break;
                case ParamValueType.Object:
                    if (value.ObjectValue is Texture tex)
                    {
                        Shader.SetGlobalTexture(id, tex);
                    }

                    break;
            }
        }

        // ── Tick / Pause / StopAll ──

        public void Tick(float dt)
        {
            for (var i = 0; i < _animated.Count; i++)
            {
                var built = _animated[i];
                if (built.Paused || built.Material == null)
                {
                    continue;
                }

                built.Elapsed += dt;
                TickAnims(built);
            }

            for (var i = _activeFades.Count - 1; i >= 0; i--)
            {
                var handle = _activeFades[i];
                if (!_fades.TryGet(handle, out var fade))
                {
                    _activeFades.RemoveAt(i);
                    continue;
                }

                if (fade.Renderer == null || fade.Temp == null)
                {
                    Release(handle, fade);
                    continue;
                }

                if (fade.Paused)
                {
                    continue;
                }

                fade.Elapsed += dt;
                var t = Mathf.Clamp01(fade.Elapsed / fade.Duration);
                if (fade.From != null && fade.From.shader == fade.To.Material.shader)
                {
                    fade.Temp.Lerp(fade.From, fade.To.Material, t);
                }
                else
                {
                    // シェーダーが違う(または from 無し)場合は Lerp が成立しないため、半分を過ぎたら to へ切り替える。
                    if (t >= 0.5f)
                    {
                        Finish(handle, fade);
                        continue;
                    }
                }

                if (t >= 1f)
                {
                    Finish(handle, fade);
                }
            }
        }

        public void OnPause(PauseChannel channel, bool paused)
        {
            for (var i = 0; i < _animated.Count; i++)
            {
                if (_animated[i].Data.Flags.Pause == PauseMode.PauseWithGame)
                {
                    _animated[i].Paused = paused;
                }
            }

            for (var i = 0; i < _activeFades.Count; i++)
            {
                if (_fades.TryGet(_activeFades[i], out var fade) && fade.To.Data.Flags.Pause == PauseMode.PauseWithGame)
                {
                    fade.Paused = paused;
                }
            }
        }

        // 進行中のフェードを全部確定する(生成済み共有 Material は保持する。破棄は Clear)。
        public void StopAll(StopReason reason)
        {
            for (var i = _activeFades.Count - 1; i >= 0; i--)
            {
                if (_fades.TryGet(_activeFades[i], out var fade))
                {
                    Finish(_activeFades[i], fade);
                }
            }

            _activeFades.Clear();
            _applied.Clear();
        }

        public void OnSceneUnload() => StopAll(StopReason.SceneUnload);

        // 生成した共有 Material を全部破棄する(テスト / エディタのプレビュー終了用)。
        public void Clear()
        {
            StopAll(StopReason.Manual);
            foreach (var built in _built.Values)
            {
                if (built.Material != null)
                {
                    if (Application.isPlaying)
                    {
                        Object.Destroy(built.Material);
                    }
                    else
                    {
                        Object.DestroyImmediate(built.Material);
                    }
                }
            }

            _built.Clear();
            _animated.Clear();
        }

        // ── 内部 ──

        private Built GetOrBuild(MaterialData data)
        {
            if (_built.TryGetValue(data, out var built) && built.Material != null)
            {
                return built;
            }

            var shader = data.Shader != null ? data.Shader : DefaultShader;
            var material = shader != null ? new UnityEngine.Material(shader) : new UnityEngine.Material(Shader.Find("Hidden/InternalErrorShader"));
            material.name = "DD_" + (string.IsNullOrEmpty(data.DisplayName) ? data.name : data.DisplayName);
            material.hideFlags = HideFlags.DontSave;

            MaterialCommonBinding.Apply(material, data.Common, ResolveTexture);
            ApplySpecific(material, data.Specific);
            material.renderQueue = data.RenderQueue;

            built = new Built { Data = data, Material = material };
            if (data.HasAnims)
            {
                built.BaseOffsets = new Vector2[data.Anims.Length];
                for (var i = 0; i < data.Anims.Length; i++)
                {
                    var anim = data.Anims[i];
                    if (anim.Channel != MaterialAnimChannel.Float && !string.IsNullOrEmpty(anim.Property) && material.HasProperty(anim.Property))
                    {
                        built.BaseOffsets[i] = material.GetTextureOffset(anim.Property);
                    }
                }

                _animated.Add(built);
                TickAnims(built);
            }

            _built[data] = built;
            return built;
        }

        private Texture ResolveTexture(AssetId<TextureMarker> id)
        {
            if (!id.IsValid || _registry == null)
            {
                return null;
            }

            return _registry.TryResolveSync<TextureData>(id.Value, out var tex) && tex != null ? tex.Texture : null;
        }

        private static void ApplySpecific(UnityEngine.Material material, ShaderParam[] specific)
        {
            if (specific == null)
            {
                return;
            }

            for (var i = 0; i < specific.Length; i++)
            {
                var param = specific[i];
                if (string.IsNullOrEmpty(param.Property) || !material.HasProperty(param.Property))
                {
                    continue;
                }

                var id = Shader.PropertyToID(param.Property);
                switch (param.Value.Type)
                {
                    case ParamValueType.Float: material.SetFloat(id, param.Value.FloatValue); break;
                    case ParamValueType.Int: material.SetInteger(id, param.Value.IntValue); break;
                    case ParamValueType.Bool: material.SetFloat(id, param.Value.BoolValue ? 1f : 0f); break;
                    case ParamValueType.Color: material.SetColor(id, param.Value.ColorValue); break;
                    case ParamValueType.Vector: material.SetVector(id, param.Value.VectorValue); break;
                    case ParamValueType.Object:
                        if (param.Value.ObjectValue is Texture tex)
                        {
                            material.SetTexture(id, tex);
                        }

                        break;
                }
            }
        }

        private static void TickAnims(Built built)
        {
            var anims = built.Data.Anims;
            var material = built.Material;
            for (var i = 0; i < anims.Length; i++)
            {
                var anim = anims[i];
                if (string.IsNullOrEmpty(anim.Property) || !material.HasProperty(anim.Property))
                {
                    continue;
                }

                var value = anim.Value.EvaluateAt(built.Elapsed);
                switch (anim.Channel)
                {
                    case MaterialAnimChannel.Float:
                        material.SetFloat(anim.Property, value);
                        break;

                    case MaterialAnimChannel.OffsetU:
                    {
                        var offset = material.GetTextureOffset(anim.Property);
                        offset.x = built.BaseOffsets[i].x + value;
                        material.SetTextureOffset(anim.Property, offset);
                        break;
                    }

                    case MaterialAnimChannel.OffsetV:
                    {
                        var offset = material.GetTextureOffset(anim.Property);
                        offset.y = built.BaseOffsets[i].y + value;
                        material.SetTextureOffset(anim.Property, offset);
                        break;
                    }
                }
            }
        }

        private void Track(Renderer renderer, int slot, Built built)
        {
            for (var i = _applied.Count - 1; i >= 0; i--)
            {
                var entry = _applied[i];
                if (entry.renderer == null || (entry.renderer == renderer && entry.slot == slot))
                {
                    _applied.RemoveAt(i);
                }
            }

            _applied.Add((renderer, slot, built));
        }

        private void Finish(Handle<MaterialMarker> handle, FadeInstance fade)
        {
            if (fade.Renderer != null && fade.To.Material != null)
            {
                var materials = fade.Renderer.sharedMaterials;
                if (fade.Slot < materials.Length)
                {
                    materials[fade.Slot] = fade.To.Material;
                    fade.Renderer.sharedMaterials = materials;
                }

                Track(fade.Renderer, fade.Slot, fade.To);
            }

            Release(handle, fade);
        }

        private void Release(Handle<MaterialMarker> handle, FadeInstance fade)
        {
            if (fade.Temp != null)
            {
                if (Application.isPlaying)
                {
                    Object.Destroy(fade.Temp);
                }
                else
                {
                    Object.DestroyImmediate(fade.Temp);
                }
            }

            _fades.Remove(handle);
            _activeFades.Remove(handle);
        }
    }
}
