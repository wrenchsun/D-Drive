/**
 * docs/DesignerManual/*.html のページ名一覧（拡張子なし）。
 * Tools/SpecWeb/tools/build-manual.js が生成する。手で編集しない。
 * src/Manual.js の manualGet がこの一覧に対して p を検証し、無ければ
 * SPEC_WEB_MANUAL_TOP_PAGE（Readme）にフォールバックする（未知ページで例外にしない）。
 */
var SPEC_WEB_MANUAL_TOP_PAGE = "Readme";
var SPEC_WEB_MANUAL_PAGE_NAMES = [
  "Readme",
  "anchor-data",
  "anchor-group",
  "anim-editor",
  "anim2d-editor",
  "asset-browser",
  "audio-editor",
  "bgm-data",
  "camera-haptics",
  "canvas-data",
  "canvas-editor",
  "getting-started",
  "glossary",
  "material-data",
  "material-editor",
  "model-editor",
  "prefab-data",
  "presentation",
  "scene-sound",
  "se-data",
  "spec-sync",
  "ui-skin",
  "ui-tween",
  "validation",
  "vfx-data",
  "vfx-editor"
];
