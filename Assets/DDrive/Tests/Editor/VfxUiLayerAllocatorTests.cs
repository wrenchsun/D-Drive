using DDrive.Editor.Vfx;
using NUnit.Framework;

namespace DDrive.Tests.Editor
{
    public class VfxUiLayerAllocatorTests
    {
        private static string[] EmptyLayers()
        {
            var layers = new string[32];
            for (var i = 0; i < layers.Length; i++)
            {
                layers[i] = string.Empty;
            }

            return layers;
        }

        [Test]
        public void FindOrClaim_AlreadyPresent_ReturnsExistingIndex()
        {
            var layers = EmptyLayers();
            layers[10] = "VfxUI";

            var index = VfxUiLayerAllocator.FindOrClaim(layers, "VfxUI");

            Assert.AreEqual(10, index);
        }

        [Test]
        public void FindOrClaim_NotPresent_ClaimsFirstEmptyUserSlot()
        {
            var layers = EmptyLayers();
            layers[8] = "SomeOtherLayer";

            var index = VfxUiLayerAllocator.FindOrClaim(layers, "VfxUI");

            Assert.AreEqual(9, index);
            Assert.AreEqual("VfxUI", layers[9]);
        }

        [Test]
        public void FindOrClaim_NoEmptySlots_ReturnsMinusOneAndDoesNotMutate()
        {
            var layers = EmptyLayers();
            for (var i = VfxUiLayerAllocator.FirstUserLayer; i <= VfxUiLayerAllocator.LastUserLayer; i++)
            {
                layers[i] = $"Used{i}";
            }

            var snapshot = (string[])layers.Clone();
            var index = VfxUiLayerAllocator.FindOrClaim(layers, "VfxUI");

            Assert.AreEqual(-1, index);
            CollectionAssert.AreEqual(snapshot, layers);
        }

        [Test]
        public void FindOrClaim_IgnoresBuiltinLayerSlots()
        {
            var layers = EmptyLayers();
            // ビルトインスロット(0-7)に同名が入っていても、ユーザーレイヤー範囲だけを見て新規確保する。
            layers[0] = "VfxUI";

            var index = VfxUiLayerAllocator.FindOrClaim(layers, "VfxUI");

            Assert.AreEqual(8, index);
        }
    }
}
