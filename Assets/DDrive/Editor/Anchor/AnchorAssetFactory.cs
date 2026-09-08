using System.Collections.Generic;
using System.Text;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Menu;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Anchoring;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Anchor
{
    // [21_anchor_spec.md] §3.6 / §3.9 — 既存のボーン・ヒエラルキー・AnchorRig・埋め込み AnchorDef から
    // AnchorData アセットを生成する。命名・配置・カタログ登録は AssetCreationService に任せる。
    public static class AnchorAssetFactory
    {
        public const string DefaultCategory = "Common";

        // 埋め込み AnchorDef(VfxData.Anchor / SeData.Anchor)からアセット化する。
        public static AnchorData CreateFromDef(in AnchorDef def, string displayName, string category, string identifier,
            string gameDataRoot = AssetCreationService.DefaultGameDataRoot)
        {
            var source = def;
            var asset = AssetCreationService.Create(typeof(AnchorData), AssetType.Anchor, displayName, category, identifier,
                a => ((AnchorData)a).CopyFrom(source), gameDataRoot) as AnchorData;
            return asset;
        }

        // Transform(ボーン配下に置いた空オブジェクト、AnchorPoint、既存の子オブジェクト等)から
        // 「親を基準にした AnchorDef」を求める。親が無ければワールド固定。
        // AnchorPoint が付いていれば SpawnOffset とランダム項目も写す(生成後は AnchorPoint 本体を参照しないため二重適用しない)。
        public static AnchorData CreateFromTransform(Transform source, string category, string identifier = null,
            string gameDataRoot = AssetCreationService.DefaultGameDataRoot)
        {
            if (source == null)
            {
                Debug.LogWarning("[DDrive] Anchor 作成: Transform が指定されていません。");
                return null;
            }

            var def = DescribeRelativeToParent(source);
            var id = string.IsNullOrEmpty(identifier) ? ToIdentifier(source.name) : identifier;
            var asset = AssetCreationService.Create(typeof(AnchorData), AssetType.Anchor, source.name, category, id,
                a =>
                {
                    var anchor = (AnchorData)a;
                    anchor.CopyFrom(def);
                    CopyPointRandom(source, anchor);
                }, gameDataRoot) as AnchorData;
            return asset;
        }

        // AnchorRig 配下の AnchorPoint ごとに AnchorData を作り、AnchorPoint の親子関係を Parent の連鎖として写す。
        // ルート(祖先に AnchorPoint が無い)の基準はその AnchorPoint の親 Transform 名(通常は AnchorRig 名)。
        public static List<AnchorData> CreateFromRig(GameObject rigRoot, string category = null,
            string gameDataRoot = AssetCreationService.DefaultGameDataRoot)
        {
            var created = new List<AnchorData>();
            if (rigRoot == null)
            {
                Debug.LogWarning("[DDrive] Anchor 一括生成: AnchorRig が指定されていません。");
                return created;
            }

            category = string.IsNullOrEmpty(category) ? ToIdentifier(rigRoot.name) : category;
            var byPoint = new Dictionary<AnchorPoint, AnchorData>();

            // GetComponentsInChildren は親 → 子の順で返るので、親の AnchorData が先に出来ている。
            foreach (var point in rigRoot.GetComponentsInChildren<AnchorPoint>(true))
            {
                var ancestor = FindAncestorPoint(point.transform);
                AnchorDef def;
                if (ancestor != null)
                {
                    def = DescribeRelativeTo(point.transform, ancestor.transform);
                    def.Space = AnchorSpace.World; // 子は継承するので既定値のまま
                    def.Path = null;
                }
                else
                {
                    def = DescribeRelativeToParent(point.transform);
                }

                var parentAsset = ancestor != null && byPoint.TryGetValue(ancestor, out var p) ? p : null;
                var capturedDef = def;
                var capturedPoint = point;
                var asset = AssetCreationService.Create(typeof(AnchorData), AssetType.Anchor, point.name, category, ToIdentifier(point.name),
                    a =>
                    {
                        var anchor = (AnchorData)a;
                        anchor.CopyFrom(capturedDef);
                        CopyPointRandom(capturedPoint.transform, anchor);
                        if (parentAsset != null)
                        {
                            anchor.Parent = new AssetId<AnchorMarker>(parentAsset.Id, AssetType.Anchor);
                        }
                    }, gameDataRoot) as AnchorData;

                if (asset != null)
                {
                    byPoint[point] = asset;
                    created.Add(asset);
                }
            }

            return created;
        }

        // 親 Transform を基準(NamedObject=親の名前)にした定義。親が無ければ World。
        public static AnchorDef DescribeRelativeToParent(Transform t)
        {
            var parent = t.parent;
            if (parent == null)
            {
                var world = AnchorDef.WorldDefault;
                world.LocalOffset = t.position;
                world.LocalEuler = t.eulerAngles;
                world.LocalScale = t.lossyScale;
                ApplyPointOffset(t, ref world, Quaternion.identity, Vector3.one);
                return world;
            }

            var def = DescribeRelativeTo(t, parent);
            def.Space = AnchorSpace.NamedObject;
            def.Path = parent.name;
            return def;
        }

        // basis を基準にした相対位置・回転・スケール。AnchorPoint の SpawnOffset は t 自身の向きで basis 空間へ畳み込む。
        public static AnchorDef DescribeRelativeTo(Transform t, Transform basis)
        {
            var def = AnchorDef.WorldDefault;
            def.LocalOffset = basis.InverseTransformPoint(t.position);
            var localRot = Quaternion.Inverse(basis.rotation) * t.rotation;
            def.LocalEuler = localRot.eulerAngles;
            var basisScale = basis.lossyScale;
            var scale = t.lossyScale;
            def.LocalScale = new Vector3(
                basisScale.x != 0f ? scale.x / basisScale.x : 1f,
                basisScale.y != 0f ? scale.y / basisScale.y : 1f,
                basisScale.z != 0f ? scale.z / basisScale.z : 1f);
            ApplyPointOffset(t, ref def, localRot, def.LocalScale);
            return def;
        }

        private static void ApplyPointOffset(Transform t, ref AnchorDef def, Quaternion localRot, Vector3 localScale)
        {
            if (t.TryGetComponent<AnchorPoint>(out var point) && point.SpawnOffset != Vector3.zero)
            {
                def.LocalOffset += localRot * Vector3.Scale(localScale, point.SpawnOffset);
            }
        }

        private static void CopyPointRandom(Transform t, AnchorData anchor)
        {
            if (!t.TryGetComponent<AnchorPoint>(out var point))
            {
                return;
            }

            anchor.PositionJitterRadius = point.PositionJitterRadius;
            anchor.EulerJitter = point.EulerJitter;
            anchor.ScaleRange = point.ScaleRange;
        }

        private static AnchorPoint FindAncestorPoint(Transform t)
        {
            var cursor = t.parent;
            while (cursor != null)
            {
                if (cursor.TryGetComponent<AnchorPoint>(out var point))
                {
                    return point;
                }

                cursor = cursor.parent;
            }

            return null;
        }

        // "Anchor_RightHand" → "RightHand"、記号除去、先頭大文字。空になったら "Anchor"。
        public static string ToIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "Anchor";
            }

            if (name.StartsWith("Anchor_", System.StringComparison.OrdinalIgnoreCase) && name.Length > 7)
            {
                name = name.Substring(7);
            }

            var sb = new StringBuilder();
            var upperNext = true;
            foreach (var c in name)
            {
                if (char.IsLetterOrDigit(c) && c < 128)
                {
                    sb.Append(upperNext ? char.ToUpperInvariant(c) : c);
                    upperNext = false;
                }
                else
                {
                    upperNext = true;
                }
            }

            var result = sb.ToString();
            if (result.Length == 0 || char.IsDigit(result[0]))
            {
                result = "Anchor" + result;
            }

            return result;
        }

        // ── メニュー ──

        [MenuItem(DDriveMenu.Generate + "選択した Transform から Anchor を作成")]
        private static void MenuCreateFromSelection()
        {
            var t = Selection.activeTransform;
            if (t == null)
            {
                Debug.LogWarning("[DDrive] Hierarchy で Transform を選択してから実行してください。");
                return;
            }

            var asset = CreateFromTransform(t, DefaultCategory);
            if (asset != null)
            {
                Debug.Log($"[DDrive] Anchor を作成しました: {AssetDatabase.GetAssetPath(asset)}");
                EditorGUIUtility.PingObject(asset);
            }
        }

        [MenuItem(DDriveMenu.Generate + "選択した AnchorRig から Anchor を一括生成")]
        private static void MenuCreateFromRig()
        {
            var go = Selection.activeGameObject;
            if (go == null || go.GetComponentInChildren<AnchorPoint>(true) == null)
            {
                Debug.LogWarning("[DDrive] AnchorPoint を含む AnchorRig(またはその親)を選択してから実行してください。");
                return;
            }

            var created = CreateFromRig(go);
            Debug.Log($"[DDrive] Anchor を {created.Count} 件生成しました(カテゴリ: {ToIdentifier(go.name)})。");
            if (created.Count > 0)
            {
                EditorGUIUtility.PingObject(created[0]);
            }
        }
    }
}
