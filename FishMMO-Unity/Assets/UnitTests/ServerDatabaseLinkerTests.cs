#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FishMMO.Shared.CustomBuildTool.Linker;
using NUnit.Framework;
using UnityEditor;

namespace FishMMO.UnitTests
{
    public class ServerDatabaseLinkerTests
    {
        [TestCase(BuildTarget.StandaloneLinux64)]
        [TestCase(BuildTarget.StandaloneWindows64)]
        [TestCase(BuildTarget.StandaloneOSX)]
        public void DedicatedServerBuildsPreserveDatabaseRuntime(BuildTarget target)
        {
            Assert.That(ServerDatabaseLinkerProcessor.AppliesTo(target, (int)StandaloneBuildSubtarget.Server), Is.True);
            Assert.That(ServerDatabaseLinkerProcessor.AppliesTo(target, (int)StandaloneBuildSubtarget.Player), Is.False);
        }

        [Test]
        public void WebGlDoesNotPreserveServerDatabaseAssemblies()
        {
            Assert.That(ServerDatabaseLinkerProcessor.AppliesTo(BuildTarget.WebGL, (int)StandaloneBuildSubtarget.Server), Is.False);
        }

        [Test]
        public void DescriptorPreservesReflectionConstructorsAndProviderDependencyChain()
        {
            string root = Path.Combine(Path.GetTempPath(), "fishmmo-server-linker-" + Guid.NewGuid().ToString("N"));
            try
            {
                string path = Path.Combine(root, "ServerDatabase.link.xml");
                ServerDatabaseLinkerProcessor.WriteDescriptor(path);
                var assemblies = XDocument.Load(path).Root.Elements("assembly").ToDictionary(e => (string)e.Attribute("fullname"));
                foreach (string name in new[] { "Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore.Relational", "Npgsql.EntityFrameworkCore.PostgreSQL", "Microsoft.Extensions.DependencyInjection", "FishMMO-DB", "EFCore.NamingConventions" })
                    Assert.That((string)assemblies[name].Attribute("preserve"), Is.EqualTo("all"), name);
                Assert.That(assemblies.Keys.Any(n => n.Contains("Design")), Is.False, "Design-time tooling is not a runtime dependency.");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
    }
}
#endif
