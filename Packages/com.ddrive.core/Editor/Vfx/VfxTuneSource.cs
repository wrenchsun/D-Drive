using System;
using System.Collections.Generic;
using System.Text;
using DDrive.Editor.Ux;
using DDrive.Foundation.Data;
using DDrive.Runtime.Vfx;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DDrive.Editor.Vfx
{
    // Vfx の Params を調整つまみ(TuneKnobs)に供給する(試験版 E の PeProps + PeTunePanel のデータ側)。
    // 「＋ つまみを追加」の候補は、VfxDataValidator が TargetProperty の存在を判定するのと同じ列挙
    // (Prefab の全 Renderer の sharedMaterial)から、実在するシェーダープロパティだけを返す。
    // (Validator の AnyRendererHasProperty は DDrive.Runtime の private で Editor アセンブリから見えないため、同じ列挙を持つ。判定は変えない。)
    internal sealed class VfxTuneSource : ITuneSource
    {
        // パレットの項目(AddKinds の順 = この enum の順)。
        private enum Kind { Color, Size, Speed, Strength, Opacity, Lifetime, Free }

        private static readonly string[] KindNameList = { "色", "サイズ", "速度", "強さ", "不透明度", "寿命", "自由入力" };
        private static readonly string[] KindLabels = { "Color", "Size", "Speed", "Strength", "Opacity", "Lifetime", "Param" };
        private static readonly string[] Fields = { "Label", "Type", "TargetProperty", "Anim" };

        private readonly VfxData _data;
        private readonly Func<SceneVfxPreviewDriver> _driver;
        private readonly Func<DDrive.Foundation.Handle.Handle<VfxMarker>> _handle;

        // driver / handle は再生中の実体へ即時反映するためのもの(再生していなければ何もしない)。
        public VfxTuneSource(VfxData data, Func<SceneVfxPreviewDriver> driver, Func<DDrive.Foundation.Handle.Handle<VfxMarker>> handle)
        {
            _data = data;
            _driver = driver;
            _handle = handle;
            OwnerKey = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(data));
        }

        public UnityEngine.Object Owner => _data;
        public string OwnerKey { get; }
        public int Count => _data.Params != null ? _data.Params.Length : 0;
        public string Label(int index) => _data.Params[index].Label;
        public string TargetName(int index) => _data.Params[index].TargetProperty;

        public TuneValueKind KindOf(int index)
        {
            switch (_data.Params[index].Type)
            {
                case VfxParamType.Color: return TuneValueKind.Color;
                case VfxParamType.Float: return TuneValueKind.Float;
                case VfxParamType.Int: return TuneValueKind.Int;
                case VfxParamType.Vector: return TuneValueKind.Vector;
                case VfxParamType.Texture: return TuneValueKind.Texture;
                default: return TuneValueKind.Other;
            }
        }

        public ParamValue Value(int index) => _data.Params[index].Default;

        public void SetValue(int index, ParamValue value) => _data.Params[index].Default = value;

        public string Signature()
        {
            var ps = _data.Params;
            if (ps == null)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            for (var i = 0; i < ps.Length; i++)
            {
                sb.Append((int)ps[i].Type).Append(';');
            }

            return sb.ToString();
        }

        public int InferCategory(int index)
        {
            var p = _data.Params[index];
            return TuneCategories.Infer(KindOf(index), p.Label, p.TargetProperty);
        }

        public string ArrayPath => "Params";
        public IReadOnlyList<string> DetailFields => Fields;
        public IReadOnlyList<string> AddKinds => KindNameList;

        public void ApplyLive(string label, ParamValue value)
        {
            var driver = _driver();
            var handle = _handle();
            if (driver != null && driver.IsPlaying(handle))
            {
                driver.Manager.SetParam(handle, label, value);
            }
        }

        // ── 追加・削除 ──

        public TuneAddPlan PlanAdd(int kindIndex)
        {
            var kind = (Kind)kindIndex;
            if (kind == Kind.Free)
            {
                return new TuneAddPlan { Free = true };
            }

            var used = new HashSet<string>();
            var ps = _data.Params;
            for (var i = 0; ps != null && i < ps.Length; i++)
            {
                if (!string.IsNullOrEmpty(ps[i].TargetProperty))
                {
                    used.Add(ps[i].TargetProperty);
                }
            }

            var all = Enumerate(_data.Prefab);
            var found = ForKind(all, kind, null);
            var cands = ForKind(all, kind, used);
            var kindName = KindNameList[kindIndex];
            if (found.Count == 0)
            {
                return new TuneAddPlan { Message = _data.Prefab == null ? "Prefab が未設定です。先に Prefab を入れてください" : "この Prefab には" + kindName + "のプロパティがありません" };
            }

            if (cands.Count == 0)
            {
                return new TuneAddPlan { Message = "この Prefab の" + kindName + "のプロパティは、すべて追加済みです" };
            }

            return new TuneAddPlan { Candidates = cands };
        }

        public void Append(int kindIndex, TuneCandidate c)
        {
            AppendParam(new VfxParam
            {
                Label = UniqueLabel(KindLabels[kindIndex]),
                Type = ParamTypeOf(c.Kind),
                TargetProperty = c.Name,
                Default = c.Value,
            });
        }

        public void AppendFree() =>
            AppendParam(new VfxParam { Label = UniqueLabel("Param"), Type = VfxParamType.Float, TargetProperty = string.Empty, Default = ParamValue.Of(1f) });

        public void RemoveAt(int index)
        {
            var old = _data.Params;
            if (old == null || index < 0 || index >= old.Length)
            {
                return;
            }

            var arr = new VfxParam[old.Length - 1];
            for (int s = 0, d = 0; s < old.Length; s++)
            {
                if (s != index)
                {
                    arr[d++] = old[s];
                }
            }

            _data.Params = arr;
        }

        private void AppendParam(VfxParam p)
        {
            var old = _data.Params ?? new VfxParam[0];
            var arr = new VfxParam[old.Length + 1];
            Array.Copy(old, arr, old.Length);
            arr[old.Length] = p;
            _data.Params = arr;
        }

        private string UniqueLabel(string baseName)
        {
            var ps = _data.Params;
            var label = baseName;
            var n = 1;
            while (Exists(ps, label))
            {
                n++;
                label = baseName + n;
            }

            return label;
        }

        private static bool Exists(VfxParam[] ps, string label)
        {
            for (var i = 0; ps != null && i < ps.Length; i++)
            {
                if (ps[i].Label == label)
                {
                    return true;
                }
            }

            return false;
        }

        private static VfxParamType ParamTypeOf(TuneValueKind k)
        {
            switch (k)
            {
                case TuneValueKind.Color: return VfxParamType.Color;
                case TuneValueKind.Vector: return VfxParamType.Vector;
                case TuneValueKind.Texture: return VfxParamType.Texture;
                case TuneValueKind.Int: return VfxParamType.Int;
                default: return VfxParamType.Float;
            }
        }

        // ── 候補の列挙(材質のシェーダープロパティ) ──

        internal static List<TuneCandidate> Enumerate(GameObject prefab)
        {
            var all = new List<TuneCandidate>();
            var hidden = new List<TuneCandidate>();
            if (prefab == null)
            {
                return all;
            }

            var seen = new HashSet<string>();
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r.sharedMaterial == null || r.sharedMaterial.shader == null)
                {
                    continue;
                }

                var mat = r.sharedMaterial;
                var sh = mat.shader;
                for (var i = 0; i < sh.GetPropertyCount(); i++)
                {
                    var name = sh.GetPropertyName(i);
                    var t = sh.GetPropertyType(i);
                    if (!mat.HasProperty(name) || !seen.Add(name))
                    {
                        continue;
                    }

                    var c = new TuneCandidate { Name = name, Kind = KindOfShader(t), Value = ReadValue(mat, name, t) };
                    if ((sh.GetPropertyFlags(i) & ShaderPropertyFlags.HideInInspector) != 0)
                    {
                        hidden.Add(c);
                    }
                    else
                    {
                        all.Add(c);
                    }
                }
            }

            return all.Count > 0 ? all : hidden;
        }

        private static TuneValueKind KindOfShader(ShaderPropertyType t)
        {
            switch (t)
            {
                case ShaderPropertyType.Color: return TuneValueKind.Color;
                case ShaderPropertyType.Vector: return TuneValueKind.Vector;
                case ShaderPropertyType.Texture: return TuneValueKind.Texture;
                default: return TuneValueKind.Float;
            }
        }

        private static ParamValue ReadValue(Material mat, string name, ShaderPropertyType t)
        {
            switch (t)
            {
                case ShaderPropertyType.Color: return ParamValue.Of(mat.GetColor(name));
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range: return ParamValue.Of(mat.GetFloat(name));
                case ShaderPropertyType.Vector: return new ParamValue { Type = ParamValueType.Vector, VectorValue = mat.GetVector(name) };
                default: return ParamValue.Of(0f);
            }
        }

        // 種類に合う候補(色 = Color 型、それ以外 = Float 型で名前に語を含むもの)。
        private static List<TuneCandidate> ForKind(List<TuneCandidate> all, Kind kind, HashSet<string> used)
        {
            var list = new List<TuneCandidate>();
            foreach (var c in all)
            {
                if (used != null && used.Contains(c.Name))
                {
                    continue;
                }

                if (kind == Kind.Color)
                {
                    if (c.Kind == TuneValueKind.Color)
                    {
                        list.Add(c);
                    }

                    continue;
                }

                if (c.Kind == TuneValueKind.Float && TuneCategories.HasWord(c.Name, TuneCategories.Words[(int)kind]))
                {
                    list.Add(c);
                }
            }

            return list;
        }
    }
}
