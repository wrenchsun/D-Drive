using System.IO;
using DDrive.Runtime.Audio;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Audio
{
    // 非破壊トリミングの適用(Sources → Clips のベイク)。SeDataEditor と AudioEditor(1-7)で共用。
    //
    // トリム結果は SeData と同じフォルダに `<SeData名>_Trimmed_<i>.wav` として書き出し、
    // Unity のインポーターに読ませて Clips に割り当てる。
    // ※当初はサブアセット(AudioClip.Create + AddObjectToAsset)方式だったが、ランタイム生成の
    //   AudioClip は PCM データがシリアライズされず、リインポートで空になるため .wav 方式に変更した。
    public static class SeTrimApplier
    {
        public static bool Apply(SeData data)
        {
            if (data == null || data.Sources == null || data.Sources.Length == 0)
            {
                return false;
            }

            var path = AssetDatabase.GetAssetPath(data);
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning("[DDrive] SeData must be saved as an asset before applying trim (trimmed .wav files are written next to it).");
                return false;
            }

            var directory = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var baseName = Path.GetFileNameWithoutExtension(path);

            var newClips = new AudioClip[data.Sources.Length];
            for (var i = 0; i < data.Sources.Length; i++)
            {
                var entry = data.Sources[i];
                var wavPath = $"{directory}/{baseName}_Trimmed_{i}.wav";

                if (entry.Source == null ||
                    !AudioClipTrimUtility.TryExtractTrimmedSamples(
                        entry.Source, entry.TrimStartSec, entry.TrimEndSec,
                        out var samples, out var channels, out var frequency))
                {
                    if (entry.Source != null)
                    {
                        Debug.LogWarning($"[DDrive] Sources[{i}] '{entry.Source.name}' のトリム範囲が不正か PCM を読めないためスキップしました。Clips[{i}] は空になります。");
                    }

                    // スキップした index の古いトリム結果が残ると「昔の音が鳴り続ける」ため削除する。
                    if (AssetDatabase.LoadAssetAtPath<AudioClip>(wavPath) != null)
                    {
                        AssetDatabase.DeleteAsset(wavPath);
                    }

                    continue;
                }

                File.WriteAllBytes(wavPath, WavWriter.Encode(samples, channels, frequency));
                AssetDatabase.ImportAsset(wavPath, ImportAssetOptions.ForceUpdate);
                newClips[i] = AssetDatabase.LoadAssetAtPath<AudioClip>(wavPath);
                if (newClips[i] == null)
                {
                    // 他の保留中の変更(Addressables 設定の保存など)が同じ Import に相乗りすると、上書き直後の
                    // ロードが null になることがある(2026-09-09 に順序依存のテストで再現)。Refresh してもう一度引く。
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    newClips[i] = AssetDatabase.LoadAssetAtPath<AudioClip>(wavPath);
                }
            }

            DeleteStaleTrimFiles(directory, baseName, data.Sources.Length);

            data.Clips = newClips;
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            return true;
        }

        // Sources を減らして再適用した場合に、余った旧 _Trimmed_ ファイルを削除する。
        private static void DeleteStaleTrimFiles(string directory, string baseName, int currentCount)
        {
            if (string.IsNullOrEmpty(directory))
            {
                return;
            }

            for (var i = currentCount; i < currentCount + 16; i++)
            {
                var stale = $"{directory}/{baseName}_Trimmed_{i}.wav";
                if (AssetDatabase.LoadAssetAtPath<AudioClip>(stale) != null)
                {
                    AssetDatabase.DeleteAsset(stale);
                }
            }
        }
    }
}
