/**
 * doGet / doPost のルーティング（唯一の入口）。
 *
 * 2 つのデプロイ（docs/32_spec_web.md §2.3）は同じこの doGet/doPost を指す:
 *   ① 人向け SPA: 実行者=アクセスした人、Anyone with Google account
 *   ② D-Drive API: 実行者=Me、Anyone（ログイン不要・token で保護）
 *
 * 振り分けは `?api=1` の有無だけで行う。
 *   - 無い場合: ① の SPA を返す（Google ログイン + users.json 許可リスト）
 *   - 有る場合: ② の API 経路。**API トークン必須**（2026-09-17 の P1-2 修正）。
 *
 * 2026-09-17（P1-2、CSRF 対策の仕様変更）: `?api=1` で token が無いときに
 * Google セッションへフォールバックする挙動を廃止した。`/exec` への通常のブラウザ遷移は
 * アクセスした人の Google セッションで実行されるため、
 * `.../exec?api=1&name=users.upsert&email=...&role=admin` のようなリンクを admin に
 * 踏ませるだけで admin 権限の API が走る CSRF になっていた。人向け SPA（①）は
 * `google.script.run`（→ `specWebUiCall`、Google 側の CSRF 保護あり）しか使わないため、
 * この経路を使う正当なクライアントはもう存在しない（docs/32_spec_web.md §2.3・README §7）。
 *
 * 実際の API（アセット CRUD・調整値 等）はこのファイルを編集せず、
 * 各チケットが自分のファイルで `registerApi_(name, handler)` するだけで追加できる
 * （Api/Registry.js）。
 *
 * 追補（2026-09-14）: `token` は GET（doGet）のクエリパラメータでは受け付けない
 * （handleApiRequest_ 参照）。GET のクエリ文字列は GAS の実行ログ・中継プロキシ・
 * ブラウザ履歴に残るため、token を運ぶリクエストは必ず POST（doPost）の本文で送らせる
 * （docs/32_spec_web.md §7）。人向け SPA（①）は token を使わない（セッション認証のみ）ため、
 * SPA 自身の `?api=1` の GET 呼び出し（html/App.html の SpecWebClient.callApi）は影響を受けない。
 */

function doGet(e) {
  return handleSpecWebRequest_(e || {}, 'GET');
}

function doPost(e) {
  return handleSpecWebRequest_(e || {}, 'POST');
}

function handleSpecWebRequest_(e, method) {
  var params = e.parameter || {};
  if (params.api === '1') {
    return handleApiRequest_(e, method);
  }
  return renderUi_(params);
}

/**
 * `?page=manual&p=<ページ名>` / `?page=order&id=<id>` / `?page=group&id=<id>` を
 * 人向け SPA の初期画面へ変換する（2026-09-14 追加、O-13 で order/group を追加）。
 * Unity の「マニュアル」ボタン（Assets/DDrive/Editor/Manual/ManualUrlBuilder.cs）が
 * 開く URL 契約: `<人向けURL>?page=manual&p=<ページ名（拡張子なし、トップは Readme）>`。
 * `p` が不正・未知でもトップ（SPEC_WEB_MANUAL_TOP_PAGE）へフォールバックする（例外で止めない）。
 *
 * O-13「発注リンクをコピー」が生成する URL 契約:
 *   - `<人向けURL>?page=order&id=<種別::識別子>` → 一覧画面（assets）をその発注の詳細パネルが
 *     開いた状態で表示する（html/Assets.html 側が `openId` を見て assets.get する）
 *   - `<人向けURL>?page=group&id=<og_...>` → 発注ツリー画面（orders）をその発注グループへ
 *     スクロールした状態で表示する（html/OrderTree.html 側が `openGroupId` を見る）
 * 2026-09-17（P1-3、反射型 XSS 対策）: `id` は `SPEC_WEB_INITIAL_ID_PATTERN` で形だけ検証し、
 * 外れたら `screen: null`（既定画面）にする。この値は html/Index.html の `<script>` 内へ
 * 埋め込まれるため（`specWebJsonForScript_` でエスケープもする＝二重の防御）、
 * `</script>` を含むような値をそもそも通さない。存在するかどうかはここでは検証しない
 * （サーバー往復が要るため、「見つかりません」表示は画面側が assets.get/orderGroups.get の
 * 結果を見て行う。例外で止めない）。
 *
 * `page` パラメータが無い（通常のアクセス）場合は既定画面（html/App.html の DEFAULT_SCREEN_ID）
 * のままにする（screen: null）。
 */
/**
 * `?page=order&id=…` / `?page=group&id=…` の id として受け付けてよい形かを見る。
 *
 * 2026-09-17（P1-3、反射型 XSS の二重防御）: 埋め込み側は specWebJsonForScript_ で
 * 無害化したが、そもそも発注 id / 発注グループ id に記号は入らない（識別子は
 * PascalCase、`Presentation:Name` の形まで）。想定外の値は開かずに既定画面へ落とす。
 * 空文字も「指定なし」として既定画面にする（従来と同じ扱い）。
 */
function specWebIsSafeOpenId_(id) {
  return !!id && id.length <= 200 && /^[A-Za-z0-9_:.\-]+$/.test(id);
}

function resolveInitialScreen_(params) {
  if (params && params.page === 'manual') {
    var requested = String(params.p || '');
    var page = SPEC_WEB_MANUAL_PAGE_NAMES.indexOf(requested) !== -1 ? requested : SPEC_WEB_MANUAL_TOP_PAGE;
    return { screen: 'manual', params: { p: page } };
  }
  if (params && params.page === 'order') {
    var orderId = String(params.id || '');
    if (!specWebIsSafeOpenId_(orderId)) return { screen: null, params: {} };
    // O-15: リネーム済みの発注は旧 id のリンクのままでも新 id へ振り替える
    // （specWebResolveAssetRenameChain_、src/Assets.js。記録が無ければ orderId をそのまま返す）。
    return { screen: 'assets', params: { openId: specWebResolveAssetRenameChain_(orderId) } };
  }
  if (params && params.page === 'group') {
    var groupId = String(params.id || '');
    if (!specWebIsSafeOpenId_(groupId)) return { screen: null, params: {} };
    return { screen: 'orders', params: { openGroupId: groupId } };
  }
  return { screen: null, params: {} };
}

/**
 * O-13: トップの exec URL（`.../exec`）。iframe サンドボックス内の `window.location` は
 * `script.googleusercontent.com` を指すため使えず（docs/32 §2.4 訂正）、コピー用リンクは
 * サーバー側の `ScriptApp.getService().getUrl()` をテンプレート経由で埋め込む必要がある
 * （html/Index.html の `window.SpecWebExecUrl`）。ScriptApp が使えない状況（テスト等）でも
 * 例外で止めず空文字にフォールバックする。
 */
function specWebExecUrl_() {
  try {
    return ScriptApp.getService().getUrl() || '';
  } catch (e) {
    return '';
  }
}

/**
 * D-Drive の書き込みトークン（principal が 'ddrive:write'）で呼べる API 名の許可リスト
 * （docs/32_spec_web.md §5.2・§7、W-12）。
 *
 * 書き込みトークンはチーム全員の D-Drive に配る（同期担当者だけに限定しない、§9-9 決定）ため、
 * 「持っている人が増えても、企画が Web で入力した内容（アセット仕様の本文・調整値の値・
 * 機能仕様ページ・コメント）を D-Drive から上書きできない」ことをコード側で強制する必要がある。
 * auth.role（write トークンは editor 相当）による権限チェックだけでは
 * tuningScalarUpdate/assets.update 等も通ってしまう（実際に W-6〜W-8 実装時点でこの穴が
 * 残っていた。docs/32_spec_web.md「既知の未対応・引き継ぎ事項」参照）ため、
 * API 名そのものをここで固定する。ping/whoami は状態を変更しない（動作確認・トークン検証用）
 * ため許可リストに含めている。
 *
 * O-14（2026-09-14）: `users.list`/`users.upsert`/`users.remove`（ログイン許可の管理、
 * `Api/UserAdmin.js`）は意図的にこの許可リストへ加えない。write トークンの role は
 * editor 相当にしかならず、これらの API は `specWebRequireRole_(auth, SPEC_WEB_ROLES.ADMIN, ...)`
 * を要求するため、このリストに載せなくてもトークンからは常に 403 になる
 * （二重の防御。トークンで admin 相当のロールを持たせる予定も無い）。
 */
var DDRIVE_WRITE_TOKEN_ALLOWED_APIS = ['ping', 'whoami', 'choices', 'assetState', 'tuningUsage', 'assetParams'];

function handleApiRequest_(e, method) {
  var params = e.parameter || {};
  // token は POST（doPost）の本文でのみ受け付ける（上記ファイルコメント参照）。GET に token が
  // 付いていたら、有効/無効を検証する前に拒否する（無効な token を試したログを積む必要も無い）。
  if (method === 'GET' && params.token) {
    return ContentAdapter.json(
      { ok: false, error: 'token は GET のクエリパラメータでは受け付けません。POST の本文で送ってください。' },
      400
    );
  }
  var auth = authenticateRequest_(e);
  if (!auth.ok) {
    return ContentAdapter.json({ ok: false, error: auth.message }, auth.status);
  }
  var name = params.name;
  if (auth.tokenKind === 'write' && DDRIVE_WRITE_TOKEN_ALLOWED_APIS.indexOf(name) === -1) {
    return ContentAdapter.json(
      { ok: false, error: '書き込みトークンで呼べる API ではありません（choices/assetState/tuningUsage/assetParams のみ許可）: ' + name },
      403
    );
  }
  var body = specWebInvokeApi_(name, params, auth);
  return ContentAdapter.json(body, body.status || (body.ok ? 200 : 500));
}

/**
 * 登録済み API（registerApi_）を 1 件呼び出し、呼び出し元（handleApiRequest_・specWebUiCall）
 * 共通の応答形（{ok:true, ...} または {ok:false, status, error, currentRevision?}）に整える。
 *
 * 2026-09-14 追補（実デプロイで判明した誤りの修正、docs/32_spec_web.md §2.4 訂正）:
 * 人向け SPA（① デプロイ）は HtmlService の iframe サンドボックス内で動くため、
 * `fetch(window.location.href + ...)` は `/exec` ではなく別オリジンの
 * サンドボックス URL を指すだけで機能しない（`allow-same-origin` があっても `/exec` への
 * 同一オリジン fetch にはならない）。GAS 公式の方法は `google.script.run`
 * （https://developers.google.com/apps-script/guides/html/communication）であり、
 * これはクエリパラメータ・POST 本文を経由しない（HTTP リクエストではない）ため、
 * `specWebUiCall`（下記）という google.script.run 専用の入口を新設した。
 * `doGet`/`doPost`（② D-Drive API、token 認証）はこの変更の影響を受けない。
 */
function specWebInvokeApi_(name, params, auth) {
  var handler = getApi_(name);
  if (!handler) {
    return { ok: false, status: 404, error: '未登録の API です: ' + name };
  }
  try {
    var result = handler({ e: { parameter: params }, params: params, auth: auth }) || {};
    var body = {};
    for (var key in result) {
      if (Object.prototype.hasOwnProperty.call(result, key)) body[key] = result[key];
    }
    body.ok = true;
    return body;
  } catch (err) {
    // RevisionConflictError（409・currentRevision 付き）専用の分岐を、
    // 「err.status を持つ任意のエラー」を汎用的に本文の status へ変換する形に一般化した
    // （W-4/W-5 の入力検証エラー、W-6/W-7 の SpecWebValidationError(400)/SpecWebNotFoundError(404)/
    // SpecWebForbiddenError(403) 等、個別の err.name チェックではなく err.status を汎用的に見る形に
    // すれば同じ throw new Error() + err.status で表現できる。RevisionConflictError の挙動・
    // 既存テストは変えていない）。
    var status = err && typeof err.status === 'number' ? err.status : 500;
    var body = { ok: false, status: status, error: String((err && err.message) || err) };
    if (err && err.currentRevision !== undefined) {
      body.currentRevision = err.currentRevision;
    }
    return body;
  }
}

/**
 * 人向け SPA（① デプロイ）専用の入口。`html/App.html` の `SpecWebClient.callApi` が
 * `google.script.run.withSuccessHandler(...).specWebUiCall(name, params)` として呼ぶ
 * （上記 specWebInvokeApi_ のコメント参照）。トークンは使わず、常に
 * `authenticateSession_()`（Google ログイン + users.json 許可リスト）で認証する。
 * D-Drive の書き込みトークン kind 許可リスト（DDRIVE_WRITE_TOKEN_ALLOWED_APIS）はここには
 * 適用しない（token を使わない経路のため無関係。企画側の人がログインして呼ぶ操作は、
 * 各 API 自身の role チェック（hasRole_ 等）でのみ制御する）。
 * @param {string} name `registerApi_` で登録された API 名
 * @param {Object} params プレーンな JSON 互換オブジェクト（google.script.run の制約）
 * @return {Object} { ok:true, ... } または { ok:false, status, error, currentRevision? }
 */
function specWebUiCall(name, params) {
  params = params || {};
  var session = authenticateSession_();
  if (!session.ok) {
    return { ok: false, status: session.status, error: session.message };
  }
  var auth = {
    ok: true,
    principal: session.email,
    email: session.email,
    role: session.role,
    displayName: session.displayName
  };
  return specWebInvokeApi_(name, params, auth);
}

/**
 * ① SPA 本体を返す。許可リスト外なら「メンバーのみ利用できます」ページを返す。
 * @param {Object} [params] e.parameter（`?page=manual&p=...` 等、初期画面の解決に使う。§resolveInitialScreen_）
 */
function renderUi_(params) {
  var auth = authenticateSession_();
  if (!auth.ok) {
    // 2026-09-14 追補（O-14）: 本人が管理者に伝えやすいよう、ログイン中のアカウントを表示する
    // （Google ログイン済みだが許可リスト外＝ auth.email がある場合のみ。未ログイン=401 では出せない）。
    var emailBlock = auth.email
      ? '<p>ログイン中のアカウント: ' + specWebEscapeHtml_(auth.email) + '</p>' +
        '<p>このメールアドレスを管理者に伝えてください。</p>'
      : '';
    return HtmlService.createHtmlOutput(
      '<!DOCTYPE html><html><head><meta charset="utf-8"></head>' +
        '<body style="font-family:sans-serif;padding:2rem;">' +
        '<p>' + specWebEscapeHtml_(auth.message) + '</p>' +
        emailBlock +
        '</body></html>'
    );
  }
  var initial = resolveInitialScreen_(params || {});
  var template = HtmlService.createTemplateFromFile('html/Index');
  template.currentUser = auth;
  template.initialScreen = initial.screen;
  template.initialParams = initial.params;
  template.execUrl = specWebExecUrl_(); // O-13: 発注リンクのコピー用（html/Index.html 参照）
  return template
    .evaluate()
    .setTitle('D-Drive 仕様書')
    .addMetaTag('viewport', 'width=device-width, initial-scale=1')
    // 2026-09-17（[41](../../docs/41_phase6_review_2026-09-17.md) 整理項目）:
    // `ALLOWALL`（= X-Frame-Options を付けない＝どのサイトからでも iframe に埋め込める）から
    // GAS 既定の `DEFAULT` に戻した。ALLOWALL が必要なのは「自分たちのサイトに Web アプリを
    // 埋め込む」用途（docs/32 §4.6 の機能仕様ページの埋め込み）だが、それは v2 でまだ無い。
    // `/exec` を直接開く通常の使い方は DEFAULT（= setXFrameOptionsMode を呼ばない場合と同じ）で
    // 動く（GAS 自身の iframe 越しの配信は Google 側で成立している）ため、
    // クリックジャッキングの面だけを縮める。**埋め込みを始めるときは ALLOWALL に戻すこと。**
    .setXFrameOptionsMode(HtmlService.XFrameOptionsMode.DEFAULT);
}

/** html/*.html から include するためのヘルパー（HtmlTemplate のスクリプトレット内で使う）。 */
function include_(filename) {
  return HtmlService.createHtmlOutputFromFile(filename).getContent();
}

/**
 * `<script>` の中に JSON リテラルとして埋め込むための文字列を作る（html/Index.html が使う）。
 *
 * 2026-09-17 セキュリティ修正（P1-3、反射型 XSS）: 以前は html/Index.html が
 * `<?!= JSON.stringify(x) ?>` を直接書いていた。`<?!= ?>` は非エスケープ出力で、
 * `JSON.stringify` は `<` も `/` もエスケープしないため、値に `</script><script>…` を
 * 含められると script 要素が閉じられて任意 JS が実行できた（`?page=order&id=...` の
 * id や、admin が users.upsert で設定する displayName が経路になっていた）。
 * `<` を `<` にすれば JSON としての意味は変わらないまま `</script>` を作れなくなる。
 * U+2028 / U+2029 は JSON では生のまま許されるが JS の行終端子なので合わせて退避する。
 */
function specWebJsonForScript_(value) {
  // 2026-09-17 追補: 正規表現リテラルの中に U+2028 / U+2029 を**生の文字で**書くと、
  // JS の仕様上そこが行終端子として扱われリテラルが閉じられず
  // 「SyntaxError: Invalid regular expression: missing /」になる
  // （GAS ではプロジェクト全体が読み込めなくなり、全リクエストが失敗する）。
  // 必ず \u2028 / \u2029 のエスケープで書くこと（コメントの中も同じ）。
  return JSON.stringify(value === undefined ? null : value)
    .replace(/</g, '\\u003c')
    .replace(/\u2028/g, '\\u2028')
    .replace(/\u2029/g, '\\u2029');
}

/** HTML への埋め込み用にエスケープする（拒否ページに表示するメールアドレス用、O-14）。 */
function specWebEscapeHtml_(text) {
  return String(text === null || text === undefined ? '' : text)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}
