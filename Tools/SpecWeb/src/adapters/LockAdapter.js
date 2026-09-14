/**
 * LockService を薄く包むアダプタ。
 * Storage.js の書き込み（putItem/deleteItem）を直列化するために使う
 * （docs/32_spec_web.md §2.2「同時編集」・§8 W-2 AC）。
 */

var LockAdapter = {
  getScriptLock: function () {
    return LockService.getScriptLock();
  },

  /**
   * lock.waitLock(timeoutMs) を呼び、成功したら true・タイムアウト例外は
   * false に変換する（呼び出し側で例外の型を気にしなくてよいようにする）。
   */
  waitLock: function (lock, timeoutMs) {
    try {
      lock.waitLock(timeoutMs);
      return true;
    } catch (err) {
      return false;
    }
  },

  releaseLock: function (lock) {
    lock.releaseLock();
  }
};
