using System;
using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Timeline;

namespace DDrive.Editor.Dependencies
{
    // [11_tasks.md] 5-5 — 1 ファイル分の走査。テキストで .asset/.prefab/.unity をパースせず、
    // Unity API(SerializedObject)経由で読む(CLAUDE.md の設計意図に合わせる。要判断は docs/28 参照)。
    // 例外で個々のコンポーネント/ファイルの走査に失敗しても全体を止めない(警告 + スキップ)。
    internal static class DependencyGraphCollector
    {
        // AssetId<TMarker> の SerializedProperty.type は総称引数を含まない "AssetId`1" になる
        // (AssetIdDrawer と同じ実測値。2026-09-14 確認)。
        // internal(削除の確認画面、2026-09-14): ReferenceReplaceService が「参照差し替え」で同じ判定を
        // 使い回すために公開した(判定ロジックを2箇所に重複させない)。
        internal const string AssetIdPropertyType = "AssetId`1";
        internal const string AssetRefPropertyType = "AssetRef";

        // 削除の確認画面(2026-09-14)の参照差し替えが使う: SerializedProperty(AssetId<T> または AssetRef の
        // コンテナ側プロパティ)から、id/type の子プロパティ名を判定する。判定できなければ false。
        internal static bool TryGetIdTypeFieldNames(SerializedProperty prop, out string idField, out string typeField)
        {
            if (prop.propertyType == SerializedPropertyType.Generic && prop.type == AssetIdPropertyType)
            {
                idField = "value";
                typeField = "type";
                return true;
            }

            if (prop.propertyType == SerializedPropertyType.Generic && prop.type == AssetRefPropertyType)
            {
                idField = "Id";
                typeField = "Type";
                return true;
            }

            idField = null;
            typeField = null;
            return false;
        }

        public static List<DependencyEdgeRecord> CollectFromDataAsset(AssetDataBase asset)
        {
            var edges = new List<DependencyEdgeRecord>();
            if (asset == null)
            {
                return edges;
            }

            try
            {
                var so = new SerializedObject(asset);
                WalkProperties(so, string.Empty, asset.GetType().Name, edges);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DDrive] DependencyGraph: {AssetDatabase.GetAssetPath(asset)} を走査できませんでした: {e.Message}");
            }

            return edges;
        }

        public static List<DependencyEdgeRecord> CollectFromPrefab(string path)
        {
            var edges = new List<DependencyEdgeRecord>();

            GameObject root;
            try
            {
                root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DDrive] DependencyGraph: Prefab を読み込めませんでした({path}): {e.Message}");
                return edges;
            }

            if (root == null)
            {
                return edges;
            }

            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                {
                    continue; // Missing script(CLAUDE.md §0-4: 例外で止めない)
                }

                try
                {
                    var objectPath = TransformPath.GetRelative(root.transform, component.transform);
                    var so = new SerializedObject(component);
                    WalkProperties(so, objectPath, component.GetType().Name, edges);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[DDrive] DependencyGraph: {path} の {component.GetType().Name} を走査できませんでした: {e.Message}");
                }
            }

            return edges;
        }

        // [51_tdrive_integration.md] §4.8(FC-7) — Timeline(.playable)の走査。トラック → クリップの PlayableAsset と
        // トラック / タイムラインのマーカーを、Prefab・Scene と同じプロパティ走査(WalkProperties)で歩く。
        // 型は決め打ちしない(D-Drive 自身の CutsceneSeClip 等も、外部パッケージのクリップ・マーカーも、
        // AssetId<T> / AssetRef のフィールドがあれば同じ走査で拾える)。Presentation クリップの PresentationId のような
        // 入れ子の参照先(PresentationData 側の参照)は、その Data 自身の辺として既に索引済みなので辿れる。
        // ObjectPath = "トラック名/クリップ名#番号"(マーカーは "トラック名/[Marker] 時刻#番号")、ComponentType = クリップ / マーカーの型名。
        public static List<DependencyEdgeRecord> CollectFromTimeline(string path)
        {
            var edges = new List<DependencyEdgeRecord>();

            TimelineAsset timeline;
            try
            {
                timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DDrive] DependencyGraph: Timeline を読み込めませんでした({path}): {e.Message}");
                return edges;
            }

            if (timeline == null)
            {
                return edges;
            }

            try
            {
                var seenTracks = new HashSet<TrackAsset>();
                foreach (var track in timeline.GetOutputTracks())
                {
                    CollectFromTrack(track, seenTracks, path, edges);
                }

                // タイムライン直下のマーカートラックは GetOutputTracks に含まれないことがあるため別に歩く。
                CollectFromTrack(timeline.markerTrack, seenTracks, path, edges);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DDrive] DependencyGraph: {path} を走査できませんでした: {e.Message}");
            }

            return edges;
        }

        private static void CollectFromTrack(TrackAsset track, HashSet<TrackAsset> seen, string path, List<DependencyEdgeRecord> edges)
        {
            if (track == null || !seen.Add(track))
            {
                return;
            }

            var trackName = track.name;

            // トラック自身(AssetId を持つトラックにも対応)。
            WalkObject(track, trackName, track.GetType().Name, path, edges);

            var index = 0;
            foreach (var clip in track.GetClips())
            {
                index++;
                if (clip == null || clip.asset == null)
                {
                    continue; // Missing のクリップ(例外で止めない)
                }

                var clipName = string.IsNullOrEmpty(clip.displayName) ? clip.asset.GetType().Name : clip.displayName;
                WalkObject(clip.asset, $"{trackName}/{clipName}#{index}", clip.asset.GetType().Name, path, edges);
            }

            index = 0;
            foreach (var marker in track.GetMarkers())
            {
                index++;
                if (marker is UnityEngine.Object markerObject && markerObject != null)
                {
                    WalkObject(markerObject, $"{trackName}/[Marker] {marker.time:0.###}#{index}", marker.GetType().Name, path, edges);
                }
            }
        }

        private static void WalkObject(UnityEngine.Object target, string objectPath, string componentType, string path, List<DependencyEdgeRecord> edges)
        {
            try
            {
                var so = new SerializedObject(target);
                WalkProperties(so, objectPath, componentType, edges);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DDrive] DependencyGraph: {path} の {componentType} を走査できませんでした: {e.Message}");
            }
        }

        // 現在開いている / 変更中のシーンには触れない。対象シーンを Additive で開いて読み終えたら必ず閉じる。
        public static List<DependencyEdgeRecord> CollectFromScene(string path)
        {
            var edges = new List<DependencyEdgeRecord>();

            // 既に開いているシーン(ユーザーが編集中・保存直後の差分更新など)は OpenScene が同じ Scene を返すため、
            // 閉じるとユーザーのシーンを閉じてしまう。開いていればそのまま走査し、閉じない。
            var loaded = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);
            if (loaded.IsValid() && loaded.isLoaded)
            {
                foreach (var rootGo in loaded.GetRootGameObjects())
                {
                    CollectFromGameObjectTree(rootGo, edges, path);
                }

                return edges;
            }

            UnityEngine.SceneManagement.Scene scene = default;
            var opened = false;
            try
            {
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                opened = scene.IsValid();

                if (opened)
                {
                    foreach (var rootGo in scene.GetRootGameObjects())
                    {
                        CollectFromGameObjectTree(rootGo, edges, path);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DDrive] DependencyGraph: シーンを開けませんでした({path}): {e.Message}");
            }
            finally
            {
                if (opened)
                {
                    try
                    {
                        EditorSceneManager.CloseScene(scene, removeScene: true);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[DDrive] DependencyGraph: シーンを閉じられませんでした({path}): {e.Message}");
                    }
                }
            }

            return edges;
        }

        private static void CollectFromGameObjectTree(GameObject rootGo, List<DependencyEdgeRecord> edges, string scenePath)
        {
            foreach (var component in rootGo.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                {
                    continue;
                }

                try
                {
                    var relative = TransformPath.GetRelative(rootGo.transform, component.transform);
                    var objectPath = string.IsNullOrEmpty(relative) ? rootGo.name : $"{rootGo.name}/{relative}";
                    var so = new SerializedObject(component);
                    WalkProperties(so, objectPath, component.GetType().Name, edges);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[DDrive] DependencyGraph: {scenePath} の {component.GetType().Name} を走査できませんでした: {e.Message}");
                }
            }
        }

        // SerializedObject を先頭から深さ優先で全走査する(配列・入れ子の Serializable クラス・
        // [SerializeReference] による多態も NextVisible が自動的にたどるため、型ごとの特別扱いは不要)。
        private static void WalkProperties(SerializedObject so, string objectPath, string componentType, List<DependencyEdgeRecord> edges)
        {
            var prop = so.GetIterator();
            var enterChildren = true;

            while (prop.NextVisible(enterChildren))
            {
                enterChildren = true;

                if (prop.propertyType != SerializedPropertyType.Generic)
                {
                    continue;
                }

                if (prop.type == AssetIdPropertyType)
                {
                    TryAddEdge(prop, "value", "type", objectPath, componentType, edges);
                }
                else if (prop.type == AssetRefPropertyType)
                {
                    TryAddEdge(prop, "Id", "Type", objectPath, componentType, edges);
                }
            }
        }

        private static void TryAddEdge(SerializedProperty prop, string idField, string typeField, string objectPath, string componentType, List<DependencyEdgeRecord> edges)
        {
            var idProp = prop.FindPropertyRelative(idField);
            var typeProp = prop.FindPropertyRelative(typeField);
            if (idProp == null || typeProp == null)
            {
                return;
            }

            var id = idProp.ulongValue;
            if (id == 0)
            {
                return; // 未設定(<None>)は依存として扱わない
            }

            edges.Add(new DependencyEdgeRecord
            {
                ObjectPath = objectPath,
                ComponentType = componentType,
                PropertyPath = prop.propertyPath,
                TargetType = typeProp.intValue,
                TargetId = id,
            });
        }
    }
}
