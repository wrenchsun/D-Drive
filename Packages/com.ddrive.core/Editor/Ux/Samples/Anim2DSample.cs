using System.IO;
using DDrive.Editor.Anim2D;
using DDrive.Foundation.Data;
using DDrive.Foundation.Event;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Values;
using DDrive.Runtime.Anim2D;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Vfx;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Anim
{
    // 実運用規模の確認用サンプル(docs/1010 D-10。「勇者の歩き」= 24 コマのスプライトシート 1 枚から作った 8 方向(Clip 8 本 + 主 Clip)の Anim2DData、
    // イベント 12 件(SE / VFX / そのほか・対象未設定・Clip 長超えのエラー 1 件を含む。Frame / Time / OnLoop の混在))。
    // 指定のフォルダ(Assets/ 配下)に、生成したスプライトシート(PNG。Sprite Multiple に切る)・Clip・参照先の SE 3 / VFX 3・Anim2DData 3 つ(うち 1 つが対象)を作る。
    // 呼び出し側が使い終わったらフォルダごと消す(実データ Assets/GameData には作らない。Addressables にも登録しない)。スクリーンショット・テスト・見通しの確認用。
    internal static class Anim2DSample
    {
        public const ulong BaseId = 0xDD700000;
        public const int FrameCount = 24;

        public sealed class Result
        {
            public Anim2DData Anim;
            public Texture2D Sheet;
            public Sprite[] Sprites;
            public string Folder;
            public int EventCount;
            public int ClipCount;
        }

        public static Result Build(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                var parent = Path.GetDirectoryName(folder).Replace((char)92, (char)47);
                AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
            }

            var seStep = Create<SeData>(folder, "SE_Footstep", BaseId + 1, "足音");
            var seSwing = Create<SeData>(folder, "SE_Swing", BaseId + 2, "武器を振る音");
            var seHit = Create<SeData>(folder, "SE_Hit", BaseId + 3, "ヒット音");
            var vfxDust = Create<VfxData>(folder, "VFX_Dust", BaseId + 4, "土煙");
            var vfxSpark = Create<VfxData>(folder, "VFX_Spark", BaseId + 5, "火花");
            var vfxAura = Create<VfxData>(folder, "VFX_Aura", BaseId + 6, "オーラ");

            // 24 コマ(8 列 × 3 行)のシート。コマごとに色相が変わり、棒の高さが上下する。
            var sheet = MakeSheet(folder, "Hero_Walk");
            SpriteSlicer.SliceAndCollect(sheet, 8, 3, FrameCount, out var sprites);

            // 8 方向の Clip(0 / 45 / … / 315)。主 Clip = 0°。
            var clips = new AnimationClip[8];
            for (var i = 0; i < 8; i++)
            {
                AnimationClipBuilder.Build(sprites, folder, NamingRuleResolver.BuildClipName("Hero", "Walk", DirectionAngle.EightAngles[i]), 12, ClipLengthMode.Frames, FrameCount, true, out clips[i]);
            }

            var anim = New<Anim2DData>(BaseId + 100, "勇者 歩き");
            anim.Clip = clips[0];
            anim.StateName = "Walk";
            anim.Loop = true;
            anim.Directions = DirectionSet.Eight;
            anim.DirectionClips = clips;
            anim.Retiming = ValueDef.Constant01(1f);
            anim.Events = new[]
            {
                PlayAsset(EventTrigger.Frame, 3f, EventRepeat.EveryLoop, seStep.Id, AssetType.Se),
                PlayAsset(EventTrigger.Frame, 9f, EventRepeat.EveryLoop, seStep.Id, AssetType.Se),
                PlayAsset(EventTrigger.Frame, 15f, EventRepeat.EveryLoop, seStep.Id, AssetType.Se),
                PlayAsset(EventTrigger.Frame, 21f, EventRepeat.EveryLoop, seStep.Id, AssetType.Se),
                PlayAsset(EventTrigger.Frame, 4f, EventRepeat.EveryLoop, vfxDust.Id, AssetType.Vfx),
                PlayAsset(EventTrigger.Frame, 16f, EventRepeat.EveryLoop, vfxDust.Id, AssetType.Vfx),
                PlayAsset(EventTrigger.Time, 0.5f, EventRepeat.Once, seSwing.Id, AssetType.Se),
                PlayAsset(EventTrigger.Time, 1.2f, EventRepeat.Once, seHit.Id, AssetType.Se),
                PlayAsset(EventTrigger.Time, 1.2f, EventRepeat.Once, vfxSpark.Id, AssetType.Vfx),
                PlayAsset(EventTrigger.Frame, 12f, EventRepeat.KeepWhilePlaying, vfxAura.Id, AssetType.Vfx),
                new AssetEvent { Trigger = EventTrigger.Time, Time = 1.8f, Action = EventAction.SendMessage, CustomKey = "StepEnd" },
                PlayAsset(EventTrigger.Frame, 90f, EventRepeat.EveryLoop, seStep.Id, AssetType.Se), // Clip 長(24 フレーム)超え = 検証のエラー 1 件
            };
            AssetDatabase.CreateAsset(anim, $"{folder}/ANIM2D_HeroWalk_Sample.asset");

            // 方向なしの AnimData 2 つ(データ切替・一覧の確認用)。
            foreach (var name in new[] { "Idle", "Run" })
            {
                AnimationClipBuilder.Build(sprites, folder, NamingRuleResolver.BuildClipName("Hero", name, -1), 12, ClipLengthMode.Frames, name == "Idle" ? 12 : 8, true, out var clip);
                var d = New<Anim2DData>(BaseId + 200 + (ulong)name.Length, "勇者 " + name);
                d.Clip = clip;
                d.StateName = name;
                d.Loop = true;
                AssetDatabase.CreateAsset(d, $"{folder}/ANIM2D_Hero{name}_Sample.asset");
            }

            DDrive.Editor.Versioning.DDriveAssetSave.SaveAllSuppressed();
            return new Result
            {
                Anim = anim,
                Sheet = sheet,
                Sprites = sprites,
                Folder = folder,
                EventCount = anim.Events.Length,
                ClipCount = clips.Length + 2,
            };
        }

        // 色の付いたスプライトシートの PNG を作って取り込む(読み込める Texture2D を返す)。
        private static Texture2D MakeSheet(string folder, string name)
        {
            const int cell = 32;
            const int cols = 8;
            const int rows = 3;
            var tex = new Texture2D(cell * cols, cell * rows, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[tex.width * tex.height];
            for (var f = 0; f < cols * rows; f++)
            {
                var col = f % cols;
                var row = rows - 1 - f / cols; // 左上から行優先 = テクスチャは左下原点
                var c = (Color32)Color.HSVToRGB(f / (float)(cols * rows), 0.55f, 0.95f);
                var bar = 6 + Mathf.RoundToInt(Mathf.Abs(Mathf.Sin(f * 0.5f)) * 18f);
                for (var y = 0; y < cell; y++)
                {
                    for (var x = 0; x < cell; x++)
                    {
                        var inside = x >= 6 && x < cell - 6 && y >= 4 && y < 4 + bar;
                        pixels[(row * cell + y) * tex.width + col * cell + x] = inside ? c : new Color32(0, 0, 0, 0);
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            var path = $"{folder}/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static T Create<T>(string folder, string name, ulong id, string display) where T : AssetDataBase
        {
            var d = New<T>(id, display);
            AssetDatabase.CreateAsset(d, $"{folder}/{name}.asset");
            return d;
        }

        // 中身を作ってから CreateAsset する(CreateAsset のあとに書き換えると、取り込み直しで中身が消えることがあるため)。
        private static T New<T>(ulong id, string display) where T : AssetDataBase
        {
            var d = ScriptableObject.CreateInstance<T>();
            d.Id = id;
            d.DisplayName = display;
            return d;
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
