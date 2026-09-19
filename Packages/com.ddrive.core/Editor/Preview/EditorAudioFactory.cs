using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Audio;
using UnityEngine;

namespace DDrive.Editor.Preview
{
    // [11_tasks.md] 5-4 実装メモ — シーン内で SE を鳴らす実 AudioManager を用意する定型手順
    // (PoolService + テンプレ AudioSource(非アクティブ) + AudioListener の有無確認)を
    // SceneAnimPreviewDriver.EnsureManagers と ScenePresentationPreviewDriver(5-4)で共用するために
    // 切り出した(コピペ防止)。生成する GameObject 構成・命名は元の EnsureManagers と同一。
    public static class EditorAudioFactory
    {
        // pool: 呼び出し側が用意した(Instance 親を設定済みの)PoolService。他 Manager(ModelsManager 等)と
        // 共有する場合はそのインスタンスをそのまま渡すこと(元の EnsureManagers と同じく 1 つの Pool を共用する)。
        // parent: テンプレート AudioSource / AudioListener の配置先(DontSave のプレビュールート)。
        public static AudioManager Create(PoolService pool, Transform parent, AssetRegistry registry)
        {
            var template = new GameObject("SeSourceTemplate");
            template.transform.SetParent(parent, false);
            template.AddComponent<AudioSource>();
            template.SetActive(false);

            if (Object.FindFirstObjectByType<AudioListener>() == null)
            {
                parent.gameObject.AddComponent<AudioListener>();
            }

            return new AudioManager(pool, registry, template);
        }
    }
}
