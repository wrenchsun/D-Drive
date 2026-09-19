/**
 * ガント テンプレート生成（発注ツールへの同梱、2026-09-20）。
 * docs/32_spec_web.md §10.5 の「①メンバー取り込み」「②WBS 番号によるガントへのリンク」が
 * 前提にしている、企画担当が所有する Google スプレッドシート「03_ガントチャート」
 * （スケジュール／個人別タスク／ダッシュボード／設定 の 4 タブ）を、GAS から新規に
 * 生成できるようにしたもの。
 *
 * **なぜ xlsx を同梱しないか**: 元シートは Google 固有の関数（FILTER/SORT/SPARKLINE/QUERY）と
 * container-bound script（メニュー「📅 ガント」）を持つ。xlsx へ書き出すと Google 固有関数は
 * `__xludf.DUMMYFUNCTION("元の式")` に化けて実行できなくなり、bound script も取り出せない。
 * そのため「xlsx を Drive に置く」のではなく、**この GAS が SpreadsheetApp.create で
 * 新しい Google スプレッドシートを組み立てる**方式にした（同梱物はコードそのもの）。
 *
 * **実名は入れない**: 担当者マスタは `担当者A(PLN)` のようなプレースホルダー、
 * プロジェクト名は既定で `(プロジェクト名)`、タスクは「サンプル投入」相当の汎用行のみ。
 * 元シートの祝日リスト（2026〜2028、公開情報の祝日カレンダーであり実データではない）だけは
 * 実用性のためそのまま同梱する。
 *
 * **ファイル構成の方針（テスト容易性）**: `SpreadsheetApp` に依存する部分（実際にシートへ
 * 書き込む関数群、下の「---- SpreadsheetApp 依存 ----」以下）と、依存しない純関数
 * （定数・フォームラ文字列の組み立て・祝日リスト・担当者マスタの整形、上の「---- 純データ /
 * 純関数（Node テスト対象） ----」）を分離した。テストは `test/ganttTemplate.test.js` を参照
 * （既存 `test/load-gas.js` の流儀。`SpreadsheetApp` のフェイクは用意していないため、
 * `SpreadsheetApp` を実際に呼ぶ関数はテスト対象にしていない）。
 *
 * 登録している公開運用関数（すべて `specWebAssertAdminSession_()` 必須。README §5 の
 * ブートストラップ関数と同じ位置付けで、Apps Script エディタから手で実行する）:
 *   - createGanttTemplate(projectName, startDateIso, weeks)  新しいガント スプレッドシートを作る
 *   - ganttJumpToToday(spreadsheetId)     表示週（スケジュール!F2）を今日が入る週へ進める
 *   - ganttReapplyFormulas(spreadsheetId) 空欄になっている G/I/K 列にテンプレートの数式を再設定する
 *   - ganttInsertSampleData(spreadsheetId) サンプル行（8〜12行目）を汎用データで書き直す
 *
 * 元シートの container-bound script（メニュー「📅 ガント」の 今日へジャンプ / 数式再適用 /
 * サンプル投入の3機能）は、**GAS API から他のスプレッドシートへ bound script を直接付けることが
 * できない**ため、次の2通りで代替した（§「bound script 相当」参照、両方を採用）:
 *   (a) 同じ機能を上記 3 つの SpecWeb 側運用関数として提供する（admin が Apps Script エディタ
 *       または `clasp run-function` から実行する運用）
 *   (b) 生成したスプレッドシートの「設定」タブ「使い方」欄に、メニューが欲しい場合に
 *       「拡張機能 > Apps Script」へ貼り付けるための最小限のコード片（
 *       `SPEC_WEB_GANTT_BOUND_MENU_SNIPPET_`）を書き込む
 */

// ==================================================================
// ---- 純データ / 純関数（Node テスト対象。SpreadsheetApp 非依存） ----
// ==================================================================

var SPEC_WEB_GANTT_SHEET_NAMES_ = {
  SCHEDULE: 'スケジュール',
  PERSONAL: '個人別タスク',
  DASHBOARD: 'ダッシュボード',
  SETTINGS: '設定'
};

/** ガント領域（日付列）の最初の列インデックス（1始まり）。元シートに合わせて M 列。 */
var SPEC_WEB_GANTT_FIRST_DAY_COLUMN_INDEX_ = 13;

/** 元シートの既定行数（8〜107 行 = タスク100行分）・週数（26週 = 182日分、M〜GL列）。 */
var SPEC_WEB_GANTT_DEFAULT_TASK_ROW_COUNT_ = 100;
var SPEC_WEB_GANTT_DEFAULT_WEEKS_ = 26;
var SPEC_WEB_GANTT_FIRST_TASK_ROW_ = 8;

/**
 * 実名は入れないプレースホルダーの担当者マスタ。元シートの職種内訳（PLN/PRG/DZN）と
 * 「全体」という集計用の1件を再現しつつ、名前だけ汎用化した。
 * 「設定」タブの担当者マスタ範囲（A2 以降）にこの順で入れる。
 */
var SPEC_WEB_GANTT_PLACEHOLDER_MEMBERS_ = [
  '担当者A(PLN)',
  '担当者B(PLN)',
  '担当者C(PRG)',
  '担当者D(PRG)',
  '担当者E(DZN)',
  '担当者F(DZN)',
  '全体'
];

/**
 * 担当者マスタの入力範囲は、元シート同様に将来の追加分の空行を確保しておく
 * （A2:A21 = 20行ぶん。プレースホルダーは 7 件だが、残りは空欄のまま予約する）。
 */
var SPEC_WEB_GANTT_MEMBERS_RANGE_ROW_COUNT_ = 20;

/**
 * 祝日リスト（2026〜2028）。公開されている日本の祝日カレンダーであり、担当者名のような
 * 実データ・個人情報ではないため、実用性のためそのまま同梱する（元シートの注記
 * 「2028年の春分・秋分の日は暦要項の正式公表前の計算値」も引き継ぐ）。
 */
var SPEC_WEB_GANTT_HOLIDAYS_ = [
  { date: '2026-01-01', name: '元日' },
  { date: '2026-01-12', name: '成人の日' },
  { date: '2026-02-11', name: '建国記念の日' },
  { date: '2026-02-23', name: '天皇誕生日' },
  { date: '2026-03-20', name: '春分の日' },
  { date: '2026-04-29', name: '昭和の日' },
  { date: '2026-05-03', name: '憲法記念日' },
  { date: '2026-05-04', name: 'みどりの日' },
  { date: '2026-05-05', name: 'こどもの日' },
  { date: '2026-05-06', name: '振替休日' },
  { date: '2026-07-20', name: '海の日' },
  { date: '2026-08-11', name: '山の日' },
  { date: '2026-09-21', name: '敬老の日' },
  { date: '2026-09-22', name: '国民の休日' },
  { date: '2026-09-23', name: '秋分の日' },
  { date: '2026-10-12', name: 'スポーツの日' },
  { date: '2026-11-03', name: '文化の日' },
  { date: '2026-11-23', name: '勤労感謝の日' },
  { date: '2027-01-01', name: '元日' },
  { date: '2027-01-11', name: '成人の日' },
  { date: '2027-02-11', name: '建国記念の日' },
  { date: '2027-02-23', name: '天皇誕生日' },
  { date: '2027-03-21', name: '春分の日' },
  { date: '2027-03-22', name: '振替休日' },
  { date: '2027-04-29', name: '昭和の日' },
  { date: '2027-05-03', name: '憲法記念日' },
  { date: '2027-05-04', name: 'みどりの日' },
  { date: '2027-05-05', name: 'こどもの日' },
  { date: '2027-07-19', name: '海の日' },
  { date: '2027-08-11', name: '山の日' },
  { date: '2027-09-20', name: '敬老の日' },
  { date: '2027-09-23', name: '秋分の日' },
  { date: '2027-10-11', name: 'スポーツの日' },
  { date: '2027-11-03', name: '文化の日' },
  { date: '2027-11-23', name: '勤労感謝の日' },
  { date: '2028-01-01', name: '元日' },
  { date: '2028-01-10', name: '成人の日' },
  { date: '2028-02-11', name: '建国記念の日' },
  { date: '2028-02-23', name: '天皇誕生日' },
  { date: '2028-03-20', name: '春分の日※' },
  { date: '2028-04-29', name: '昭和の日' },
  { date: '2028-05-03', name: '憲法記念日' },
  { date: '2028-05-04', name: 'みどりの日' },
  { date: '2028-05-05', name: 'こどもの日' },
  { date: '2028-07-17', name: '海の日' },
  { date: '2028-08-11', name: '山の日' },
  { date: '2028-09-18', name: '敬老の日' },
  { date: '2028-09-22', name: '秋分の日※' },
  { date: '2028-10-09', name: 'スポーツの日' },
  { date: '2028-11-03', name: '文化の日' },
  { date: '2028-11-23', name: '勤労感謝の日' }
];

var SPEC_WEB_GANTT_LEGEND_TEXT_ =
  '■進行中　■完了　■遅延　■マイルストーン　■今日　■土日祝\n' +
  '「先行」にWBSを入れると開始日が自動計算されます（先行タスクは自分より上の行に）';

var SPEC_WEB_GANTT_SCHEDULE_HEADERS_ = ['WBS1', 'WBS2', 'WBS3', 'タスク', '担当', '先行', '開始', '終了', '日数', '進捗', '状態'];

/**
 * 使い方（設定タブ I 列）。元シートの7項目を踏襲しつつ、⑦だけ「メニュー実行」から
 * 「SpecWeb 管理者の運用関数を実行」に書き換えた（bound script を直接持たせられないため）。
 */
function specWebGanttUsageLines_() {
  return [
    '① 「日数」に営業日数を入れると「終了」が自動計算されます（土日祝スキップ）',
    '② 「先行」に先行タスクのWBS（例: 1.2、複数はカンマ区切り）→「開始」が自動計算',
    '   ※ 先行タスクは必ず自分より上の行に置いてください',
    '③ 先行が無いタスクは「開始」に日付を直接入力',
    '④ 「日数」を 0 にするとマイルストーン（紫のマス）',
    '⑤ WBSに「.」が無い行（1,2,3…）は大分類として自動でグレー太字',
    '⑥ 「表示週」を変えるとカレンダーの表示期間がスライド',
    '⑦ 今日へジャンプ / 数式再適用 / サンプル投入は、発注ツール(SpecWeb)の管理者が' +
      ' Apps Script エディタから ganttJumpToToday / ganttReapplyFormulas / ganttInsertSampleData' +
      ' を実行してください（このスプレッドシートの ID を引数に渡します）。' +
      'メニューから実行したい場合は、下のセルのコードを「拡張機能 > Apps Script」に貼り付けてください',
    '※ 2028年の春分・秋分は暦要項の正式公表前の計算値です。F列に行を足せば自由に休業日を追加できます。'
  ];
}

/**
 * (b) メニューが欲しい場合に生成先スプレッドシートの Apps Script エディタへ貼り付ける、
 * 最小限の container-bound script。SpecWeb 側の運用関数とは別プロジェクトなので、
 * このスプレッドシート自身の値だけで完結する自己完結コードにしてある。
 */
function specWebGanttBoundMenuSnippet_() {
  return [
    'function onOpen() {',
    '  SpreadsheetApp.getUi()',
    '    .createMenu(\'📅 ガント\')',
    '    .addItem(\'今日へジャンプ\', \'ganttMenuJumpToToday\')',
    '    .addItem(\'数式再適用\', \'ganttMenuReapplyFormulas\')',
    '    .addItem(\'サンプル投入\', \'ganttMenuInsertSampleData\')',
    '    .addToUi();',
    '}',
    '',
    'function ganttMenuJumpToToday() {',
    '  var sheet = SpreadsheetApp.getActiveSpreadsheet().getSheetByName(\'スケジュール\');',
    '  var start = sheet.getRange(\'D2\').getValue();',
    '  var weekStart = new Date(start);',
    '  weekStart.setDate(weekStart.getDate() - ((weekStart.getDay() + 6) % 7));',
    '  var diffDays = Math.floor((new Date() - weekStart) / 86400000);',
    '  sheet.getRange(\'F2\').setValue(Math.floor(diffDays / 7) + 1);',
    '}',
    '',
    'function ganttMenuReapplyFormulas() {',
    '  SpreadsheetApp.getUi().alert(',
    '    \'数式の再適用は発注ツール(SpecWeb)の管理者に ganttReapplyFormulas の実行を依頼してください。\'',
    '  );',
    '}',
    '',
    'function ganttMenuInsertSampleData() {',
    '  SpreadsheetApp.getUi().alert(',
    '    \'サンプル投入は発注ツール(SpecWeb)の管理者に ganttInsertSampleData の実行を依頼してください。\'',
    '  );',
    '}'
  ].join('\n');
}

/** 1始まりの列インデックスを A1 表記の列名に変換する（13 -> "M"、194 -> "GL"）。 */
function specWebGanttColumnLetter_(index) {
  var letters = '';
  var n = index;
  while (n > 0) {
    var rem = (n - 1) % 26;
    letters = String.fromCharCode(65 + rem) + letters;
    n = Math.floor((n - 1) / 26);
  }
  return letters;
}

/**
 * ガント領域の日付列メタ情報を組み立てる（純関数）。
 * @param {number} weeks 表示週数
 * @return {Array<{index:number, colLetter:string, weekNumber:number, isFirstOfWeek:boolean}>}
 */
function specWebGanttDayColumns_(weeks) {
  var totalDays = weeks * 7;
  var columns = [];
  for (var d = 0; d < totalDays; d++) {
    var index = SPEC_WEB_GANTT_FIRST_DAY_COLUMN_INDEX_ + d;
    columns.push({
      index: index,
      colLetter: specWebGanttColumnLetter_(index),
      weekNumber: Math.floor(d / 7) + 1,
      isFirstOfWeek: d % 7 === 0
    });
  }
  return columns;
}

/** 祝日フラグ行（3行目）の数式。 */
function specWebGanttHolidayFlagFormula_(col) {
  return '=N(COUNTIF(祝日,' + col.colLetter + '5)>0)';
}

/** 週ヘッダ行（4行目、週の最初の列だけ）の数式。 */
function specWebGanttWeekHeaderFormula_(col) {
  return '="W"&(' + col.weekNumber + ')&"  "&TEXT(' + col.colLetter + '5,"m/d")';
}

/** 日付行（5行目）の数式。週の最初の列は表示週から算出、以降は前列+1。 */
function specWebGanttDateRowFormula_(col, prevColLetter) {
  if (!prevColLetter) {
    return '=$D$2-WEEKDAY($D$2,3)+($F$2-1)*7';
  }
  return '=' + prevColLetter + '5+1';
}

/** 曜日行（6行目）の数式。5行目と同じ日付を参照し、表示形式（ddd）だけ変える。 */
function specWebGanttWeekdayRowFormula_(col) {
  return '=' + col.colLetter + '5';
}

/**
 * 「開始」（G列）の数式。「先行」（F列）が入っていれば先行タスクの終了日の翌営業日を計算する。
 * 先行が無い行は空文字を返す（= 直接日付を入力する運用、使い方③）。
 * @param {number} row 対象行番号（8以上）
 */
function specWebGanttStartFormula_(row) {
  var prevLast = row - 1;
  return (
    '=IF($F' + row + '="","",IFERROR(LET(p,MAX(FILTER($H$8:$H$' + prevLast +
    ',COUNTIF(SPLIT($F' + row + ',",、 "),IF($C$8:$C$' + prevLast + '<>"",$C$8:$C$' + prevLast +
    ',IF($B$8:$B$' + prevLast + '<>"",$B$8:$B$' + prevLast + ',$A$8:$A$' + prevLast +
    ')))>0)),IF(N(p)<1,"",IF(土日稼働,p+1,WORKDAY(p,1,IFERROR(FILTER(祝日,ISNUMBER(祝日)),0))))),"")'
  );
}

/** 「日数」（I列）の数式。営業日数（NETWORKDAYS、祝日除外は「祝日」名前付き範囲、土日稼働なら暦日数）。 */
function specWebGanttDaysFormula_(row) {
  return (
    '=IF(OR($G' + row + '="",$H' + row + '=""),"",IF($H' + row + '<$G' + row +
    ',"",IF(土日稼働,$H' + row + '-$G' + row + '+1,NETWORKDAYS($G' + row + ',$H' + row +
    ',IFERROR(FILTER(祝日,ISNUMBER(祝日)),0)))))'
  );
}

/** 「状態」（K列）の数式。完了/遅延/未着手/進行中を開始・終了・進捗・今日から判定する。 */
function specWebGanttStatusFormula_(row) {
  return (
    '=IF(OR($G' + row + '="",$H' + row + '=""),"",IF(N($J' + row + ')>=1,"完了",' +
    'IF($H' + row + '<TODAY(),"遅延",IF($G' + row + '>TODAY(),"未着手","進行中"))))'
  );
}

/** 2行目の集計式（進捗・遅延件数・今日）。 @param {number} lastRow */
function specWebGanttSummaryFormulas_(lastRow) {
  return {
    progress: '=IFERROR(SUMPRODUCT($I$8:$I$' + lastRow + ',$J$8:$J$' + lastRow + ')/SUM($I$8:$I$' + lastRow + '),0)',
    delayedCount: '=COUNTIF($K$8:$K$' + lastRow + ',"遅延")',
    today: '=TODAY()'
  };
}

/** K列の状態別の条件付き書式ルール（純データ）。 */
function specWebGanttStatusColorRules_() {
  return [
    { formula: '$K8="完了"', background: '#E6F4EA', fontColor: '#137333' },
    { formula: '$K8="進行中"', background: '#E8F0FE', fontColor: '#1967D2' },
    { formula: '$K8="遅延"', background: '#FCE8E6', fontColor: '#C5221F' },
    { formula: '$K8="未着手"', background: '#F1F3F4', fontColor: '#5F6368' }
  ];
}

/** WBS が「.」を含まない行（大分類）をグレー太字にするルール（純データ）。 */
function specWebGanttCategoryRowRule_() {
  return {
    formula: 'AND(IF($C8<>"",$C8,IF($B8<>"",$B8,$A8))<>"",NOT(ISNUMBER(FIND(".",IF($C8<>"",$C8,IF($B8<>"",$B8,$A8))&""))))',
    bold: true,
    fontColor: '#37474F',
    background: '#ECEFF1'
  };
}

/** ガント本体（M:GL列）のバー描画ルール（マイルストーン・進捗・遅延・通常の優先順、純データ）。 */
function specWebGanttBarRules_() {
  return [
    { formula: 'AND($I8=0,$G8<>"",M$5=$G8)', background: '#7B4DD8' },
    { formula: 'AND($G8<>"",$H8<>"",N($J8)>0,M$5>=$G8,M$5<=$G8+ROUND(($H8-$G8+1)*$J8,0)-1)', background: '#5B8FF9' },
    { formula: 'AND($K8="遅延",M$5>=$G8,M$5<=$H8)', background: '#E5484D' },
    { formula: 'AND($G8<>"",$H8<>"",M$5>=$G8,M$5<=$H8)', background: '#CFD8DC' }
  ];
}

/** 今日・祝日・週末のカレンダー強調ルール（ヘッダ行 M4:GL6 とガント本体 M8:GL{lastRow} の両方に適用、純データ）。 */
function specWebGanttCalendarHighlightRules_() {
  return [
    { formula: 'M$5=TODAY()', background: '#FFF59D' },
    { formula: 'M$3=1', background: '#FBE4E2' },
    { formula: 'WEEKDAY(M$5,2)>5', background: '#F4F6F7' }
  ];
}

/** データ検証（担当・進捗）の定義（純データ）。 @param {number} lastRow */
function specWebGanttDataValidationSpecs_(lastRow) {
  var membersLastRow = 1 + SPEC_WEB_GANTT_MEMBERS_RANGE_ROW_COUNT_;
  return [
    {
      rangeA1: 'E8:E' + lastRow,
      kind: 'list-from-range',
      sourceRangeA1: "'" + SPEC_WEB_GANTT_SHEET_NAMES_.SETTINGS + "'!$A$2:$A$" + membersLastRow
    },
    { rangeA1: 'J8:J' + lastRow, kind: 'decimal-between', min: 0, max: 1 },
    { rangeA1: 'I8:I' + lastRow, kind: 'decimal' }
  ];
}

/**
 * サンプル投入（O-の相当機能）で書き込む汎用タスク行（純データ）。
 * 実名・実プロジェクトの内容は含まない。11行目（サンプルタスクB）は「先行」に 10行目の
 * WBS を入れることで、開始日の自動計算（使い方②）を実演する。
 * @param {string} startDateIso 'YYYY-MM-DD'（スケジュール!D2 の開始日）
 * @return {Array<Object>} 行データ（row はスケジュールタブの実行番号、8始まり）
 */
function specWebGanttSampleTaskRows_(startDateIso) {
  var start = specWebGanttParseIsoDate_(startDateIso);
  return [
    {
      row: 8, wbs1: '0', task: '(プロジェクト名)', assignee: '全体',
      start: startDateIso, end: specWebGanttAddDaysIso_(start, 27)
    },
    {
      row: 9, wbs1: '1', task: 'サンプル: 企画', assignee: '全体',
      start: startDateIso, end: specWebGanttAddDaysIso_(start, 13)
    },
    {
      row: 10, wbs2: '1.1', task: 'サンプルタスクA', assignee: SPEC_WEB_GANTT_PLACEHOLDER_MEMBERS_[0],
      start: startDateIso, end: specWebGanttAddDaysIso_(start, 6), progress: 0.5
    },
    {
      row: 11, wbs2: '1.2', task: 'サンプルタスクB', assignee: SPEC_WEB_GANTT_PLACEHOLDER_MEMBERS_[1],
      predecessor: '1.1', progress: 0
      // 開始（G）は「先行」から自動計算させるため、ここでは書かない（テンプレートの G 数式のまま）。
      // 終了（H）だけ、日数3日ぶん先の日付を目安として入れておく。
      , end: specWebGanttAddDaysIso_(start, 10)
    },
    {
      row: 12, wbs1: '2', task: 'サンプルタスクC', assignee: SPEC_WEB_GANTT_PLACEHOLDER_MEMBERS_[2],
      start: specWebGanttAddDaysIso_(start, 14), end: specWebGanttAddDaysIso_(start, 20), progress: 0
    }
  ];
}

/** 'YYYY-MM-DD' を { y, m, d } に分解する（純関数、タイムゾーンに依存しない）。 */
function specWebGanttParseIsoDate_(iso) {
  var parts = String(iso).split('-');
  return { y: Number(parts[0]), m: Number(parts[1]), d: Number(parts[2]) };
}

/** { y, m, d } に日数を足して 'YYYY-MM-DD' を返す（純関数）。 */
function specWebGanttAddDaysIso_(ymd, days) {
  // UTC 固定で計算する（サーバーのタイムゾーンに依存させない。書き込み時に Date へ変換する側で
  // スプレッドシートのタイムゾーンに合わせる）。
  var utcMs = Date.UTC(ymd.y, ymd.m - 1, ymd.d) + days * 86400000;
  var d = new Date(utcMs);
  var yyyy = d.getUTCFullYear();
  var mm = String(d.getUTCMonth() + 1).length === 1 ? '0' + (d.getUTCMonth() + 1) : String(d.getUTCMonth() + 1);
  var dd = String(d.getUTCDate()).length === 1 ? '0' + d.getUTCDate() : String(d.getUTCDate());
  return yyyy + '-' + mm + '-' + dd;
}

/**
 * 他シートの列範囲を A1 表記で組み立てる（例: specWebGanttColRange_('スケジュール','D',8,107,true)
 * -> "'スケジュール'!D$8:D$107"）。`lockRows` を true にすると行番号に $ を付ける
 * （元シートの担当者別セクションのように、下方向へコピーしても参照範囲がずれないようにする用途。
 * 列文字には付けない＝列は常に相対。左右にはコピーしない前提のため）。
 */
function specWebGanttColRange_(sheetName, colLetter, firstRow, lastRow, lockRows) {
  var first = lockRows ? '$' + firstRow : String(firstRow);
  var last = lockRows ? '$' + lastRow : String(lastRow);
  return "'" + sheetName + "'!" + colLetter + first + ':' + colLetter + last;
}

/**
 * 個人別タスクタブの A5 セル（FILTER+SORT の配列数式、モダン Sheets では自動スピルする）。
 * @param {number} lastRow スケジュールタブの最終行
 */
function specWebGanttPersonalTaskFormula_(lastRow) {
  var s = SPEC_WEB_GANTT_SHEET_NAMES_.SCHEDULE;
  var A = specWebGanttColRange_(s, 'A', 8, lastRow, false);
  var B = specWebGanttColRange_(s, 'B', 8, lastRow, false);
  var C = specWebGanttColRange_(s, 'C', 8, lastRow, false);
  var D = specWebGanttColRange_(s, 'D', 8, lastRow, false);
  var E = specWebGanttColRange_(s, 'E', 8, lastRow, false);
  var G = specWebGanttColRange_(s, 'G', 8, lastRow, false);
  var H = specWebGanttColRange_(s, 'H', 8, lastRow, false);
  var I = specWebGanttColRange_(s, 'I', 8, lastRow, false);
  var J = specWebGanttColRange_(s, 'J', 8, lastRow, false);
  var K = specWebGanttColRange_(s, 'K', 8, lastRow, false);
  return (
    '=IFERROR(IF($B$2="","",IFERROR(SORT(FILTER({IF(' + A + '<>"",' + A +
    ',IF(' + B + '<>"",' + B + ',' + C + ')),' +
    D + ',' + G + ',' + H + ',' + I + ',' +
    J + ',' + K + '},' + E + '=$B$2,' + D +
    '<>""),4,TRUE),{"該当タスクなし","","","","","",""})),"エラーが発生しました")'
  );
}

/**
 * ダッシュボードタブの主要な数式一式（純データ、セル -> 数式）。
 * @param {number} lastRow スケジュールタブの最終行
 * @param {string[]} memberLabels 担当者別セクションに列挙する担当者表記（「全体」を含む）
 */
function specWebGanttDashboardFormulas_(lastRow, memberLabels) {
  var s = SPEC_WEB_GANTT_SHEET_NAMES_.SCHEDULE;
  var col = function (letter) { return specWebGanttColRange_(s, letter, 8, lastRow, true); };
  var G = col('G'), H = col('H'), I = col('I'), J = col('J'), K = col('K'), E = col('E');
  var cells = {
    B4: "='" + s + "'!A1",
    B5: '=IFERROR(MIN(FILTER(' + G + ',' + G + '<>"")),"")',
    B6: '=IFERROR(MAX(FILTER(' + H + ',' + H + '<>"")),"")',
    B7: '=IF(OR($B$5="",$B$6=""),"",$B$6-$B$5+1)',
    B8: '=IF($B$6="","",MAX(0,$B$6-TODAY()))',
    B9: '=IFERROR(SUMPRODUCT(' + I + ',' + J + ')/SUM(' + I + '),0)',
    B10: '=SUMPRODUCT((' + K + '<>"")*1)',
    B11: '=COUNTIF(' + K + ',"完了")',
    B12: '=COUNTIF(' + K + ',"進行中")',
    B13: '=COUNTIF(' + K + ',"未着手")',
    B14: '=COUNTIF(' + K + ',"遅延")',
    F4: '=$B$11', F5: '=$B$12', F6: '=$B$13', F7: '=$B$14',
    A43: '=IFERROR(QUERY(' + "'" + s + "'!$A$8:$K$" + lastRow +
      ',"select A,B,C,E,F,H where I = \'遅延\' order by F",0),"遅延タスクはありません")',
    A68: '=IFERROR(QUERY(' + "'" + s + "'!$A$8:$K$" + lastRow +
      ',"select A,B,C,E,F,H,I where E <= date \'"&TEXT(TODAY()-WEEKDAY(TODAY(),3)+6,"yyyy-mm-dd")&"\' ' +
      'and F >= date \'"&TEXT(TODAY()-WEEKDAY(TODAY(),3),"yyyy-mm-dd")&"\' and I <> \'完了\' order by E",0),' +
      '"該当タスクはありません")'
  };
  // 担当者別セクション（19行目〜）。元シートは名前付き範囲からの FILTER で自動列挙していたが、
  // ここでは生成時点で担当者が確定しているため、行ごとに直接埋める（担当者を追加/削除したときは
  // 「使い方」に案内した運用関数の再実行、または手動での数式コピーが必要になる。§「省略」参照）。
  memberLabels.forEach(function (label, i) {
    var row = 19 + i;
    cells['A' + row] = label;
    cells['B' + row] = '=IF($A' + row + '="","",COUNTIF(' + E + ',$A' + row + '))';
    cells['C' + row] = '=IF($A' + row + '="","",COUNTIFS(' + E + ',$A' + row + ',' + K + ',"完了"))';
    cells['D' + row] = '=IF($A' + row + '="","",COUNTIFS(' + E + ',$A' + row + ',' + K + ',"遅延"))';
    cells['E' + row] = '=IF($A' + row + '="","",IFERROR(SUMPRODUCT((' + E + '=$A' + row + ')*' +
      I + '*' + J + ')/SUMPRODUCT((' + E + '=$A' + row + ')*' +
      I + '),0))';
  });
  return cells;
}

// ==================================================================
// ---- SpreadsheetApp 依存（Node テスト対象外。README/HANDOVER.md 参照） ----
// ==================================================================

/** 純データの検証ルールを実際の DataValidation として組み立てる。 */
function specWebGanttBuildDataValidation_(sheet, spec) {
  var range = sheet.getRange(spec.rangeA1);
  var builder = SpreadsheetApp.newDataValidation();
  if (spec.kind === 'list-from-range') {
    var sourceRange = sheet.getParent().getRange(spec.sourceRangeA1);
    builder.requireValueInRange(sourceRange, true);
  } else if (spec.kind === 'decimal-between') {
    builder.requireNumberBetween(spec.min, spec.max);
  } else if (spec.kind === 'decimal') {
    builder.requireNumberGreaterThanOrEqualTo(0);
  }
  range.setDataValidation(builder.setAllowInvalid(true).build());
}

function specWebGanttApplyConditionalRule_(sheet, ranges, ruleSpec) {
  var builder = SpreadsheetApp.newConditionalFormatRule().whenFormulaSatisfied(ruleSpec.formula).setRanges(ranges);
  if (ruleSpec.background) builder.setBackground(ruleSpec.background);
  if (ruleSpec.fontColor) builder.setFontColor(ruleSpec.fontColor);
  if (ruleSpec.bold) builder.setBold(true);
  return builder.build();
}

/** 「設定」タブを組み立てる。 */
function specWebGanttApplySettingsSheet_(sheet) {
  sheet.getRange('A1').setValue('■ 担当者マスタ');
  sheet.getRange('C1').setValue('■ 稼働設定');
  sheet.getRange('F1').setValue('■ 祝日リスト（自動計算に使用）');
  sheet.getRange('I1').setValue('■ 使い方');

  var members = SPEC_WEB_GANTT_PLACEHOLDER_MEMBERS_;
  var memberValues = [];
  for (var i = 0; i < SPEC_WEB_GANTT_MEMBERS_RANGE_ROW_COUNT_; i++) {
    memberValues.push([i < members.length ? members[i] : '']);
  }
  sheet.getRange(2, 1, memberValues.length, 1).setValues(memberValues);

  sheet.getRange('C2').setValue('土日祝も稼働日にする');
  sheet.getRange('D2').setValue(false);
  sheet.getRange('C3').setValue('（OFF = 土日祝を除いた営業日で計算）');

  var holidays = SPEC_WEB_GANTT_HOLIDAYS_;
  var holidayValues = holidays.map(function (h) {
    var ymd = specWebGanttParseIsoDate_(h.date);
    return [new Date(ymd.y, ymd.m - 1, ymd.d), h.name];
  });
  sheet.getRange(2, 6, holidayValues.length, 2).setValues(holidayValues);

  var usage = specWebGanttUsageLines_();
  var usageValues = usage.map(function (line) { return [line]; });
  sheet.getRange(2, 9, usageValues.length, 1).setValues(usageValues);
  sheet.getRange(2 + usageValues.length + 1, 9).setValue(specWebGanttBoundMenuSnippet_());

  sheet.setColumnWidth(1, 140);
  sheet.setColumnWidth(3, 180);
  sheet.setColumnWidth(6, 100);
  sheet.setColumnWidth(7, 140);
  sheet.setColumnWidth(9, 480);
}

/** 「スケジュール」タブを組み立てる（ヘッダ・数式・条件付き書式・データ検証）。 */
function specWebGanttApplyScheduleSheet_(sheet, options) {
  var lastRow = SPEC_WEB_GANTT_FIRST_TASK_ROW_ + options.taskRowCount - 1;

  sheet.getRange('A1').setValue(options.projectName);
  sheet.getRange('A1:E1').merge();
  sheet.getRange('F1:K1').merge();
  sheet.getRange('A2').setValue('開始日');
  sheet.getRange('D2').setValue(specWebGanttIsoToDate_(options.startDate));
  sheet.getRange('E2').setValue('表示週');
  sheet.getRange('F2').setValue(1);
  sheet.getRange('G2').setValue('進捗');
  sheet.getRange('I2').setValue('遅延');
  var summary = specWebGanttSummaryFormulas_(lastRow);
  sheet.getRange('H2').setFormula(summary.progress);
  sheet.getRange('J2').setFormula(summary.delayedCount);
  sheet.getRange('K2').setFormula(summary.today);

  sheet.getRange('A4').setValue(SPEC_WEB_GANTT_LEGEND_TEXT_);
  sheet.getRange('A4:K6').merge().setWrap(true);

  sheet.getRange(7, 1, 1, SPEC_WEB_GANTT_SCHEDULE_HEADERS_.length).setValues([SPEC_WEB_GANTT_SCHEDULE_HEADERS_]).setFontWeight('bold');

  var columns = specWebGanttDayColumns_(options.weeks);
  var row3 = [], row4 = [], row5 = [], row6 = [];
  var prevColLetter = null;
  columns.forEach(function (col) {
    row3.push([specWebGanttHolidayFlagFormula_(col)]);
    row4.push([col.isFirstOfWeek ? specWebGanttWeekHeaderFormula_(col) : '']);
    row5.push([specWebGanttDateRowFormula_(col, prevColLetter)]);
    row6.push([specWebGanttWeekdayRowFormula_(col)]);
    prevColLetter = col.colLetter;
  });
  var dayColStart = SPEC_WEB_GANTT_FIRST_DAY_COLUMN_INDEX_;
  var dayColCount = columns.length;
  sheet.getRange(3, dayColStart, 1, dayColCount).setFormulas([specWebGanttFlattenColumn_(row3)]);
  sheet.getRange(4, dayColStart, 1, dayColCount).setFormulas([specWebGanttFlattenColumn_(row4)]);
  sheet.getRange(5, dayColStart, 1, dayColCount).setFormulas([specWebGanttFlattenColumn_(row5)]).setNumberFormat('d');
  sheet.getRange(6, dayColStart, 1, dayColCount).setFormulas([specWebGanttFlattenColumn_(row6)]).setNumberFormat('ddd');
  sheet.getRange(4, dayColStart, 1, dayColCount).setFontWeight('bold').setFontColor('#FFFFFF').setBackground('#546E7A');

  columns.forEach(function (col) {
    if (col.isFirstOfWeek) {
      sheet.getRange(4, col.index, 1, 7).merge();
    }
  });

  // タスク行: I/K は全行に数式、G はテンプレート行（サンプル未投入の行）にだけ数式を置く。
  var iFormulas = [], kFormulas = [];
  for (var row = SPEC_WEB_GANTT_FIRST_TASK_ROW_; row <= lastRow; row++) {
    iFormulas.push([specWebGanttDaysFormula_(row)]);
    kFormulas.push([specWebGanttStatusFormula_(row)]);
  }
  sheet.getRange(SPEC_WEB_GANTT_FIRST_TASK_ROW_, 9, iFormulas.length, 1).setFormulas(iFormulas);
  sheet.getRange(SPEC_WEB_GANTT_FIRST_TASK_ROW_, 11, kFormulas.length, 1).setFormulas(kFormulas);

  var sampleRows = specWebGanttSampleTaskRows_(options.startDate);
  var sampleLastRow = sampleRows[sampleRows.length - 1].row;
  specWebGanttWriteSampleRows_(sheet, sampleRows);

  var gFormulas = [];
  for (var r2 = sampleLastRow + 1; r2 <= lastRow; r2++) {
    gFormulas.push([specWebGanttStartFormula_(r2)]);
  }
  if (gFormulas.length > 0) {
    sheet.getRange(sampleLastRow + 1, 7, gFormulas.length, 1).setFormulas(gFormulas);
  }

  // 条件付き書式
  var rules = [];
  var kRange = sheet.getRange('K8:K' + lastRow);
  specWebGanttStatusColorRules_().forEach(function (spec) {
    rules.push(specWebGanttApplyConditionalRule_(sheet, [kRange], spec));
  });
  var akRange = sheet.getRange('A8:K' + lastRow);
  rules.push(specWebGanttApplyConditionalRule_(sheet, [akRange], specWebGanttCategoryRowRule_()));
  var barRange = sheet.getRange(8, dayColStart, lastRow - 7, dayColCount);
  specWebGanttBarRules_().forEach(function (spec) {
    rules.push(specWebGanttApplyConditionalRule_(sheet, [barRange], spec));
  });
  var calendarRanges = [sheet.getRange(4, dayColStart, 3, dayColCount), barRange];
  specWebGanttCalendarHighlightRules_().forEach(function (spec) {
    rules.push(specWebGanttApplyConditionalRule_(sheet, calendarRanges, spec));
  });
  sheet.setConditionalFormatRules(rules);

  specWebGanttDataValidationSpecs_(lastRow).forEach(function (spec) {
    specWebGanttBuildDataValidation_(sheet, spec);
  });

  sheet.setColumnWidth(1, 50);
  sheet.setColumnWidth(4, 220);
  sheet.setColumnWidth(5, 90);
  sheet.setColumnWidth(6, 70);
  sheet.setColumnWidth(7, 90);
  sheet.setColumnWidth(8, 90);
  sheet.setColumnWidth(9, 50);
  sheet.setColumnWidth(10, 55);
  sheet.setColumnWidth(11, 70);
  sheet.setColumnWidths(dayColStart, dayColCount, 26);
  sheet.setFrozenRows(7);
  sheet.setFrozenColumns(11);
}

/** サンプル行（純データの specWebGanttSampleTaskRows_ の結果）を実際のセルへ書き込む。 */
function specWebGanttWriteSampleRows_(sheet, sampleRows) {
  sampleRows.forEach(function (row) {
    if (row.wbs1) sheet.getRange(row.row, 1).setValue(row.wbs1);
    if (row.wbs2) sheet.getRange(row.row, 2).setValue(row.wbs2);
    if (row.wbs3) sheet.getRange(row.row, 3).setValue(row.wbs3);
    if (row.task) sheet.getRange(row.row, 4).setValue(row.task);
    if (row.assignee) sheet.getRange(row.row, 5).setValue(row.assignee);
    if (row.predecessor) sheet.getRange(row.row, 6).setValue(row.predecessor);
    if (row.start) {
      sheet.getRange(row.row, 7).setValue(specWebGanttIsoToDate_(row.start));
    } else if (row.predecessor) {
      // 「開始」を直接入力しない行（先行タスクから自動計算させたい行）は、
      // 使い方②の実演として G 列にテンプレートの数式をそのまま入れておく。
      sheet.getRange(row.row, 7).setFormula(specWebGanttStartFormula_(row.row));
    }
    if (row.end) sheet.getRange(row.row, 8).setValue(specWebGanttIsoToDate_(row.end));
    if (row.progress !== undefined && row.progress !== null) sheet.getRange(row.row, 10).setValue(row.progress);
  });
}

function specWebGanttIsoToDate_(iso) {
  var ymd = specWebGanttParseIsoDate_(iso);
  return new Date(ymd.y, ymd.m - 1, ymd.d);
}

function specWebGanttFlattenColumn_(rows) {
  return rows.map(function (r) { return r[0]; });
}

/** 「個人別タスク」タブを組み立てる。 */
function specWebGanttApplyPersonalSheet_(sheet, lastRow) {
  sheet.getRange('A1').setValue('個人別タスク一覧');
  sheet.getRange('A1:G1').merge();
  sheet.getRange('A2').setValue('担当者');
  sheet.getRange('A3').setValue('担当者を選ぶと、スケジュールのタスクが期日順に自動表示されます。');
  sheet.getRange('A3:G3').merge();
  sheet.getRange(4, 1, 1, 7).setValues([['WBS', 'タスク', '開始日', '期日', '日数', '進捗', '状態']]).setFontWeight('bold');
  sheet.getRange('A5').setFormula(specWebGanttPersonalTaskFormula_(lastRow));

  var membersRange = sheet.getParent().getSheetByName(SPEC_WEB_GANTT_SHEET_NAMES_.SETTINGS)
    .getRange(2, 1, SPEC_WEB_GANTT_MEMBERS_RANGE_ROW_COUNT_, 1);
  var rule = SpreadsheetApp.newDataValidation().requireValueInRange(membersRange, true).setAllowInvalid(true).build();
  sheet.getRange('B2').setDataValidation(rule);
}

/** 「ダッシュボード」タブを組み立てる。 */
function specWebGanttApplyDashboardSheet_(sheet, lastRow) {
  sheet.getRange('A1').setValue('📊 プロジェクト ダッシュボード');
  sheet.getRange('A1:H1').merge();
  sheet.getRange('A3').setValue('■ サマリー');
  sheet.getRange('E3').setValue('■ ステータス内訳');
  var labels = {
    A4: 'プロジェクト名', A5: '開始日', A6: '終了予定日', A7: '期間（日）', A8: '残り日数', A9: '全体進捗',
    A10: 'タスク総数', A11: '完了', A12: '進行中', A13: '未着手', A14: '遅延',
    E4: '完了', E5: '進行中', E6: '未着手', E7: '遅延',
    A17: '■ 担当者別', A18: '担当', B18: '件数', C18: '完了', D18: '遅延', E18: '進捗',
    A41: '■ 遅延タスク（終了日を過ぎて未完了）', A66: '■ 今週のタスク'
  };
  Object.keys(labels).forEach(function (a1) { sheet.getRange(a1).setValue(labels[a1]); });
  sheet.getRange(42, 1, 1, 6).setValues([['WBS', 'タスク', '担当', '開始', '終了', '進捗']]).setFontWeight('bold');
  sheet.getRange(67, 1, 1, 7).setValues([['WBS', 'タスク', '担当', '開始', '終了', '進捗', '状態']]).setFontWeight('bold');

  var members = SPEC_WEB_GANTT_PLACEHOLDER_MEMBERS_;
  var formulas = specWebGanttDashboardFormulas_(lastRow, members);
  Object.keys(formulas).forEach(function (a1) {
    var value = formulas[a1];
    if (typeof value === 'string' && value.charAt(0) === '=') {
      sheet.getRange(a1).setFormula(value);
    } else {
      sheet.getRange(a1).setValue(value);
    }
  });
}

/** 「祝日」「土日稼働」「担当者」の名前付き範囲を作る（元シートと同じ役割）。 */
function specWebGanttCreateNamedRanges_(ss) {
  var settings = ss.getSheetByName(SPEC_WEB_GANTT_SHEET_NAMES_.SETTINGS);
  ss.setNamedRange('祝日', settings.getRange(2, 6, SPEC_WEB_GANTT_HOLIDAYS_.length + 150, 1));
  ss.setNamedRange('土日稼働', settings.getRange('D2'));
  ss.setNamedRange('担当者', settings.getRange(2, 1, SPEC_WEB_GANTT_MEMBERS_RANGE_ROW_COUNT_, 1));
}

/**
 * 内部ビルダー。新規スプレッドシートを作り、4タブを組み立てて URL を返す。
 * `SpreadsheetApp` に依存するため Node テストの対象外（`createGanttTemplate` から
 * admin セッション確認のあとにだけ呼ばれる）。
 * @param {{projectName?:string, startDate?:string, weeks?:number, taskRowCount?:number}} [options]
 */
function createGanttTemplate_(options) {
  options = options || {};
  var resolved = {
    projectName: options.projectName || '(プロジェクト名)',
    startDate: options.startDate || specWebGanttTodayIso_(),
    weeks: options.weeks || SPEC_WEB_GANTT_DEFAULT_WEEKS_,
    taskRowCount: options.taskRowCount || SPEC_WEB_GANTT_DEFAULT_TASK_ROW_COUNT_
  };

  var ss = SpreadsheetApp.create('03_ガントチャート（テンプレート）: ' + resolved.projectName);
  var scheduleSheet = ss.getSheets()[0];
  scheduleSheet.setName(SPEC_WEB_GANTT_SHEET_NAMES_.SCHEDULE);
  var personalSheet = ss.insertSheet(SPEC_WEB_GANTT_SHEET_NAMES_.PERSONAL);
  var dashboardSheet = ss.insertSheet(SPEC_WEB_GANTT_SHEET_NAMES_.DASHBOARD);
  var settingsSheet = ss.insertSheet(SPEC_WEB_GANTT_SHEET_NAMES_.SETTINGS);

  specWebGanttApplySettingsSheet_(settingsSheet);
  specWebGanttCreateNamedRanges_(ss);
  specWebGanttApplyScheduleSheet_(scheduleSheet, resolved);
  var lastRow = SPEC_WEB_GANTT_FIRST_TASK_ROW_ + resolved.taskRowCount - 1;
  specWebGanttApplyPersonalSheet_(personalSheet, lastRow);
  specWebGanttApplyDashboardSheet_(dashboardSheet, lastRow);

  ss.setActiveSheet(scheduleSheet);
  return { spreadsheetId: ss.getId(), url: ss.getUrl() };
}

function specWebGanttTodayIso_() {
  var d = new Date();
  return specWebGanttAddDaysIso_({ y: d.getFullYear(), m: d.getMonth() + 1, d: d.getDate() }, 0);
}

// ---- 運用者がエディタから実行する公開関数（admin セッション必須） ----

/**
 * 新しいガント テンプレート スプレッドシートを作る（README §5 のブートストラップ関数と同じ
 * 位置付け。Apps Script エディタから実行する）。
 * @param {string} [projectName]
 * @param {string} [startDateIso] 'YYYY-MM-DD'
 * @param {number} [weeks]
 * @return {{spreadsheetId:string, url:string}}
 */
function createGanttTemplate(projectName, startDateIso, weeks) {
  specWebAssertAdminSession_('ガント テンプレートの生成（createGanttTemplate）');
  return createGanttTemplate_({ projectName: projectName, startDate: startDateIso, weeks: weeks });
}

/** メニュー「今日へジャンプ」相当。表示週（スケジュール!F2）を今日が入る週へ進める。 */
function ganttJumpToToday(spreadsheetId) {
  specWebAssertAdminSession_('ガントの今日へジャンプ（ganttJumpToToday）');
  var sheet = SpreadsheetApp.openById(spreadsheetId).getSheetByName(SPEC_WEB_GANTT_SHEET_NAMES_.SCHEDULE);
  var start = sheet.getRange('D2').getValue();
  var weekStart = new Date(start.getTime());
  weekStart.setDate(weekStart.getDate() - ((weekStart.getDay() + 6) % 7));
  var diffDays = Math.floor((new Date() - weekStart) / 86400000);
  sheet.getRange('F2').setValue(Math.floor(diffDays / 7) + 1);
}

/** メニュー「数式再適用」相当。空欄の G/I/K 列にテンプレートの数式を書き戻す。 */
function ganttReapplyFormulas(spreadsheetId) {
  specWebAssertAdminSession_('ガントの数式再適用（ganttReapplyFormulas）');
  var sheet = SpreadsheetApp.openById(spreadsheetId).getSheetByName(SPEC_WEB_GANTT_SHEET_NAMES_.SCHEDULE);
  var lastRow = sheet.getLastRow();
  for (var row = SPEC_WEB_GANTT_FIRST_TASK_ROW_; row <= lastRow; row++) {
    var gCell = sheet.getRange(row, 7);
    if (gCell.getValue() === '') gCell.setFormula(specWebGanttStartFormula_(row));
    var iCell = sheet.getRange(row, 9);
    if (iCell.getValue() === '') iCell.setFormula(specWebGanttDaysFormula_(row));
    var kCell = sheet.getRange(row, 11);
    if (kCell.getValue() === '') kCell.setFormula(specWebGanttStatusFormula_(row));
  }
}

/** メニュー「サンプル投入」相当。サンプル行（8〜12行目）を汎用データで書き直す。 */
function ganttInsertSampleData(spreadsheetId) {
  specWebAssertAdminSession_('ガントのサンプル投入（ganttInsertSampleData）');
  var ss = SpreadsheetApp.openById(spreadsheetId);
  var sheet = ss.getSheetByName(SPEC_WEB_GANTT_SHEET_NAMES_.SCHEDULE);
  var startDateIso = specWebGanttDateToIso_(sheet.getRange('D2').getValue());
  specWebGanttWriteSampleRows_(sheet, specWebGanttSampleTaskRows_(startDateIso));
}

function specWebGanttDateToIso_(date) {
  var yyyy = date.getFullYear();
  var mm = String(date.getMonth() + 1).length === 1 ? '0' + (date.getMonth() + 1) : String(date.getMonth() + 1);
  var dd = String(date.getDate()).length === 1 ? '0' + date.getDate() : String(date.getDate());
  return yyyy + '-' + mm + '-' + dd;
}
