using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Runtime.Anchoring;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Preview
{
    // SceneView 上の Anchor 編集ハンドル(移動/回転)と、AnchorData の連鎖をエディタ側で合成する補助。
    // VfxEditorWindow(埋め込み Anchor)と AnchorEditorWindow(AnchorData)が同じ描画・逆変換を使う
    // ([21_anchor_spec.md] §3.6)。式は Runtime の AnchorPose / AnchorChain と同じものを通す。
    //
    // 2026-09-17(U-24): 最終位置だけでなく「基準(原点)」も描くようにした。基準 = LocalOffset を積む前の
    // 出発点で、ルートは解決先 Transform(Space/Path で見つけたボーン等。見つからなければワールド原点)、
    // 連鎖の各段はひとつ上の段の合成姿勢。基準には 3 軸とラベル(名前 + ワールド座標)を描き、
    // 基準 → 最終位置を線で結んでオフセット量を表示する。
    public static class AnchorSceneHandles
    {
        // 基準の色。対象(最終位置)のギズモとは別色にして「どちらが基準か」を一目で分かるようにする。
        public static readonly Color OriginColor = new(1f, 0.4f, 0.45f);

        public struct Result
        {
            public bool PositionChanged;
            public Vector3 LocalOffset;   // 合成済み(target-local)の新しいオフセット
            public bool RotationChanged;
            public Vector3 LocalEuler;    // 合成済み(target-local)の新しい回転
        }

        // anchor: 表示・編集対象の(合成済み)定義。baseTransform: 解決先(null = ワールド)。extraOffset: AnchorPoint/ランダム分。
        // 戻り値の LocalOffset/LocalEuler は合成済み空間の値。埋め込み Anchor ならそのまま書き戻し、
        // AnchorData の子ノードなら ToChildLocal で親基準に変換してから書き戻す。
        public static Result Draw(in AnchorDef anchor, Transform baseTransform, Vector3 extraOffset, string label, Color color)
        {
            var result = default(Result);
            var worldPos = AnchorPose.WorldPosition(anchor, baseTransform, extraOffset);
            var worldRot = AnchorPose.WorldRotation(anchor, baseTransform, Quaternion.identity);
            DrawTargetMarker(anchor, baseTransform, extraOffset, label, color);

            if (Tools.current == Tool.Rotate)
            {
                EditorGUI.BeginChangeCheck();
                var newRot = Handles.RotationHandle(worldRot, worldPos);
                if (EditorGUI.EndChangeCheck())
                {
                    result.RotationChanged = true;
                    result.LocalEuler = AnchorPose.LocalEulerFromWorld(anchor, baseTransform, newRot);
                }

                return result;
            }

            EditorGUI.BeginChangeCheck();
            var handleRot = Tools.pivotRotation == PivotRotation.Local ? worldRot : Quaternion.identity;
            var newPos = Handles.PositionHandle(worldPos, handleRot);
            if (EditorGUI.EndChangeCheck())
            {
                result.PositionChanged = true;
                result.LocalOffset = AnchorPose.LocalOffsetFromWorld(baseTransform, newPos, extraOffset);
            }

            return result;
        }

        // 対象(最終位置)の円とラベル。Draw と同じ見た目で、ハンドル(編集)は付けない。
        // 他アセット(AnchorData)を参照している VFX など、その場では編集させない表示に使う。
        public static void DrawTargetMarker(in AnchorDef anchor, Transform baseTransform, Vector3 extraOffset, string label, Color color)
        {
            var worldPos = AnchorPose.WorldPosition(anchor, baseTransform, extraOffset);
            var size = HandleUtility.GetHandleSize(worldPos);

            Handles.color = color;
            Handles.DrawWireDisc(worldPos, Vector3.up, size * 0.25f);
            Handles.Label(worldPos + Vector3.up * size * 0.35f, label);
        }

        // 描画権を持たないウィンドウ用: ハンドル無し・薄い円と短いラベルだけ(重なっても読める最小限)。
        public static void DrawInactiveMarker(in AnchorDef anchor, Transform baseTransform, Vector3 extraOffset, string label, Color color)
        {
            var worldPos = AnchorPose.WorldPosition(anchor, baseTransform, extraOffset);
            var size = HandleUtility.GetHandleSize(worldPos);
            Handles.color = new Color(color.r, color.g, color.b, 0.25f);
            Handles.DrawWireDisc(worldPos, Vector3.up, size * 0.18f);
            Handles.Label(worldPos + Vector3.up * size * 0.28f, label, FadedStyle(color));
        }

        // [08_presentation.md] 実装メモ(2026-09-20、指摘1「SceneView の点をクリックしても選択されない」) —
        // クリック可能な目印。描画権を持たないウィンドウの薄い目印(active=false)にも、描画権を持つ
        // ウィンドウの非選択項目(active=true、旧 DrawSelectableEffectiveMarker 相当)にも使う共通 API
        // (PresentationEditorWindow.SceneAnchors.cs / VfxEditorWindow.Anchor.cs / AnchorEditorWindow.cs が使う。
        // コピペしない)。当たり判定(pickSize)は可視の円(DrawTargetMarker と同じ半径 handleSize*0.25)に
        // 合わせて広げる(以前は handleSize*0.12〜0.18 相当で小さすぎてクリックしにくかった)。
        // 戻り値: このフレームでクリックされたら true(呼び出し側が選択・オーナー切替を行う)。
        public static bool DrawClickableMarker(in AnchorDef anchor, Transform baseTransform, Vector3 extraOffset, string label, Color color, bool active)
        {
            var worldPos = AnchorPose.WorldPosition(anchor, baseTransform, extraOffset);
            var handleSize = HandleUtility.GetHandleSize(worldPos);
            var visualSize = handleSize * (active ? 0.12f : 0.1f);
            var pickSize = handleSize * 0.25f;
            Handles.color = active ? color : new Color(color.r, color.g, color.b, 0.3f);
            var clicked = Handles.Button(worldPos, Quaternion.identity, visualSize, pickSize, Handles.SphereHandleCap);
            Handles.Label(worldPos + Vector3.up * visualSize * 1.6f, label, active ? EditorStyles.miniLabel : FadedStyle(color));
            return clicked;
        }

        // ── 基準(原点)の描画(U-24) ──

        // 基準 = LocalOffset を積む前の出発点。解決先 Transform の位置に 3 軸とラベルを描き、
        // AnchorPoint の SpawnOffset がある場合は「解決先 → SpawnOffset 適用後」を点線で結ぶ。
        // 戻り値は LocalOffset の起点になるワールド位置(= extraOffset 適用後の基準点)。
        public static Vector3 DrawOrigin(Transform baseTransform, Vector3 extraOffset, string label, bool followRotation)
        {
            var rootPos = baseTransform != null ? baseTransform.position : Vector3.zero;
            var rootRot = baseTransform != null ? baseTransform.rotation : Quaternion.identity;
            var originPos = baseTransform != null ? baseTransform.TransformPoint(extraOffset) : extraOffset;
            var size = HandleUtility.GetHandleSize(rootPos);

            DrawAxes(rootPos, rootRot, size * 0.45f);

            Handles.color = OriginColor;
            Handles.SphereHandleCap(0, rootPos, Quaternion.identity, size * 0.07f, EventType.Repaint);
            Handles.Label(
                rootPos - Vector3.up * size * 0.22f,
                $"{label} {Format(rootPos)}\n回転の基準: {(followRotation ? "この向きに追従" : "ワールド")}",
                OriginStyle());

            if (extraOffset != Vector3.zero)
            {
                Handles.color = OriginColor;
                Handles.DrawDottedLine(rootPos, originPos, 3f);
                Handles.Label(originPos + Vector3.up * size * 0.16f, $"★AnchorPoint SpawnOffset {Format(extraOffset)}", OriginStyle());
            }

            return originPos;
        }

        // 基準 → 最終位置を実線で結び、中点にオフセット量と距離を出す。
        public static void DrawOffsetLink(Vector3 originWorld, Vector3 targetWorld, Vector3 localOffset, Color color)
        {
            var delta = targetWorld - originWorld;
            var size = HandleUtility.GetHandleSize(targetWorld);
            Handles.color = color;

            if (delta.sqrMagnitude < 1e-8f)
            {
                Handles.Label(targetWorld + Vector3.up * size * 0.18f, "LocalOffset 0(基準と同じ位置)", FadedStyle(color));
                return;
            }

            Handles.DrawLine(originWorld, targetWorld, 2f);
            Handles.Label(Vector3.Lerp(originWorld, targetWorld, 0.5f), $"LocalOffset {Format(localOffset)}  ({delta.magnitude:0.##}m)", FadedStyle(color));
        }

        // 基準のラベル文字列。解決できなかった場合は「ワールド原点扱い」であることを明示する
        // (デザイナーが「設定が効いていない」のか「そこが基準」なのかを見分けられるように)。
        public static string DescribeBase(in AnchorDef def, Transform baseTransform)
        {
            if (baseTransform != null)
            {
                return $"基準: {baseTransform.name}";
            }

            return def.Space switch
            {
                AnchorSpace.World => "基準: ワールド原点",
                AnchorSpace.ContextTarget => "基準: ⚠ スポーン先が未指定 → ワールド原点",
                _ => string.IsNullOrEmpty(def.Path)
                    ? "基準: ⚠ Path が空 → ワールド原点"
                    : $"基準: ⚠ '{def.Path}' が見つからない → ワールド原点",
            };
        }

        // 連鎖(基準 → 各中間段)を点線で結び、各段に基準としての 3 軸とラベルを描く。
        // 最終段(対象)の位置・ハンドルは Draw が、基準 → 最終位置の線は DrawOffsetLink が描くため、
        // ここでは最終段の手前までを描き、「最終段の基準になるワールド位置」を返す。
        public static Vector3 DrawChain(IReadOnlyList<AnchorData> rootToTarget, Transform baseTransform, Vector3 extraOffset, Vector3 originWorld, Color color)
        {
            var previous = originWorld;
            if (rootToTarget == null || rootToTarget.Count == 0)
            {
                return previous;
            }

            for (var i = 0; i < rootToTarget.Count - 1; i++)
            {
                var def = AnchorChainEditor.ComposeUpTo(rootToTarget, i);
                var pos = AnchorPose.WorldPosition(def, baseTransform, extraOffset);
                var rot = AnchorPose.WorldRotation(def, baseTransform, Quaternion.identity);
                DrawChainNode(previous, pos, rot, $"基準{i + 1}: {rootToTarget[i].name}", rootToTarget[i].PositionJitterRadius, color);
                previous = pos;
            }

            // 対象自身のランダム半径(最終段)。位置の線とラベルは呼び出し側が描く。
            var last = rootToTarget[rootToTarget.Count - 1];
            var lastPos = AnchorPose.WorldPosition(AnchorChainEditor.ComposeUpTo(rootToTarget, rootToTarget.Count - 1), baseTransform, extraOffset);
            DrawJitter(last.PositionJitterRadius, lastPos, new Color(color.r, color.g, color.b, 0.6f));

            return previous;
        }

        // 汎用: 連鎖の 1 中間段(軸 + ラベル + 前段からの点線 + ジッター半径)を描く。DrawChain の内部から、
        // および AnchorData を持たない仮想ノード(Presentation の「トラック Anchor」等)からも呼べる
        // ([08_presentation.md] 実装メモ 2026-09-19「トラック/アセット両方の Anchor 参照」)。
        public static void DrawChainNode(Vector3 previousWorld, Vector3 worldPos, Quaternion worldRot, string label, float positionJitterRadius, Color color)
        {
            var size = HandleUtility.GetHandleSize(worldPos);
            var chained = new Color(color.r, color.g, color.b, 0.6f);

            Handles.color = chained;
            Handles.DrawDottedLine(previousWorld, worldPos, 4f);
            Handles.DrawWireDisc(worldPos, Vector3.up, size * 0.12f);
            DrawAxes(worldPos, worldRot, size * 0.25f);
            Handles.color = chained;
            Handles.Label(worldPos + Vector3.up * size * 0.2f, label, OriginStyle());

            DrawJitter(positionJitterRadius, worldPos, chained);
        }

        // ── 内部 ──

        private static void DrawJitter(float positionJitterRadius, Vector3 pos, Color color)
        {
            if (positionJitterRadius <= 0f)
            {
                return;
            }

            Handles.color = color;
            Handles.DrawWireDisc(pos, Vector3.up, positionJitterRadius);
            Handles.DrawWireDisc(pos, Vector3.right, positionJitterRadius);
            Handles.DrawWireDisc(pos, Vector3.forward, positionJitterRadius);
        }

        // 基準の姿勢を示す小さな 3 軸(X=赤 / Y=緑 / Z=青)。Unity の Transform ギズモと同じ色にする。
        private static void DrawAxes(Vector3 position, Quaternion rotation, float length)
        {
            Handles.color = Handles.xAxisColor;
            Handles.DrawLine(position, position + rotation * Vector3.right * length, 3f);
            Handles.color = Handles.yAxisColor;
            Handles.DrawLine(position, position + rotation * Vector3.up * length, 3f);
            Handles.color = Handles.zAxisColor;
            Handles.DrawLine(position, position + rotation * Vector3.forward * length, 3f);
        }

        private static string Format(Vector3 v) => $"({v.x:0.##}, {v.y:0.##}, {v.z:0.##})";

        private static GUIStyle _originStyle;

        private static GUIStyle OriginStyle()
        {
            if (_originStyle == null && EditorStyles.miniLabel != null)
            {
                _originStyle = new GUIStyle(EditorStyles.miniLabel) { richText = false };
            }

            if (_originStyle == null)
            {
                return GUIStyle.none;
            }

            _originStyle.normal.textColor = OriginColor;
            return _originStyle;
        }

        private static GUIStyle _fadedStyle;

        private static GUIStyle FadedStyle(Color color)
        {
            if (_fadedStyle == null && EditorStyles.miniLabel != null)
            {
                _fadedStyle = new GUIStyle(EditorStyles.miniLabel);
            }

            if (_fadedStyle == null)
            {
                return GUIStyle.none;
            }

            _fadedStyle.normal.textColor = new Color(color.r, color.g, color.b, 0.9f);
            return _fadedStyle;
        }
    }

    // AnchorData の連鎖をアセット参照(AssetDatabase)から集めて合成する。Registry を持たないエディタ UI 用。
    public static class AnchorChainEditor
    {
        // 対象から Parent を辿り、ルート → 対象の順に返す。循環・深さ超過は途中で打ち切る(Validator が別途報告する)。
        public static List<AnchorData> CollectRootToTarget(AnchorData target)
        {
            var leafToRoot = new List<AnchorData>();
            var cursor = target;
            while (cursor != null && leafToRoot.Count < AnchorChain.MaxDepth && !leafToRoot.Contains(cursor))
            {
                leafToRoot.Add(cursor);
                cursor = cursor.Parent.IsValid ? EditorAnchorRegistry.Find(cursor.Parent.Value) : null;
            }

            leafToRoot.Reverse();
            return leafToRoot;
        }

        // ルート → index 番目までを合成した AnchorDef(ランダム無し)。
        public static AnchorDef ComposeUpTo(IReadOnlyList<AnchorData> rootToTarget, int index)
        {
            var count = index + 1;
            var leafToRoot = new AnchorData[count];
            for (var i = 0; i < count; i++)
            {
                leafToRoot[i] = rootToTarget[index - i];
            }

            return AnchorChain.Compose(leafToRoot, count, sampleRandom: false).Def;
        }

        // 合成済み(target-local)のオフセットを、親までの合成姿勢を基準にした「子ノード自身の LocalOffset」へ戻す。
        // parent: ルート → 親 の合成結果(ルート自身なら null を渡す)。
        public static Vector3 ToChildLocalOffset(AnchorDef? parent, Vector3 composedOffset)
        {
            if (!parent.HasValue)
            {
                return composedOffset;
            }

            var p = parent.Value;
            var rot = Quaternion.Euler(p.LocalEuler);
            var scale = p.LocalScale == Vector3.zero ? Vector3.one : p.LocalScale;
            var local = Quaternion.Inverse(rot) * (composedOffset - p.LocalOffset);
            return new Vector3(
                scale.x != 0f ? local.x / scale.x : 0f,
                scale.y != 0f ? local.y / scale.y : 0f,
                scale.z != 0f ? local.z / scale.z : 0f);
        }

        public static Vector3 ToChildLocalEuler(AnchorDef? parent, Vector3 composedEuler)
        {
            if (!parent.HasValue)
            {
                return composedEuler;
            }

            return (Quaternion.Inverse(Quaternion.Euler(parent.Value.LocalEuler)) * Quaternion.Euler(composedEuler)).eulerAngles;
        }
    }
}
