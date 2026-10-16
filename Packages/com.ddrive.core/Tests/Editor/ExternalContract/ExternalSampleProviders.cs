using System.Collections.Generic;
using DDrive.Editor.Extension;
using DDrive.Foundation.Data;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ExternalContract.Tests
{
    // [1014_extension_platform.md] §2.4(X-4) / [42_distribution.md] §5.14 E-28 — 持ち込み先が書く ISampleProvider / IPreviewSceneProvider /
    // IManualPageProvider を模したテスト用のダミー(ExternalSampleData = 種別 "Sample" 用)。テスト用アセンブリなので
    // ExtensionProviders.IncludeTestAssemblies = true のときだけ発見される。

    // テストが差し替える状態(ダミーは引数なしコンストラクタしか持てないので static で受け取る)。
    public static class ExternalProviderTestState
    {
        public static string GameDataRoot;
        public static string ScenePath;
        public static string ManualPath;
        public static int SetupCount;
        public static int CreateSampleCount;
        public static string LastSampleFolder;
        public static bool SampleThrows;

        public static void Reset()
        {
            GameDataRoot = null;
            ScenePath = null;
            ManualPath = null;
            SetupCount = 0;
            CreateSampleCount = 0;
            LastSampleFolder = null;
            SampleThrows = false;
        }
    }

    public sealed class ExternalSampleSampleProvider : ISampleProvider
    {
        public string KindKey => ExternalSampleKind.Key;
        public string DisplayName => "Sample";

        public AssetDataBase CreateSample(string folder)
        {
            ExternalProviderTestState.CreateSampleCount++;
            ExternalProviderTestState.LastSampleFolder = folder;
            if (ExternalProviderTestState.SampleThrows)
            {
                throw new System.InvalidOperationException("sample boom");
            }

            // 保存していない Data を返す(D-Drive が命名規則どおりに保存・カタログ・Addressables 登録する)。
            var d = ScriptableObject.CreateInstance<ExternalSampleData>();
            d.Intensity = 2f;
            d.Knobs.Add(new ExternalSampleKnob { Name = "Size", Value = 0.3f });
            return d;
        }
    }

    // 同じ KindKey を名乗る 2 つ目(型のフルネームが後ろ。重複の警告 + 先勝ち)。
    public sealed class ExternalSampleSampleProviderDuplicate : ISampleProvider
    {
        public string KindKey => ExternalSampleKind.Key;
        public string DisplayName => "Sample (dup)";
        public AssetDataBase CreateSample(string folder) => null;
    }

    public sealed class ExternalSamplePreviewSceneProvider : IPreviewSceneProvider
    {
        public string KindKey => ExternalSampleKind.Key;
        public string ScenePath => ExternalProviderTestState.ScenePath;

        public void Setup(Scene scene) => ExternalProviderTestState.SetupCount++;
    }

    public sealed class ExternalSampleManualPages : IManualPageProvider
    {
        public IReadOnlyList<(string key, string path, string title)> Pages => new[]
        {
            ("external-sample", ExternalProviderTestState.ManualPath ?? "Assets/Tests/DDriveTemp/none.html", "Sample Editor"),
            ("index", "Assets/Tests/DDriveTemp/shadow.html", "組み込みと同じ key(組み込みが先)"),
        };
    }
}
