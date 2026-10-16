using System;
using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;

namespace ExternalContract.Tests
{
    // 調整つまみ 1 本(ExternalSampleTuneSource が TuneKnobs に供給する)。
    [Serializable]
    public sealed class ExternalSampleKnob
    {
        public string Name = "Knob";
        public float Value = 0.5f;
    }

    // ScriptableObject として保存するので、クラス名 = ファイル名にする(MonoScript の解決条件)。
    // X-3(2026-10-16): UxEditorWindow<T> 派生の窓(ExternalSampleEditorWindow)で全部品を使うため、欄を足した(追加のみ)。
    [AssetIdDefinition(AssetType.Extension, typeof(ExternalSampleMarker), "SMPID", KindKey = ExternalSampleKind.Key)]
    public sealed class ExternalSampleData : AssetDataBase
    {
        public float Intensity = 1f;
        public bool Loop;
        public float Duration = 1f;
        public float FadeOutSec = 0.2f;
        public List<ExternalSampleKnob> Knobs = new List<ExternalSampleKnob>();
    }
}
