using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Event;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Vfx;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace DDrive.Editor.Anim
{
    // 実運用規模の確認用サンプル(docs/1010 D-10。「剣の連撃」= Animator の Controller にステート 14 行(BlendTree の 2 クリップを含む)・クリップ 14 本・Layer 2 つ、
    // 対象の AnimData はイベント 12 件(SE / VFX / そのほか・対象未設定・Clip 長超えのエラー 1 件を含む)・IK(左手 + 右足 + ウェイトカーブ)・BlendShape 3 本)。
    // 指定のフォルダ(Assets/ 配下)に、Controller・クリップ・参照先の SE 3 / VFX 3・AnimData 6 つ(うち 1 つが対象)を作る。呼び出し側が使い終わったらフォルダごと消す
    // (実データ Assets/GameData には作らない。Addressables にも登録しない)。スクリーンショット・テスト・見通しの確認用。
    // モデルは作らない(呼び出し側が Animator 付きの GameObject を置いて Controller を割り当てる。BuildAnimatorObject)。
    internal static class AnimSample
    {
        public const ulong BaseId = 0xDD600000;

        public sealed class Result
        {
            public AnimData Anim;
            public AnimatorController Controller;
            public string Folder;
            public int EventCount;
            public int StateCount;
            public int ClipCount;
        }

        private static readonly string[] BaseStates =
        {
            "Idle", "Locomotion", "Jump", "Land", "Attack1", "Attack2", "Attack3", "Hit", "Down", "GetUp", "Dash", "Taunt",
        };

        public static Result Build(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                var parent = System.IO.Path.GetDirectoryName(folder).Replace((char)92, (char)47);
                AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
            }

            // SE / VFX(イベントの参照先)
            var seSwing = Create<SeData>(folder, "SE_SwordSwing", BaseId + 1, "剣を振る音");
            var seHit = Create<SeData>(folder, "SE_SwordHit", BaseId + 2, "斬撃のヒット音");
            var seStep = Create<SeData>(folder, "SE_Footstep", BaseId + 3, "足音");
            var vfxDust = Create<VfxData>(folder, "VFX_Dust", BaseId + 4, "土煙");
            var vfxSlash = Create<VfxData>(folder, "VFX_SlashTrail", BaseId + 5, "斬撃の軌跡");
            var vfxSpark = Create<VfxData>(folder, "VFX_HitSpark", BaseId + 6, "ヒット火花");

            // Controller + クリップ
            var clips = new Dictionary<string, AnimationClip>();
            var controllerPath = $"{folder}/AC_SwordFighter.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Direction", AnimatorControllerParameterType.Float);
            controller.AddParameter("IsGrounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);

            var sm = controller.layers[0].stateMachine;
            foreach (var name in BaseStates)
            {
                if (name == "Locomotion")
                {
                    var walk = MakeClip(folder, "Walk", 1.0f, true);
                    var run = MakeClip(folder, "Run", 0.8f, true);
                    clips["Walk"] = walk;
                    clips["Run"] = run;
                    controller.CreateBlendTreeInController("Locomotion", out var tree);
                    tree.blendParameter = "Speed";
                    tree.AddChild(walk, 0f);
                    tree.AddChild(run, 1f);
                    continue;
                }

                var length = name == "Attack1" ? 1.2f : name == "Dash" ? 0.6f : name.StartsWith("Attack") ? 1.0f : 0.8f;
                var clip = MakeClip(folder, name, length, name == "Idle" || name == "Taunt");
                clips[name] = clip;
                var state = sm.AddState(name);
                state.motion = clip;
            }

            controller.AddLayer("Upper Body");
            var layers = controller.layers;
            layers[1].defaultWeight = 1f;
            controller.layers = layers;
            var wave = MakeClip(folder, "Wave", 1.0f, false);
            clips["Wave"] = wave;
            controller.layers[1].stateMachine.AddState("Wave").motion = wave;
            EditorUtility.SetDirty(controller);

            // 対象の AnimData(Attack1)
            var anim = New<AnimData>(BaseId + 100, "剣の連撃 1 段目");
            anim.Clip = clips["Attack1"];
            anim.StateName = "Attack1";
            anim.Layer = 0;
            anim.Loop = false;
            anim.DefaultCrossFade = 0.12f;
            anim.Ik = new IkProfile
            {
                LeftHand = true,
                RightFoot = true,
                Weight = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.3f, 1f), new Keyframe(0.8f, 1f), new Keyframe(1f, 0f)),
            };
            anim.BlendShapes = new[]
            {
                new BlendShapeTrack { ShapeName = "mouth_A", Weight = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.2f, 80f), new Keyframe(0.5f, 0f)) },
                new BlendShapeTrack { ShapeName = "eye_blink", Weight = new AnimationCurve(new Keyframe(0.1f, 0f), new Keyframe(0.15f, 100f), new Keyframe(0.2f, 0f)) },
                new BlendShapeTrack { ShapeName = "smile", Weight = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 60f)) },
            };
            anim.Events = new[]
            {
                PlayAsset(EventTrigger.Frame, 4f, EventRepeat.EveryLoop, seStep.Id, AssetType.Se),
                PlayAsset(EventTrigger.Frame, 6f, EventRepeat.EveryLoop, seSwing.Id, AssetType.Se),
                PlayAsset(EventTrigger.Frame, 6f, EventRepeat.Once, vfxSlash.Id, AssetType.Vfx),
                PlayAsset(EventTrigger.Frame, 12f, EventRepeat.EveryLoop, seSwing.Id, AssetType.Se),
                PlayAsset(EventTrigger.Frame, 14f, EventRepeat.Once, vfxSlash.Id, AssetType.Vfx),
                PlayAsset(EventTrigger.Time, 0.55f, EventRepeat.EveryLoop, seHit.Id, AssetType.Se),
                PlayAsset(EventTrigger.Time, 0.55f, EventRepeat.Once, vfxSpark.Id, AssetType.Vfx),
                PlayAsset(EventTrigger.Frame, 22f, EventRepeat.EveryLoop, seStep.Id, AssetType.Se),
                PlayAsset(EventTrigger.Frame, 24f, EventRepeat.KeepWhilePlaying, vfxDust.Id, AssetType.Vfx),
                new AssetEvent { Trigger = EventTrigger.Time, Time = 0.9f, Action = EventAction.SendMessage, CustomKey = "ComboWindowOpen" },
                new AssetEvent { Trigger = EventTrigger.Frame, Time = 8f, Action = EventAction.PlayAsset }, // 対象未設定
                PlayAsset(EventTrigger.Frame, 90f, EventRepeat.EveryLoop, seStep.Id, AssetType.Se), // Clip 長(36 フレーム)超え = 検証のエラー 1 件
            };
            AssetDatabase.CreateAsset(anim, $"{folder}/ANIM_Attack1_Sample.asset");
            AssetDatabase.SaveAssetIfDirty(controller);

            // 同じ Controller のほかのステートに対応する AnimData(一部だけ。「あり」と「なし」の混在)
            foreach (var name in new[] { "Idle", "Jump", "Attack2", "Hit", "Dash" })
            {
                var d = New<AnimData>(BaseId + 200 + (ulong)name.Length * 7 + (ulong)name[0], name);
                d.Clip = clips[name];
                d.StateName = name;
                d.Loop = name == "Idle";
                AssetDatabase.CreateAsset(d, $"{folder}/ANIM_{name}_Sample.asset");
            }

            DDrive.Editor.Versioning.DDriveAssetSave.SaveAllSuppressed();
            return new Result
            {
                Anim = anim,
                Controller = controller,
                Folder = folder,
                EventCount = anim.Events.Length,
                StateCount = BaseStates.Length - 1 + 2 + 1, // Locomotion は BlendTree の 2 クリップ、+ Upper Body の Wave
                ClipCount = clips.Count,
            };
        }

        // 動かす相手(Animator + Controller 付きの空の GameObject。呼び出し側がシーンに置き、使い終わったら DestroyImmediate する)。
        public static Animator BuildAnimatorObject(AnimatorController controller)
        {
            var go = new GameObject("AnimSample_Model");
            go.hideFlags = HideFlags.DontSave;
            var animator = go.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;

            // BlendShape(mouth_A / eye_blink / brow_up と、外部管理の FC_Blink)を持つ顔のメッシュ。サンプルの "smile" だけモデルに無い(⚠ の確認用)。
            var face = new GameObject("Face");
            face.hideFlags = HideFlags.DontSave;
            face.transform.SetParent(go.transform, false);
            var mesh = new Mesh { name = "AnimSample_Face", hideFlags = HideFlags.DontSave };
            mesh.vertices = new[] { new Vector3(0f, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(1f, 0f, 0f) };
            mesh.triangles = new[] { 0, 1, 2 };
            var delta = new[] { Vector3.zero, Vector3.up * 0.1f, Vector3.zero };
            foreach (var shape in new[] { "mouth_A", "eye_blink", "brow_up", "FC_Blink" })
            {
                mesh.AddBlendShapeFrame(shape, 100f, delta, null, null);
            }

            var smr = face.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            return animator;
        }

        private static T Create<T>(string folder, string name, ulong id, string display) where T : AssetDataBase
        {
            var d = New<T>(id, display);
            AssetDatabase.CreateAsset(d, $"{folder}/{name}.asset");
            return d;
        }

        // 中身を作ってから CreateAsset する(CreateAsset のあとに書き換えると、取り込み直しで中身が消えることがあるため。PresentationSample と同じ作り方)。
        private static T New<T>(ulong id, string display) where T : AssetDataBase
        {
            var d = ScriptableObject.CreateInstance<T>();
            d.Id = id;
            d.DisplayName = display;
            return d;
        }

        private static AnimationClip MakeClip(string folder, string name, float length, bool loop)
        {
            var clip = new AnimationClip { name = name, frameRate = 30f };
            clip.SetCurve(string.Empty, typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, length, 0.5f));
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AssetDatabase.CreateAsset(clip, $"{folder}/{name}.anim");
            return clip;
        }

        private static AssetEvent PlayAsset(EventTrigger trigger, float time, EventRepeat repeat, ulong id, AssetType type) => new AssetEvent
        {
            Trigger = trigger,
            Time = time,
            Action = EventAction.PlayAsset,
            Repeat = repeat,
            Target = new AssetRef { Type = type, Id = id },
        };
    }
}
