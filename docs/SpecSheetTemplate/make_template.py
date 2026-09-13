# -*- coding: utf-8 -*-
"""
D-Drive 仕様書テンプレート生成スクリプト(チケット 5-12)

再生成方法:
    pip install openpyxl
    python docs/SpecSheetTemplate/make_template.py

出力: docs/SpecSheetTemplate/DDrive_仕様書テンプレート.xlsx

構成・列定義の根拠は docs/27_spec_sheet.md §2〜3 を参照。
このスクリプトを直接編集して再生成すれば、テンプレートの内容と本スクリプトが常に一致する
(手作業で .xlsx を直接編集しないこと)。
"""

from openpyxl import Workbook
from openpyxl.styles import Alignment, Border, Font, PatternFill, Side
from openpyxl.worksheet.datavalidation import DataValidation
from openpyxl.utils import get_column_letter

# ---------------------------------------------------------------------------
# 共通スタイル
# ---------------------------------------------------------------------------

HEADER_FILL = PatternFill(start_color="FFDCE6F1", end_color="FFDCE6F1", fill_type="solid")
HEADER_FONT = Font(bold=True)
TITLE_FONT = Font(bold=True, size=14)
SECTION_FONT = Font(bold=True, size=12)
NOTE_FONT = Font(italic=True, color="FF808080", size=9)
WRAP = Alignment(wrap_text=True, vertical="top")
WRAP_CENTER = Alignment(wrap_text=True, vertical="center")
THIN = Side(style="thin", color="FFB0B0B0")
BOX = Border(left=THIN, right=THIN, top=THIN, bottom=THIN)


def style_header_row(ws, row, first_col, last_col):
    for c in range(first_col, last_col + 1):
        cell = ws.cell(row=row, column=c)
        cell.fill = HEADER_FILL
        cell.font = HEADER_FONT
        cell.border = BOX
        cell.alignment = WRAP_CENTER


def set_widths(ws, widths):
    for i, w in enumerate(widths, start=1):
        ws.column_dimensions[get_column_letter(i)].width = w


# ---------------------------------------------------------------------------
# 種別・状態・型の一覧 (docs/27_spec_sheet.md §3 対応表と一致させること)
# ---------------------------------------------------------------------------

# AssetType (Assets/DDrive/Foundation/Identity/AssetType.cs) の enum 名そのまま。
# 表記は docs/10_workflow.md §3.3 の SourceAssets 9 種別命名(Se/Bgm/Texture/...)と揃える。
ASSET_TYPES = [
    "Se",
    "Bgm",
    "Vfx",
    "Anim",
    "Anim2D",
    "Material",
    "Texture",
    "Canvas",
    "Prefab",
    "Presentation",
    "Shake",
    "Haptics",
    "UiTween",
    "Model",
    "Anchor",
    "AnchorGroup",
    "ControlSkin",
]

ASSET_STATES = ["未着手", "仮", "本番", "保留"]

TUNING_TYPES = ["float", "int", "bool", "string"]


def build_workbook():
    wb = Workbook()

    build_readme(wb)
    build_overview(wb)
    build_feature_sample(wb)
    build_asset_sheet(wb)
    build_tuning_sheet(wb)
    build_choices_sheet(wb)

    # タブ順を明示的に固定する(Workbook 生成順=タブ順だが、保険で並べ直す)
    order = ["README", "概要", "機能_サンプル", "アセット", "調整値", "_選択肢"]
    wb._sheets = [wb[name] for name in order]  # noqa: SLF001 (openpyxl の公式な並べ替え手段がこれしか無い)
    wb.active = 0

    return wb


# ---------------------------------------------------------------------------
# README タブ
# ---------------------------------------------------------------------------

def build_readme(wb):
    ws = wb.active
    ws.title = "README"
    set_widths(ws, [3, 100])

    lines = [
        ("title", "D-Drive 仕様書テンプレート 記入ガイド(要約)"),
        ("blank", ""),
        ("section", "このシートの目的"),
        ("body", "企画がこのスプレッドシートに仕様を書くと、D-Drive(Unity ツール)が「アセット」タブと"
                 "「調整値」タブを読み取って、必要なアセットの空データ(Placeholder)と調整値を自動で作ります。"
                 "企画は「何を・どういう意味で作るか」だけを書けば十分で、ファイル名や ID は書きません。"),
        ("blank", ""),
        ("section", "タブの分類"),
        ("body", "「README」「概要」「機能_〇〇」は人が自由に書くタブです。結合セル・画像・改行、どれも自由に使えます。"
                 "D-Drive はこれらのタブの内容を読み取りません(参照リンクとしてのみ使います)。"),
        ("body", "「アセット」「調整値」はツールが読み取るタブです。次の3つの約束を必ず守ってください。"),
        ("bullet", "1 行目が見出し行(列の意味)。見出し行の文言・列の並びは変えない"),
        ("bullet", "1 行 = 1 件。1 セルに複数の情報をまとめない"),
        ("bullet", "結合セルを使わない(結合すると読み取りがずれます)"),
        ("body", "「_選択肢」タブは種別・状態・型のプルダウン元です(非表示)。触らなくて構いません。"
                 "D-Drive 側の一覧が増えたら「選択肢をコピー」機能(docs/27_spec_sheet.md §4.4)で更新されたものを"
                 "貼り直します。"),
        ("blank", ""),
        ("section", "書くときのルール"),
        ("bullet", "行頭が # の行と、完全に空の行は無視されます(メモ・区切りに自由に使ってよい)"),
        ("bullet", "種別ごとの細かいパラメータ(音量・色・長さ・カーブ等)はこのシートに書きません。"
                    "書くと Unity 側の実データと二重管理になり、どちらかが必ず古くなります。"
                    "細かい値は D-Drive の専用エディタでデザイナーが作ります"),
        ("bullet", "識別子(「アセット」タブの識別子列)は英語 PascalCase(例: PlayerSlash)。"
                    "ファイル名・保存用の ID は D-Drive が自動生成するので、企画は識別子と表示名(日本語可)だけ考えます"),
        ("bullet", "「アセット」タブの「種別 + 識別子」の組がツールにとっての一意キーです。"
                    "同じ組み合わせを 2 行に書かない"),
        ("bullet", "「調整値」タブの「キー」は <機能名>/<名前> の書式(例: Influence/FanBase)。"
                    "Signal キーと同じ考え方です"),
        ("blank", ""),
        ("section", "アップロード〜運用の流れ"),
        ("bullet", "1. この .xlsx を自分の Google ドライブにアップロードする"),
        ("bullet", "2. ファイルを右クリック →「アプリで開く」→「Google スプレッドシート」"),
        ("bullet", "3. 共有設定を「リンクを知っている全員が閲覧可」にする(D-Drive が URL だけで読み取るため)"),
        ("bullet", "4. スプレッドシートの URL を D-Drive 側の担当者(プログラマー)に渡す"),
        ("bullet", "5. 「機能_〇〇」タブをコピーして機能ごとに増やしながら書き進める"),
        ("body", "詳しい手順・よくある間違いは同フォルダの README.md(記入ガイド)を参照してください。"),
        ("blank", ""),
        ("note", "このテンプレートは docs/27_spec_sheet.md の設計に基づき、"
                 "docs/SpecSheetTemplate/make_template.py で生成しています。"),
    ]

    row = 1
    for kind, text in lines:
        cell = ws.cell(row=row, column=2, value=text if text else None)
        if kind == "title":
            cell.font = TITLE_FONT
        elif kind == "section":
            cell.font = SECTION_FONT
        elif kind == "bullet":
            cell.value = "・" + text
            cell.alignment = WRAP
        elif kind == "note":
            cell.font = NOTE_FONT
            cell.alignment = WRAP
        elif kind == "body":
            cell.alignment = WRAP
        row += 1

    ws.freeze_panes = "A2"


# ---------------------------------------------------------------------------
# 概要タブ(見出しだけの雛形)
# ---------------------------------------------------------------------------

def build_overview(wb):
    ws = wb.create_sheet("概要")
    set_widths(ws, [22, 90])

    ws.cell(row=1, column=1, value="概要").font = TITLE_FONT
    ws.cell(row=2, column=1,
            value="(このタブは自由記述です。D-Drive は読み取りません。見出しの下に書き込んでください)").font = NOTE_FONT

    headings = [
        "タイトル",
        "ジャンル / コンセプト",
        "対象プラットフォーム",
        "対象ユーザー",
        "コアループ(1〜3行)",
        "参考タイトル・参考資料",
        "マイルストーン / スケジュール",
        "未決事項・懸念点",
    ]

    row = 4
    for h in headings:
        cell = ws.cell(row=row, column=1, value=h)
        cell.font = HEADER_FONT
        cell.fill = HEADER_FILL
        cell.border = BOX
        cell.alignment = WRAP_CENTER
        ws.cell(row=row, column=2, value="").border = BOX
        ws.row_dimensions[row].height = 40
        row += 1


# ---------------------------------------------------------------------------
# 機能_サンプル タブ(記入例 1 件)
# ---------------------------------------------------------------------------

def build_feature_sample(wb):
    ws = wb.create_sheet("機能_サンプル")
    set_widths(ws, [3, 34, 56])

    ws.cell(row=1, column=1,
            value="(このタブは自由記述の記入例です。機能ごとにこのタブをコピーして「機能_<機能名>」を増やしてください)").font = NOTE_FONT

    # ■ 見出し行: 項目名 / 状態 / 担当 / 更新
    ws.cell(row=3, column=1, value="■ 剣の斬撃(近接攻撃 演出)").font = SECTION_FONT
    ws.merge_cells(start_row=3, start_column=1, end_row=3, end_column=2)
    ws.cell(row=4, column=1, value="状態: 確定    担当: やまだ    更新: 9/13")
    ws.merge_cells(start_row=4, start_column=1, end_row=4, end_column=2)

    # イメージ枠 | 仕様要件
    ws.cell(row=6, column=1, value="イメージ(画像・図)").font = HEADER_FONT
    ws.cell(row=6, column=1).fill = HEADER_FILL
    ws.cell(row=6, column=1).border = BOX
    ws.cell(row=6, column=2, value="仕様要件(箇条書き)").font = HEADER_FONT
    ws.cell(row=6, column=2).fill = HEADER_FILL
    ws.cell(row=6, column=2).border = BOX

    ws.merge_cells(start_row=7, start_column=1, end_row=14, end_column=1)
    img_cell = ws.cell(row=7, column=1, value="(ここに画像・図を貼る。またはリンク)")
    img_cell.alignment = Alignment(wrap_text=True, vertical="center", horizontal="center")
    img_cell.border = BOX

    requirements = (
        "・剣を振った瞬間に斬撃線 VFX を表示する\n"
        "・同時に斬撃 SE を再生する(3 段階で音程を変える。3 連撃の3段目だけ強め)\n"
        "・敵に当たった位置に小さいヒットスパークを出す\n"
        "・スロー(ヒットストップ)を 0.05 秒程度入れる\n"
        "・コンボ数によって斬撃線の色を変える(1〜2段: 白、3段目: 赤)"
    )
    req_cell = ws.cell(row=7, column=2, value=requirements)
    req_cell.alignment = WRAP
    req_cell.border = BOX
    ws.merge_cells(start_row=7, start_column=2, end_row=14, end_column=2)
    ws.row_dimensions[7].height = 140

    ws.cell(row=16, column=1,
            value="関連アセット: (「アセット」タブの識別子を列挙。例) VFX Player/Slash, SE Player/Slash, VFX Player/HitSpark")
    ws.merge_cells(start_row=16, start_column=1, end_row=16, end_column=2)
    ws.cell(row=17, column=1,
            value="調整値: (「調整値」タブのキーを列挙。例) Combat/HitStopSec, Combat/Slash3rdVolumeGain")
    ws.merge_cells(start_row=17, start_column=1, end_row=17, end_column=2)

    for r in (16, 17):
        ws.cell(row=r, column=1).alignment = WRAP


# ---------------------------------------------------------------------------
# アセット タブ(ツール向け)
# ---------------------------------------------------------------------------

ASSET_HEADERS = ["種別", "カテゴリ", "識別子", "表示名", "状態", "担当", "仕様", "備考"]


def build_asset_sheet(wb):
    ws = wb.create_sheet("アセット")
    set_widths(ws, [12, 18, 20, 24, 10, 10, 30, 30])

    for c, h in enumerate(ASSET_HEADERS, start=1):
        ws.cell(row=1, column=c, value=h)
    style_header_row(ws, 1, 1, len(ASSET_HEADERS))
    ws.freeze_panes = "A2"

    examples = [
        ["Se", "Player/Attack", "Slash", "剣の斬撃音", "仮", "よしだ",
         "機能_サンプル!B7", "3 段階で音程を変える"],
        ["Vfx", "Player/Attack", "Slash", "剣の斬撃線", "仮", "たなか",
         "機能_サンプル!B7", "コンボ 3 段目だけ赤くする"],
        ["Vfx", "Player/Attack", "HitSpark", "ヒットスパーク", "未着手", "たなか",
         "機能_サンプル!B7", ""],
        ["# 以下メモ: BGM は次スプリントでまとめて起票する", "", "", "", "", "", "", ""],
        ["Bgm", "Field/Town", "MainTheme", "城下町 BGM", "保留", "", "", "作曲家未決定のため保留"],
    ]
    for r, row in enumerate(examples, start=2):
        for c, v in enumerate(row, start=1):
            cell = ws.cell(row=r, column=c, value=v)
            cell.border = BOX
            cell.alignment = WRAP

    # プルダウン: 種別(A列) / 状態(E列)
    dv_type = DataValidation(type="list", formula1=f"='_選択肢'!$A$2:$A${len(ASSET_TYPES) + 1}",
                              allow_blank=True, showDropDown=False)
    dv_type.error = "「_選択肢」タブの種別一覧から選んでください。"
    dv_type.errorTitle = "種別が一覧に無い"
    ws.add_data_validation(dv_type)
    dv_type.add(f"A2:A1000")

    dv_state = DataValidation(type="list", formula1=f"='_選択肢'!$C$2:$C${len(ASSET_STATES) + 1}",
                               allow_blank=True, showDropDown=False)
    dv_state.error = "「_選択肢」タブの状態一覧から選んでください。"
    dv_state.errorTitle = "状態が一覧に無い"
    ws.add_data_validation(dv_state)
    dv_state.add(f"E2:E1000")


# ---------------------------------------------------------------------------
# 調整値 タブ(ツール向け)
# ---------------------------------------------------------------------------

TUNING_HEADERS = ["キー", "値", "型", "最小", "最大", "単位", "説明"]


def build_tuning_sheet(wb):
    ws = wb.create_sheet("調整値")
    set_widths(ws, [26, 10, 10, 10, 10, 10, 40])

    for c, h in enumerate(TUNING_HEADERS, start=1):
        ws.cell(row=1, column=c, value=h)
    style_header_row(ws, 1, 1, len(TUNING_HEADERS))
    ws.freeze_panes = "A2"

    examples = [
        ["Combat/HitStopSec", 0.05, "float", 0, 0.3, "秒", "ヒット時のスロー(ヒットストップ)の長さ"],
        ["Combat/Slash3rdVolumeGain", 1.2, "float", 1, 2, "倍", "3 段目斬撃の SE 音量倍率"],
        ["Influence/FanBase", 1.0, "float", 0, 10, "", "ファン 1 人あたりの影響力の素点"],
    ]
    for r, row in enumerate(examples, start=2):
        for c, v in enumerate(row, start=1):
            cell = ws.cell(row=r, column=c, value=v)
            cell.border = BOX
            cell.alignment = WRAP

    dv_ttype = DataValidation(type="list", formula1=f"='_選択肢'!$E$2:$E${len(TUNING_TYPES) + 1}",
                               allow_blank=True, showDropDown=False)
    dv_ttype.error = "「_選択肢」タブの型一覧(float/int/bool/string)から選んでください。"
    dv_ttype.errorTitle = "型が一覧に無い"
    ws.add_data_validation(dv_ttype)
    dv_ttype.add("C2:C1000")


# ---------------------------------------------------------------------------
# _選択肢 タブ(非表示。プルダウン元)
# ---------------------------------------------------------------------------

def build_choices_sheet(wb):
    ws = wb.create_sheet("_選択肢")
    set_widths(ws, [14, 3, 10, 3, 10, 3, 60])

    ws.cell(row=1, column=1, value="種別").font = HEADER_FONT
    ws.cell(row=1, column=3, value="状態").font = HEADER_FONT
    ws.cell(row=1, column=5, value="型").font = HEADER_FONT
    ws.cell(row=1, column=7,
            value="カテゴリは種別ごとに D-Drive 側の一覧から「選択肢をコピー」して"
                  "この右側(G列以降)や別シートに貼り、必要に応じてプルダウンを追加してください"
                  "(docs/27_spec_sheet.md §4.4)。D-Drive からは自動生成できないため運用でコピーします。").font = NOTE_FONT
    ws.cell(row=1, column=7).alignment = WRAP

    for i, v in enumerate(ASSET_TYPES, start=2):
        ws.cell(row=i, column=1, value=v)
    for i, v in enumerate(ASSET_STATES, start=2):
        ws.cell(row=i, column=3, value=v)
    for i, v in enumerate(TUNING_TYPES, start=2):
        ws.cell(row=i, column=5, value=v)

    ws.sheet_state = "hidden"


if __name__ == "__main__":
    import os

    workbook = build_workbook()
    out_path = os.path.join(os.path.dirname(__file__), "DDrive_仕様書テンプレート.xlsx")
    workbook.save(out_path)
    print(f"wrote {out_path}")
