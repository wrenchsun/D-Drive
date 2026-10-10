using System;
using System.Collections.Generic;
using DDrive.Editor.Anchor;
using DDrive.Editor.Preview;
using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Vfx;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Vfx
{
    // VFX の Anchor を SceneView / ドロップダウン / 解決状況の文で扱う共通部品。
    // VfxEditorWindow と試験版エディタ(PrototypeE)の両方から使う(旧エディタの private だった処理を切り出したもの)。
    public static class VfxAnchorSceneGui
    {
        // 「今の設定でどこに出るか」の説明文。AnchorId 使用時は空。
        public static string DescribeResolution(VfxData target, GameObject attachTarget)
        {
            if (target == null || target.AnchorId.IsValid)
            {
                return string.Empty;
            }

            var anchor = target.Anchor;
            var attach = attachTarget != null ? attachTarget.transform : null;

            switch (anchor.Space)
            {
                case AnchorSpace.World:
                    return "解決: World 固定(スポーン先は使いません。オフセット = ワールド座標)";

                case AnchorSpace.ContextTarget:
                    return attach != null
                        ? $"解決: ✓ スポーン先 '{attach.name}' そのもの"
                        : "解決: ⚠ スポーン先が未指定のため、ワールド固定として扱われます";

                default:
                    if (attach == null)
                    {
                        return "解決: ⚠ スポーン先が未指定のため Path を検索できません(ワールド固定扱い)。「スポーン先」にキャラクターや AnchorRig を指定してください";
                    }

                    if (string.IsNullOrEmpty(anchor.Path))
                    {
                        return "解決: ⚠ Path が空です。「一覧から選択」でボーンか ★AnchorPoint を選んでください";
                    }

                    var resolved = AnchorResolver.Resolve(anchor, attach);
                    if (resolved == null)
                    {
                        return $"解決: ⚠ '{anchor.Path}' がスポーン先 '{attach.name}' の階層に見つかりません(ワールド固定扱い)";
                    }

                    var isPoint = resolved.TryGetComponent<AnchorPoint>(out _);
                    return $"解決: ✓ '{resolved.name}'{(isPoint ? "(★AnchorPoint: SpawnOffset/ランダム散らばりが追加適用されます)" : string.Empty)}";
            }
        }

        // スポーン先の階層から選べる Path の一覧(AnchorPoint を先頭に「★」付き)を menu に作り直す。
        public static void FillPathMenu(DropdownMenu menu, GameObject attachTarget, Action<string> select)
        {
            menu.MenuItems().Clear();

            if (attachTarget == null)
            {
                menu.AppendAction("(「スポーン先」にシーン内のキャラクターや AnchorRig を指定してください)", _ => { }, DropdownMenuAction.Status.Disabled);
                return;
            }

            foreach (var point in attachTarget.GetComponentsInChildren<AnchorPoint>(true))
            {
                var name = point.name;
                menu.AppendAction($"★ {name}", _ => select(name));
            }

            menu.AppendSeparator();

            foreach (var t in attachTarget.GetComponentsInChildren<Transform>(true))
            {
                var name = t.name;
                menu.AppendAction(name, _ => select(name));
            }
        }

        // SceneView の Anchor 目印とハンドル。再生中は実体の追従先、停止中はスポーン先から解決した Transform を基準に
        // Anchor のワールド姿勢を求め、ハンドルの結果を AnchorDef に逆変換して書き戻す。
        // applyAnchor: 埋め込み Anchor への書き戻し(Undo・SetDirty は呼び出し側)。afterEdit: UI の更新。
        public static void Draw(
            EditorWindow owner,
            VfxData target,
            SceneVfxPreviewDriver driver,
            Handle<VfxMarker> mainHandle,
            GameObject attachTarget,
            Action<Func<AnchorDef, AnchorDef>> applyAnchor,
            Action afterEdit)
        {
            if (target == null || driver == null)
            {
                return;
            }

            // AnchorId 使用時は AnchorEditor と同じ連鎖表示(基準 → 各段 → 最終位置)+ ハンドル編集にする。
            // ハンドルは参照先の AnchorData アセットを書き換える(同じ Anchor を使う他の VFX / SE にも効く)。
            List<AnchorData> assetChain = null;
            if (target.AnchorId.IsValid)
            {
                var asset = EditorAnchorRegistry.Find(target.AnchorId.Value);
                assetChain = asset != null ? AnchorChainEditor.CollectRootToTarget(asset) : null;
                if (assetChain == null || assetChain.Count == 0)
                {
                    return;
                }
            }

            var anchor = assetChain != null ? assetChain[0].ToDef() : target.Anchor;
            Transform baseTransform;
            var extraOffset = Vector3.zero;

            if (driver.IsPlaying(mainHandle) && driver.Manager.TryGetAnchorTarget(mainHandle, out var followTarget))
            {
                baseTransform = followTarget;
                extraOffset = driver.Manager.GetAnchorExtraOffset(mainHandle);
            }
            else
            {
                baseTransform = AnchorResolver.Resolve(anchor, attachTarget != null ? attachTarget.transform : null);
                if (baseTransform != null && baseTransform.TryGetComponent<AnchorPoint>(out var point))
                {
                    extraOffset = point.SpawnOffset;
                }
            }

            // 描画権が他のウィンドウにあるときは薄い目印だけ(重なりを避ける)。
            var vfxColor = new Color(0.35f, 0.85f, 0.65f);
            var vfxName = target.DisplayName ?? target.name;
            if (assetChain != null)
            {
                var targetDef = AnchorChainEditor.ComposeUpTo(assetChain, assetChain.Count - 1);
                if (!SceneGuiOwner.IsOwner(owner))
                {
                    if (AnchorSceneHandles.DrawClickableMarker(targetDef, baseTransform, extraOffset, $"VFX: {vfxName}", vfxColor, active: false))
                    {
                        SceneGuiOwner.Claim(owner);
                        owner.Focus();
                    }

                    return;
                }

                var chainOrigin = AnchorSceneHandles.DrawOrigin(baseTransform, extraOffset, AnchorSceneHandles.DescribeBase(anchor, baseTransform), anchor.FollowRotation);
                var chainParent = AnchorSceneHandles.DrawChain(assetChain, baseTransform, extraOffset, chainOrigin, vfxColor);
                AnchorSceneHandles.DrawOffsetLink(chainParent, AnchorPose.WorldPosition(targetDef, baseTransform, extraOffset), assetChain[assetChain.Count - 1].LocalOffset, vfxColor);
                var chainResult = AnchorSceneHandles.Draw(targetDef, baseTransform, extraOffset, $"VFX Anchor: {vfxName}(Anchor アセットを編集)", vfxColor);
                if (chainResult.PositionChanged || chainResult.RotationChanged)
                {
                    ApplyAnchorAssetHandle(driver, assetChain, chainResult, afterEdit);
                }

                return;
            }

            if (!SceneGuiOwner.IsOwner(owner))
            {
                if (AnchorSceneHandles.DrawClickableMarker(anchor, baseTransform, extraOffset, $"VFX: {vfxName}", vfxColor, active: false))
                {
                    SceneGuiOwner.Claim(owner);
                    owner.Focus();
                }

                return;
            }

            // 最終位置だけだと「何を基準にしたオフセットか」が分からないので、基準(解決先 Transform。
            // 未解決ならワールド原点)にも 3 軸とラベルを描き、基準 → 最終位置を線で結ぶ(U-24)。
            var originWorld = AnchorSceneHandles.DrawOrigin(baseTransform, extraOffset, AnchorSceneHandles.DescribeBase(anchor, baseTransform), anchor.FollowRotation);
            AnchorSceneHandles.DrawOffsetLink(originWorld, AnchorPose.WorldPosition(anchor, baseTransform, extraOffset), anchor.LocalOffset, vfxColor);
            var result = AnchorSceneHandles.Draw(anchor, baseTransform, extraOffset, $"VFX Anchor: {vfxName}", vfxColor);
            if (result.RotationChanged)
            {
                var euler = result.LocalEuler;
                applyAnchor(a =>
                {
                    a.LocalEuler = euler;
                    return a;
                });
                afterEdit?.Invoke();
            }

            if (result.PositionChanged)
            {
                var local = result.LocalOffset;
                applyAnchor(a =>
                {
                    a.LocalOffset = local;
                    return a;
                });
                afterEdit?.Invoke();
            }
        }

        // ハンドルの結果を参照先 AnchorData(連鎖の最終段)へ書き戻す。合成済みの値を親基準に変換するのは
        // AnchorEditor と同じ(AnchorChainEditor.ToChildLocal*)。
        private static void ApplyAnchorAssetHandle(SceneVfxPreviewDriver driver, List<AnchorData> chain, AnchorSceneHandles.Result result, Action afterEdit)
        {
            var asset = chain[chain.Count - 1];
            AnchorDef? parentDef = chain.Count > 1 ? AnchorChainEditor.ComposeUpTo(chain, chain.Count - 2) : null;

            Undo.RecordObject(asset, "Move Anchor");
            if (result.PositionChanged)
            {
                asset.LocalOffset = AnchorChainEditor.ToChildLocalOffset(parentDef, result.LocalOffset);
            }

            if (result.RotationChanged)
            {
                asset.LocalEuler = AnchorChainEditor.ToChildLocalEuler(parentDef, result.LocalEuler);
            }

            EditorUtility.SetDirty(asset);
            // Registry は同じ AnchorData インスタンスを既に持っているので Refresh(全アセット走査)は不要
            // (ドラッグ中は毎フレーム呼ばれる。docs/44 P2-2)。再生中の実体への反映だけ行う。
            driver?.ReapplyAnchorToAll();
            afterEdit?.Invoke();
            SceneView.RepaintAll();
        }
    }
}
