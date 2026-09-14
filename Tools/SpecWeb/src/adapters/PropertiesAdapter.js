/**
 * PropertiesService（スクリプトプロパティ）を薄く包むアダプタ。
 * API トークン（Auth.js/TokenAdmin.js）や Drive フォルダ ID の保存に使う。
 * docs/32_spec_web.md §2.4「PropertiesService 500KB(総量)/9KB(1値)」の制約に注意
 * （トークン・設定値のような小さい値だけを置く。正データは Drive JSON 側）。
 */

var PropertiesAdapter = {
  getScriptProperty: function (key) {
    return PropertiesService.getScriptProperties().getProperty(key);
  },

  setScriptProperty: function (key, value) {
    PropertiesService.getScriptProperties().setProperty(key, value);
  },

  deleteScriptProperty: function (key) {
    PropertiesService.getScriptProperties().deleteProperty(key);
  }
};
