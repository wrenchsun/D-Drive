using System;
using System.Collections.Generic;
using DDrive.Runtime.Cutscene;
using UnityEngine;

namespace DDrive.Runtime.Viewing
{
    // [51_tdrive_integration.md] §4.4(FC-3) — 「今の視点カメラ」を返す静的ファサード(Bind 不要・Manager なし)。
    //
    // 解決順: (1) 登録された IViewProvider(優先度の降順、同優先度は登録順)→ (2) カットシーンが Camera.main を
    // 駆動中なら Camera.main の現在姿勢(Source = Cutscene)→ (3) Camera.main(Source = MainCamera)→ (4) false(Source = None、警告なし)。
    //
    // 実行順の契約([26_timeline.md] §4.6.5): カットシーン中のカット姿勢は DDriveCutsceneCameraApplier(実行順 1000)が
    // LateUpdate で Camera.main に書く。そのフレームのカット姿勢が欲しいときは、LateUpdate で、実行順が
    // DDriveCutsceneCameraApplier.ExecutionOrder より後のコンポーネントから呼ぶ。
    // 定常経路: 割り当てなし(List を for で走査・struct を返す)。結果はキャッシュしない(呼び出し時点のカメラの姿勢)。
    public static class ViewCamera
    {
        private struct Entry
        {
            public IViewProvider Provider;
            public int Priority;
        }

        private static readonly List<Entry> Entries = new List<Entry>(4);

        // ドメインリロード無効(Enter Play Mode Options)でも登録が残らないようにする。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Entries.Clear();
        }

        // 優先度の高い順に問い合わせる。同じ優先度は登録順。同じ provider の再登録は優先度を更新する(登録順は最後になる)。
        public static void Register(IViewProvider provider, int priority = 0)
        {
            if (provider == null)
            {
                return;
            }

            Unregister(provider);

            var index = Entries.Count;
            for (var i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].Priority < priority)
                {
                    index = i;
                    break;
                }
            }

            Entries.Insert(index, new Entry { Provider = provider, Priority = priority });
        }

        public static void Unregister(IViewProvider provider)
        {
            if (provider == null)
            {
                return;
            }

            for (var i = Entries.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(Entries[i].Provider, provider))
                {
                    Entries.RemoveAt(i);
                }
            }
        }

        public static bool TryGetCurrent(Transform subject, out ViewPose pose)
        {
            // IViewProvider.TryGetView の中から Register / Unregister されても例外にならない(毎回 Count を読む)。
            for (var i = 0; i < Entries.Count; i++)
            {
                var provider = Entries[i].Provider;

                // 破棄済みの MonoBehaviour(Unity の null)は飛ばして取り除く。
                if (provider is UnityEngine.Object unityObject && unityObject == null)
                {
                    Entries.RemoveAt(i);
                    i--;
                    continue;
                }

                try
                {
                    if (provider.TryGetView(subject, out pose))
                    {
                        return true;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            var cam = Camera.main;
            if (cam == null)
            {
                pose = default;
                return false;
            }

            var driving = cam.TryGetComponent(out DDriveCutsceneCameraApplier applier) && applier.IsDriving;
            var t = cam.transform;
            pose = new ViewPose(
                t.position,
                t.rotation,
                cam.fieldOfView,
                driving ? ViewSource.Cutscene : ViewSource.MainCamera,
                cam);
            return true;
        }
    }
}
