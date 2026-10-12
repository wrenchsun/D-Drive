using System.Collections.Generic;
using System.Text;
using DDrive.Editor.Ux;
using DDrive.Foundation.Data;
using DDrive.Runtime.Presentation;
using DDrive.Runtime.Vfx;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Presentation
{
    // 1 本のトラックの Params(上書き値)を調整つまみ(TuneKnobs)に供給する(docs/1010 UX-5)。
    // Params[i] は参照先の VfxData.Params[i] にインデックスで対応する(PresentationManager.ApplyVfxTrackParams)。
    // つまみの名前 = Vfx トラックなら参照先 VFX の Params[i].Label、それ以外は「値 i」(HitStop の秒数など)。
    // 反映先が無い(参照先 VFX に対応する Params が無い)つまみは「反映されません」の表示になる(旧エディターの注記と同じ意味)。
    internal sealed class PresentationTrackTuneSource : ITuneSource
    {
        private static readonly string[] KindNames = { "数値", "整数", "色" };
        private static readonly string[] Fields = { "Type", "BoolValue", "StringValue", "CurveValue", "GradientValue" };

        private readonly PresentationData _data;
        private readonly int _track;
        private VfxData _vfx;
        private ulong _vfxId;

        public PresentationTrackTuneSource(PresentationData data, int trackIndex)
        {
            _data = data;
            _track = trackIndex;
            OwnerKey = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(data)) + "#" + trackIndex;
        }

        public UnityEngine.Object Owner => _data;
        public string OwnerKey { get; }

        private ParamValue[] Params
        {
            get
            {
                var tracks = _data.Tracks;
                return tracks != null && _track >= 0 && _track < tracks.Length ? tracks[_track].Params : null;
            }
        }

        public int Count => Params != null ? Params.Length : 0;

        private VfxData ReferencedVfx()
        {
            var tracks = _data.Tracks;
            if (tracks == null || _track < 0 || _track >= tracks.Length || tracks[_track].Kind != TrackKind.Vfx || !tracks[_track].Asset.IsAssigned)
            {
                _vfx = null;
                _vfxId = 0;
                return null;
            }

            var id = tracks[_track].Asset.Id;
            if (_vfx == null || _vfxId != id)
            {
                _vfxId = id;
                _vfx = PresentationTrackKindMapping.FindAssetById(typeof(VfxData), id) as VfxData;
            }

            return _vfx;
        }

        public string Label(int index)
        {
            var vfx = ReferencedVfx();
            if (vfx != null && vfx.Params != null && index < vfx.Params.Length && !string.IsNullOrEmpty(vfx.Params[index].Label))
            {
                return vfx.Params[index].Label;
            }

            var tracks = _data.Tracks;
            if (tracks != null && _track >= 0 && _track < tracks.Length && tracks[_track].Kind == TrackKind.HitStop && index == 0)
            {
                return "止める秒数";
            }

            return "値 " + index;
        }

        public string TargetName(int index)
        {
            var tracks = _data.Tracks;
            if (tracks != null && _track >= 0 && _track < tracks.Length && tracks[_track].Kind == TrackKind.Vfx)
            {
                var vfx = ReferencedVfx();
                return vfx != null && vfx.Params != null && index < vfx.Params.Length ? vfx.Params[index].Label : null;
            }

            return "Params[" + index + "]";
        }

        public TuneValueKind KindOf(int index)
        {
            switch (Params[index].Type)
            {
                case ParamValueType.Color: return TuneValueKind.Color;
                case ParamValueType.Float: return TuneValueKind.Float;
                case ParamValueType.Int: return TuneValueKind.Int;
                case ParamValueType.Vector: return TuneValueKind.Vector;
                case ParamValueType.Object: return TuneValueKind.Texture;
                default: return TuneValueKind.Other;
            }
        }

        public ParamValue Value(int index) => Params[index];

        public void SetValue(int index, ParamValue value) => Params[index] = value;

        public string Signature()
        {
            var ps = Params;
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

        public int InferCategory(int index) => TuneCategories.Infer(KindOf(index), Label(index), TargetName(index));

        public string ArrayPath => "Tracks.Array.data[" + _track + "].Params";
        public IReadOnlyList<string> DetailFields => Fields;
        public IReadOnlyList<string> AddKinds => KindNames;

        public TuneAddPlan PlanAdd(int kind)
        {
            var value = kind == 2 ? ParamValue.Of(Color.white) : kind == 1 ? ParamValue.Of(0) : ParamValue.Of(0f);
            return new TuneAddPlan
            {
                Candidates = new List<TuneCandidate>
                {
                    new TuneCandidate { Name = KindNames[kind], Kind = kind == 2 ? TuneValueKind.Color : kind == 1 ? TuneValueKind.Int : TuneValueKind.Float, Value = value },
                },
            };
        }

        public void Append(int kind, TuneCandidate candidate)
        {
            var list = new List<ParamValue>(Params ?? new ParamValue[0]) { candidate.Value };
            SetArray(list.ToArray());
        }

        public void AppendFree() => Append(0, new TuneCandidate { Value = ParamValue.Of(0f) });

        public void RemoveAt(int index)
        {
            var list = new List<ParamValue>(Params ?? new ParamValue[0]);
            if (index >= 0 && index < list.Count)
            {
                list.RemoveAt(index);
                SetArray(list.ToArray());
            }
        }

        private void SetArray(ParamValue[] values)
        {
            var tracks = _data.Tracks;
            if (tracks == null || _track < 0 || _track >= tracks.Length)
            {
                return;
            }

            var t = tracks[_track];
            t.Params = values;
            tracks[_track] = t;
        }

        // 再生中の実体へ即時反映する先は無い(統合プレビューは次の再生から反映)。
        public void ApplyLive(string label, ParamValue value) { }
    }
}
