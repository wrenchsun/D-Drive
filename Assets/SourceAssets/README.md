# SourceAssets

ここに元ファイル(音源・画像・FBX・アニメーション・Prefab)を置くと、
`ImportRule` が自動で Data・ID・カタログ・Addressables 登録まで行います。

**1 階層目のフォルダ名が種別として認識されます(大文字小文字も区別)。**
`SourceAssets` の直下に直接ファイルを置いたり、種別フォルダの上に別のフォルダを
挟んだりすると認識されません(Console に案内の警告が出ます)。

## 種別フォルダ一覧

- `Se/` — 詳細は `Se/README.md` を参照
- `Bgm/` — 詳細は `Bgm/README.md` を参照
- `Texture/` — 詳細は `Texture/README.md` を参照
- `Model/` — 詳細は `Model/README.md` を参照
- `Anim/` — 詳細は `Anim/README.md` を参照
- `Anim2D/` — 詳細は `Anim2D/README.md` を参照
- `Prefab/` — 詳細は `Prefab/README.md` を参照
- `Canvas/` — 詳細は `Canvas/README.md` を参照
- `Vfx/` — 詳細は `Vfx/README.md` を参照

詳しい仕様は `docs/09_editor_tools.md` §1.1 / `docs/10_workflow.md` §3.3 を参照してください。
