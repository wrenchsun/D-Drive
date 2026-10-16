using System.Collections.Generic;
using DDrive.Foundation.Manager;
using DDrive.Runtime.Extension;

namespace ExternalPackage.Fake
{
    // 契約 E-25(IDDriveModule)のダミー module 群。ModuleRegistry は全部を発見する(テストでは IncludeTestAssemblies = true)。
    // 順序: First(-5) → SameA(0) → SameB(0、型名順) → Faulty(3) → Late(5)。
    // 静的ファサードの代わりに FacadeTarget(Bind で結び、Unbind で外す。実際の Samples.Bind(null) と同じく無条件に外す)を持つ。
    public static class ExternalModuleProbe
    {
        public static readonly List<string> Log = new();
        public static readonly List<ExternalCountingManager> Managers = new();
        public static bool FaultyThrowsInCreate;
        public static bool FaultyReturnsNullElement;
        public static bool FaultyThrowsInBind;
        public static bool FaultyThrowsInUnbind;
        public static bool FaultyThrowsInOrder;
        public static DDriveBootstrapContext LastContext;
        public static bool HasContext;

        // 静的ファサードの結び先(Bind された最後の module の Manager)。null = 未 Bind。
        public static ExternalCountingManager FacadeTarget;
        // ファサードの Play の代わり: 結び先の Manager に 1 回 Tick を流す(未 Bind なら false で何もしない)。
        public static bool FacadePlay(float dt = 0.01f)
        {
            if (FacadeTarget == null)
            {
                return false;
            }

            FacadeTarget.Tick(dt);
            return true;
        }

        public static void Reset()
        {
            Log.Clear();
            Managers.Clear();
            FaultyThrowsInCreate = false;
            FaultyReturnsNullElement = false;
            FaultyThrowsInBind = false;
            FaultyThrowsInUnbind = false;
            FaultyThrowsInOrder = false;
            LastContext = default;
            HasContext = false;
            FacadeTarget = null;
        }

        public abstract class ModuleBase : IDDriveModule
        {
            private ExternalCountingManager _manager;

            public abstract int Order { get; }

            public virtual IReadOnlyList<IAssetManager> CreateManagers(in DDriveBootstrapContext ctx)
            {
                Log.Add("Create:" + GetType().Name);
                LastContext = ctx;
                HasContext = true;
                _manager = new ExternalCountingManager();
                Managers.Add(_manager);
                return new IAssetManager[] { _manager };
            }

            public virtual void Bind()
            {
                Log.Add("Bind:" + GetType().Name);
                FacadeTarget = _manager;
            }

            public virtual void Unbind()
            {
                Log.Add("Unbind:" + GetType().Name);
                FacadeTarget = null;
            }
        }

        public sealed class First : ModuleBase { public override int Order => -5; }
        public sealed class SameA : ModuleBase { public override int Order => 0; }
        public sealed class SameB : ModuleBase { public override int Order => 0; }
        public sealed class Late : ModuleBase { public override int Order => 5; }

        public sealed class Faulty : ModuleBase
        {
            public override int Order => FaultyThrowsInOrder ? throw new System.InvalidOperationException("order boom") : 3;

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

            public override void Bind()
            {
                if (FaultyThrowsInBind)
                {
                    throw new System.InvalidOperationException("bind boom");
                }

                base.Bind();
            }

            public override void Unbind()
            {
                if (FaultyThrowsInUnbind)
                {
                    throw new System.InvalidOperationException("unbind boom");
                }

                base.Unbind();
            }
        }
    }
}
