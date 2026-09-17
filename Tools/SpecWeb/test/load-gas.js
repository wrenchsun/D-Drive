'use strict';

/**
 * GAS のグローバルスコープ連結を Node 上で再現する小さなローダー。
 *
 * GAS は 1 プロジェクトの全ファイル（src/**\/*.js）を 1 つのグローバルスコープに
 * 連結して実行する。Node ではモジュール毎に独立したスコープを持つため、
 * `vm` で 1 つの共有コンテキストを作り、そこに src/**\/*.js を順に読み込むことで
 * 同じ状況を再現する。
 *
 * DriveApp / LockService / PropertiesService / Session / ContentService /
 * HtmlService / Utilities / Logger は GAS のホスト提供グローバルであり、
 * Node には存在しない。ここではそれぞれの最小限のフェイクをコンテキストに
 * 注入する（Storage.js 等は "アダプタ"（src/adapters/*.js）を経由してしか
 * これらを呼ばないので、フェイクを GAS グローバルの境界に注入するだけで、
 * アダプタより上の層（Storage/Auth/Code）はすべて本物のコードのまま検証できる）。
 *
 * 依存ゼロ方針のため、この読み込み処理自体も node:fs / node:path / node:vm
 * という Node 組み込みモジュールだけで書く。
 */

const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const SRC_ROOT = path.join(__dirname, '..', 'src');
const DEFAULT_FOLDER_ID = 'fake-folder';

function listJsFilesSorted_(dir) {
  const entries = fs.readdirSync(dir, { withFileTypes: true });
  let files = [];
  for (const entry of entries) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      files = files.concat(listJsFilesSorted_(full));
    } else if (entry.isFile() && entry.name.endsWith('.js')) {
      files.push(full);
    }
  }
  return files.sort();
}

function createFakeDriveApp_(initialFiles, options) {
  options = options || {};
  const folders = new Map();
  // O-14: フォルダの編集者共有（addEditor/removeEditor）を folder id ごとに追跡する。
  // `options.shareShouldFail` を渡すと、共有・共有解除の呼び出しが常に例外を投げる
  // （「Drive での共有に失敗しても users.json への追加自体は成功する」ケースの検証用）。
  const editorsByFolder = new Map();
  // 2026-09-17（docs/41 P2-11）: ファイル名ごとの書き込み回数。
  // 「N 件の一括反映で <collection>.json への書き込みが 1 回だけ」を検証するために数える
  // （`Storage.mutateMany` が 1 ロック内で 1 読み・N 件更新・1 書きになっていること）。
  const writeCounts = new Map();

  function countWrite(name) {
    writeCounts.set(name, (writeCounts.get(name) || 0) + 1);
  }

  function getOrCreateFolder(id) {
    if (!folders.has(id)) {
      folders.set(id, new Map());
    }
    if (!editorsByFolder.has(id)) {
      editorsByFolder.set(id, new Set());
    }
    return folders.get(id);
  }

  const defaultFolder = getOrCreateFolder(DEFAULT_FOLDER_ID);
  Object.keys(initialFiles || {}).forEach((name) => {
    defaultFolder.set(name, initialFiles[name]);
  });

  function fileWrapper(filesMap, name) {
    return {
      getBlob() {
        const content = filesMap.get(name);
        return {
          getDataAsString() {
            return content;
          }
        };
      },
      setContent(content) {
        filesMap.set(name, content);
        countWrite(name);
      }
    };
  }

  const DriveApp = {
    getFolderById(id) {
      const filesMap = getOrCreateFolder(id);
      const editors = editorsByFolder.get(id);
      return {
        getFilesByName(name) {
          let handed = false;
          return {
            hasNext() {
              return filesMap.has(name) && !handed;
            },
            next() {
              handed = true;
              return fileWrapper(filesMap, name);
            }
          };
        },
        createFile(name, content) {
          filesMap.set(name, content);
          countWrite(name);
          return fileWrapper(filesMap, name);
        },
        addEditor(email) {
          if (options.shareShouldFail) {
            throw new Error('フェイク DriveApp: 共有に失敗しました（テスト用）');
          }
          editors.add(String(email || '').toLowerCase());
        },
        removeEditor(email) {
          if (options.shareShouldFail) {
            throw new Error('フェイク DriveApp: 共有解除に失敗しました（テスト用）');
          }
          editors.delete(String(email || '').toLowerCase());
        }
      };
    }
  };

  return {
    DriveApp,
    defaultFolderId: DEFAULT_FOLDER_ID,
    files: defaultFolder,
    folders,
    editorsByFolder,
    writeCounts,
    /** `<collection>.json` が書かれた回数（docs/41 P2-11 の回帰テスト用）。 */
    writeCount(fileName) {
      return writeCounts.get(fileName) || 0;
    }
  };
}

function createFakePropertiesService_(initial) {
  const store = new Map(Object.entries(initial || {}));
  const api = {
    getProperty(key) {
      return store.has(key) ? store.get(key) : null;
    },
    setProperty(key, value) {
      store.set(key, String(value));
    },
    deleteProperty(key) {
      store.delete(key);
    }
  };
  const PropertiesService = { getScriptProperties: () => api };
  return { PropertiesService, store };
}

function createFakeSession_(initialEmail) {
  const state = { email: initialEmail || '' };
  const Session = {
    getActiveUser() {
      return { getEmail: () => state.email };
    }
  };
  return {
    Session,
    setActiveUserEmail(email) {
      state.email = email;
    }
  };
}

function createFakeLockService_() {
  let locked = false;
  const LockService = {
    getScriptLock() {
      return {
        waitLock(_timeoutMs) {
          if (locked) {
            throw new Error('フェイク LockService: 既にロックされています（再入不可）');
          }
          locked = true;
        },
        releaseLock() {
          locked = false;
        }
      };
    }
  };
  return { LockService };
}

function createFakeContentService_() {
  const ContentService = {
    MimeType: { JSON: 'application/json' },
    createTextOutput(text) {
      let mime = null;
      const output = {
        setMimeType(m) {
          mime = m;
          return output;
        },
        getContent() {
          return text;
        },
        getMimeType() {
          return mime;
        }
      };
      return output;
    }
  };
  return { ContentService };
}

function createFakeHtmlService_() {
  function fakeOutput(content) {
    const output = {
      _content: content,
      setTitle() {
        return output;
      },
      addMetaTag() {
        return output;
      },
      setXFrameOptionsMode() {
        return output;
      },
      getContent() {
        return output._content;
      }
    };
    return output;
  }
  const HtmlService = {
    // DEFAULT は 2026-09-17（docs/41 整理項目）で renderUi_ が使うようになった値。
    // ALLOWALL も §4.6（埋め込み、v2）で戻す可能性があるため残す。
    XFrameOptionsMode: { ALLOWALL: 'ALLOWALL', DEFAULT: 'DEFAULT' },
    createHtmlOutput(content) {
      return fakeOutput(content);
    },
    createHtmlOutputFromFile(filename) {
      return fakeOutput('<!-- include:' + filename + ' -->');
    },
    createTemplateFromFile(filename) {
      return {
        evaluate() {
          return fakeOutput('<!-- rendered:' + filename + ' -->');
        }
      };
    }
  };
  return { HtmlService };
}

function createFakeUtilities_() {
  let counter = 0;
  const Utilities = {
    getUuid() {
      counter += 1;
      return 'fake-uuid-' + counter + '-' + Math.random().toString(16).slice(2);
    }
  };
  return { Utilities };
}

// O-13: ScriptApp.getService().getUrl()（トップの exec URL、src/Code.js の specWebExecUrl_）の
// フェイク。実際の値は関係ない（発注リンクの組み立てテストは固定の execUrl を直接渡す方式のため）。
function createFakeScriptApp_(execUrl) {
  var url = execUrl || 'https://script.google.com/macros/s/fake-script-id/exec';
  var ScriptApp = {
    getService: function () {
      return { getUrl: function () { return url; } };
    }
  };
  return { ScriptApp: ScriptApp, url: url };
}

function createFakeLogger_() {
  const lines = [];
  const Logger = {
    log(message) {
      lines.push(String(message));
    }
  };
  return { Logger, lines };
}

/**
 * @param {object} [options]
 * @param {string} [options.activeUserEmail] Session.getActiveUser().getEmail() の初期値
 * @param {Object<string,string>} [options.driveFiles] 既定フォルダに置く初期ファイル { "users.json": "..." }
 * @param {Object<string,string>} [options.scriptProperties] PropertiesService の初期値
 * @param {boolean} [options.driveShareShouldFail] true にすると DriveApp の addEditor/removeEditor
 *   （O-14、フォルダ共有）が常に例外を投げる（失敗時の警告動作を検証するためのフェイク）
 * @param {string} [options.scriptExecUrl] ScriptApp.getService().getUrl() のフェイク値（O-13）
 * @return {vm.Context} 読み込んだ src の全グローバル（doGet 等）+ `__fakes`（フェイクの操作用ハンドル）
 */
function loadGas(options) {
  options = options || {};

  const drive = createFakeDriveApp_(options.driveFiles, { shareShouldFail: options.driveShareShouldFail });
  const properties = createFakePropertiesService_(options.scriptProperties);
  const session = createFakeSession_(options.activeUserEmail);
  const lock = createFakeLockService_();
  const content = createFakeContentService_();
  const html = createFakeHtmlService_();
  const utilities = createFakeUtilities_();
  const logger = createFakeLogger_();
  const script = createFakeScriptApp_(options.scriptExecUrl);

  if (!properties.store.has('SPEC_WEB_DRIVE_FOLDER_ID')) {
    properties.store.set('SPEC_WEB_DRIVE_FOLDER_ID', drive.defaultFolderId);
  }

  const sandbox = {
    console,
    DriveApp: drive.DriveApp,
    PropertiesService: properties.PropertiesService,
    Session: session.Session,
    LockService: lock.LockService,
    ContentService: content.ContentService,
    HtmlService: html.HtmlService,
    Utilities: utilities.Utilities,
    Logger: logger.Logger,
    ScriptApp: script.ScriptApp
  };

  const context = vm.createContext(sandbox);
  const files = listJsFilesSorted_(SRC_ROOT);
  for (const file of files) {
    const code = fs.readFileSync(file, 'utf8');
    vm.runInContext(code, context, { filename: file });
  }

  context.__fakes = { drive, properties, session, lock, content, html, utilities, logger, script };
  return context;
}

module.exports = { loadGas };
