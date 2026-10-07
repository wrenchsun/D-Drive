# D-Drive ルール
1. .unity/.prefab/.asset/画像/音を文字編集しない。.metaを手で作らない・消さない
2. Library/ Temp/ Logs/ UserSettings/ obj/ *.csproj *.slnは生成物(編集不可)
3. 禁止: Instantiate/Resources.Load/Addressables.Load*/Time.time系/AudioSource.Play。ランタイムからUnityEditor参照。定常経路のLINQ・クロージャ・boxing
4. 例外で止めない(警告+no-op/Placeholder)
5. Dataは読み取り専用。書くならUndo.RecordObject+SetDirty
6. メニューはDDriveMenu定数。EditorWindowはScrollViewルート
7. プレビューは実Manager駆動。確認はシーン/Prefabで
8. Managerをnewするのは起動配線・テスト・プレビューだけ
9. 迷ったら聞く(シリアライズ・asmdef・ProjectSettings)
10. 互換性は追加のみ(削除・改名・型変更はMAJOR)
MCP: 最初にddrive_status。execute_codeよりddrive_*優先。書込はMcpAllowWrite必須
