using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // docs/1010 UX-19: 各新エディターの空状態「サンプルを作る」(UxEditorWindow.HasSample)の有無を固定する。
    // 実データ(Assets/GameData)には作らない(作成そのものは execute_code で確認し、削除済み)。ウィンドウは Show せずインスタンスだけ作る。
    public class UxSamplesTests
    {
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static Assembly EditorAssembly => typeof(DDrive.Editor.Menu.DDriveMenu).Assembly;

        // 型名 → サンプルを作れるか。Anim2D は元のスプライトシートが要るのでボタンは出さない(「＋ 新規作成」のウィザードだけ)。
        private static readonly (string Type, bool Has)[] Expected =
        {
            ("DDrive.Editor.Vfx.VfxEditor", true),
            ("DDrive.Editor.Audio.SeEditor", true),
            ("DDrive.Editor.Audio.BgmEditor", true),
            ("DDrive.Editor.CanvasTool.CanvasEditor", true),
            ("DDrive.Editor.Presentation.PresentationEditor", true),
            ("DDrive.Editor.Anim.AnimEditor", true),
            ("DDrive.Editor.Anim2D.Anim2DEditor", false),
            ("DDrive.Editor.Materials.MaterialEditor", true),
            ("DDrive.Editor.Materials.TextureEditor", true),
            ("DDrive.Editor.Model.ModelEditor", true),
            ("DDrive.Editor.PrefabTool.PrefabEditor", true),
            ("DDrive.Editor.CameraFx.ShakeEditor", true),
            ("DDrive.Editor.CameraFx.HapticsEditor", true),
            ("DDrive.Editor.Anchor.AnchorEditor", true),
            ("DDrive.Editor.Anchor.AnchorGroupEditor", true),
            ("DDrive.Editor.Ui.UiTweenEditor", true),
            ("DDrive.Editor.Ui.ButtonSkinEditor", true),
            ("DDrive.Editor.Ui.SliderSkinEditor", true),
        };

        [Test]
        public void HasSample_PerEditor()
        {
            foreach (var (typeName, has) in Expected)
            {
                var type = EditorAssembly.GetType(typeName, true);
                var w = (EditorWindow)ScriptableObject.CreateInstance(type);
                try
                {
                    var prop = type.GetProperty("HasSample", Inst);
                    Assert.IsNotNull(prop, typeName);
                    Assert.AreEqual(has, (bool)prop.GetValue(w), typeName);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(w);
                }
            }
        }

        [Test]
        public void UxSamples_HasMethodForEachSampleEditor()
        {
            var t = EditorAssembly.GetType("DDrive.Editor.Ux.UxSamples", true);
            foreach (var n in new[]
                     {
                         "Vfx", "Se", "Bgm", "Texture", "Material", "Model", "Prefab", "Anim", "Canvas", "Shake", "Haptics", "Anchor",
                         "AnchorGroup", "UiTween", "ButtonSkin", "SliderSkin", "Presentation",
                     })
            {
                Assert.IsNotNull(t.GetMethod(n, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic), n);
            }
        }
    }
}
