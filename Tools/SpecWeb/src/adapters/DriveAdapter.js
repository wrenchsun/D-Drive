/**
 * DriveApp を薄く包むアダプタ。
 *
 * docs/32_spec_web.md §2.2 のとおり、コレクション(assets/tuning/users 等)は
 * 1 コレクション = Drive 上の 1 JSON ファイルとして持つ。ファイルはすべて
 * スクリプトプロパティ SPEC_WEB_DRIVE_FOLDER_ID が指すフォルダの直下に置く。
 *
 * Storage.js はこのアダプタの関数だけを呼び、DriveApp を直接参照しない
 * （Node のテストでは DriveApp 自体をフェイクに差し替えて検証する。
 *   test/load-gas.js 参照）。
 */

function driveAdapterGetRootFolder_() {
  var folderId = PropertiesAdapter.getScriptProperty('SPEC_WEB_DRIVE_FOLDER_ID');
  if (!folderId) {
    throw new Error(
      'SPEC_WEB_DRIVE_FOLDER_ID が未設定です。スクリプトプロパティで JSON ファイルを置く Drive フォルダの ID を設定してください。'
    );
  }
  return DriveApp.getFolderById(folderId);
}

function driveAdapterFindFile_(folder, fileName) {
  var it = folder.getFilesByName(fileName);
  return it.hasNext() ? it.next() : null;
}

var DriveAdapter = {
  /**
   * fileName の内容を UTF-8 文字列として返す。ファイルが無ければ null。
   */
  readFile: function (fileName) {
    var folder = driveAdapterGetRootFolder_();
    var file = driveAdapterFindFile_(folder, fileName);
    if (!file) return null;
    return file.getBlob().getDataAsString('UTF-8');
  },

  /**
   * fileName に content を書き込む。無ければ application/json で新規作成する。
   */
  writeFile: function (fileName, content) {
    var folder = driveAdapterGetRootFolder_();
    var file = driveAdapterFindFile_(folder, fileName);
    if (file) {
      file.setContent(content);
      return;
    }
    folder.createFile(fileName, content, 'application/json');
  },

  /**
   * O-14: データフォルダ（`SPEC_WEB_DRIVE_FOLDER_ID`）を編集者として共有する。
   * 既に共有済みでもエラーにしない（`DriveApp`/`Folder.addEditor` の既定動作）。
   * 呼び出し元（`Api/UserAdmin.js`）が try/catch で失敗を warning に変換する
   * （CLAUDE.md §0-4「例外で止めない」。docs/32_spec_web.md §11）。
   */
  addFolderEditor: function (email) {
    var folder = driveAdapterGetRootFolder_();
    folder.addEditor(email);
  },

  /** 上記の共有解除（既定では呼ばれない。docs/32_spec_web.md §11 の「共有も解除する」チェック時のみ）。 */
  removeFolderEditor: function (email) {
    var folder = driveAdapterGetRootFolder_();
    folder.removeEditor(email);
  }
};
