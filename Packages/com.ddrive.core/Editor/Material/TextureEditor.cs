using System.Collections.Generic;
using DDrive.Editor.Anim;
using DDrive.Editor.Inspector;
using DDrive.Editor.Menu;
using DDrive.Editor.Ux;
using DDrive.Runtime.Material;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Materials
{
    // Texture のエディター(docs/1010 UX-8。旧 MaterialEditorWindow の TextureData 分岐を、共通基盤の 1 カード + 管理情報にしたもの)。
    // 欄は TextureData の 6 つ(画像・使い道・用途・Sprite・拡縮・9-slice)+ 「見た目と Importer の現状」(画像のプレビュー・Importer の設定・
    // ファイル名の規約との一致・「命名規約を適用して再インポート」)。
    [DataEditor(typeof(TextureData), "Texture Editor で開く")]
    internal sealed class TextureEditor : UxEditorWindow<TextureData>
    {
        private const string Page = "material-editor";

        private Image _image;
        private Label _status;
        private Label _info;
        private Button _applyRule;
        private TextureImportProfile.Rule _matchedRule;
        private bool _hasMatchedRule;
        private int _infoSig = int.MinValue;

        // ── 入口 ──

        [MenuItem(DDriveMenu.Editors + "Texture")]
        public static void OpenFromMenu() => Open(Selection.activeObject as TextureData);

        public static void Open(TextureData target)
        {
            var w = OpenWindow<TextureEditor>("Texture Editor");
            if (target != null)
            {
                w.SetTarget(target);
            }
        }

        // ── 土台への指定 ──

        protected override string PrefsKey => "Texture";
        protected override FieldGuide<TextureData> Guide => TextureFieldGuide.Instance;
        protected override string ManualPage => Page;
        protected override Texture DefaultIcon => EditorGUIUtility.IconContent("Texture Icon").image;

        protected override string ManualAnchor(string purpose)
            => purpose == TextureFieldGuide.PurImage ? "texture" : purpose == FieldGuide<TextureData>.AdminPurpose ? "admin" : null;

        protected override string[] ModeTips => new[]
        {
            "必須の欄だけ。画像",
            "よく使う欄まで。使い道・用途・Sprite・Importer の現状",
            "すべての欄(拡縮・9-slice のボーダー)",
        };

        private static readonly KeyValuePair<string, string>[] MessageTokens =
        {
            new KeyValuePair<string, string>("Sprite", "Sprite"),
            new KeyValuePair<string, string>("Channel", "Channel"),
            new KeyValuePair<string, string>("AllowScale", "SliceBorder"),
            new KeyValuePair<string, string>("インポート規約", "Texture"),
            new KeyValuePair<string, string>("Max Size", "Texture"),
            new KeyValuePair<string, string>("サイズ", "Texture"),
            new KeyValuePair<string, string>("Texture", "Texture"),
        };

        protected override IReadOnlyList<KeyValuePair<string, string>> ValidationTokens => MessageTokens;

        protected override string CardDescription(string purpose)
            => purpose == TextureFieldGuide.PurImage ? "どの画像を、何の用途で使うか。Importer の設定はファイル名の規約で決まります" : "一覧に出る名前・分類・メモ";

        // ── ライフサイクル ──

        protected override void OnBuildBodyStart()
        {
            _image = null;
            _status = null;
            _info = null;
            _applyRule = null;
        }

        protected override void OnTargetChanged() => _infoSig = int.MinValue;

        // 既定はすべて折りたたみ: 構造体の欄(PropertyField)が作る Foldout も、組み終えたあとに閉じる。
        protected override void OnBodyBuilt(VisualElement page)
        {
            page.schedule.Execute(() => CollapseFoldouts(page)).StartingIn(30);
            page.schedule.Execute(() => CollapseFoldouts(page)).StartingIn(300);
        }

        private static void CollapseFoldouts(VisualElement page)
        {
            foreach (var f in page.Query<Foldout>().ToList())
            {
                if (f.value)
                {
                    f.SetValueWithoutNotify(false);
                }
            }
        }

        // ── 欄 ──

        protected override UxRow CreateRow(FieldGuideEntry e, UxCard card, bool isAdmin)
        {
            if (e.Field == TextureFieldGuide.RowInfo)
            {
                return CreateInfoRow(e);
            }

            return base.CreateRow(e, card, isAdmin);
        }

        private UxRow CreateInfoRow(FieldGuideEntry e)
        {
            var box = new VisualElement();
            _image = new Image { scaleMode = ScaleMode.ScaleToFit };
            _image.AddToClassList("ux-tex-image");
            box.Add(_image);

            _status = new Label();
            _status.AddToClassList("ux-dim");
            _status.AddToClassList("ux-note");
            box.Add(_status);

            _info = new Label();
            _info.AddToClassList("ux-note");
            box.Add(_info);

            _applyRule = MakeButton("命名規約を適用して再インポート", null, ApplyMatchedRule,
                "ファイル名の規約(TextureImportProfile)に合わせて Importer の設定を直し、再インポートする");
            box.Add(_applyRule);
            UpdateInfo();
            return new AnimAuxRow(e, e.Label, e.Hint, e.Tier, box, SyncInfo);
        }

        // 画像・Importer の設定が変わったときだけ表示を作り直す(150ms ごとの署名の比較)。
        private void SyncInfo()
        {
            if (InfoSig() != _infoSig)
            {
                UpdateInfo();
            }
        }

        private int InfoSig()
        {
            var tex = Target != null ? Target.Texture : null;
            var sig = 17;
            sig = sig * 31 + (tex != null ? tex.GetInstanceID() : 0);
            sig = sig * 31 + (Target != null ? (int)Target.Channel : -1);
            if (tex != null)
            {
                var path = AssetDatabase.GetAssetPath(tex);
                if (!string.IsNullOrEmpty(path) && AssetImporter.GetAtPath(path) is TextureImporter imp)
                {
                    sig = sig * 31 + (int)imp.textureType;
                    sig = sig * 31 + (imp.sRGBTexture ? 1 : 0);
                    sig = sig * 31 + (imp.mipmapEnabled ? 1 : 0);
                    sig = sig * 31 + (int)imp.textureCompression;
                    sig = sig * 31 + imp.maxTextureSize;
                }
            }

            return sig;
        }

        // [06] B-3/B-4 — Importer の現状と命名規約(TextureImportProfile)への適合を表示する。
        private void UpdateInfo()
        {
            _infoSig = InfoSig();
            _hasMatchedRule = false;
            if (_image == null || Target == null)
            {
                return;
            }

            var tex = Target;
            _image.image = tex.Texture;
            _image.Show(tex.Texture != null);
            _status.text = tex.Texture != null ? $"{tex.Texture.width}×{tex.Texture.height}  Channel={tex.Channel}" : "Texture が未設定です";
            if (tex.Texture == null)
            {
                _info.Show(false);
                _applyRule.Show(false);
                return;
            }

            var path = AssetDatabase.GetAssetPath(tex.Texture);
            var importer = string.IsNullOrEmpty(path) ? null : AssetImporter.GetAtPath(path) as TextureImporter;

            _info.Show(true);
            if (importer == null)
            {
                _info.text = "Importer 情報なし(メモリ上のテクスチャ)";
                _applyRule.Show(false);
                return;
            }

            var profile = TextureImportProfile.FindOrDefault();
            string ruleText;
            _applyRule.Show(false);
            if (profile.AppliesTo(path) && profile.TryMatch(path, out var rule))
            {
                _matchedRule = rule;
                _hasMatchedRule = true;
                ruleText = $"規約: {rule.Name}";
                if (TextureImportProfile.Diff(importer, rule).Count > 0)
                {
                    _applyRule.Show(true);
                }
            }
            else
            {
                ruleText = "規約に該当なし";
            }

            _info.text =
                $"Texture Type: {importer.textureType}  sRGB: {importer.sRGBTexture}  Mipmap: {importer.mipmapEnabled}  " +
                $"圧縮: {importer.textureCompression}  Max Size: {importer.maxTextureSize}\n{ruleText}";
        }

        private void ApplyMatchedRule()
        {
            var tex = Target;
            if (tex == null || tex.Texture == null || !_hasMatchedRule)
            {
                return;
            }

            var path = AssetDatabase.GetAssetPath(tex.Texture);
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
            {
                return;
            }

            if (TextureImportProfile.Apply(importer, _matchedRule))
            {
                importer.SaveAndReimport();
            }

            UpdateInfo();
            _status.text = $"命名規約 '{_matchedRule.Name}' を適用しました";
            RefreshAfterExternalWrite();
        }
    }
}
