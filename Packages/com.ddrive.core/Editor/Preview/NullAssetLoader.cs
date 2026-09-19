using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DDrive.Foundation.Loader;

namespace DDrive.Editor.Preview
{
    // プレビューは Data インスタンスを直接渡して再生する(PlaySeData/PlayBgmData)ため、
    // Registry 経由の Addressables ロードは発生しない。Manager 構築要件を満たすためだけの実装。
    internal sealed class NullAssetLoader : IAssetLoader
    {
        public UniTask<T> LoadAsync<T>(string address, CancellationToken ct) where T : UnityEngine.Object
            => UniTask.FromResult<T>(null);

        public void Release(string address)
        {
        }

        public UniTask PreloadAsync(IEnumerable<string> addresses, IProgress<float> progress) => UniTask.CompletedTask;
    }
}
