using System;

namespace DDrive.Foundation.Data
{
    // [09_editor_tools.md] §8.6 / [39_usability_fixes_2026-09-17.md] U-11 —
    // Inspector（AssetDataInspector が描く共通本文）で読み取り専用（グレーアウト表示）にしたいフィールドに付ける。
    // ID や保存フックが自動更新するフィールドなど、「手編集しないこと」という注意書きだけでは
    // 実際に手編集されてしまう項目に使う。
    //
    // Data クラス側にこの属性を付けるだけで反映される（AssetDataInspector が反射で収集する。
    // Editor/Inspector 側のコード変更は不要）。専用エディタ内に埋め込まれた「Inspector(全フィールド)」
    // (UiTweenEditorWindow / CanvasEditorWindow / MaterialEditorWindow / PrefabEditorWindow が
    // `new InspectorElement(so)` で内部的に同じ AssetDataInspector を使っている)にも自動で効く。
    //
    // 「読み取り専用」は値を見えなくするものではない(HideInInspector とは違う)。フィールド自体は表示され、
    // 値の確認はできるが、テキスト入力・チェックボックス操作等はできない(disabled)。どうしても直す必要が
    // あるときは、Editor コードから Undo.RecordObject + EditorUtility.SetDirty を使って書き換える([00] §0-5)。
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public sealed class InspectorReadOnlyAttribute : Attribute
    {
    }
}
