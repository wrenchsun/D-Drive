using DDrive.Editor.Versioning;
using DDrive.Runtime.CameraShake;
using DDrive.Runtime.Haptics;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.CameraFx
{
    // 確認用サンプル(docs/1010 UX-10 / D-10)。雛形(CameraFxPresets)10 種をそれぞれ適用した CameraShakeData 10 個と HapticsData 10 個。
    // Data 切替(◀ [名前 ▾] ▶)で 10 件を行き来して、波形グラフが雛形ごとに変わることを確かめるのに使う。
    // 指定のフォルダ(Assets/ 配下)に作る。呼び出し側が使い終わったらフォルダごと消す(実データ Assets/GameData には作らない。Addressables にも登録しない)。
    // スクリーンショット・テスト・見通しの確認用。
    internal static class CameraFxSample
    {
        public const ulong BaseId = 0xDDB00000;

        private static readonly string[] JapaneseNames =
        {
            "軽い一撃", "持続する揺れ", "心拍", "爆発", "被弾(小)", "被弾(大)", "着地", "地震", "警報", "エンジン",
        };

        public sealed class Result
        {
            public CameraShakeData[] Shakes;
            public HapticsData[] Haptics;
            public string Folder;
        }

        public static Result Build(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                var parent = System.IO.Path.GetDirectoryName(folder).Replace((char)92, (char)47);
                AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
            }

            var kinds = CameraFxPresets.All;
            var shakes = new CameraShakeData[kinds.Length];
            var haptics = new HapticsData[kinds.Length];
            for (var i = 0; i < kinds.Length; i++)
            {
                var label = CameraFxPresets.Label(kinds[i]);

                var shake = ScriptableObject.CreateInstance<CameraShakeData>();
                shake.Id = BaseId + 1 + (ulong)i;
                shake.DisplayName = $"{JapaneseNames[i]}({label})";
                shake.Category = "Sample";
                CameraFxPresets.ApplyShakePreset(shake, kinds[i]);
                AssetDatabase.CreateAsset(shake, $"{folder}/SHAKE_{label}_Sample.asset");
                shakes[i] = shake;

                var haptic = ScriptableObject.CreateInstance<HapticsData>();
                haptic.Id = BaseId + 0x100 + (ulong)i;
                haptic.DisplayName = $"{JapaneseNames[i]}({label})";
                haptic.Category = "Sample";
                CameraFxPresets.ApplyHapticsPreset(haptic, kinds[i]);
                AssetDatabase.CreateAsset(haptic, $"{folder}/HAPTIC_{label}_Sample.asset");
                haptics[i] = haptic;
            }

            DDriveAssetSave.SaveAllSuppressed();
            return new Result { Shakes = shakes, Haptics = haptics, Folder = folder };
        }
    }
}
