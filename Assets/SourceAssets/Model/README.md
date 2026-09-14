# SourceAssets/Model

このフォルダには **Model** の元ファイルを置きます。

## 対応拡張子

.fbx

## 置き方

`Model/<カテゴリ.../>ファイル名` に置いてください。
カテゴリは省略できます(この場合は種別フォルダの直下に置きます)。
カテゴリは `Player/Attack` のように複数階層にもできます。

## 生成される Data の例

`Assets/SourceAssets/Model/Sample/Foo.fbx` を置くと、
`Assets/GameData/Model/Sample/MODEL_Sample_Foo.asset` が自動的に作られます。

## 注意

元ファイルを削除しても Data 自体は消えません(参照が「未設定(または Missing)」になるだけです。Validation の一覧に欠落として表示されます)。
