#if DDRIVE_NGO
using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Net;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Net;
using DDrive.Runtime.Prefab;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

namespace DDrive.Tests.Runtime
{
    // [14_networking.md] §10(4-13) / [42_distribution.md] §2.3-9/§7 A-7(P1-1、2026-09-20) —
    // NetworkObject の検査は DDrive.Runtime から DDrive.Runtime.Ngo アセンブリの
    // `PrefabNetworkObjectValidator` へ切り出した(PrefabDataValidator 単体側の回帰確認は
    // Tests/Runtime/PrefabSimulatedSpawnTests.cs 参照)。
    public class PrefabNetworkObjectValidatorTests
    {
        private GameObject _prefab;

        [TearDown]
        public void TearDown()
        {
            if (_prefab != null)
            {
                Object.DestroyImmediate(_prefab);
            }
        }

        [Test]
        public void Simulated_WithoutNetworkObject_IsError()
        {
            _prefab = new GameObject("NoNetworkObject");
            var data = ScriptableObject.CreateInstance<PrefabData>();
            data.Prefab = _prefab;
            data.Flags.Net = NetMode.Simulated;

            var results = new List<ValidationResult>(
                new PrefabNetworkObjectValidator().Validate(data, new ValidationContext(new List<AssetDataBase> { data })));

            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("NetworkObject")));
        }

        [Test]
        public void Simulated_WithNetworkObject_NoError()
        {
            _prefab = new GameObject("WithNetworkObject");
            _prefab.AddComponent<NetworkObject>();
            var data = ScriptableObject.CreateInstance<PrefabData>();
            data.Prefab = _prefab;
            data.Flags.Net = NetMode.Simulated;

            var results = new List<ValidationResult>(
                new PrefabNetworkObjectValidator().Validate(data, new ValidationContext(new List<AssetDataBase> { data })));

            Assert.IsFalse(results.Exists(r => r.Message.Contains("NetworkObject")));
        }

        [Test]
        public void NonSimulated_NeverReports()
        {
            _prefab = new GameObject("NonSimulated");
            var data = ScriptableObject.CreateInstance<PrefabData>();
            data.Prefab = _prefab;
            data.Flags.Net = NetMode.Local;

            var results = new List<ValidationResult>(
                new PrefabNetworkObjectValidator().Validate(data, new ValidationContext(new List<AssetDataBase> { data })));

            Assert.AreEqual(0, results.Count);
        }
    }
}
#endif // DDRIVE_NGO
