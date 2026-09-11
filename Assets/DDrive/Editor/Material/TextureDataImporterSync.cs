using System.Collections.Generic;
using DDrive.Runtime.Material;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Materials
{
    // [06_material_texture.md] B-4 — TextureData の Usage / Channel / SliceBorder を TextureImporter に自動で反映する(2026-09-11)。
    // それまでは Validation の Fix を押したときだけ Importer が変わっていた(TextureDataValidator)。今は Data を編集した時点
    // (Inspector / Material Editor / Undo のいずれでも ObjectChangeEvents で検知)で同じ内容を適用する。
    //
    //   Usage=UI        : Texture Type=Sprite(Single)、SliceBorder を spriteBorder(L,B,R,T)に書き、Sprite が未設定なら割り当てる
    //   Channel=Normal  : Texture Type=NormalMap(Usage=Model のとき)
    //   Channel=Mask    : sRGB off / Albedo・Emission: sRGB on(Usage=Model のとき。Other は触らない)
    //   Normal 以外で NormalMap になっていれば Default に戻す
    //
    // ファイル名規約(TextureImportProfile、再インポートのたびに OnPreprocessTexture で適用される)と食い違う設定は書かない。
    // 書いても次のインポートで規約に戻されて打ち消し合うため、警告を出してファイル名か Data のどちらかを直してもらう。
    public static class TextureDataImporterSync
    {
        // テストや一括処理で自動適用を止めるスイッチ(通常は true)。
        public static bool AutoApply = true;

        private static readonly HashSet<int> Pending = new();
        private static readonly List<int> PendingBuffer = new();
        private static bool _scheduled;

        // 規約との食い違い警告を出した TextureData(1 アセットにつき 1 回だけ出す。プロジェクト変更・ドメインリロードで戻す)。
        private static readonly HashSet<int> WarnedConflicts = new();

        [InitializeOnLoadMethod]
        private static void Register()
        {
            ObjectChangeEvents.changesPublished += OnChanges;
            EditorApplication.projectChanged += WarnedConflicts.Clear;
        }

        private static void OnChanges(ref ObjectChangeEventStream stream)
        {
            if (!AutoApply)
            {
                return;
            }

            for (var i = 0; i < stream.length; i++)
            {
                if (stream.GetEventType(i) != ObjectChangeKind.ChangeAssetObjectProperties)
                {
                    continue;
                }

                stream.GetChangeAssetObjectPropertiesEvent(i, out var evt);
                if (EditorUtility.InstanceIDToObject(evt.instanceId) is TextureData)
                {
                    MarkPending(evt.instanceId);
                }
            }
        }

        // 次の delayCall で Apply する対象に積む(テストから直接呼べるように公開している)。
        public static void MarkPending(TextureData data)
        {
            if (data != null)
            {
                MarkPending(data.GetInstanceID());
            }
        }

        private static void MarkPending(int instanceId)
        {
            if (!Pending.Add(instanceId) || _scheduled)
            {
                return;
            }

            // 変更イベントの中で Reimport しない(delayCall)。同じフレームの多重変更は 1 回にまとめる。
            _scheduled = true;
            EditorApplication.delayCall += ProcessPending;
        }

        // 積まれている TextureData に Apply する。通常は delayCall から呼ばれる(テストは直接呼ぶ)。
        public static void ProcessPending()
        {
            _scheduled = false;
            if (Pending.Count == 0)
            {
                return;
            }

            PendingBuffer.Clear();
            PendingBuffer.AddRange(Pending);
            Pending.Clear();
            for (var i = 0; i < PendingBuffer.Count; i++)
            {
                if (EditorUtility.InstanceIDToObject(PendingBuffer[i]) is TextureData data)
                {
                    Apply(data);
                }
            }

            PendingBuffer.Clear();
        }

        // Data の設定を Importer に書く。戻り値は変更点(空なら既に一致 / 対象外)。profile はテスト用の差し替え(通常は FindOrDefault)。
        public static List<string> Apply(TextureData data) => Apply(data, null);

        public static List<string> Apply(TextureData data, TextureImportProfile profileOverride)
        {
            var changes = new List<string>();
            if (data == null || data.Texture == null)
            {
                return changes;
            }

            var path = AssetDatabase.GetAssetPath(data.Texture);
            if (string.IsNullOrEmpty(path) || AssetImporter.GetAtPath(path) is not TextureImporter importer)
            {
                return changes;
            }

            // ファイル名規約との衝突チェック
            // profileOverride を渡した場合はパス条件(AppliesTo)を呼び出し側の責任として省く(テスト配下は AppliesTo が常に false のため)。
            var profile = profileOverride != null ? profileOverride : TextureImportProfile.FindOrDefault();
            TextureImportProfile.Rule rule = default;
            var hasRule = profile != null && profile.Enabled && (profileOverride != null || profile.AppliesTo(path)) && profile.TryMatch(path, out rule);

            var desiredType = DesiredType(data, importer.textureType);
            if (hasRule && rule.Type != desiredType)
            {
                // 同じ Data を編集するたびに出さない(1 アセットにつき 1 回。2026-09-11 レビュー対応)。
                if (WarnedConflicts.Add(data.GetInstanceID()))
                {
                    Debug.LogWarning($"[DDrive] TextureData '{data.name}': Usage/Channel から求まる Texture Type({desiredType})がファイル名規約 '{rule.Name}'({rule.Type})と食い違うため適用しません。ファイル名か Data のどちらかを合わせてください: {path}");
                }

                return changes;
            }

            if (importer.textureType != desiredType)
            {
                importer.textureType = desiredType;
                changes.Add($"Texture Type → {desiredType}");
            }

            if (data.Usage == TextureUsage.Model && desiredType != TextureImporterType.NormalMap)
            {
                bool? srgb = data.Channel switch
                {
                    TextureChannel.Mask => false,
                    TextureChannel.Albedo => true,
                    TextureChannel.Emission => true,
                    _ => null,
                };
                if (srgb.HasValue && !(hasRule && rule.SRgb != srgb.Value) && importer.sRGBTexture != srgb.Value)
                {
                    importer.sRGBTexture = srgb.Value;
                    changes.Add($"sRGB → {srgb.Value}");
                }
            }

            if (data.Usage == TextureUsage.UI)
            {
                // None、または Multiple なのに実際には 1 枚も切られていない(Default から切り替えた直後の Unity 既定)なら Single にする。
                // 切り済みのスプライトシート(Multiple + Sprite あり)はそのまま。
                if (importer.spriteImportMode == SpriteImportMode.None ||
                    (importer.spriteImportMode == SpriteImportMode.Multiple && !HasAnySprite(path)))
                {
                    importer.spriteImportMode = SpriteImportMode.Single;
                    changes.Add("Sprite Mode → Single");
                }

                // テクスチャより大きい境界は Importer 側で切り詰められるので、先に同じ形に丸めてから比べる。
                // 丸めずに書くと「書く → 切り詰められる → 次も食い違う」で毎回再インポートになる(2026-09-11 レビュー対応)。
                // 境界の座標空間は元画像サイズ(Max Size で縮小されたテクスチャの大きさではない。SpriteSlicer と同じ)。
                importer.GetSourceTextureWidthAndHeight(out var sourceWidth, out var sourceHeight);
                var border = ClampBorder(data.SliceBorder, sourceWidth, sourceHeight);
                if (importer.spriteBorder != border)
                {
                    importer.spriteBorder = border;
                    changes.Add($"Sprite Border → {border}");
                }
            }

            if (changes.Count > 0)
            {
                importer.SaveAndReimport();
            }

            // UI なら Sprite を割り当てる(Importer が変わっていなくても未設定なら埋める)
            if (data.Usage == TextureUsage.UI && data.Sprite == null)
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite != null)
                {
                    Undo.RecordObject(data, "Assign Sprite");
                    data.Sprite = sprite;
                    EditorUtility.SetDirty(data);
                    changes.Add("Sprite を割り当て");
                }
            }

            return changes;
        }

        // 9-slice の境界(x=左 / y=下 / z=右 / w=上)をテクスチャサイズに収める。左+右 ≤ 幅、下+上 ≤ 高さ。
        public static Vector4 ClampBorder(Vector4 border, int width, int height)
        {
            var left = Mathf.Clamp(Mathf.Round(border.x), 0f, width);
            var bottom = Mathf.Clamp(Mathf.Round(border.y), 0f, height);
            var right = Mathf.Clamp(Mathf.Round(border.z), 0f, width - left);
            var top = Mathf.Clamp(Mathf.Round(border.w), 0f, height - bottom);
            return new Vector4(left, bottom, right, top);
        }

        private static bool HasAnySprite(string path)
        {
            foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
            {
                if (o is Sprite)
                {
                    return true;
                }
            }

            return false;
        }

        private static TextureImporterType DesiredType(TextureData data, TextureImporterType current)
        {
            if (data.Usage == TextureUsage.UI)
            {
                return TextureImporterType.Sprite;
            }

            if (data.Channel == TextureChannel.Normal)
            {
                return TextureImporterType.NormalMap;
            }

            // Normal 以外で NormalMap / Sprite のままなら Default に戻す。それ以外(Cookie 等)は触らない
            return current == TextureImporterType.NormalMap || current == TextureImporterType.Sprite ? TextureImporterType.Default : current;
        }
    }
}
