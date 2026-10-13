using DDrive.Runtime.Tuning;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Tuning
{
    // 実運用規模の確認用サンプル(docs/1010 D-10 / UX-15)。MS2026 の TuningTable に近い規模・混在。
    // キー 51(Float 28 / Int 10 / Bool 7 / String 2 / Enum 4。数値は範囲あり・なし混在)・カテゴリ 9(Player 12 / Minigame 8 / Match 7 / Fan 6 / Interact 5 / Camera 5 / UI 4 / Audio 3 / (未分類) 1)、
    // Tables 3(EnemyParams = 6 行 × 5 列 / DropRates = 8 行 × 3 列 / LevelCurve = 25 行 × 3 列 = ListView)。
    // わざと矛盾を 2 つ入れてある(Fan/DecayPerMin の値が範囲の外 = エラー、Camera/Preset の値が選択肢に無い = 警告)。検証の見え方の確認用。
    // 指定のフォルダ(Assets/ 配下)に作る。呼び出し側が使い終わったらフォルダごと消す(実データ Assets/GameData には作らない)。
    // スクリーンショット・テスト・見通しの確認用。
    internal static class TuningSample
    {
        public sealed class Result
        {
            public TuningTable Table;
            public string Folder;
        }

        public static Result Build(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                var parent = System.IO.Path.GetDirectoryName(folder).Replace((char)92, (char)47);
                AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
            }

            var table = ScriptableObject.CreateInstance<TuningTable>();
            table.name = "SampleTuningTable";
            table.Entries = new[]
            {
                // Player(12)
                F("Player/MoveSpeedInitial", 3f, 0f, 20f, "m/s", "入力開始時の速度"),
                F("Player/MoveSpeedMax", 8f, 0f, 20f, "m/s", "走り続けたときの最高速度"),
                F("Player/TurnRateDeg", 720f, 0f, 1440f, "deg/s", "見た目の回転補間"),
                F("Player/JumpHeight", 1.2f, 0f, 5f, "m", "ジャンプの高さ"),
                F("Player/Gravity", -9.81f, 0f, 0f, "m/s²", "重力。範囲を決めていないので数値欄"),
                F("Player/CoyoteTimeSec", 0.12f, 0f, 0.5f, "s", "足場を離れてからジャンプできる猶予"),
                F("Player/StaminaRegenPerSec", 12.5f, 0f, 100f, "/s", "スタミナの回復速度"),
                I("Player/StaminaMax", 100, 10f, 500f, string.Empty, "スタミナの最大値"),
                B("Player/DashEnabled", true, "ダッシュを許可する"),
                B("Player/SlideEnabled", false, string.Empty),
                S("Player/Nickname", "Player", "仮のプレイヤー名"),
                E("Player/ControlScheme", "Relative", "操作の方式", "Classic", "Relative", "Camera"),

                // Interact(5)
                F("Interact/Range", 1.8f, 0f, 10f, "m", "調べられる距離"),
                F("Interact/HoldSec", 0.6f, 0f, 3f, "s", "長押しの時間"),
                F("Interact/PromptDelaySec", 0.2f, 0f, 2f, "s", string.Empty),
                B("Interact/AutoPickup", true, "近づくだけで拾う"),
                I("Interact/MaxCarry", 3, 1f, 10f, "個", "同時に持てる数"),

                // Match(7)
                F("Match/DurationSec", 180f, 30f, 600f, "s", "1 試合の長さ"),
                F("Match/WarmupSec", 10f, 0f, 60f, "s", "開始前のカウントダウン"),
                F("Match/RespawnDelaySec", 3f, 0f, 15f, "s", "やられてから復活するまで"),
                I("Match/MaxPlayers", 4, 2f, 8f, "人", "参加人数の上限"),
                I("Match/ScoreToWin", 100, 10f, 1000f, "点", "この点数で勝ち"),
                B("Match/FriendlyFire", false, "味方への攻撃を当てる"),
                E("Match/Mode", "Score", "遊び方", "Score", "Survival", "Race"),

                // Fan(6)。DecayPerMin はわざと範囲の外
                F("Fan/BaseGain", 1f, 0f, 10f, "倍", "ファンが増える基本の速さ"),
                F("Fan/DecayPerMin", 1.4f, 0f, 1f, "/min", "放っておいたときの減り方(わざと範囲外)"),
                F("Fan/CheerRadius", 6f, 0f, 30f, "m", "声援が届く距離"),
                I("Fan/MaxFans", 40, 0f, 200f, "人", "同時に出るファンの数"),
                I("Fan/Tier1Threshold", 20, 0f, 0f, "人", "最初の段階に上がる人数"),
                I("Fan/Tier2Threshold", 60, 0f, 0f, "人", "次の段階に上がる人数"),

                // Minigame(8)
                F("Minigame/TimeLimitSec", 45f, 5f, 120f, "s", "制限時間"),
                F("Minigame/BonusMultiplier", 1.5f, 1f, 5f, "倍", "ノーミスのボーナス"),
                F("Minigame/ComboWindowSec", 0.8f, 0.1f, 3f, "s", "コンボが切れるまでの猶予"),
                F("Minigame/SpawnIntervalSec", 1.2f, 0.1f, 10f, "s", "次が出てくる間隔"),
                I("Minigame/Lives", 3, 1f, 9f, string.Empty, "ミスできる回数"),
                I("Minigame/Seed", 0, 0f, 0f, string.Empty, "0 なら毎回ランダム"),
                B("Minigame/ShowHints", true, string.Empty),
                E("Minigame/Difficulty", "Normal", "難しさ", "Easy", "Normal", "Hard"),

                // Camera(5)。Preset はわざと選択肢に無い値
                F("Camera/FovDefault", 60f, 30f, 120f, "deg", "ふだんの画角"),
                F("Camera/FollowDistance", 5.5f, 1f, 20f, "m", "追いかける距離"),
                F("Camera/FollowLagSec", 0.15f, 0f, 1f, "s", "追従の遅れ"),
                F("Camera/ShakeScale", 1f, 0f, 2f, "倍", "揺れの強さの全体倍率"),
                E("Camera/Preset", "Cinematic", "カメラの型(わざと選択肢に無い値)", "Follow", "Orbit", "Fixed"),

                // UI(4)
                F("UI/HudScale", 1f, 0.5f, 2f, "倍", "HUD の大きさ"),
                F("UI/ToastSec", 2.5f, 0.5f, 10f, "s", "通知の表示時間"),
                S("UI/Language", "ja", "表示言語のコード"),
                B("UI/ShowFps", false, string.Empty),

                // Audio(3)
                F("Audio/MasterVolume", 0.8f, 0f, 1f, string.Empty, "全体の音量"),
                F("Audio/BgmFadeSec", 1.5f, 0f, 10f, "s", "BGM が切り替わる長さ"),
                I("Audio/SeMaxVoices", 24, 4f, 64f, "音", "同時に鳴らす効果音の数"),

                // (未分類)
                B("DebugOverlay", false, "接頭辞が無いキー"),
            };

            table.Tables = new[]
            {
                Enemy(),
                Drops(),
                Levels(),
            };

            AssetDatabase.CreateAsset(table, folder + "/SampleTuningTable.asset");
            return new Result { Table = table, Folder = folder };
        }

        private static TuningEntry F(string key, float v, float min, float max, string unit, string desc) =>
            new TuningEntry { Key = key, Type = TuningValueType.Float, ValueFloat = v, Min = min, Max = max, Unit = unit, Description = desc };

        private static TuningEntry I(string key, int v, float min, float max, string unit, string desc) =>
            new TuningEntry { Key = key, Type = TuningValueType.Int, ValueInt = v, Min = min, Max = max, Unit = unit, Description = desc };

        private static TuningEntry B(string key, bool v, string desc) =>
            new TuningEntry { Key = key, Type = TuningValueType.Bool, ValueBool = v, Description = desc };

        private static TuningEntry S(string key, string v, string desc) =>
            new TuningEntry { Key = key, Type = TuningValueType.String, ValueString = v, Description = desc };

        private static TuningEntry E(string key, string v, string desc, params string[] options) =>
            new TuningEntry { Key = key, Type = TuningValueType.Enum, ValueString = v, Description = desc, EnumOptions = options };

        private static TuningTableColumn Col(string key, TuningValueType type, float min = 0f, float max = 0f, string unit = "", params string[] options) =>
            new TuningTableColumn { Key = key, Type = type, Min = min, Max = max, Unit = unit, EnumOptions = options.Length > 0 ? options : null };

        private static TuningCellValue Cell(string column, float f = 0f, int i = 0, bool b = false, string s = "") =>
            new TuningCellValue { ColumnKey = column, F = f, I = i, B = b, S = s };

        private static TuningTableEntry Enemy()
        {
            var rows = new[]
            {
                EnemyRow("Slime", 30, 4f, 1.5f, false, "Slime"),
                EnemyRow("Bat", 18, 6f, 5f, false, "Bat"),
                EnemyRow("Golem", 240, 22f, 0.8f, false, "Golem"),
                EnemyRow("Orc", 120, 15f, 2.2f, false, "Golem"),
                EnemyRow("Imp", 45, 9f, 4f, false, "Bat"),
                EnemyRow("Troll", 900, 40f, 1.2f, true, "Golem"),
            };
            return new TuningTableEntry
            {
                Key = "EnemyParams",
                Columns = new[]
                {
                    Col("Hp", TuningValueType.Int, 1f, 9999f, "HP"),
                    Col("Atk", TuningValueType.Float, 0f, 500f),
                    Col("Speed", TuningValueType.Float, 0f, 20f, "m/s"),
                    Col("Boss", TuningValueType.Bool),
                    Col("Kind", TuningValueType.Enum, 0f, 0f, string.Empty, "Slime", "Bat", "Golem"),
                },
                Rows = rows,
            };
        }

        private static TuningTableRow EnemyRow(string id, int hp, float atk, float speed, bool boss, string kind) => new TuningTableRow
        {
            RowId = id,
            Cells = new[] { Cell("Hp", i: hp), Cell("Atk", f: atk), Cell("Speed", f: speed), Cell("Boss", b: boss), Cell("Kind", s: kind) },
        };

        private static TuningTableEntry Drops()
        {
            var rows = new TuningTableRow[8];
            for (var i = 0; i < rows.Length; i++)
            {
                rows[i] = new TuningTableRow
                {
                    RowId = "Stage" + (i + 1),
                    Cells = new[] { Cell("Common", f: 0.8f - i * 0.05f), Cell("Rare", f: 0.15f + i * 0.03f), Cell("Epic", f: 0.02f + i * 0.015f) },
                };
            }

            return new TuningTableEntry
            {
                Key = "DropRates",
                Columns = new[] { Col("Common", TuningValueType.Float, 0f, 1f), Col("Rare", TuningValueType.Float, 0f, 1f), Col("Epic", TuningValueType.Float, 0f, 1f) },
                Rows = rows,
            };
        }

        private static TuningTableEntry Levels()
        {
            var rows = new TuningTableRow[25];
            for (var i = 0; i < rows.Length; i++)
            {
                var lv = i + 1;
                rows[i] = new TuningTableRow
                {
                    RowId = "Lv" + lv,
                    Cells = new[] { Cell("Exp", i: 100 * lv * lv), Cell("Hp", i: 50 + 12 * lv), Cell("Atk", f: 5f + 1.5f * lv) },
                };
            }

            return new TuningTableEntry
            {
                Key = "LevelCurve",
                Columns = new[] { Col("Exp", TuningValueType.Int, 0f, 0f, "pt"), Col("Hp", TuningValueType.Int, 1f, 9999f), Col("Atk", TuningValueType.Float, 0f, 999f) },
                Rows = rows,
            };
        }
    }
}
