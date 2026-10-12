using System;
using System.Collections.Generic;
using DDrive.Editor.AssetBrowser;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Values;
using DDrive.Runtime.Anim2D;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace DDrive.Editor.Anim2D
{
    // スプライトの切り方(旧 Anim2DCreateWindow の入力モード)。
    internal enum Anim2DSliceMode
    {
        Grid,
        Automatic,
        Existing,
    }

    // 「スプライトから作成」の入力一式(docs/1010 UX-7。ウィザードが集める)。
    internal sealed class Anim2DCreateRequest
    {
        public Anim2DImportProfile Profile;
        public Anim2DSliceMode Slice = Anim2DSliceMode.Grid;
        public Texture2D Texture;
        public int GridColumns = 4;
        public int GridRows = 1;
        public int GridSpriteCount = 4;
        public int AutoMinSize = 16;
        public int AutoExtrude;
        public DirectionSet Directions = DirectionSet.None;
        public Texture2D[] DirectionTextures = Array.Empty<Texture2D>(); // 角度順(Four = 0/90/180/270、Eight = 45 度刻み)
        public string Name = string.Empty;
        public string State = string.Empty;
        public DirectionMode DirectionMode = DirectionMode.None;
        public int FrameRate = 12;
        public bool Loop = true;
        public ClipLengthMode LengthMode = ClipLengthMode.Frames;
        public float Length = 8f;
        public AnimatorController Controller;
        public int Layer;
        public string ClipFolder;   // null = Profile.DefaultClipFolder
        public string GameDataRoot; // null = 既定(Assets/GameData)
        public Func<int, BlendTreeConflictResolution> ConfirmOverwrite; // null = 常に上書き
    }

    // スプライト分割 → 命名 → AnimationClip 生成 → BlendTree 登録 → Anim2DData 自動生成(AssetCreationService。ID・カタログ・Addressables 登録込み)。
    // 旧 Anim2DCreateWindow の生成ロジックを、画面から切り離して移した物(挙動は同じ)。
    internal static class Anim2DCreateService
    {
        public static string ResolveClipFolder(Anim2DCreateRequest r)
        {
            if (!string.IsNullOrEmpty(r.ClipFolder))
            {
                return r.ClipFolder;
            }

            var profile = r.Profile ?? Anim2DImportProfile.FindOrDefault();
            return string.IsNullOrEmpty(profile.DefaultClipFolder) ? "Assets/GameData/Anim2D/Clips" : profile.DefaultClipFolder;
        }

        // 作れない理由(無ければ null)。ウィザードの「次へ」「作成」の可否に使う。
        public static string CheckInputs(Anim2DCreateRequest r)
        {
            if (r.Directions == DirectionSet.None)
            {
                if (r.Texture == null)
                {
                    return "素材の画像(Texture)を選んでください。";
                }
            }
            else
            {
                var any = false;
                if (r.DirectionTextures != null)
                {
                    foreach (var t in r.DirectionTextures)
                    {
                        any |= t != null;
                    }
                }

                if (!any)
                {
                    return "方向ごとの画像を 1 枚以上選んでください(空欄の方向は作られません)。";
                }
            }

            return null;
        }

        public static string CheckNaming(Anim2DCreateRequest r)
        {
            if (string.IsNullOrWhiteSpace(r.Name))
            {
                return "Name を入力してください。";
            }

            if (string.IsNullOrWhiteSpace(r.State))
            {
                return "State を入力してください。";
            }

            return null;
        }

        public static Anim2DData Create(Anim2DCreateRequest r, out string message)
        {
            message = null;
            var inputs = CheckInputs(r) ?? CheckNaming(r);
            if (inputs != null)
            {
                message = inputs;
                return null;
            }

            var folder = ResolveClipFolder(r);
            AssetCreationService.EnsureFolder(folder);
            return r.Directions == DirectionSet.None ? CreateSingle(r, folder, out message) : CreateDirectionSet(r, folder, out message);
        }

        private static bool TrySlice(Anim2DCreateRequest r, Texture2D texture, out Sprite[] sprites)
        {
            sprites = null;
            if (texture == null)
            {
                Debug.LogError("[Anim2DCreateService] Texture が未設定です。");
                return false;
            }

            switch (r.Slice)
            {
                case Anim2DSliceMode.Grid:
                    return SpriteSlicer.SliceAndCollect(texture, r.GridColumns, r.GridRows, r.GridSpriteCount, out sprites);
                case Anim2DSliceMode.Automatic:
                    if (!AutomaticSpriteSlicer.DetectRects(texture, r.AutoMinSize, r.AutoExtrude, out var rects, out _, out _, out _))
                    {
                        return false;
                    }

                    return AutomaticSpriteSlicer.ApplyRectsAndCollect(texture, rects, out sprites);
                default:
                    return ExistingSpriteCollector.Collect(texture, out sprites);
            }
        }

        private static Anim2DData CreateSingle(Anim2DCreateRequest r, string folder, out string message)
        {
            var profile = r.Profile ?? Anim2DImportProfile.FindOrDefault();
            message = null;
            if (!TrySlice(r, r.Texture, out var sprites))
            {
                message = "スプライトの取得に失敗しました(Console 参照)。";
                return null;
            }

            var angle = DirectionAngle.HasAngle(r.DirectionMode) ? DirectionAngle.ToAngle(r.DirectionMode) : -1;
            if (r.DirectionMode == DirectionMode.FromName && r.Texture != null && NamingRuleResolver.TryExtractAngleFromName(r.Texture.name, out var extracted))
            {
                angle = extracted;
            }

            var clipName = NamingRuleResolver.BuildClipName(r.Name, r.State, angle);
            if (!AnimationClipBuilder.Build(sprites, folder, clipName, r.FrameRate, r.LengthMode, r.Length, r.Loop, out var clip))
            {
                message = "AnimationClip の生成に失敗しました(Console 参照)。";
                return null;
            }

            if (r.Controller != null && angle >= 0)
            {
                BlendTreeRegistrar.Register(r.Controller, r.Layer, r.State, clip, angle, r.ConfirmOverwrite ?? AlwaysOverwrite);
            }

            var asset = AssetCreationService.Create(
                typeof(Anim2DData),
                AssetType.Anim2D,
                $"{r.Name} {r.State}",
                profile.ResolveCategory(r.Name),
                AssetNamingService.ToIdentifier(r.Name + r.State, "Anim2D"),
                configure: d =>
                {
                    var a = (Anim2DData)d;
                    a.Clip = clip;
                    a.StateName = r.State;
                    a.Layer = r.Layer;
                    a.Loop = r.Loop;
                    a.Directions = DirectionSet.None;
                    a.Retiming = ValueDef.Constant01(1f);
                },
                gameDataRoot: r.GameDataRoot ?? AssetCreationService.DefaultGameDataRoot);

            if (asset == null)
            {
                message = "Anim2DData の作成に失敗しました(Console 参照)。";
                return null;
            }

            message = $"{clip.name}({sprites.Length} 枚)→ {AssetDatabase.GetAssetPath(asset)}";
            return asset as Anim2DData;
        }

        private static Anim2DData CreateDirectionSet(Anim2DCreateRequest r, string folder, out string message)
        {
            message = null;
            var profile = r.Profile ?? Anim2DImportProfile.FindOrDefault();
            var angles = r.Directions == DirectionSet.Four ? DirectionAngle.FourAngles : DirectionAngle.EightAngles;
            var required = r.Directions == DirectionSet.Four ? Anim2DData.FourDirections : Anim2DData.EightDirections;
            var ordered = new List<(int angle, AnimationClip clip)>();

            for (var i = 0; i < angles.Length && r.DirectionTextures != null && i < r.DirectionTextures.Length; i++)
            {
                var texture = r.DirectionTextures[i];
                if (texture == null)
                {
                    continue;
                }

                if (!TrySlice(r, texture, out var sprites))
                {
                    Debug.LogError($"[Anim2DCreateService] 角度 {angles[i]} のスプライト取得に失敗しました。スキップします。");
                    continue;
                }

                var clipName = NamingRuleResolver.BuildClipName(r.Name, r.State, angles[i]);
                if (!AnimationClipBuilder.Build(sprites, folder, clipName, r.FrameRate, r.LengthMode, r.Length, r.Loop, out var clip))
                {
                    Debug.LogError($"[Anim2DCreateService] 角度 {angles[i]} の AnimationClip 生成に失敗しました。スキップします。");
                    continue;
                }

                if (r.Controller != null)
                {
                    BlendTreeRegistrar.Register(r.Controller, r.Layer, r.State, clip, angles[i], r.ConfirmOverwrite ?? AlwaysOverwrite);
                }

                ordered.Add((angles[i], clip));
            }

            if (ordered.Count == 0)
            {
                message = "有効な方向クリップが 1 枚も作れませんでした(Console 参照)。Anim2DData は作っていません。";
                return null;
            }

            ordered.Sort((a, b) => a.angle.CompareTo(b.angle));
            var directionClips = new AnimationClip[ordered.Count];
            for (var i = 0; i < ordered.Count; i++)
            {
                directionClips[i] = ordered[i].clip;
            }

            var primary = ordered[0].clip;
            var asset = AssetCreationService.Create(
                typeof(Anim2DData),
                AssetType.Anim2D,
                $"{r.Name} {r.State}",
                profile.ResolveCategory(r.Name),
                AssetNamingService.ToIdentifier(r.Name + r.State, "Anim2D"),
                configure: d =>
                {
                    var a = (Anim2DData)d;
                    a.Clip = primary;
                    a.StateName = r.State;
                    a.Layer = r.Layer;
                    a.Loop = r.Loop;
                    a.Directions = r.Directions;
                    a.DirectionClips = directionClips;
                    a.ParamXName = BlendTreeRegistrar.ParamXName;
                    a.ParamYName = BlendTreeRegistrar.ParamYName;
                    a.Retiming = ValueDef.Constant01(1f);
                },
                gameDataRoot: r.GameDataRoot ?? AssetCreationService.DefaultGameDataRoot);

            if (asset == null)
            {
                message = "Anim2DData の作成に失敗しました(Console 参照)。";
                return null;
            }

            message = ordered.Count < required
                ? $"{ordered.Count} / {required} 方向を作りました(足りない方向は Validation で知らせます)→ {AssetDatabase.GetAssetPath(asset)}"
                : $"{ordered.Count} 方向 → {AssetDatabase.GetAssetPath(asset)}";
            return asset as Anim2DData;
        }

        private static BlendTreeConflictResolution AlwaysOverwrite(int angle) => BlendTreeConflictResolution.Overwrite;

        // ウィザード用: 重複したときだけ聞く。
        public static BlendTreeConflictResolution AskOverwrite(int angle)
        {
            return EditorUtility.DisplayDialog("BlendTree 登録", $"角度 {angle} には既にクリップが登録されています。上書きしますか?", "上書き", "スキップ")
                ? BlendTreeConflictResolution.Overwrite
                : BlendTreeConflictResolution.Skip;
        }
    }
}
