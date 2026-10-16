using System.Collections.Generic;
using DDrive.Foundation.Manager;
using DDrive.Runtime.Extension;

namespace ExternalPackage.Fake
{
    // 契約 E-25(IDDriveModule)のダミー module 群。ModuleRegistry は全部を発見する(テストでは IncludeTestAssemblies = true)。
    // 順序: First(-5) → SameA(0) → SameB(0、型名順) → Faulty(3) → Late(5)。
    public static class ExternalModuleProbe
    {
        public static readonly List<string> Log = new();
        public static readonly List<ExternalCountingManager> Managers = new();
        public static bool FaultyThrowsInCreate;
        public static bool FaultyReturnsNullElement;
        public static DDriveBootstrapContext LastContext;
        public static bool HasContext;

        public static void Reset()
        {
            Log.Clear();
            Managers.Clear();
            FaultyThrowsInCreate = false;
            FaultyReturnsNullElement = false;
            LastContext = default;
            HasContext = false;
        }

        public abstract class ModuleBase : IDDriveModule
        {
            public abstract int Order { get; }

            public virtual IReadOnlyList<IAssetManager> CreateManagers(in DDriveBootstrapContext ctx)
            {
                Log.Add("Create:" + GetType().Name);
                LastContext = ctx;
                HasContext = true;
                var m = new ExternalCountingManager();
                Managers.Add(m);
                return new IAssetManager[] { m };
            }

            public void Bind() => Log.Add("Bind:" + GetType().Name);
            public void Unbind() => Log.Add("Unbind:" + GetType().Name);
        }

        public sealed class First : ModuleBase { public override int Order => -5; }
        public sealed class SameA : ModuleBase { public override int Order => 0; }
        public sealed class SameB : ModuleBase { public override int Order => 0; }
        public sealed class Late : ModuleBase { public override int Order => 5; }

        public sealed class Faulty : ModuleBase
        {
            public override int Order => 3;

            public override IReadOnlyList<IAssetManager> CreateManagers(in DDriveBootstrapContext ctx)
            {
                if (FaultyThrowsInCreate)
                {
                    throw new System.InvalidOperationException("boom");
                }

                var list = new List<IAssetManager>(base.CreateManagers(in ctx));
                if (FaultyReturnsNullElement)
                {
                    list.Insert(0, null);
                }

                return list;
            }
        }
    }
}
