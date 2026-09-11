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

        [InitializeOnLoadMethod]
        private static void Register() => ObjectChangeEvents.changesPublished += OnChanges;

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
                if (EditorUtility.InstanceIDToObject(evt.instanceId) is TextureData data && Pending.Add(evt.instanceId))
                {
                    // 変更イベントの中で Reimport しない(delayCall)。同じフレームの多重変更は 1 回にまとめる。
                    var id = evt.instanceId;
                    EditorApplication.delayCall += () =>
                    {
                        Pending.Remove(id);
                        if (data != null)
                        {
                            Apply(data);
                        }
                    };
                }
            }
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
                Debug.LogWarning($"[DDrive] TextureData '{data.name}': Usage/Channel から求まる Texture Type({desiredType})がファイル名規約 '{rule.Name}'({rule.Type})と食い違うため適用しません。ファイル名か Data のどちらかを合わせてください: {path}");
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

                if (importer.spriteBorder != data.SliceBorder)
                {
                    importer.spriteBorder = data.SliceBorder;
                    changes.Add($"Sprite Border → {data.SliceBorder}");
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
