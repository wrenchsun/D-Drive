using DDrive.Editor.Versioning;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Vfx;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Anchor
{
    // 実運用規模の確認用サンプル(docs/1010 D-10)。
    // Anchor: 右手(ボーン) → 銃口 → マズルフラッシュ の 3 段の連鎖(ルートはボーン基準・子は親からのオフセット)+ 頭・足元・頭上の単独 Anchor。
    // Anchor Group: 「戦場の配置(サンプル)」= Grid 4×4(16 点)+ 手置き 6 点 = 22 点(20 行を超えるので一覧は ListView)。
    //   種類が混在: パターンの点・手置きの点・出さない(Skip)2 点・VFX / SE の上書き 2 点・入れ子(子の配置セット 1 つ)・全点共通の VFX 2 / SE 1・原点は Anchor アセットの連鎖。
    //   ほかに Circle / Line / Random / Manual の配置セットと、入れ子に使う子の配置セット。
    // 指定のフォルダ(Assets/ 配下)に作る。呼び出し側が使い終わったらフォルダごと消す(実データ Assets/GameData には作らない。Addressables にも登録しない)。
    // スクリーンショット・テスト・見通しの確認用。
    internal static class AnchorSample
    {
        public const ulong BaseId = 0xDDC00000;

        public sealed class Result
        {
            public AnchorData[] Anchors;       // [0] 右手 [1] 銃口 [2] マズルフラッシュ [3] 頭 [4] 足元 [5] 頭上
            public AnchorGroupData Main;       // 22 点の配置セット
            public AnchorGroupData[] Groups;   // [0] Main [1] Ring [2] Line [3] Random [4] Manual [5] 子
            public VfxData[] Vfx;
            public SeData[] Se;
            public string Folder;
        }

        public static Result Build(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                var parent = System.IO.Path.GetDirectoryName(folder).Replace((char)92, (char)47);
                AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
            }

            var vfx = new[]
            {
                Create<VfxData>(folder, "VFX_Sample_MuzzleFlash", BaseId + 0x300, "マズルフラッシュ"),
                Create<VfxData>(folder, "VFX_Sample_Dust", BaseId + 0x301, "砂ぼこり"),
                Create<VfxData>(folder, "VFX_Sample_Spark", BaseId + 0x302, "火花"),
            };
            var se = new[]
            {
                Create<SeData>(folder, "SE_Sample_Shot", BaseId + 0x400, "発砲音"),
                Create<SeData>(folder, "SE_Sample_Shell", BaseId + 0x401, "薬莢の音"),
            };

            var hand = CreateAnchor(folder, "ANC_Sample_RightHand", BaseId + 1, "右手", a =>
            {
                a.Space = AnchorSpace.BoneName;
                a.Path = "Hand_R";
                a.LocalOffset = new Vector3(0f, 0.05f, 0.1f);
                a.FollowRotation = true;
            });
            var muzzle = CreateAnchor(folder, "ANC_Sample_Muzzle", BaseId + 2, "銃口", a =>
            {
                a.Parent = new AssetId<AnchorMarker>(hand.Id, AssetType.Anchor);
                a.LocalOffset = new Vector3(0f, 0.02f, 0.62f);
                a.DelaySec = 0.1f;
                a.SpawnChance = 0.9f;
            });
            var flash = CreateAnchor(folder, "ANC_Sample_MuzzleFlash", BaseId + 3, "マズルフラッシュ", a =>
            {
                a.Parent = new AssetId<AnchorMarker>(muzzle.Id, AssetType.Anchor);
                a.LocalOffset = new Vector3(0.05f, 0.04f, 0.12f);
                a.LocalEuler = new Vector3(0f, 12f, 0f);
                a.PositionJitterRadius = 0.05f;
                a.ScaleRange = new Vector2(0.8f, 1.2f);
                a.DelayJitterSec = 0.05f;
            });
            var head = CreateAnchor(folder, "ANC_Sample_Head", BaseId + 4, "頭", a =>
            {
                a.Space = AnchorSpace.BoneName;
                a.Path = "Head";
                a.LocalOffset = new Vector3(0f, 0.3f, 0f);
            });
            var feet = CreateAnchor(folder, "ANC_Sample_Feet", BaseId + 5, "足元", a =>
            {
                a.Space = AnchorSpace.World;
                a.LocalOffset = new Vector3(0f, 0f, 0f);
            });
            var overhead = CreateAnchor(folder, "ANC_Sample_Overhead", BaseId + 6, "頭上(スポーン先の真上)", a =>
            {
                a.Space = AnchorSpace.ContextTarget;
                a.LocalOffset = new Vector3(0f, 2f, 0f);
                a.EulerJitter = new Vector3(0f, 180f, 0f);
                a.DelayJitterSec = 0.2f;
            });

            var child = CreateGroup(folder, "ANCG_Sample_Sparks", BaseId + 0x110, "火花の輪(子)", g =>
            {
                g.Layout = AnchorLayoutKind.Circle;
                g.CircleCount = 4;
                g.CircleRadius = 0.3f;
                g.SharedVfx = new[] { Vfx(vfx[2]) };
            });

            var main = CreateGroup(folder, "ANCG_Sample_Battlefield", BaseId + 0x100, "戦場の配置(サンプル)", g =>
            {
                g.OriginAnchorId = new AssetId<AnchorMarker>(muzzle.Id, AssetType.Anchor);
                g.Layout = AnchorLayoutKind.Grid;
                g.GridCountX = 4;
                g.GridCountY = 1;
                g.GridCountZ = 4;
                g.GridSpacing = new Vector3(1.2f, 1f, 1.2f);
                g.Points = new[]
                {
                    Point("Boss", new Vector3(0f, 0f, 4.5f)),
                    Point("左の見張り", new Vector3(-3f, 0f, 3f)),
                    Point("右の見張り", new Vector3(3f, 0f, 3f)),
                    Point("入口", new Vector3(0f, 0f, -3.5f)),
                    Point("高台", new Vector3(2.5f, 1.5f, 1f)),
                    Point("予備", new Vector3(-2.5f, 0f, -2f)),
                };
                g.DelayPerIndex = 0.05f;
                g.ChancePerPoint = 0.9f;
                g.PositionJitterRadius = 0.1f;
                g.SharedVfx = new[] { Vfx(vfx[0]), Vfx(vfx[1]) };
                g.SharedSe = new[] { new AssetId<SeMarker>(se[0].Id, AssetType.Se) };
                g.Overrides = new[]
                {
                    new AnchorGroupOverride { Index = 0, Skip = true },
                    new AnchorGroupOverride { Index = 5, Vfx = new[] { Vfx(vfx[2]) } },
                    new AnchorGroupOverride { Index = 17, Skip = true },
                    new AnchorGroupOverride { Index = 20, Se = new[] { new AssetId<SeMarker>(se[1].Id, AssetType.Se) } },
                };
                g.Children = new[]
                {
                    new AnchorGroupChild { Group = new AssetId<AnchorGroupMarker>(child.Id, AssetType.AnchorGroup), AtIndex = 3 },
                };
            });

            var ring = CreateGroup(folder, "ANCG_Sample_Ring", BaseId + 0x101, "円陣(8 点)", g =>
            {
                g.OriginAnchorId = new AssetId<AnchorMarker>(feet.Id, AssetType.Anchor);
                g.Layout = AnchorLayoutKind.Circle;
                g.CircleCount = 8;
                g.CircleRadius = 2f;
                g.SharedVfx = new[] { Vfx(vfx[1]) };
            });
            var line = CreateGroup(folder, "ANCG_Sample_Line", BaseId + 0x102, "一列(5 点)", g =>
            {
                g.Layout = AnchorLayoutKind.Line;
                g.LineCount = 5;
                g.LineLength = 6f;
                g.DelayPerIndex = 0.1f;
                g.SharedVfx = new[] { Vfx(vfx[0]) };
            });
            var random = CreateGroup(folder, "ANCG_Sample_Scatter", BaseId + 0x103, "散らす(12 点)", g =>
            {
                g.OriginAnchorId = new AssetId<AnchorMarker>(overhead.Id, AssetType.Anchor);
                g.Layout = AnchorLayoutKind.Random;
                g.RandomCount = 12;
                g.RandomRadius = 3f;
                g.RandomSeed = 7;
                g.SharedVfx = new[] { Vfx(vfx[2]) };
            });
            var manual = CreateGroup(folder, "ANCG_Sample_Manual", BaseId + 0x104, "手置き(5 点)", g =>
            {
                g.Layout = AnchorLayoutKind.Manual;
                g.Points = new[]
                {
                    Point("A", new Vector3(-2f, 0f, 0f)),
                    Point("B", new Vector3(-1f, 0f, 1f)),
                    Point("C", new Vector3(0f, 0f, 2f)),
                    Point("D", new Vector3(1f, 0f, 1f)),
                    Point("E", new Vector3(2f, 0f, 0f)),
                };
                g.SharedVfx = new[] { Vfx(vfx[0]) };
            });

            DDriveAssetSave.SaveAllSuppressed();
            return new Result
            {
                Anchors = new[] { hand, muzzle, flash, head, feet, overhead },
                Main = main,
                Groups = new[] { main, ring, line, random, manual, child },
                Vfx = vfx,
                Se = se,
                Folder = folder,
            };
        }

        private static AssetId<VfxMarker> Vfx(VfxData d) => new AssetId<VfxMarker>(d.Id, AssetType.Vfx);

        private static AnchorGroupPoint Point(string name, Vector3 offset) =>
            new AnchorGroupPoint { Name = name, LocalOffset = offset, LocalScale = Vector3.one };

        // 値は CreateAsset の前に入れる(作ったあとに書き換えると、Unity が先にディスクの既定値で読み直したとき失われることがある)。
        private static T Create<T>(string folder, string name, ulong id, string display, System.Action<T> setup = null) where T : AssetDataBase
        {
            var d = ScriptableObject.CreateInstance<T>();
            d.Id = id;
            d.DisplayName = display;
            d.Category = "Sample";
            setup?.Invoke(d);
            AssetDatabase.CreateAsset(d, $"{folder}/{name}.asset");
            return d;
        }

        private static AnchorData CreateAnchor(string folder, string name, ulong id, string display, System.Action<AnchorData> setup) =>
            Create(folder, name, id, display, setup);

        private static AnchorGroupData CreateGroup(string folder, string name, ulong id, string display, System.Action<AnchorGroupData> setup) =>
            Create(folder, name, id, display, setup);
    }
}
