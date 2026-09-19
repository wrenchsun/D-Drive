using DDrive.Editor.Dependencies;
using DDrive.Runtime.Audio;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [11_tasks.md] 5-6 — Archived タグの付与/解除。ScriptableObject.CreateInstance だけで完結する
    // (アセット化不要)ため、GameData/カタログには一切触れない。
    public class ArchiveTagServiceTests
    {
        [Test]
        public void SetArchived_True_AddsTag_False_RemovesIt()
        {
            var data = ScriptableObject.CreateInstance<SeData>();
            try
            {
                Assert.IsFalse(ArchiveTagService.IsArchived(data));

                ArchiveTagService.SetArchived(data, true);
                Assert.IsTrue(ArchiveTagService.IsArchived(data));
                Assert.Contains(ArchiveTagService.Tag, data.Tags);

                ArchiveTagService.SetArchived(data, false);
                Assert.IsFalse(ArchiveTagService.IsArchived(data));
                CollectionAssert.DoesNotContain(data.Tags, ArchiveTagService.Tag);
            }
            finally
            {
                Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void SetArchived_PreservesOtherTags()
        {
            var data = ScriptableObject.CreateInstance<SeData>();
            try
            {
                data.Tags = new[] { "Enemy", "Boss" };

                ArchiveTagService.SetArchived(data, true);
                CollectionAssert.AreEquivalent(new[] { "Enemy", "Boss", ArchiveTagService.Tag }, data.Tags);

                ArchiveTagService.SetArchived(data, false);
                CollectionAssert.AreEquivalent(new[] { "Enemy", "Boss" }, data.Tags);
            }
            finally
            {
                Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void SetArchived_Idempotent_DoesNotDuplicateTag()
        {
            var data = ScriptableObject.CreateInstance<SeData>();
            try
            {
                ArchiveTagService.SetArchived(data, true);
                ArchiveTagService.SetArchived(data, true);

                var count = 0;
                foreach (var tag in data.Tags)
                {
                    if (tag == ArchiveTagService.Tag)
                    {
                        count++;
                    }
                }

                Assert.AreEqual(1, count);
            }
            finally
            {
                Object.DestroyImmediate(data);
            }
        }
    }
}
