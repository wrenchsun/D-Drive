/**
 * Utilities / Logger を薄く包むアダプタ。
 *
 * `newUuid` はトークン発行（TokenAdmin.js）・コメント id 等で使う。
 * `log` は 2026-09-17（[41] 整理項目）でトークン発行からの呼び出しを外したため現在は未使用
 * （Logger 出力は Cloud Logging に残るため、トークン等の秘密を渡さないこと）。
 */

var UtilitiesAdapter = {
  newUuid: function () {
    return Utilities.getUuid();
  },

  log: function (message) {
    Logger.log(message);
  }
};
