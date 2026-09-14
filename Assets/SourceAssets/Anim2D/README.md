# SourceAssets/Anim2D

このフォルダには **Anim2D** の元ファイルを置きます。

## 対応拡張子

.anim / .fbx

## 置き方

`Anim2D/<カテゴリ.../>ファイル名` に置いてください。
カテゴリは省略できます(この場合は種別フォルダの直下に置きます)。
カテゴリは `Player/Attack` のように複数階層にもできます。

## 生成される Data の例

`Assets/SourceAssets/Anim2D/Sample/Foo.anim` を置くと、
`Assets/GameData/Anim2D/Sample/ANIM2D_Sample_Foo.asset` が自動的に作られます。

## 注意

元ファイルを削除しても Data 自体は消えません(参照が「未設定(または Missing)」になるだけです。Validation の一覧に欠落として表示されます)。
