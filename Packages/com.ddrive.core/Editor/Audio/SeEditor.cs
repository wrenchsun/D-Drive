using System.Collections.Generic;
using DDrive.Editor.Anchor;
using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Editor.Ux;
using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Audio;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Audio
{
    // SE のエディター(docs/1010 UX-3。旧 AudioEditorWindow は Archive メニューへ退避)。
    // 共通基盤(UxEditorWindow<SeData> / FieldGuide / UxRow / UxCard / AnchorPad)だけで組み、ここに書くのは SE 固有の部分だけ:
    // 欄の表(SeFieldGuide)・波形とトリム・再生開始位置・3D サウンド確認パッド・Anchor・再生(実 AudioManager)。
    // 試聴は実 AudioManager を Editor から駆動する(ADR-4。PreviewService)。
    [DDrive.Editor.Inspector.DataEditor(typeof(SeData), "Audio Editor で開く(波形・トリム編集)")]
    internal sealed class SeEditor : UxEditorWindow<SeData>
    {
        // 空状態の「サンプルを作る」(docs/1010 UX-19)
        protected override bool HasSample => true;

        protected override SeData CreateSample() => UxSamples.Se();

        private const string Page = "audio-editor";

        [SerializeField] private bool _loopPreview;

        private PreviewService _preview;
        private Handle<SeMarker> _handle = Handle<SeMarker>.Invalid;
        private AnchorPad _anchorPad;
        private AudioListenerPad _pad;

        // ── 入口 ──

        // メニューは SE / BGM 共通の入口: BGM が選ばれていれば BGM Editor、それ以外は SE Editor を開く。
        [MenuItem(DDriveMenu.Editors + "Audio Editor")]
        public static void OpenFromMenu()
        {
            if (Selection.activeObject is BgmData bgm)
            {
                BgmEditor.Open(bgm);
                return;
            }

            Open(Selection.activeObject as SeData);
        }

        // BGM Editor から切り替えてきたとき: 選択中の SE、なければ最近開いた SE を開く。ウィンドウは元のウィンドウの隣に出す。
        public static void SwitchFrom(EditorWindow from)
        {
            var w = GetWindow<SeEditor>("Audio Editor", true, from.GetType());
            w.minSize = new Vector2(500, 380);
            var target = Selection.activeObject as SeData;
            if (target == null)
            {
                var recent = DataSwitcher.LoadRecent(typeof(SeData));
                target = recent.Count > 0 ? recent[0] as SeData : null;
            }

            if (target != null)
            {
                w.SetTarget(target);
            }
        }

        public static void Open(SeData target)
        {
            var w = OpenWindow<SeEditor>("Audio Editor");
            if (target != null)
            {
                w.SetTarget(target);
            }
        }

        // ── 土台への指定 ──

        protected override string PrefsKey => "Se";
        protected override FieldGuide<SeData> Guide => SeFieldGuide.Instance;
        protected override string ManualPage => Page;

        protected override string ManualAnchor(string purpose)
        {
            switch (purpose)
            {
                case SeFieldGuide.PurPlay: return "se-play";
                case SeFieldGuide.PurCut: return "se-cut";
                case SeFieldGuide.PurHear: return "se-hear";
                case SeFieldGuide.PurLimit: return "se-limit";
                case SeFieldGuide.PurLink: return "link";
                case FieldGuide<SeData>.AdminPurpose: return "admin";
                default: return null;
            }
        }

        protected override bool SupportsPlayback => true;
        protected override Texture DefaultIcon => EditorGUIUtility.IconContent("AudioClip Icon").image;
        protected override string SearchHint => "ラベル・説明・検索語に一致する設定だけ出す(例: トリム / 距離 / ミキサー)";

        protected override string[] ModeTips => new[]
        {
            "必須の欄だけ。Clip を入れて ▶ で聞くところまで",
            "よく使う欄まで。音量・トリム・ミキサー・3D・同時再生",
            "すべての欄",
        };

        private static readonly KeyValuePair<string, string>[] MessageTokens =
        {
            new KeyValuePair<string, string>("Pool", "Flags"),
            new KeyValuePair<string, string>("Domain", "Flags"),
        };

        protected override IReadOnlyList<KeyValuePair<string, string>> ValidationTokens => MessageTokens;

        protected override string CardDescription(string purpose)
        {
            switch (purpose)
            {
                case SeFieldGuide.PurPlay: return "どの音を、どんな大きさ・高さで鳴らすか";
                case SeFieldGuide.PurCut: return "元の音から切り出す範囲と、鳴らし始める位置";
                case SeFieldGuide.PurHear: return "出力先のミキサーと、3D で聞こえる位置・距離";
                case SeFieldGuide.PurLimit: return "同時に鳴らせる数と連打の防止";
                case SeFieldGuide.PurLink: return "節目で別の音や演出を一緒に鳴らす";
                default: return "一覧に出る名前・分類・メモ";
            }
        }

        // ── ヒーロー(SE ⇄ BGM の切替) ──

        protected override void AddLeadingHeroActions(VisualElement actions) =>
            actions.Add(MakeButton("BGM Editor へ", null, () => BgmEditor.SwitchFrom(this), "BGM を編集する Audio Editor に切り替える(いま開いている SE はこのウィンドウに残ります)"));

        // ── ライフサイクル・再生(実 Manager) ──

        protected override void OnWindowEnable()
        {
            _preview = new PreviewService();
            _handle = Handle<SeMarker>.Invalid;
        }

        protected override void OnWindowDisable()
        {
            _preview?.Dispose();
            _preview = null;
            _handle = Handle<SeMarker>.Invalid;
        }

        protected override bool IsPlaying => _preview != null && _preview.IsInitialized && _preview.AudioManager.IsPlaying(_handle);

        protected override void StartPlayback(bool focus)
        {
            if (Target == null || _preview == null)
            {
                return;
            }

            _preview.Initialize();
            _preview.Speed = Speed;
            _handle = _preview.PlaySe(Target, _loopPreview);
            ApplyListener();
        }

        protected override void StopPlayback()
        {
            _preview?.StopAll();
            _handle = Handle<SeMarker>.Invalid;
        }

        protected override void RestartPlayback()
        {
            StopPlayback();
            StartPlayback(false);
        }

        protected override void OnSpeedChanged(float speed)
        {
            if (_preview != null)
            {
                _preview.Speed = speed;
            }
        }

        protected override void OnPlaybackReset() => _handle = Handle<SeMarker>.Invalid;

        protected override void OnBuildBodyStart()
        {
            _anchorPad = null;
            _pad = null;
        }

        protected override void OnTargetChanged() => _handle = Handle<SeMarker>.Invalid;

        // 再生中の音に、編集をそのまま反映する(音量は即時、距離・Spatial などは鳴らし直し)。
        protected override void OnPropertyEdited(string path)
        {
            if (Target == null)
            {
                return;
            }

            if (path == "Volume")
            {
                if (IsPlaying)
                {
                    _preview.AudioManager.SetVolume(_handle, Target.Volume);
                }
            }
            else if (path.StartsWith("Spatial") || path.StartsWith("Anchor") || path == "MinDistance" || path == "MaxDistance"
                     || path == "Rolloff" || path.StartsWith("Rolloff") || path == "Spread" || path == "DopplerEnabled"
                     || path == "Mixer" || path.StartsWith("Clips") || path == "Loop" || path == "SelectMode")
            {
                RestartIfPlaying();
            }

            _pad?.Sync();
        }

        protected override void OnRowsRefreshed()
        {
            RefreshAnchorNote();
            _pad?.Sync();
        }

        // 3D パッド: 再生中の音源を、リスナーから見た相対位置へ動かす(距離減衰・パンをその場で聞く)。
        private void ApplyListener()
        {
            if (_pad == null || Target == null || Target.Spatial == SpatialMode.None || _preview == null || !_preview.IsInitialized
                || !_preview.AudioManager.IsPlaying(_handle) || _preview.ListenerTransform == null)
            {
                return;
            }

            var local = ListenerPadMath.SourceOffsetInListenerLocal(_pad.ListenerPos, _pad.AngleDeg);
            _preview.AudioManager.Move(_handle, _preview.ListenerTransform.TransformPoint(local));
        }

        private (bool valid, string reason) PadValidity()
        {
            var se = Target;
            if (se == null)
            {
                return (false, "対象アセットが選択されていません。");
            }

            if (se.Spatial == SpatialMode.None)
            {
                return (false, "この SE は Spatial = None(2D)なので、位置・距離・角度は音に影響しません。\n3D で確認するには「聞こえ方」の Spatial を Anchor か AtPosition にしてください。");
            }

            if (se.Clips == null || se.Clips.Length == 0 || se.Clips[0] == null)
            {
                return (false, "Clips が未設定のため再生できません。\nAudioClip を設定(またはトリミングを適用)してください。");
            }

            return (true, null);
        }

        private float Playhead() => IsPlaying ? _preview.AudioManager.GetTime(_handle) ?? -1f : -1f;

        // ── 欄 ──

        protected override UxRow CreateRow(FieldGuideEntry e, UxCard card, bool isAdmin)
        {
            var data = Target;
            switch (e.Field)
            {
                case nameof(SeData.Sources):
                {
                    var trim = new SeTrimInput(data, Playhead, final => VisualEdited(final, false));
                    var visual = new UxVisualRow(So, DefaultSo, new[] { e }, "波形とトリム範囲", "波形の端をドラッグして範囲を決めます。決めたら「トリミングを適用」で Clips を作り直します。", FieldTier.Common, trim, RowChanged) { NoFold = true };
                    AddRow(card, visual, false);

                    // 一覧そのもの(要素の追加・削除)は詳細でだけ。
                    return new UxRow(So, DefaultSo, new[] { e }, "元の音とトリム範囲(一覧)", e.Hint, FieldTier.Advanced, true,
                        GuidedField.MakeInput(So, e.Field), RowChanged) { IsDuplicate = true };
                }

                case nameof(SeData.StartOffsetSec):
                {
                    var input = new SeStartOffsetInput(data, final => VisualEdited(final, false));
                    return new UxVisualRow(So, DefaultSo, new[] { e }, e.Label, e.Hint, e.Tier, input, RowChanged);
                }

                case nameof(SeData.Loop):
                    return new SeLoopRow(So, DefaultSo, e, () => _loopPreview, v => _loopPreview = v, RowChangedRestart);

                case nameof(SeData.AnchorId):
                    return new SeAnchorIdRow(So, DefaultSo, e, () => Target != null && Target.AnchorId.IsValid, AnchorAssetText, OpenAnchorEditor, ConvertEmbeddedAnchorToAsset, RowChanged);

                case nameof(SeData.Anchor):
                    return CreateAnchorRows(e, card);

                case nameof(SeData.DopplerEnabled):
                {
                    // 3D サウンド確認パッドは 3D の欄の最後に置く。
                    AddRow(card, base.CreateRow(e, card, isAdmin), false);
                    AddAuxRow(card, CreatePadRow());
                    return null;
                }

                default:
                    return base.CreateRow(e, card, isAdmin);
            }
        }

        private UxRow CreatePadRow()
        {
            var entry = new FieldGuideEntry
            {
                Field = "(3dcheck)",
                Label = "3D サウンド確認",
                Hint = "中央が音源。リスナー(橙)をドラッグして、距離と向きで聞こえ方が変わるのを確かめます。先に ▶ で鳴らしておくと、動かすたびに反映されます。",
                Tier = FieldTier.Common,
                Purpose = SeFieldGuide.PurHear,
                Keywords = new[] { "3D", "距離", "リスナー", "パン", "定位", "確認", "パッド", "減衰" },
            };
            _pad = new AudioListenerPad(() => Target, PadValidity, (pos, angle) => ApplyListener());
            return new UxVisualRow(null, null, new[] { entry }, entry.Label, entry.Hint, entry.Tier, _pad, null) { NoFold = true };
        }

        private UxRow CreateAnchorRows(FieldGuideEntry e, UxCard card)
        {
            var data = Target;
            var pad = new AnchorPad(
                data,
                () => data.Anchor.LocalOffset,
                v =>
                {
                    var a = data.Anchor;
                    a.LocalOffset = v;
                    data.Anchor = a;
                },
                final => VisualEdited(final, false));
            _anchorPad = pad;
            var anchorRow = new UxVisualRow(So, DefaultSo, new[] { e }, "鳴る位置", "上から見たパッドをドラッグ。高さは右のスライダー。Spatial が Anchor のときに使われます。", FieldTier.Advanced, pad, RowChanged);
            RefreshAnchorNote();
            AddRow(card, anchorRow, false);

            // 同じ欄の全設定(基準・回転・スケール)。
            return new UxRow(So, DefaultSo, new[] { e }, "位置の全設定(基準・回転)", e.Hint, FieldTier.Advanced, true,
                GuidedField.MakeInput(So, e.Field), RowChanged) { IsDuplicate = true };
        }

        private void RefreshAnchorNote()
        {
            if (_anchorPad == null || Target == null)
            {
                return;
            }

            if (Target.Spatial != SpatialMode.Anchor)
            {
                _anchorPad.SetDimmed(true);
                _anchorPad.SetNote("Spatial が Anchor のときだけ使われます(「聞こえ方」の Spatial)。");
            }
            else if (Target.AnchorId.IsValid)
            {
                _anchorPad.SetDimmed(true);
                _anchorPad.SetNote("Anchor アセットが設定されているため、この位置は使われません(詳細の「共通の鳴る位置」)。");
            }
            else if (Target.Anchor.Space != AnchorSpace.World)
            {
                _anchorPad.SetDimmed(false);
                _anchorPad.SetNote($"基準が {Target.Anchor.Space} です。位置は基準からの相対(メートル)になります。");
            }
            else
            {
                _anchorPad.SetDimmed(false);
                _anchorPad.SetNote(null);
            }
        }

        private string AnchorAssetText()
        {
            if (Target == null || !Target.AnchorId.IsValid)
            {
                return string.Empty;
            }

            var asset = EditorAnchorRegistry.Find(Target.AnchorId.Value);
            return asset != null
                ? $"Anchor アセット '{asset.name}' を使用中(埋め込みの位置は無視されます)。位置・ランダム・ディレイの調整は「Anchor Editor で開く」から。"
                : $"⚠ AnchorId 0x{Target.AnchorId.Value:X} のアセットが見つかりません(World 原点扱い)。";
        }

        private void OpenAnchorEditor()
        {
            var asset = Target != null && Target.AnchorId.IsValid ? EditorAnchorRegistry.Find(Target.AnchorId.Value) : null;
            if (asset != null)
            {
                DDrive.Editor.Anchor.AnchorEditor.Open(asset);
            }
        }

        // 埋め込み Anchor → Anchor アセット化(移行補助)。埋め込み値は残す。
        private void ConvertEmbeddedAnchorToAsset()
        {
            if (Target == null || Target.AnchorId.IsValid)
            {
                return;
            }

            var category = string.IsNullOrEmpty(Target.Category) ? AnchorAssetFactory.DefaultCategory : AnchorAssetFactory.ToIdentifier(Target.Category);
            var identifier = AnchorAssetFactory.ToIdentifier(Target.name) + "Anchor";
            var asset = AnchorAssetFactory.CreateFromDef(Target.Anchor, $"{(Target.DisplayName ?? Target.name)} の Anchor", category, identifier);
            if (asset == null)
            {
                return;
            }

            Undo.RecordObject(Target, "Set SE AnchorId");
            Target.AnchorId = new AssetId<AnchorMarker>(asset.Id, AssetType.Anchor);
            EditorUtility.SetDirty(Target);
            RefreshAfterExternalWrite();
            EditorGUIUtility.PingObject(asset);
        }
    }
}
