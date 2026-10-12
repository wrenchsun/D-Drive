using System;
using System.Collections.Generic;
using System.Text;
using DDrive.Editor.Ux;
using DDrive.Foundation.Data;
using DDrive.Runtime.Material;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DDrive.Editor.Materials
{
    // MaterialData.Specific(シェーダー固有パラメータ)を調整つまみ(TuneKnobs)に供給する(docs/1010 UX-8)。
    // 分類(チップ)の語彙は固定の 2 つ(共通チャンネル / 描画ステート = Common を上書きしてしまう名前)+ 接頭辞ごと(_ToonRamp / _ToonShade → Toon)+ その他。
    //   共通チャンネル・描画ステート: MaterialCommonNaming.Classify の結果(Specific に紛れていると Common の値を上書きするので、見つけやすいよう別のチップにする)
    //   接頭辞ごと: 固有の名前の先頭の語(_ToonShadeColor → Toon、_Outline_Width → Outline)。同じ接頭辞が 2 件以上あるものだけチップにする
    //   その他: 上のどれにも当たらないもの(付随名・予約名を含む)
    // 「＋ つまみを追加」の候補は、MaterialSpecificResolver.Resolve(シェーダーの固有プロパティの既定値付き列挙)のうち Specific にまだ無いもの。
    // 外部契約(docs/42 §5.14 E-10〜E-15: _Toon* の扱い・MaterialCommonNaming の分類・Specific の Merge)は変えない(ここは読み取りと追加のしかただけ)。
    internal sealed class MaterialTuneSource : ITuneSource
    {
        private const int FixedCategories = 2; // 0 共通チャンネル / 1 描画ステート
        private const int MinGroupSize = 2;    // 接頭辞のチップにする最小件数

        private static readonly string[] Fields = { "Property", "Value" };
        private static readonly string[] KindNameList = { "色", "数値", "テクスチャ", "ベクトル", "自由入力" };
        private const int KindColor = 0;
        private const int KindFloat = 1;
        private const int KindTexture = 2;
        private const int KindVector = 3;
        private const int KindFree = 4;

        private readonly MaterialData _data;
        private string _sig = null;
        private List<string> _names = new List<string>();
        private int[] _category = Array.Empty<int>();

        public MaterialTuneSource(MaterialData data)
        {
            _data = data;
            OwnerKey = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(data));
        }

        public UnityEngine.Object Owner => _data;
        public string OwnerKey { get; }
        public int Count => _data.Specific != null ? _data.Specific.Length : 0;
        public string Label(int index) => _data.Specific[index].Property;

        // 反映先 = シェーダーに実在するプロパティ名。無ければ空(「反映されません」の表示)。
        public string TargetName(int index)
        {
            var name = _data.Specific[index].Property;
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            return _data.Shader != null && _data.Shader.FindPropertyIndex(name) >= 0 ? name : string.Empty;
        }

        public TuneValueKind KindOf(int index)
        {
            switch (_data.Specific[index].Value.Type)
            {
                case ParamValueType.Color: return TuneValueKind.Color;
                case ParamValueType.Float: return TuneValueKind.Float;
                case ParamValueType.Int: return TuneValueKind.Int;
                case ParamValueType.Vector: return TuneValueKind.Vector;
                case ParamValueType.Object: return TuneValueKind.Texture;
                default: return TuneValueKind.Other;
            }
        }

        public ParamValue Value(int index) => _data.Specific[index].Value;

        public void SetValue(int index, ParamValue value) => _data.Specific[index].Value = value;

        public string ArrayPath => "Specific";
        public IReadOnlyList<string> DetailFields => Fields;
        public IReadOnlyList<string> AddKinds => KindNameList;
        public bool WideNames => true;

        // 再生中の実体は無い(Material の見た目は編集のたびにエディター側が共有 Material を作り直して反映する)。
        public void ApplyLive(string label, ParamValue value) { }

        public bool TryRange(int index, out float min, out float max)
        {
            min = 0f;
            max = 1f;
            var shader = _data.Shader;
            var name = _data.Specific[index].Property;
            if (shader == null || string.IsNullOrEmpty(name))
            {
                return false;
            }

            var p = shader.FindPropertyIndex(name);
            if (p < 0 || shader.GetPropertyType(p) != ShaderPropertyType.Range)
            {
                return false;
            }

            var limits = shader.GetPropertyRangeLimits(p);
            min = limits.x;
            max = limits.y;
            return true;
        }

        // ── 分類 ──

        public IReadOnlyList<string> CategoryNames
        {
            get
            {
                EnsureCategories();
                return _names;
            }
        }

        public int InferCategory(int index)
        {
            EnsureCategories();
            return index >= 0 && index < _category.Length ? _category[index] : _names.Count - 1;
        }

        // 並び・型・名前・シェーダーが変わったか(変わると TuneKnobs が作り直す)。
        public string Signature()
        {
            var sig = BuildSignature();
            if (sig != _sig)
            {
                _sig = sig;
                Recompute();
            }

            return sig;
        }

        private string BuildSignature()
        {
            var sb = new StringBuilder();
            sb.Append(_data.Shader != null ? _data.Shader.GetInstanceID() : 0).Append('|');
            var sp = _data.Specific;
            for (var i = 0; sp != null && i < sp.Length; i++)
            {
                sb.Append(sp[i].Property).Append(':').Append((int)sp[i].Value.Type).Append(';');
            }

            return sb.ToString();
        }

        private void EnsureCategories()
        {
            if (_sig == null)
            {
                Signature();
            }
        }

        private void Recompute()
        {
            var sp = _data.Specific;
            var n = sp != null ? sp.Length : 0;
            var kinds = new MaterialCommonNaming.Kind[n];
            var prefixes = new string[n];
            var groupCount = new Dictionary<string, int>();
            for (var i = 0; i < n; i++)
            {
                var name = sp[i].Property;
                kinds[i] = MaterialCommonNaming.Classify(name);
                if (kinds[i] == MaterialCommonNaming.Kind.Specific)
                {
                    prefixes[i] = PrefixOf(name);
                    groupCount.TryGetValue(prefixes[i], out var c);
                    groupCount[prefixes[i]] = c + 1;
                }
            }

            var names = new List<string> { "共通チャンネル", "描画ステート" };
            var groupIndex = new Dictionary<string, int>();
            for (var i = 0; i < n; i++)
            {
                var p = prefixes[i];
                if (p != null && groupCount[p] >= MinGroupSize && !groupIndex.ContainsKey(p))
                {
                    groupIndex[p] = names.Count;
                    names.Add(p);
                }
            }

            var other = names.Count;
            names.Add("その他");

            var cats = new int[n];
            for (var i = 0; i < n; i++)
            {
                switch (kinds[i])
                {
                    case MaterialCommonNaming.Kind.CommonChannel:
                        cats[i] = 0;
                        break;
                    case MaterialCommonNaming.Kind.RenderState:
                        cats[i] = 1;
                        break;
                    case MaterialCommonNaming.Kind.Specific:
                        cats[i] = groupIndex.TryGetValue(prefixes[i], out var g) ? g : other;
                        break;
                    default:
                        cats[i] = other;
                        break;
                }
            }

            _names = names;
            _category = cats;
        }

        // 固有名の先頭の語(_ToonShadeColor → Toon、_Outline_Width → Outline、_RimPower → Rim)。先頭の _ を除き、
        // 区切り(_ / 数字 / 小文字→大文字の境目〔2 文字目以降〕)までを返す。取れなければ名前そのもの。
        internal static string PrefixOf(string property)
        {
            if (string.IsNullOrEmpty(property))
            {
                return string.Empty;
            }

            var start = 0;
            while (start < property.Length && property[start] == '_')
            {
                start++;
            }

            if (start >= property.Length)
            {
                return property;
            }

            var end = start + 1;
            while (end < property.Length)
            {
                var c = property[end];
                if (c == '_' || char.IsDigit(c) || (char.IsUpper(c) && end - start >= 2 && char.IsLower(property[end - 1])))
                {
                    break;
                }

                end++;
            }

            return property.Substring(start, end - start);
        }

        // ── 追加・削除 ──

        // 候補 = シェーダーの固有プロパティ(Resolve)のうち、Specific にまだ無いもの。種類(色 / 数値 / テクスチャ / ベクトル)で分ける。
        public TuneAddPlan PlanAdd(int kind)
        {
            if (kind == KindFree)
            {
                return new TuneAddPlan { Free = true };
            }

            var shader = _data.Shader;
            if (shader == null)
            {
                return new TuneAddPlan { Message = "Shader が未設定です。先にシェーダーを選んでください" };
            }

            var resolved = MaterialSpecificResolver.Resolve(shader);
            var kindName = KindNameList[kind];
            var any = false;
            var list = new List<TuneCandidate>();
            for (var i = 0; i < resolved.Count; i++)
            {
                var tk = KindOfValue(resolved[i].Value);
                if (!Fits(kind, tk))
                {
                    continue;
                }

                any = true;
                if (!HasProperty(resolved[i].Property))
                {
                    list.Add(new TuneCandidate { Name = resolved[i].Property, Kind = tk, Value = resolved[i].Value });
                }
            }

            if (!any)
            {
                return new TuneAddPlan { Message = "このシェーダーには" + kindName + "の固有プロパティがありません" };
            }

            if (list.Count == 0)
            {
                return new TuneAddPlan { Message = "このシェーダーの" + kindName + "の固有プロパティは、すべて追加済みです" };
            }

            return new TuneAddPlan { Candidates = list };
        }

        public void Append(int kind, TuneCandidate candidate)
        {
            AppendParam(new ShaderParam { Property = candidate.Name, Value = candidate.Value });
        }

        public void AppendFree()
        {
            AppendParam(new ShaderParam { Property = string.Empty, Value = ParamValue.Of(0f) });
        }

        public void RemoveAt(int index)
        {
            var old = _data.Specific;
            if (old == null || index < 0 || index >= old.Length)
            {
                return;
            }

            var arr = new ShaderParam[old.Length - 1];
            for (int s = 0, d = 0; s < old.Length; s++)
            {
                if (s != index)
                {
                    arr[d++] = old[s];
                }
            }

            _data.Specific = arr;
        }

        private void AppendParam(ShaderParam p)
        {
            var old = _data.Specific ?? Array.Empty<ShaderParam>();
            var arr = new ShaderParam[old.Length + 1];
            Array.Copy(old, arr, old.Length);
            arr[old.Length] = p;
            _data.Specific = arr;
        }

        private bool HasProperty(string name)
        {
            var sp = _data.Specific;
            for (var i = 0; sp != null && i < sp.Length; i++)
            {
                if (sp[i].Property == name)
                {
                    return true;
                }
            }

            return false;
        }

        private static TuneValueKind KindOfValue(ParamValue v)
        {
            switch (v.Type)
            {
                case ParamValueType.Color: return TuneValueKind.Color;
                case ParamValueType.Int: return TuneValueKind.Int;
                case ParamValueType.Vector: return TuneValueKind.Vector;
                case ParamValueType.Object: return TuneValueKind.Texture;
                default: return TuneValueKind.Float;
            }
        }

        private static bool Fits(int kind, TuneValueKind v)
        {
            switch (kind)
            {
                case KindColor: return v == TuneValueKind.Color;
                case KindFloat: return v == TuneValueKind.Float || v == TuneValueKind.Int;
                case KindTexture: return v == TuneValueKind.Texture;
                case KindVector: return v == TuneValueKind.Vector;
                default: return false;
            }
        }
    }
}
