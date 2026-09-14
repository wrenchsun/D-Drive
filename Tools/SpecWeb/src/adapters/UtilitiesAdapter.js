/**
 * Utilities / Logger を薄く包むアダプタ。トークン発行（TokenAdmin.js）で使う。
 */

var UtilitiesAdapter = {
  newUuid: function () {
    return Utilities.getUuid();
  },

  log: function (message) {
    Logger.log(message);
  }
};
