/**
 * docs/DesignerManual・docs/ProgrammerManual の *.html のページ名一覧（拡張子なし）。
 * kind（"designer"/"programmer"）ごとに分かれている（ページ名が両マニュアルで重複するため）。
 * Tools/SpecWeb/tools/build-manual.js が生成する。手で編集しない。
 * src/Manual.js の manualGet がこの一覧に対して kind+p を検証し、無ければ
 * SPEC_WEB_MANUAL_DEFAULT_KIND / SPEC_WEB_MANUAL_TOP_PAGE にフォールバックする
 * （未知の kind/ページで例外にしない）。
 */
var SPEC_WEB_MANUAL_KINDS = ["designer","programmer"];
var SPEC_WEB_MANUAL_DEFAULT_KIND = "designer";
var SPEC_WEB_MANUAL_TOP_PAGE = "Readme";
var SPEC_WEB_MANUAL_PAGE_NAMES = {
  "designer": [
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
    "cutscene-maya-export",
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
  ],
  "programmer": [
    "Readme",
    "audio-api",
    "bootstrap",
    "concepts",
    "extending",
    "getting-started",
    "handle",
    "model-anim-api",
    "net-api",
    "presentation-api",
    "rules",
    "ui-api",
    "vfx-api"
  ]
};
