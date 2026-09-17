/**
 * O-10: 発注ツール全体の設定値（現時点ではガントの URL のみ）。
 * docs/32_spec_web.md §10.5②（WBS 番号によるガントへのリンク）を実装したもの。
 *
 * ガントの URL・スプレッドシート ID は本書・コード・テストには書かない
 * （§10.5・§2.5 の API トークンと同じ扱い）。`PropertiesService`（PropertiesAdapter 経由）に
 * 保存する運用とし、設定は admin ロールの管理画面（Web UI）から行う（O-9 のメンバー取り込みと
 * 同じ「設定保存の仕組みを共用」= どちらも PropertiesAdapter 経由の小さな設定値という形を踏襲）。
 *
 * 登録している API:
 *   - settings.get         現在の設定値を返す（閲覧のみでも参照可。リンクを開くだけの用途のため）
 *   - settings.setGanttUrl ガント URL を設定/クリアする（admin のみ）
 */

var SPEC_WEB_SETTINGS_GANTT_URL_PROPERTY_KEY = 'SPEC_WEB_SETTINGS_GANTT_URL';

function specWebSettingsError_(message, status) {
  var err = new Error(message);
  err.status = status || 400;
  return err;
}

registerApi_('settings.get', function () {
  return { ganttUrl: PropertiesAdapter.getScriptProperty(SPEC_WEB_SETTINGS_GANTT_URL_PROPERTY_KEY) || '' };
});

registerApi_('settings.setGanttUrl', function (ctx) {
  specWebRequireRole_(ctx.auth, SPEC_WEB_ROLES.ADMIN, 'ガント URL の設定には admin 権限が必要です');
  var url = String(ctx.params.url || '').trim();
  if (url && !/^https?:\/\//.test(url)) {
    throw specWebSettingsError_('URL は http(s):// で始まる必要があります', 400);
  }
  if (url) {
    PropertiesAdapter.setScriptProperty(SPEC_WEB_SETTINGS_GANTT_URL_PROPERTY_KEY, url);
  } else {
    PropertiesAdapter.deleteScriptProperty(SPEC_WEB_SETTINGS_GANTT_URL_PROPERTY_KEY);
  }
  return { ganttUrl: url };
});
