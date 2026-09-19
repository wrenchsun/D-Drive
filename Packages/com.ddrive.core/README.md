# D-Drive (`com.ddrive.core`)

D-Drive（Designer-Driven Re: IDE Visual Environment）は、プログラマーが **ID だけ**でモックを完成させ、デザイナーが専用エディタで中身（Audio/VFX/Model/Animation/Material/UI/Presentation/Timeline 等）を作れるようにする Unity フレームワークです。

> このファイルは雛形です（P-5 で作成）。導入手順の完成版は P-10 で仕上げます。

## 導入（5 ステップ・雛形）

1. **依存パッケージを追加**する（`package.json` の `dependencies` は自動解決されるが、git 配布の UniTask / R3 は手動追加が必要。詳細は消費側ドキュメント・セットアップウィザード（P-6）を参照）
2. **このパッケージを `manifest.json` に追加**する（git URL + `?path=Packages/com.ddrive.core` + タグ、[docs/42_distribution.md](https://github.com/wrenchsun/D-Drive) §3.2 参照）
3. **プロジェクト設定を確認**する（URP / Input System / API Compatibility Level。セットアップウィザード（P-6）が検査する）
4. **Addressables を初期化**する（`AddressableAssetSettings` が無ければ作成）
5. **起動オブジェクトを配置**し、`Tools > D-Drive > Generate` で `GameData` 等の既定フォルダを用意する

## 依存関係

| 種別 | パッケージ |
|---|---|
| レジストリ配布（`package.json` で自動解決） | `com.unity.addressables` / `com.unity.inputsystem` / `com.unity.nuget.newtonsoft-json` / `com.unity.render-pipelines.universal` / `com.unity.timeline` / `com.unity.ugui` |
| git 配布（手動追加が必要） | `com.cysharp.unitask` / `com.cysharp.r3`（+ scoped registry `org.nuget.r3`） |
| 任意（NGO を使う場合のみ） | `com.unity.netcode.gameobjects`（`versionDefines` の `DDRIVE_NGO` が自動で有効になる） |

## 既知の制約

- レンダーパイプラインは **URP のみ**対応（Built-in / HDRP は非対応）
- Unity **6000.3** 以上

## ドキュメント

- 設計書・運用ドキュメントは開発リポジトリの `docs/`（[docs/42_distribution.md](../../docs/42_distribution.md) が配布・互換性ポリシーの正本）
- デザイナー / プログラマー向けマニュアルは `Documentation~/`（P-9 のリリース手順で `docs/DesignerManual` / `docs/ProgrammerManual` から同期する予定）

## バージョニング

`CHANGELOG.md`（開発リポジトリ直下）に従って [Semantic Versioning](https://semver.org/lang/ja/) を採用する。互換性ポリシーの詳細は [docs/42_distribution.md](../../docs/42_distribution.md) §5 を参照。
