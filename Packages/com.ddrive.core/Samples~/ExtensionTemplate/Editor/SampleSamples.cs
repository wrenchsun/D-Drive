using DDrive.Editor.Extension;
using DDrive.Foundation.Data;
using UnityEngine;

namespace SampleExtension.Editor
{
    // 空状態の「サンプルを作る」(docs/1014 §2.4)。保存していない Data を返せば、D-Drive が命名規則どおり(SMP_Sample_…)に保存し、
    // カタログと Addressables に登録する。同じ表示名のサンプルが既にあれば呼ばれず、それが選ばれる。
    public sealed class SampleSampleProvider : ISampleProvider
    {
        public string KindKey => SampleKind.Key;

        public string DisplayName => "Sample";

        public AssetDataBase CreateSample(string folder)
        {
            // folder は素材(音・画像・Prefab など)を置く場所。この種別は素材が無いので使わない。
            var data = ScriptableObject.CreateInstance<SampleData>();
            data.Intensity = 1f;
            data.Duration = 1.5f;
            return data;
        }
    }
}
