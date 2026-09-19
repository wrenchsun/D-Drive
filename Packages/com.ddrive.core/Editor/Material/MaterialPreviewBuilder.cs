using System;
using DDrive.Foundation.Handle;
using DDrive.Runtime.Material;
using DDrive.Runtime.Model;
using UnityEngine;

namespace DDrive.Editor.Materials
{
    // [06_material_texture.md] A-4 — MaterialEditor プレビューの形状(3-9)。
    public enum MaterialPreviewShape
    {
        Sphere,
        Plane, // 板(Quad)
        Cube,
        Model, // 任意 ModelData
    }

    // [06_material_texture.md] A-4 — MaterialEditorWindow のプレビュー生成をウィンドウから切り離した純粋なロジック(3-9)。
    // 球/板/Cube は GameObject.CreatePrimitive、Model は ModelsManager.SpawnData で配置し、
    // 実 MaterialManager(ApplyData)で共有 Material を適用する(ADR-4: Editor 専用の再生経路を作らない)。
    // ウィンドウ側は生成物の親(DontSave のプレビュー Root)を渡すだけで、テストは UI なしにこのクラスだけを検証できる。
    public static class MaterialPreviewBuilder
    {
        // 生成したプレビュー 1 体(球/板/Cube の Renderer、または Model の Spawn 結果)。Dispose で確実に撤去する。
        public sealed class Preview : IDisposable
        {
            public GameObject Root;
            public Renderer[] Renderers = Array.Empty<Renderer>();
            public ModelsManager Models;
            public Handle<ModelMarker> ModelHandle = Handle<ModelMarker>.Invalid;

            public void Dispose()
            {
                if (Models != null && Models.IsValid(ModelHandle))
                {
                    Models.Despawn(ModelHandle);
                }
                else if (Root != null)
                {
                    UnityEngine.Object.DestroyImmediate(Root);
                }

                Root = null;
                Renderers = Array.Empty<Renderer>();
                Models = null;
                ModelHandle = Handle<ModelMarker>.Invalid;
            }
        }

        // shape / model / material に応じてプレビューを生成し、Material を適用する。
        // shape=Model のときは model と modelsManager が必須(どちらか無ければ null)。
        public static Preview Create(MaterialPreviewShape shape, ModelData model, MaterialData material, MaterialManager materials,
            Transform parent, Vector3 localPosition, ModelsManager modelsManager, string name)
        {
            if (materials == null)
            {
                return null;
            }

            var preview = shape == MaterialPreviewShape.Model
                ? CreateModel(model, modelsManager, parent, localPosition)
                : CreatePrimitive(shape, parent, localPosition, name);

            if (preview == null)
            {
                return null;
            }

            Apply(preview, shape, model, material, materials);
            return preview;
        }

        // 既存の Preview(Renderer/Root は変えない)に Material を再適用する。「再生成」用。
        public static void Apply(Preview preview, MaterialPreviewShape shape, ModelData model, MaterialData material, MaterialManager materials)
        {
            if (preview == null || material == null || materials == null)
            {
                return;
            }

            if (shape == MaterialPreviewShape.Model && model != null && model.Slots != null && model.Slots.Length > 0 && preview.Root != null)
            {
                foreach (var slot in model.Slots)
                {
                    var renderer = ResolveRenderer(preview.Root.transform, slot.RendererPath);
                    if (renderer != null)
                    {
                        materials.ApplyData(renderer, slot.SlotIndex, material);
                    }
                }

                return;
            }

            ApplyToAllSlots(preview.Renderers, material, materials);
        }

        private static Preview CreatePrimitive(MaterialPreviewShape shape, Transform parent, Vector3 localPosition, string name)
        {
            var type = shape switch
            {
                MaterialPreviewShape.Plane => PrimitiveType.Quad,
                MaterialPreviewShape.Cube => PrimitiveType.Cube,
                _ => PrimitiveType.Sphere,
            };

            var go = GameObject.CreatePrimitive(type);
            go.name = string.IsNullOrEmpty(name) ? go.name : name;
            go.hideFlags = HideFlags.DontSave;
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            go.transform.localPosition = localPosition;
            var renderer = go.GetComponent<Renderer>();
            return new Preview { Root = go, Renderers = renderer != null ? new[] { renderer } : Array.Empty<Renderer>() };
        }

        private static Preview CreateModel(ModelData model, ModelsManager modelsManager, Transform parent, Vector3 localPosition)
        {
            if (model == null || model.Prefab == null || modelsManager == null)
            {
                return null;
            }

            var worldPos = parent != null ? parent.TransformPoint(localPosition) : localPosition;
            var rotation = parent != null ? parent.rotation : Quaternion.identity;
            var handle = modelsManager.SpawnData(model, worldPos, rotation);
            var go = modelsManager.GetGameObject(handle);
            if (go == null)
            {
                return null;
            }

            go.hideFlags = HideFlags.DontSave;
            if (parent != null)
            {
                go.transform.SetParent(parent, true);
            }

            var renderers = go.GetComponentsInChildren<Renderer>(true);
            return new Preview { Root = go, Renderers = renderers, Models = modelsManager, ModelHandle = handle };
        }

        private static void ApplyToAllSlots(Renderer[] renderers, MaterialData material, MaterialManager materials)
        {
            if (renderers == null)
            {
                return;
            }

            foreach (var renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                var slotCount = renderer.sharedMaterials.Length;
                for (var slot = 0; slot < slotCount; slot++)
                {
                    materials.ApplyData(renderer, slot, material);
                }
            }
        }

        // ModelsManager.ResolveRendererByPath と同じ規約(空文字ならルート自身)。
        private static Renderer ResolveRenderer(Transform root, string rendererPath)
        {
            if (string.IsNullOrEmpty(rendererPath))
            {
                return root.GetComponent<Renderer>();
            }

            var target = root.Find(rendererPath);
            return target != null ? target.GetComponent<Renderer>() : null;
        }
    }
}
