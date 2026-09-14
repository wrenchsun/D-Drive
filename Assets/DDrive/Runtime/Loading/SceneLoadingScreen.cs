using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DDrive.Runtime.Loading
{
    // [11_tasks.md] 5-7 AC「ロード画面で Preload 完了」の最小確認用コンポーネント。
    // 専用の Data/CanvasData は起こさず(スコープ超過のため)、既存の ScenePreload API(進捗 0〜1 + 完了)を
    // そのまま UnityEngine.UI の Slider/Text に反映するだけの薄いラッパー。実運用の UI は
    // デザイナーが Canvas/UiManager(4-1 以降)で組み替えてよい([28_manual_verification_phase5.md] 5-7 節参照)。
    public sealed class SceneLoadingScreen : MonoBehaviour
    {
        [Tooltip("集計済みの Preload リスト(ScenePreloadGenerator が GameData/Preload 配下に生成する)")]
        [SerializeField] private ScenePreloadList preloadList;

        [Tooltip("進捗表示(0〜1)。未設定なら表示しない")]
        [SerializeField] private Slider progressSlider;

        [Tooltip("進捗テキスト(任意)。未設定なら表示しない")]
        [SerializeField] private Text progressText;

        [Tooltip("有効化時に自動で Preload を開始する")]
        [SerializeField] private bool autoStartOnEnable = true;

        [Tooltip("Preload 完了時に呼ぶ(次シーンへの遷移トリガ等に使う)")]
        [SerializeField] private UnityEvent onPreloadComplete;

        public bool IsDone { get; private set; }
        public float Progress { get; private set; }

        private IProgress<float> _progressReporter;

        // P5 レビュー対応(2026-09-14): RunAsync が実際に ScenePreload.RunAsync を呼んだ(= 参照を確保した)
        // ときだけ立てる。OnDisable で無条件に Release すると、RunAsync が一度も走っていない
        // (autoStartOnEnable=false で手動呼び出しも無い等)場合に他インスタンスの参照カウントを
        // 誤って減らしてしまう。
        private bool _preloadStarted;

        private void Awake()
        {
            _progressReporter = new Progress<float>(OnProgressChanged);
        }

        private void OnEnable()
        {
            if (autoStartOnEnable)
            {
                RunAsync().Forget();
            }
        }

        private void OnDisable()
        {
            // 例外で止めない(CLAUDE.md §0-4): 破棄済みシーンで参照を握りっぱなしにしない後始末。
            // RunAsync が実行されていない場合は Release しない(上記フィールドの説明参照)。
            if (_preloadStarted)
            {
                ScenePreload.Release(preloadList);
                _preloadStarted = false;
            }
        }

        public async UniTaskVoid RunAsync()
        {
            IsDone = false;
            Progress = 0f;
            OnProgressChanged(0f);
            _preloadStarted = true;

            await ScenePreload.RunAsync(preloadList, _progressReporter);

            IsDone = true;
            Progress = 1f;
            OnProgressChanged(1f);
            onPreloadComplete?.Invoke();
        }

        private void OnProgressChanged(float value)
        {
            Progress = value;

            if (progressSlider != null)
            {
                progressSlider.value = value;
            }

            if (progressText != null)
            {
                progressText.text = $"Loading... {Mathf.RoundToInt(value * 100f)}%";
            }
        }
    }
}
