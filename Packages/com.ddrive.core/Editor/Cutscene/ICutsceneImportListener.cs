using System;
using System.Collections.Generic;
using DDrive.Runtime.Cutscene;
using UnityEngine.Timeline;

namespace DDrive.Editor.Cutscene
{
    // [51_tdrive_integration.md] §4.6 / [26_timeline.md] §5.2(FC-5、2026-10-03) — カットシーン取り込み完了の公開イベント。
    //
    // `CutsceneImportService` がショット 1 つ分の CutsceneData / TimelineAsset を作る・更新し終えるたびに、
    // 発見された全リスナーの `OnCutsceneShotImported` を呼ぶ。外部パッケージ(別アセンブリ)が「取り込み後に
    // .playable へトラックを足す」「Bindings へ binding を追記する」といった後処理をするための汎用の拡張点。
    //
    // 発見: `TypeCache`(`DDriveMigrationRunner` と同じ方式)。public な非 abstract 型 + public な引数なし
    // コンストラクタが必須。`DDrive.Tests*` で始まるアセンブリの実装は除外する(テスト用ダミーが実運用に混ざらない)。
    // 呼び出し順: `Order` 昇順、同値は型のフルネーム順。
    //
    // 呼び出しの時点: `ProcessShot` が Bindings / SourceFbxGuids / Timeline を確定し `SetDirty` した後、保存の前。
    // リスナーは `Result.Data` / `Result.Timeline` を直接書き換えてよい(`EditorUtility.SetDirty` は D-Drive が
    // 呼ぶが、リスナー側でも呼んで構わない)。**全リスナーの呼び出しが終わった後に D-Drive が 1 回だけ保存する**
    // (リスナーは保存を呼ばなくてよい)。`CutsceneImportService.ProcessPaths` / `ScanAll`(手動の再取り込み)・
    // FBX 取り込み(`CutsceneFbxPostprocessor`)のいずれも同じ経路を通る。
    //
    // リスナーが投げた例外は個別に捕まえて `Debug.LogException` し、次のリスナーと取り込み自体は継続する。
    //
    // 再取り込みでは同じショットで再び呼ばれる(`IsNew == false`)。D-Drive は再取り込みで「自分が作ったトラック
    // だけ更新し、既存の Bindings とデザイナー / 外部が足したトラックは保持する」ので、リスナーは「既に足してあるか」を
    // `Result.Data.Bindings`(`TrackName`)と `Result.Timeline.GetOutputTracks()`(名前)で確認してから足すこと
    // (毎回足すと重複する)。
    //
    // 使用例(外部パッケージ側。D-Drive は特定の用途を前提にしない):
    //   public sealed class MyListener : ICutsceneImportListener
    //   {
    //       public int Order => 100;
    //       public void OnCutsceneShotImported(CutsceneImportResult result) { /* result.Roles を見て Track を足す */ }
    //   }
    public interface ICutsceneImportListener
    {
        // 小さい順に呼ぶ(同値は型のフルネーム順)。
        int Order { get; }

        void OnCutsceneShotImported(CutsceneImportResult result);
    }

    // 役の種別。**末尾追加のみ**([42_distribution.md] §5.9)。
    public enum CutsceneImportRoleKind
    {
        Camera = 0,
        Prop = 1,
        Character = 2,
    }

    // 1 ショットの取り込み結果。フィールドは追加のみ(削除・改名・型変更は [42] §5.9 の手続き)。
    public sealed class CutsceneImportResult
    {
        // ファイル名の <Shot> 部分(生の名前。識別子化前)。
        public string ShotName;

        // SourceAssets/Cutscene/ 直下からのカテゴリ(サブフォルダ。無ければ空)。
        public string Category;

        // 更新済みの CutsceneData(Bindings・SourceFbxGuids 反映済み)。リスナーが追記してよい。
        public CutsceneData Data;

        // `.playable` 本体(= Data.Timeline)。
        public TimelineAsset Timeline;

        // `.playable` のアセットパス。
        public string TimelinePath;

        // CutsceneData を新規作成したか(再取り込みなら false)。
        public bool IsNew;

        // 今回の取り込みで生成 / 更新した役(トラック)の一覧。FBX に対応するトラックができなかった役は含まれない。
        public IReadOnlyList<CutsceneImportRole> Roles;
    }

    // 役名 → トラックの対応。
    public sealed class CutsceneImportRole
    {
        // = TrackName(Bindings の TrackName)。キャラは FBX ファイル名の `__` 以降、小物は `PRP_` 接頭辞を除いた名前、カメラは Camera のオブジェクト名。
        public string RoleName;

        // キャラ: `__` 以降から重複接尾辞を除いたもの。カメラ / 小物は空。
        public string ModelIdentifier;

        public CutsceneImportRoleKind Kind;

        // 生成 / 更新した AnimationTrack または CutsceneCameraTrack。
        public TrackAsset Track;

        // 元 FBX のアセットパス。
        public string SourcePath;
    }

    // リスナーの発見と呼び出し。`CutsceneImportService` が使う。テストからも呼べるよう public。
    public static class CutsceneImportListeners
    {
        // `Order` 昇順(同値は型のフルネーム順)で新しいインスタンスを返す。
        public static List<ICutsceneImportListener> Discover()
        {
            var list = new List<ICutsceneImportListener>();
            foreach (var type in UnityEditor.TypeCache.GetTypesDerivedFrom<ICutsceneImportListener>())
            {
                if (type == null || type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
                {
                    continue;
                }

                // CI.DiscoverValidators / DDriveMigrationRunner と同じ理由でテストアセンブリのダミー実装は除外する。
                if (type.Assembly.GetName().Name.StartsWith("DDrive.Tests", StringComparison.Ordinal))
                {
                    continue;
                }

                if (type.GetConstructor(Type.EmptyTypes) == null)
                {
                    continue;
                }

                try
                {
                    if (Activator.CreateInstance(type) is ICutsceneImportListener listener)
                    {
                        list.Add(listener);
                    }
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogException(e);
                }
            }

            // Order は例外を投げうるので呼び出しは 1 回だけキャッシュして並べる。
            var keyed = new List<(int order, string name, ICutsceneImportListener listener)>(list.Count);
            foreach (var listener in list)
            {
                var order = 0;
                try
                {
                    order = listener.Order;
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogException(e);
                }

                keyed.Add((order, listener.GetType().FullName, listener));
            }

            keyed.Sort((a, b) =>
            {
                var c = a.order.CompareTo(b.order);
                return c != 0 ? c : string.CompareOrdinal(a.name, b.name);
            });

            var sorted = new List<ICutsceneImportListener>(keyed.Count);
            foreach (var k in keyed)
            {
                sorted.Add(k.listener);
            }

            return sorted;
        }

        // 全リスナーを順に呼ぶ。個別に try/catch(例外は Debug.LogException して継続)。呼べたリスナー数を返す。
        public static int Notify(CutsceneImportResult result)
        {
            if (result == null)
            {
                return 0;
            }

            var count = 0;
            foreach (var listener in Discover())
            {
                try
                {
                    listener.OnCutsceneShotImported(result);
                    count++;
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogException(e);
                }
            }

            return count;
        }
    }
}
