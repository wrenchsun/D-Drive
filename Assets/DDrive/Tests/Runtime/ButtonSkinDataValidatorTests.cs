using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Ui;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Runtime
{
    // [15_ui_interaction.md] A-4 — ButtonSkinDataValidator。
    public class ButtonSkinDataValidatorTests
    {
        private static List<ValidationResult> Run(ButtonSkinData data)
        {
            var results = new List<ValidationResult>();
            foreach (var r in new ButtonSkinDataValidator().Validate(data, new ValidationContext(new List<AssetDataBase> { data })))
            {
                results.Add(r);
            }

            return results;
        }

        [Test]
        public void AllTintAlphaZero_ReportsWarning()
        {
            var data = ScriptableObject.CreateInstance<ButtonSkinData>();
            var transparent = new Color(1f, 1f, 1f, 0f);
            data.Normal.Tint = transparent;
            data.Hover.Tint = transparent;
            data.Pressed.Tint = transparent;
            data.Selected.Tint = transparent;
            data.Disabled.Tint = transparent;
            data.Locked.Tint = transparent;

            var results = Run(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Warning));
            Object.DestroyImmediate(data);
        }

        [Test]
        public void NonZeroTintAlpha_DoesNotReportWarning()
        {
            var data = ScriptableObject.CreateInstance<ButtonSkinData>();
            var results = Run(data);
            Assert.IsFalse(results.Exists(r => r.Severity == ValidationSeverity.Warning));
            Object.DestroyImmediate(data);
        }

        [Test]
        public void MissingClickSe_ReportsInfo()
        {
            var data = ScriptableObject.CreateInstance<ButtonSkinData>();
            var results = Run(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Info && r.Message.Contains("ClickSe")));
            Object.DestroyImmediate(data);
        }
    }
}
