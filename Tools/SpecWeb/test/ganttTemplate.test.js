'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { loadGas } = require('./load-gas.js');

// ガント テンプレート生成（Tools/SpecWeb/src/GanttTemplate.js、2026-09-20）。
// SpreadsheetApp に依存しない純関数・純データだけをここで検証する
// （SpreadsheetApp のフェイクは無いため、createGanttTemplate_ 自体は呼ばない）。

// 元シートに実在した担当者名（実名）。テンプレートのどこにも出てはいけない。
const FORBIDDEN_REAL_NAMES = ['吉田', '佐々木', '押谷', '山口', '安藤', '有馬', '武田', '李', '岸本'];

function assertNoRealNames(value, label) {
  const text = JSON.stringify(value);
  for (const name of FORBIDDEN_REAL_NAMES) {
    assert.ok(!text.includes(name), label + ' に実名 "' + name + '" が含まれています: ' + text);
  }
}

test('specWebGanttColumnLetter_: 1始まりの列番号を A1 表記の列名に変換する', () => {
  const ctx = loadGas();
  assert.equal(ctx.specWebGanttColumnLetter_(1), 'A');
  assert.equal(ctx.specWebGanttColumnLetter_(13), 'M');
  assert.equal(ctx.specWebGanttColumnLetter_(26), 'Z');
  assert.equal(ctx.specWebGanttColumnLetter_(27), 'AA');
  assert.equal(ctx.specWebGanttColumnLetter_(194), 'GL');
});

test('specWebGanttDayColumns_: 26週分（182列）を M列始まりで生成し、週番号・週頭フラグが正しい', () => {
  const ctx = loadGas();
  const columns = ctx.specWebGanttDayColumns_(26);
  assert.equal(columns.length, 182);
  assert.equal(columns[0].index, 13);
  assert.equal(columns[0].colLetter, 'M');
  assert.equal(columns[0].weekNumber, 1);
  assert.equal(columns[0].isFirstOfWeek, true);
  assert.equal(columns[6].weekNumber, 1);
  assert.equal(columns[6].isFirstOfWeek, false);
  assert.equal(columns[7].weekNumber, 2);
  assert.equal(columns[7].isFirstOfWeek, true);
  const last = columns[columns.length - 1];
  assert.equal(last.index, 194);
  assert.equal(last.colLetter, 'GL');
  assert.equal(last.weekNumber, 26);
});

test('specWebGanttHolidayFlagFormula_/WeekHeaderFormula_/DateRowFormula_/WeekdayRowFormula_: 数式が名前付き範囲・自列を正しく参照する', () => {
  const ctx = loadGas();
  const cols = ctx.specWebGanttDayColumns_(2);
  const first = cols[0]; // M, week1, firstOfWeek
  const second = cols[1]; // N, week1

  assert.equal(ctx.specWebGanttHolidayFlagFormula_(first), '=N(COUNTIF(祝日,M5)>0)');
  assert.equal(ctx.specWebGanttWeekHeaderFormula_(first), '="W"&(1)&"  "&TEXT(M5,"m/d")');
  assert.equal(ctx.specWebGanttDateRowFormula_(first, null), '=$D$2-WEEKDAY($D$2,3)+($F$2-1)*7');
  assert.equal(ctx.specWebGanttDateRowFormula_(second, 'M'), '=M5+1');
  assert.equal(ctx.specWebGanttWeekdayRowFormula_(first), '=M5');
});

test('specWebGanttStartFormula_/DaysFormula_/StatusFormula_: 行番号を正しく差し込み、名前付き範囲を参照する', () => {
  const ctx = loadGas();
  const g = ctx.specWebGanttStartFormula_(42);
  assert.ok(g.includes('$F42='), '対象行の先行(F)列を見る');
  assert.ok(g.includes('$H$8:$H$41'), '自分より上の行までを検索範囲にする');
  assert.ok(g.includes('土日稼働') && g.includes('祝日'), '名前付き範囲を使う');

  const i = ctx.specWebGanttDaysFormula_(42);
  assert.ok(i.includes('NETWORKDAYS($G42,$H42') || i.includes('$G42') , '自分の行の開始/終了を参照する');
  assert.ok(i.includes('祝日'));

  const k = ctx.specWebGanttStatusFormula_(42);
  assert.ok(k.includes('$G42') && k.includes('$H42') && k.includes('$J42'));
  assert.ok(k.includes('完了') && k.includes('遅延') && k.includes('未着手') && k.includes('進行中'));
});

test('specWebGanttSummaryFormulas_: lastRow を差し込んだ範囲で進捗・遅延件数・今日を返す', () => {
  const ctx = loadGas();
  const s = ctx.specWebGanttSummaryFormulas_(107);
  assert.equal(s.progress, '=IFERROR(SUMPRODUCT($I$8:$I$107,$J$8:$J$107)/SUM($I$8:$I$107),0)');
  assert.equal(s.delayedCount, '=COUNTIF($K$8:$K$107,"遅延")');
  assert.equal(s.today, '=TODAY()');
});

test('specWebGanttStatusColorRules_/CategoryRowRule_/BarRules_/CalendarHighlightRules_: ルール件数と主要な数式トークン', () => {
  const ctx = loadGas();
  assert.equal(ctx.specWebGanttStatusColorRules_().length, 4);
  ['完了', '進行中', '遅延', '未着手'].forEach((status, i) => {
    assert.ok(ctx.specWebGanttStatusColorRules_()[i].formula.includes('"' + status + '"'));
  });

  const category = ctx.specWebGanttCategoryRowRule_();
  assert.ok(category.formula.includes('FIND(".",'));
  assert.equal(category.bold, true);

  const bars = ctx.specWebGanttBarRules_();
  assert.equal(bars.length, 4);
  assert.ok(bars[0].formula.includes('$I8=0'), 'マイルストーン判定が先頭');
  assert.ok(bars[2].formula.includes('"遅延"'));

  const calendar = ctx.specWebGanttCalendarHighlightRules_();
  assert.equal(calendar.length, 3);
  assert.ok(calendar[0].formula.includes('TODAY()'));
  assert.ok(calendar[1].formula.includes('M$3'));
  assert.ok(calendar[2].formula.includes('WEEKDAY('));
});

test('specWebGanttDataValidationSpecs_: 担当(list)・進捗(0-1)・日数の3件を返す', () => {
  const ctx = loadGas();
  const specs = ctx.specWebGanttDataValidationSpecs_(107);
  assert.equal(specs.length, 3);
  assert.equal(specs[0].rangeA1, 'E8:E107');
  assert.equal(specs[0].kind, 'list-from-range');
  assert.equal(specs[0].sourceRangeA1, "'設定'!$A$2:$A$21");
  assert.equal(specs[1].rangeA1, 'J8:J107');
  assert.equal(specs[1].min, 0);
  assert.equal(specs[1].max, 1);
});

test('SPEC_WEB_GANTT_PLACEHOLDER_MEMBERS_ / SPEC_WEB_GANTT_HOLIDAYS_ / usage lines: 実名を含まない', () => {
  const ctx = loadGas();
  assertNoRealNames(ctx.SPEC_WEB_GANTT_PLACEHOLDER_MEMBERS_, 'プレースホルダー担当者マスタ');
  assertNoRealNames(ctx.SPEC_WEB_GANTT_HOLIDAYS_, '祝日リスト');
  assertNoRealNames(ctx.specWebGanttUsageLines_(), '使い方');
  assertNoRealNames(ctx.specWebGanttBoundMenuSnippet_(), 'メニュー用コード片');

  // 職種の内訳（PLN/PRG/DZN）と「全体」は再現しているが、名前は汎用化されている。
  assert.ok(ctx.SPEC_WEB_GANTT_PLACEHOLDER_MEMBERS_.some((m) => m.includes('(PLN)')));
  assert.ok(ctx.SPEC_WEB_GANTT_PLACEHOLDER_MEMBERS_.some((m) => m.includes('(PRG)')));
  assert.ok(ctx.SPEC_WEB_GANTT_PLACEHOLDER_MEMBERS_.some((m) => m.includes('(DZN)')));
  assert.ok(ctx.SPEC_WEB_GANTT_PLACEHOLDER_MEMBERS_.includes('全体'));
});

test('SPEC_WEB_GANTT_HOLIDAYS_: 2026〜2028年分 51件、日付は ISO 形式', () => {
  const ctx = loadGas();
  const holidays = ctx.SPEC_WEB_GANTT_HOLIDAYS_;
  assert.equal(holidays.length, 51);
  assert.equal(holidays[0].date, '2026-01-01');
  assert.equal(holidays[0].name, '元日');
  assert.equal(holidays[holidays.length - 1].date, '2028-11-23');
  holidays.forEach((h) => {
    assert.ok(/^\d{4}-\d{2}-\d{2}$/.test(h.date), h.date + ' は ISO 日付形式であるべき');
  });
});

test('specWebGanttParseIsoDate_ / AddDaysIso_: 往復変換・日数加算が正しい（タイムゾーン非依存）', () => {
  const ctx = loadGas();
  const ymd = ctx.specWebGanttParseIsoDate_('2026-09-04');
  // vm コンテキスト（別の実現域）のオブジェクトなので deepEqual（strict）は prototype 不一致で
  // 落ちる（storage.test.js/globals.test.js の注記と同じ理由）。フィールドを個別に見る。
  assert.equal(ymd.y, 2026);
  assert.equal(ymd.m, 9);
  assert.equal(ymd.d, 4);
  assert.equal(ctx.specWebGanttAddDaysIso_(ymd, 0), '2026-09-04');
  assert.equal(ctx.specWebGanttAddDaysIso_(ymd, 1), '2026-09-05');
  assert.equal(ctx.specWebGanttAddDaysIso_(ymd, 30), '2026-10-04');
  // 月またぎ・年またぎ
  assert.equal(ctx.specWebGanttAddDaysIso_({ y: 2026, m: 12, d: 31 }, 1), '2027-01-01');
});

test('specWebGanttSampleTaskRows_: 汎用5行で実名を含まず、先行(WBS 1.2)が1.1を参照する', () => {
  const ctx = loadGas();
  const rows = ctx.specWebGanttSampleTaskRows_('2026-09-04');
  assert.equal(rows.length, 5);
  assertNoRealNames(rows, 'サンプルタスク行');

  const taskB = rows.find((r) => r.wbs2 === '1.2');
  assert.ok(taskB, 'WBS 1.2 のサンプル行が存在する');
  assert.equal(taskB.predecessor, '1.1');
  assert.equal(taskB.start, undefined, '先行から自動計算させるため開始日は直接持たない');

  rows.forEach((r) => {
    assert.equal(typeof r.row, 'number');
    assert.ok(r.row >= 8);
  });
});

test('specWebGanttPersonalTaskFormula_: 列と行が正しく組み立てられる（列文字の二重化バグが無い）', () => {
  const ctx = loadGas();
  const formula = ctx.specWebGanttPersonalTaskFormula_(107);
  assert.ok(formula.includes("'スケジュール'!D8:D107"), 'D列の範囲は D8:D107 の形式であるべき');
  assert.ok(formula.includes("'スケジュール'!E8:E107=$B$2"));
  assert.ok(!formula.includes('!D8:107'), '列文字が重複せず抜け落ちていないこと（過去の実装バグの回帰）');
});

test('specWebGanttDashboardFormulas_: 主要セルの数式と担当者別セクションが実名なしで組み立つ', () => {
  const ctx = loadGas();
  const members = ctx.SPEC_WEB_GANTT_PLACEHOLDER_MEMBERS_;
  const cells = ctx.specWebGanttDashboardFormulas_(107, members);
  assertNoRealNames(cells, 'ダッシュボード数式');

  assert.equal(cells.B4, "='スケジュール'!A1");
  assert.ok(cells.B5.includes("'スケジュール'!G$8:G$107"), '下方向にずれない行固定の範囲であるべき');
  assert.ok(cells.B6.includes('MAX(FILTER('));
  assert.ok(cells.A43.includes('QUERY('));
  assert.ok(cells.A68.includes('QUERY('));

  members.forEach((label, i) => {
    const row = 19 + i;
    assert.equal(cells['A' + row], label);
    assert.ok(cells['B' + row].includes('COUNTIF('));
  });
});

test('specWebGanttColRange_: lockRows の有無で行番号への $ が切り替わる（列には付けない）', () => {
  const ctx = loadGas();
  assert.equal(ctx.specWebGanttColRange_('スケジュール', 'D', 8, 107, false), "'スケジュール'!D8:D107");
  assert.equal(ctx.specWebGanttColRange_('スケジュール', 'D', 8, 107, true), "'スケジュール'!D$8:D$107");
});

// ── globals.test.js と同じ趣旨: 公開の運用関数は admin セッションが無ければ即座に失敗する ──
// （SpreadsheetApp に触れる前に specWebAssertAdminSession_ で止まることを確認する。
// フェイクの SpreadsheetApp が無いため、admin セッションが通った場合の成功系はここでは検証しない）

test('createGanttTemplate / ganttJumpToToday / ganttReapplyFormulas / ganttInsertSampleData は admin でなければ失敗する', () => {
  const ctx = loadGas({
    activeUserEmail: 'viewer@example.com',
    driveFiles: {
      'users.json': JSON.stringify({
        items: {
          'admin@example.com': { id: 'admin@example.com', email: 'admin@example.com', displayName: '管理者', role: 'admin', revision: 1 },
          'viewer@example.com': { id: 'viewer@example.com', email: 'viewer@example.com', displayName: '閲覧者', role: 'viewer', revision: 1 }
        }
      })
    }
  });
  assert.throws(() => ctx.createGanttTemplate('サンプル', '2026-09-04', 4), /admin/);
  assert.throws(() => ctx.ganttJumpToToday('fake-id'), /admin/);
  assert.throws(() => ctx.ganttReapplyFormulas('fake-id'), /admin/);
  assert.throws(() => ctx.ganttInsertSampleData('fake-id'), /admin/);
});
