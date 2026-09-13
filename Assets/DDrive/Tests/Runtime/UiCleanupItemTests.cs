using System.Collections.Generic;
using DDrive.Runtime.Ui;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Runtime
{
    // 2026-09-14 — docs/24 整理項目の対応(共通のパス計算 TransformPath、Options.OnChanged の Bind 前購読)。
    public class UiCleanupItemTests
    {
        [Test]
        public void TransformPath_ReturnsPathUsableByFind()
        {
            var root = new GameObject("Root");
            try
            {
                var a = new GameObject("A");
                a.transform.SetParent(root.transform, false);
                var b = new GameObject("B");
                b.transform.SetParent(a.transform, false);

                var path = TransformPath.GetRelative(root.transform, b.transform);

                Assert.AreEqual("A/B", path);
                Assert.AreSame(b.transform, root.transform.Find(path));
                Assert.AreEqual(string.Empty, TransformPath.GetRelative(root.transform, root.transform));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // 以前は未 Bind のときの購読を黙って捨てていた。Bind 後に届き、Bind し直すと新しいインスタンスへ付け替わる。
        [Test]
        public void Options_SubscribedBeforeBind_ReceivesChanges_AndFollowsRebind()
        {
            Options.Bind(null);
            var received = new List<float>();
            void Handler(OptionKey key, float value) => received.Add(value);
            Options.OnChanged += Handler;
            try
            {
                var first = new OptionStore();
                Options.Bind(first);
                first.Set(OptionKey.UiSpeedScale, 0.5f);
                Assert.AreEqual(new List<float> { 0.5f }, received);

                var second = new OptionStore();
                Options.Bind(second);
                first.Set(OptionKey.UiSpeedScale, 0.7f);
                second.Set(OptionKey.UiSpeedScale, 0.6f);
                Assert.AreEqual(new List<float> { 0.5f, 0.6f }, received, "前のインスタンスからは外れ、新しいインスタンスに付く");
            }
            finally
            {
                Options.OnChanged -= Handler;
                Options.Bind(null);
            }
        }
    }
}
