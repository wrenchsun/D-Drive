using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Import;
using DDrive.Foundation.Easing;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Values;
using DDrive.Runtime.Cutscene;
using DDrive.Runtime.Cutscene.Tracks;
using DDrive.Runtime.Model;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace DDrive.Editor.Cutscene
{
    // [26_timeline.md] §5(6-10c) — Maya FBX(スクリプト無し)→ CutsceneData + TimelineAsset の自動構築。
    //
    // [09_editor_tools.md] §1.1 / [26_timeline.md] §6 実装メモ(2026-09-18): 既存の `IImportRuleHandler`
    // (`ImportRuleService`)は「1 元ファイル = 1 Data」の Configure しか持たないため採用しなかった。
    // Cutscene は「1 ショット = カメラ+小物 FBX 1 本 + キャラごとの FBX N 本 → CutsceneData 1 個」という
    // N:1 の対応で、かつ再取り込みは「自動生成トラックだけ差し替え、デザイナーが足したトラック/設定は保持」
    // という個別の Data 更新が要る(単純な Configure(data, source, path) では表現できない)。そのため
    // `MayaModelPostprocessor`(FBX→MaterialData、3-7)と同じ位置付けの専用パイプラインとして実装し、
    // `ImportRuleService.KnownNonTargetTypeFolders` に "Cutscene" を加えて汎用の案内ログ対象からは外した
    // ("Shaders" が AiStandardSurfacePreprocessor 専用フォルダとして除外されているのと同じ扱い)。
    // 識別子・カテゴリ・保存先フォルダの規約(`AssetNamingService`)・二重生成防止(`ImportSourceGuid`)は
    // 既存の ImportRule 系と揃えている。
    public static class CutsceneImportService
    {
        public const string TypeFolder = "Cutscene";

        // テスト / 一括インポート中の抑止(ImportRuleService.AutoImport と同じ役割)。
        public static bool AutoImport = true;

        public sealed class Report
        {
            public int Created;
            public int Updated;
            public int Skipped;
            public readonly List<string> Lines = new();

            public void Log(string line) => Lines.Add(line);

            public override string ToString()
                => $"CutsceneData 新規 {Created} / 更新 {Updated}(スキップ {Skipped})\n" + string.Join("\n", Lines);
        }

        private sealed class ShotGroup
        {
            public string Category = string.Empty;
            public string ShotRawName;
            public string CameraPropsPath;
            public readonly List<string> CharacterPaths = new();
        }

        // AssetPostprocessor 経由(ImportRulePostprocessor と同じ delayCall バッチ)・テストの両方から呼ぶ中核処理。
        public static Report ProcessPaths(
            IEnumerable<string> paths,
            string sourceRoot = ImportRuleService.DefaultSourceRoot,
            string gameDataRoot = AssetCreationService.DefaultGameDataRoot)
        {
            var report = new Report();
            if (paths == null)
            {
                return report;
            }

            var root = (string.IsNullOrEmpty(sourceRoot) ? ImportRuleService.DefaultSourceRoot : sourceRoot).TrimEnd('/') + "/" + TypeFolder + "/";
            var groups = new Dictionary<string, ShotGroup>(StringComparer.Ordinal);

            foreach (var rawPath in paths)
            {
                var path = rawPath?.Replace('\\', '/');
                if (string.IsNullOrEmpty(path) || !path.StartsWith(root, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var rest = path.Substring(root.Length); // "<カテゴリ.../>ファイル名.fbx"
                var lastSlash = rest.LastIndexOf('/');
                var fileName = lastSlash < 0 ? rest : rest.Substring(lastSlash + 1);
                var category = lastSlash < 0 ? string.Empty : rest.Substring(0, lastSlash);
                var fileNameNoExt = Path.GetFileNameWithoutExtension(fileName);

                CutsceneShotParser.ParseFileName(fileNameNoExt, out var shot, out var isCharacter, out var modelIdentifierRaw);

                var key = category + "" + shot;
                if (!groups.TryGetValue(key, out var group))
                {
                    group = new ShotGroup { Category = category, ShotRawName = shot };
                    groups[key] = group;
                }

                if (isCharacter)
                {
                    group.CharacterPaths.Add(path);
                }
                else
                {
                    group.CameraPropsPath = path;
                }
            }

            foreach (var group in groups.Values)
            {
                ProcessShot(group, gameDataRoot, report);
            }

            return report;
        }

        // 手動フォールバック(ImportRuleService.ScanAll と同じ形)。
        public static Report ScanAll(string sourceRoot = ImportRuleService.DefaultSourceRoot, string gameDataRoot = AssetCreationService.DefaultGameDataRoot)
        {
            var folder = (string.IsNullOrEmpty(sourceRoot) ? ImportRuleService.DefaultSourceRoot : sourceRoot).TrimEnd('/') + "/" + TypeFolder;
            if (!AssetDatabase.IsValidFolder(folder))
            {
                return new Report();
            }

            var absoluteRoot = Path.GetFullPath(folder);
            if (!Directory.Exists(absoluteRoot))
            {
                return new Report();
            }

            var projectRoot = Path.GetFullPath(".").Replace('\\', '/').TrimEnd('/') + "/";
            var paths = new List<string>();
            foreach (var file in Directory.GetFiles(absoluteRoot, "*.fbx", SearchOption.AllDirectories))
            {
                var normalized = file.Replace('\\', '/');
                if (normalized.StartsWith(projectRoot, StringComparison.Ordinal))
                {
                    paths.Add(normalized.Substring(projectRoot.Length));
                }
            }

            return ProcessPaths(paths, sourceRoot, gameDataRoot);
        }

        // ── ショット単位の CutsceneData / TimelineAsset 構築 ──

        private static void ProcessShot(ShotGroup group, string gameDataRoot, Report report)
        {
            var profile = CutsceneImportProfile.FindOrDefault();
            var shotIdentifier = AssetNamingService.ToIdentifier(group.ShotRawName, profile.IdentifierFallback);
            var dataPath = ComputeCutsceneDataPath(gameDataRoot, group.Category, shotIdentifier);

            var data = AssetDatabase.LoadAssetAtPath<CutsceneData>(dataPath);
            var isNew = data == null;

            if (isNew)
            {
                if (string.IsNullOrEmpty(group.CameraPropsPath))
                {
                    report.Log($"保留: {group.ShotRawName}(カメラ+小物 FBX '{group.ShotRawName}.fbx' が見つからないため CutsceneData を新規作成できません)");
                    report.Skipped++;
                    return;
                }

                var created = AssetCreationService.Create(
                    typeof(CutsceneData),
                    AssetType.Cutscene,
                    group.ShotRawName,
                    group.Category,
                    shotIdentifier,
                    d => ((CutsceneData)d).ImportSourceGuid = AssetDatabase.AssetPathToGUID(group.CameraPropsPath),
                    gameDataRoot);

                if (created == null)
                {
                    report.Log($"失敗: {group.ShotRawName}(識別子 '{shotIdentifier}' が規約に合わない可能性があります)");
                    return;
                }

                data = (CutsceneData)created;
                report.Created++;
            }
            else
            {
                report.Updated++;
            }

            var timeline = data.Timeline;
            if (timeline == null)
            {
                timeline = ScriptableObject.CreateInstance<TimelineAsset>();
                timeline.name = data.name + "_Timeline";
                var timelinePath = ComputeTimelinePath(dataPath);
                AssetCreationService.EnsureFolder(Path.GetDirectoryName(timelinePath)?.Replace('\\', '/'));
                AssetDatabase.CreateAsset(timeline, AssetDatabase.GenerateUniqueAssetPath(timelinePath));
                data.Timeline = timeline;
            }

            var bindings = new List<CutsceneBinding>(data.Bindings ?? Array.Empty<CutsceneBinding>());
            var bindingNames = new HashSet<string>(bindings.Select(b => b.TrackName), StringComparer.Ordinal);
            var guids = new List<string>(data.SourceFbxGuids ?? Array.Empty<string>());

            float? detectedFps = null;

            if (!string.IsNullOrEmpty(group.CameraPropsPath))
            {
                AddGuidIfMissing(guids, group.CameraPropsPath);
                detectedFps = ProcessCameraAndProps(timeline, group.CameraPropsPath, profile, data.SourceFrameRange, bindings, bindingNames, report);
            }

            foreach (var characterPath in group.CharacterPaths)
            {
                AddGuidIfMissing(guids, characterPath);
                var fps = ProcessCharacterFile(timeline, characterPath, data.SourceFrameRange, bindings, bindingNames, report);
                detectedFps ??= fps;
            }

            data.SourceFbxGuids = guids.ToArray();
            data.Bindings = bindings.ToArray();

            // [26_timeline.md] §5.3 — FrameRate は「取り込みで自動設定」= 新規作成時のみ。再取り込みで
            // 上書きすると、手で直した値やプロジェクト既定変更後の再取り込みで意図せず変わってしまう
            // (Validator の不一致 Warning + FixAction で直す運用は 6-10d の対象)。
            if (isNew)
            {
                data.FrameRate = detectedFps ?? profile.DefaultFrameRate;
                timeline.editorSettings.frameRate = data.FrameRate;
            }

            EditorUtility.SetDirty(data);
            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();

            report.Log($"{(isNew ? "新規" : "更新")}: {data.name}(FBX {guids.Count} 件)");
        }

        public static string ComputeCutsceneDataPath(string gameDataRoot, string category, string shotIdentifier)
        {
            var folder = $"{gameDataRoot}/{AssetNamingService.GetTargetFolder(AssetType.Cutscene, category)}";
            var fileName = AssetNamingService.BuildFileName(AssetType.Cutscene, category, shotIdentifier);
            return $"{folder}/{fileName}.asset";
        }

        private static string ComputeTimelinePath(string cutsceneDataPath)
        {
            var dir = Path.GetDirectoryName(cutsceneDataPath)?.Replace('\\', '/') ?? string.Empty;
            var name = Path.GetFileNameWithoutExtension(cutsceneDataPath);
            return $"{dir}/{name}_Timeline.playable";
        }

        private static void AddGuidIfMissing(List<string> guids, string assetPath)
        {
            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (!string.IsNullOrEmpty(guid) && !guids.Contains(guid))
            {
                guids.Add(guid);
            }
        }

        // ── カメラ + 小物(1 FBX) ──

        // 戻り値: 埋め込みアニメーションの FBX 検出 fps(取れなければ null)。
        private static float? ProcessCameraAndProps(
            TimelineAsset timeline,
            string assetPath,
            CutsceneImportProfile profile,
            FrameRange sourceFrameRange,
            List<CutsceneBinding> bindings,
            HashSet<string> bindingNames,
            Report report)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (root == null)
            {
                report.Log($"スキップ: {assetPath}(モデルを読み込めません)");
                return null;
            }

            var clip = AnimSourceLoader.Load(assetPath);
            var detectedClipFps = clip != null ? clip.frameRate : 0f;
            if (clip != null && CutsceneFrameRangeTrimmer.ShouldTrim(sourceFrameRange))
            {
                clip = CutsceneFrameRangeTrimmer.TrimClip(clip, sourceFrameRange, detectedClipFps);
            }

            var cameras = root.GetComponentsInChildren<Camera>(true);
            if (cameras.Length == 0)
            {
                report.Log($"警告: {assetPath} にカメラが見つかりません(Camera クリップは生成しません。[26_timeline.md] §5.5)");
            }
            else
            {
                if (cameras.Length > 1)
                {
                    report.Log($"警告: {assetPath} に複数のカメラがあります('{cameras[0].name}' を使用します。Maya の Ubercam 化を検討してください)");
                }

                if (clip == null)
                {
                    report.Log($"警告: {assetPath} にアニメーションが見つからないため Camera クリップを作れません");
                }
                else
                {
                    var camTransform = cameras[0].transform;
                    var camPath = camTransform == root.transform
                        ? string.Empty
                        : AnimationUtility.CalculateTransformPath(camTransform, root.transform);

                    BuildOrUpdateCameraTrack(timeline, clip, camPath, cameras[0].gameObject.name, profile, bindings, bindingNames, report);
                }
            }

            // 小物: ルート直下の子のうち、カメラでないもの([26] §5.5「小物は名前空間/PRP_接頭辞」)。
            foreach (Transform child in root.transform)
            {
                if (child.GetComponent<Camera>() != null)
                {
                    continue;
                }

                ProcessPropChild(timeline, clip, child, bindings, bindingNames, report);
            }

            return clip != null && clip.frameRate > 0f ? clip.frameRate : (float?)null;
        }

        private static void ProcessPropChild(
            TimelineAsset timeline,
            AnimationClip clip,
            Transform child,
            List<CutsceneBinding> bindings,
            HashSet<string> bindingNames,
            Report report)
        {
            if (clip == null)
            {
                return;
            }

            var propIdentifierRaw = CutsceneShotParser.StripPropPrefix(child.name);
            var trackName = string.IsNullOrEmpty(propIdentifierRaw) ? child.name : propIdentifierRaw;

            BuildOrUpdateAnimationRoleTrack(timeline, clip, trackName, propIdentifierRaw, bindings, bindingNames, report);
        }

        // ── キャラ(1 FBX = 1 キャラ、Humanoid) ──

        private static float? ProcessCharacterFile(
            TimelineAsset timeline,
            string assetPath,
            FrameRange sourceFrameRange,
            List<CutsceneBinding> bindings,
            HashSet<string> bindingNames,
            Report report)
        {
            var fileNameNoExt = Path.GetFileNameWithoutExtension(assetPath);
            CutsceneShotParser.ParseFileName(fileNameNoExt, out _, out _, out var modelIdentifierRaw);

            var clip = AnimSourceLoader.Load(assetPath);
            if (clip == null)
            {
                report.Log($"警告: {assetPath} にアニメーションが見つかりません");
                return null;
            }

            if (CutsceneFrameRangeTrimmer.ShouldTrim(sourceFrameRange))
            {
                clip = CutsceneFrameRangeTrimmer.TrimClip(clip, sourceFrameRange, clip.frameRate);
            }

            var modelIdentifier = CutsceneShotParser.StripDuplicateSuffix(modelIdentifierRaw);
            BuildOrUpdateAnimationRoleTrack(timeline, clip, modelIdentifierRaw, modelIdentifier, bindings, bindingNames, report);

            return clip.frameRate > 0f ? clip.frameRate : (float?)null;
        }

        // ── 共通: Animation トラック(キャラ/小物とも Target=SpawnModel、[26] §4.2/§5.2) ──

        private static void BuildOrUpdateAnimationRoleTrack(
            TimelineAsset timeline,
            AnimationClip clip,
            string trackName,
            string modelIdentifier,
            List<CutsceneBinding> bindings,
            HashSet<string> bindingNames,
            Report report)
        {
            var track = FindExistingTrack<AnimationTrack>(timeline, trackName);
            if (track == null)
            {
                track = timeline.CreateTrack<AnimationTrack>(null, trackName);
            }

            var existingClip = track.GetClips().FirstOrDefault();
            AnimationPlayableAsset asset;
            TimelineClip timelineClip;
            if (existingClip != null && existingClip.asset is AnimationPlayableAsset existingAsset)
            {
                timelineClip = existingClip;
                asset = existingAsset;
            }
            else
            {
                timelineClip = track.CreateClip<AnimationPlayableAsset>();
                asset = (AnimationPlayableAsset)timelineClip.asset;
            }

            asset.clip = clip;
            timelineClip.start = 0d;
            timelineClip.duration = Math.Max(0.01, clip.length);
            timelineClip.displayName = trackName;

            if (!bindingNames.Contains(trackName))
            {
                var model = FindModelDataByIdentifier(modelIdentifier);
                bindings.Add(new CutsceneBinding
                {
                    TrackName = trackName,
                    Target = CutsceneBindTarget.SpawnModel,
                    Model = model != null ? new AssetId<ModelMarker>(model.Id, AssetType.Model) : default,
                });
                bindingNames.Add(trackName);

                if (model == null)
                {
                    report.Log($"警告: '{trackName}' に対応する ModelData('{modelIdentifier}')が見つかりません(Bindings で手動設定してください。[26_timeline.md] §5.2)");
                }
            }
        }

        // ── 共通: Camera トラック ──

        private static void BuildOrUpdateCameraTrack(
            TimelineAsset timeline,
            AnimationClip clip,
            string cameraPath,
            string trackName,
            CutsceneImportProfile profile,
            List<CutsceneBinding> bindings,
            HashSet<string> bindingNames,
            Report report)
        {
            var track = FindExistingTrack<CutsceneCameraTrack>(timeline, trackName);
            var isNewTrack = track == null;
            if (isNewTrack)
            {
                track = timeline.CreateTrack<CutsceneCameraTrack>(null, trackName);
            }

            var existingClip = track.GetClips().FirstOrDefault();
            CutsceneCameraClip asset;
            TimelineClip timelineClip;
            if (existingClip != null && existingClip.asset is CutsceneCameraClip existingAsset)
            {
                timelineClip = existingClip;
                asset = existingAsset;
            }
            else
            {
                timelineClip = track.CreateClip<CutsceneCameraClip>();
                asset = (CutsceneCameraClip)timelineClip.asset;
                // [26_timeline.md] §4.6.3/§7.2-6 — 新規クリップにのみ既定値を入れる(再取り込みでは
                // StepFps/BlendIn/BlendOut/Focus はデザイナー設定として保持する)。
                asset.StepFps = profile.DefaultCameraStepFps;
                asset.BlendIn = BuildDefaultBlend(profile.DefaultBlendSeconds);
                asset.BlendOut = BuildDefaultBlend(profile.DefaultBlendSeconds);
                asset.Focus = CameraFocusMode.Volume;
            }

            var found = CutsceneCameraCurveExtractor.Extract(clip, cameraPath, asset);
            if (!found)
            {
                report.Log($"警告: '{trackName}' のカメラカーブが抽出できませんでした(位置/回転/画角のいずれも見つかりません)");
            }

            timelineClip.start = 0d;
            timelineClip.duration = Math.Max(0.01, clip.length);
            timelineClip.displayName = trackName;

            if (!bindingNames.Contains(trackName))
            {
                bindings.Add(new CutsceneBinding { TrackName = trackName, Target = CutsceneBindTarget.MainCamera });
                bindingNames.Add(trackName);
            }
        }

        private static ValueDef BuildDefaultBlend(float seconds) => new()
        {
            Mode = ValueMode.Parametric,
            From = 0f,
            To = 1f,
            Parametric = EaseDef.Named(Ease.InOutSine),
            Time = TimeDef.Duration(Mathf.Max(0f, seconds)),
            Loop = LoopMode.Once,
        };

        private static T FindExistingTrack<T>(TimelineAsset timeline, string name) where T : TrackAsset
        {
            foreach (var track in timeline.GetOutputTracks())
            {
                if (track is T typed && track.name == name)
                {
                    return typed;
                }
            }

            return null;
        }

        // ── ModelData 検索(識別子の末尾一致。[26] §5.2「使うモデルは ModelData 識別子を自動で探す」) ──

        // OnPreprocessModel(CutsceneFbxPostprocessor)からも使う共通ロジックのため public。
        public static ModelData FindModelDataByIdentifier(string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
            {
                return null;
            }

            foreach (var guid in AssetSearch.FindAssets("t:" + nameof(ModelData)))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var fileNameNoExt = Path.GetFileNameWithoutExtension(path);
                var segments = fileNameNoExt.Split('_');
                var lastSegment = segments.Length > 0 ? segments[segments.Length - 1] : fileNameNoExt;
                if (string.Equals(lastSegment, identifier, StringComparison.Ordinal))
                {
                    return AssetDatabase.LoadAssetAtPath<ModelData>(path);
                }
            }

            return null;
        }
    }
}
