# SourceAssets/Bgm

このフォルダには **Bgm** の元ファイルを置きます。

## 対応拡張子

.wav / .mp3 / .ogg / .aiff / .aif

## 置き方

`Bgm/<カテゴリ.../>ファイル名` に置いてください。
カテゴリは省略できます(この場合は種別フォルダの直下に置きます)。
カテゴリは `Player/Attack` のように複数階層にもできます。

## 生成される Data の例

`Assets/SourceAssets/Bgm/Sample/Foo.wav` を置くと、
`Assets/GameData/Audio/BGM/Sample/BGM_Sample_Foo.asset` が自動的に作られます。

## 注意

元ファイルを削除しても Data 自体は消えません(参照が「未設定(または Missing)」になるだけです。Validation の一覧に欠落として表示されます)。
