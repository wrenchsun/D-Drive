using System.Collections.Generic;
using DDrive.Editor.Ux;
using DDrive.Runtime.Ui;

namespace DDrive.Editor.Ui
{
    // ButtonSkinData / SliderSkinData の案内表(新デザイン ButtonSkinEditor / SliderSkinEditor 用。docs/1010 UX-13)。AssetDataBase の共通欄(管理情報カード)は基底が持つ。
    // 段の根拠: ButtonSkinDataValidator / SliderSkinDataValidator の Error は NotchSeMinIntervalSec が負のときだけ(「必須」の欄は無い)。
    //   ただし状態・パーツの見た目は Skin の本体なので、「かんたん」でも見せるため必須の段に置く(バッジは出さない)。
    //   Warning の対象(全状態 Tint.a = 0・HapticId・当たり判定の Read/Write・Scroll Material・Anim Frames)と普段触る欄 = よく使う、既定のままでよい欄 = 詳細。
    // 名前が "(" で始まる欄は Data のフィールドではなく補助の行(雛形・確認する)。

    internal static class SkinGuideText
    {
        public const string RowPresets = "(presets)";
        public const string RowCheck = "(check)";

        public static FieldGuideEntry E(string field, string label, string hint, FieldTier tier, string purpose, FieldRowKind kind, params string[] kw) =>
            new FieldGuideEntry { Field = field, Label = label, Hint = hint, Tier = tier, Purpose = purpose, Keywords = kw, Kind = kind };

        // 状態 6 つ(Normal〜Locked)。1 つの一覧の行(SkinStateList)がまとめて持つ。
        public static FieldGuideEntry[] StateEntries(string purpose)
        {
            var list = new List<FieldGuideEntry>();
            foreach (var p in SkinParts.States)
            {
                list.Add(E(p.Field, p.Label + "(" + p.Meaning + ")", "この状態の見た目: 色・拡大率・画像・コマ画像・スクロールと、状態に入ったときの演出(Tween / Preset)。",
                    FieldTier.Required, purpose, FieldRowKind.Visual, "状態", "見た目", "色", "Tint", "拡大", "Scale", "画像", "スプライト", "コマ", "アニメ", "スクロール", "演出", "Tween", "Preset", p.State.ToString(), p.Meaning));
            }

            return list.ToArray();
        }

        public static FieldGuideEntry ScrollMaterialEntry(string purpose) =>
            E(nameof(ControlSkinData.ScrollMaterial), "スクロール用マテリアル", "画像を流す(Scroll Speed)状態があるときに必要。DDrive/UI/Scroll シェーダーのマテリアルを入れます。空のままだとスクロールしません。",
                FieldTier.Advanced, purpose, FieldRowKind.Normal, "スクロール", "流す", "マテリアル", "Scroll", "Material");

        public static FieldGuideEntry HitAreaExpandEntry(string purpose) =>
            E(nameof(ControlSkinData.HitAreaExpand), "押せる範囲の広げ幅", "Hit Area Expand。押せる範囲を四辺ごとに広げる(+)/ 狭める(-)。単位は UI のピクセル(X = 左・Y = 下・Z = 右・W = 上)。確認する の「当たり判定を表示」で枠を見ながら、SceneView でドラッグ調整もできます。",
                FieldTier.Common, purpose, FieldRowKind.Normal, "当たり判定", "判定", "広げる", "タッチ", "ヒット", "HitArea", "範囲");

        public static FieldGuideEntry AlphaHitEntry(string purpose) =>
            E(nameof(ControlSkinData.AlphaHitThreshold), "透明部分の判定", "Alpha Hit Threshold。画像の不透明度がこの値未満のピクセルは押せません(0 = 無効、例 0.5)。画像の Read/Write を ON にし、Sprite Atlas には入れないでください。実行時のみ効きます。",
                FieldTier.Advanced, purpose, FieldRowKind.Normal, "透明", "アルファ", "判定", "Read/Write", "AlphaHitThreshold");

        public static FieldGuideEntry CheckEntry(string purpose, string hint) =>
            E(RowCheck, "実際の部品で確かめる", hint, FieldTier.Common, purpose, FieldRowKind.Visual,
                "確認", "再生", "プレビュー", "遷移", "確認用シーン", "停止", "撤去", "当たり判定", "試聴");
    }

    internal sealed class ButtonSkinFieldGuide : FieldGuide<ButtonSkinData>
    {
        public const string PurStates = "状態ごとの見た目";
        public const string PurSe = "音(SE)";
        public const string PurHit = "押せる範囲";
        public const string PurCheck = "確認する";

        public static readonly ButtonSkinFieldGuide Instance = new ButtonSkinFieldGuide();

        private static readonly string[] PurposeList = { PurStates, PurSe, PurHit, PurCheck };

        private static FieldGuideEntry N(string field, string label, string hint, FieldTier tier, string purpose, params string[] kw) =>
            SkinGuideText.E(field, label, hint, tier, purpose, FieldRowKind.Normal, kw);

        private static readonly FieldGuideEntry[] Table = Build();

        private static FieldGuideEntry[] Build()
        {
            var list = new List<FieldGuideEntry>(SkinGuideText.StateEntries(PurStates));
            list.Add(SkinGuideText.ScrollMaterialEntry(PurStates));
            list.Add(N(nameof(ButtonSkinData.HoverSe), "ホバーの音", "Hover SE。Hover / Selected 状態に入ったときの SE(任意)。右の ▶ で試聴できます。", FieldTier.Common, PurSe, "ホバー", "カーソル", "SE", "音", "Hover"));
            list.Add(N(nameof(ButtonSkinData.ClickSe), "クリックの音", "Click SE。クリックを確定したときの SE。未設定だと「ClickSe が未設定です」の案内が出ます。", FieldTier.Common, PurSe, "クリック", "決定", "押した", "SE", "音", "Click"));
            list.Add(N(nameof(ButtonSkinData.LongPressSe), "長押しの音", "Long Press SE。長押しが成立したときの SE。", FieldTier.Advanced, PurSe, "長押し", "ホールド", "SE", "音", "LongPress"));
            list.Add(N(nameof(ButtonSkinData.DeniedSe), "拒否の音", "Denied SE。Disabled / Locked の状態で押されたときの SE。", FieldTier.Common, PurSe, "拒否", "押せない", "ロック", "無効", "SE", "音", "Denied"));
            list.Add(SkinGuideText.HitAreaExpandEntry(PurHit));
            list.Add(SkinGuideText.AlphaHitEntry(PurHit));
            list.Add(SkinGuideText.CheckEntry(PurCheck, "確認用シーンに実際の UiButton を置いて、各状態の見た目・演出・SE を動かして確かめます。状態の行の ▶ もここで置いた部品を使います。"));
            list.Add(FlagsEntry(AdminPurpose));
            list.Add(EventsEntry(AdminPurpose));
            return list.ToArray();
        }

        protected override IReadOnlyList<FieldGuideEntry> SpecificEntries => Table;

        protected override IReadOnlyList<string> SpecificPurposes => PurposeList;
    }

    internal sealed class SliderSkinFieldGuide : FieldGuide<SliderSkinData>
    {
        public const string PurPreset = "動きの雛形(確認用)";
        public const string PurStates = "状態・パーツの見た目";
        public const string PurParts = "パーツと操作";
        public const string PurSe = "音と触覚";
        public const string PurHit = "押せる範囲";
        public const string PurCheck = "確認する";

        public static readonly SliderSkinFieldGuide Instance = new SliderSkinFieldGuide();

        private static readonly string[] PurposeList = { PurPreset, PurStates, PurParts, PurSe, PurHit, PurCheck };

        private static FieldGuideEntry N(string field, string label, string hint, FieldTier tier, string purpose, params string[] kw) =>
            SkinGuideText.E(field, label, hint, tier, purpose, FieldRowKind.Normal, kw);

        private static readonly FieldGuideEntry[] Table = Build();

        private static FieldGuideEntry[] Build()
        {
            var list = new List<FieldGuideEntry>();
            list.Add(SkinGuideText.E(SkinGuideText.RowPresets, "雛形",
                "5 種のうち 1 つを押すと、確認用スライダーの値の範囲・刻み・目盛り・応答・追従を一式書き換えます(Undo で戻せます)。",
                FieldTier.Required, PurPreset, FieldRowKind.Visual, "プリセット", "テンプレート", "音量", "感度", "HP", "スタミナ", "キャラメイク", "刻み", "目盛り"));

            list.AddRange(SkinGuideText.StateEntries(PurStates));
            foreach (var p in SkinParts.SliderParts)
            {
                list.Add(SkinGuideText.E(p.Field, p.Label + "(" + p.Meaning + ")", "このパーツの見た目: 画像・色・拡大率。状態に関係なく常に反映されます(色が未設定 = 透明な黒、拡大率 0 のままなら変えません)。",
                    FieldTier.Required, PurStates, FieldRowKind.Visual, "パーツ", "トラック", "フィル", "ハンドル", "つまみ", "溝", "画像", "色", p.Label));
            }

            list.Add(SkinGuideText.ScrollMaterialEntry(PurStates));

            list.Add(N(nameof(SliderSkinData.NotchSprite), "目盛りの装飾画像", "Notch Sprite。目盛り(ノッチ)の位置に並べる装飾スプライト(任意)。", FieldTier.Common, PurParts, "ノッチ", "目盛り", "装飾", "画像"));
            list.Add(N(nameof(SliderSkinData.HideHandleOnGamepad), "パッドではつまみを隠す", "Hide Handle On Gamepad。パッド操作のときはつまみを隠し、フォーカス枠だけ表示します。", FieldTier.Advanced, PurParts, "パッド", "ハンドル", "つまみ", "隠す", "フォーカス"));

            list.Add(N(nameof(SliderSkinData.GrabSe), "掴んだ音", "Grab SE。つまみを掴んだとき(ドラッグ開始)の SE。右の ▶ で試聴できます。", FieldTier.Common, PurSe, "掴む", "ドラッグ", "SE", "音", "Grab"));
            list.Add(N(nameof(SliderSkinData.ReleaseSe), "離した音", "Release SE。つまみを離したとき(ドラッグ終了)の SE。", FieldTier.Common, PurSe, "離す", "SE", "音", "Release"));
            list.Add(N(nameof(SliderSkinData.NotchSe), "目盛りを通る音", "Notch SE。目盛り(ノッチ)を通過したときの SE。", FieldTier.Common, PurSe, "ノッチ", "目盛り", "カチカチ", "SE", "音", "Notch"));
            list.Add(N(nameof(SliderSkinData.LimitSe), "端に着いた音", "Limit SE。最小 / 最大に到達したときの SE。", FieldTier.Common, PurSe, "端", "最大", "最小", "リミット", "SE", "音", "Limit"));
            list.Add(N(nameof(SliderSkinData.DeniedSe), "拒否の音", "Denied SE。Disabled / Locked の状態で触られたときの SE。", FieldTier.Common, PurSe, "拒否", "押せない", "ロック", "無効", "SE", "音", "Denied"));
            list.Add(N(nameof(SliderSkinData.NotchSeMinIntervalSec), "目盛り音の間隔(秒)", "Notch Se Min Interval Sec。高速ドラッグのとき Notch SE が詰まらないようにする最小間隔。負の値はエラーです。", FieldTier.Advanced, PurSe, "間隔", "間引き", "ノッチ", "NotchSeMinIntervalSec"));
            list.Add(N(nameof(SliderSkinData.NotchHapticId), "目盛りの振動", "Notch Haptic Id。目盛りを通過したときのパッド振動の Haptics の Id(0 = なし)。見つからない Id は警告になります。", FieldTier.Advanced, PurSe, "触覚", "振動", "ハプティクス", "ノッチ", "Haptic"));
            list.Add(N(nameof(SliderSkinData.LimitHapticId), "端の振動", "Limit Haptic Id。端に到達したときのパッド振動の Haptics の Id(0 = なし)。見つからない Id は警告になります。", FieldTier.Advanced, PurSe, "触覚", "振動", "ハプティクス", "端", "Haptic"));

            list.Add(SkinGuideText.HitAreaExpandEntry(PurHit));
            list.Add(N(nameof(SliderSkinData.ExtraHitPadding), "つまみ外でも掴める幅", "Extra Hit Padding。タッチ用の当たり判定の拡張。X = 左右・Y = 上下に広げる幅で、共通の Hit Area Expand に足されます。", FieldTier.Common, PurHit, "タッチ", "当たり判定", "拡張", "つまみ", "パディング"));
            list.Add(N(nameof(SliderSkinData.HandleHitAreaExpand), "つまみの押せる範囲", "Handle Hit Area Expand。つまみ(Handle)の押せる範囲を四辺ごとに広げる(+)/ 狭める(-)。単位は UI のピクセル(X = 左・Y = 下・Z = 右・W = 上)。", FieldTier.Common, PurHit, "つまみ", "ハンドル", "当たり判定", "広げる", "HandleHitAreaExpand"));
            list.Add(SkinGuideText.AlphaHitEntry(PurHit));

            list.Add(SkinGuideText.CheckEntry(PurCheck, "確認用シーンに実際の UiSlider を置いて、動かしてみる(掴む・離す・目盛り・端・追従)と、各状態の見た目・演出・SE を確かめます。状態の行の ▶ もここで置いた部品を使います。"));
            list.Add(FlagsEntry(AdminPurpose));
            list.Add(EventsEntry(AdminPurpose));
            return list.ToArray();
        }

        protected override IReadOnlyList<FieldGuideEntry> SpecificEntries => Table;

        protected override IReadOnlyList<string> SpecificPurposes => PurposeList;
    }
}
