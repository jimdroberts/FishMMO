#if UNITY_EDITOR
using System;
using System.IO;
using System.Xml;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.UnityLinker;

namespace FishMMO.Shared.CustomBuildTool.Linker
{
    /// <summary>
    /// EF and Npgsql activate services through reflection. Preserve their runtime
    /// implementation assemblies for dedicated servers, including public constructors
    /// that UnityLinker cannot discover through the DI service registrations.
    /// </summary>
    public sealed class ServerDatabaseLinkerProcessor : IUnityLinkerProcessor
    {
        public int callbackOrder => 0;

        public static bool AppliesTo(BuildTarget target, int subtarget)
        {
            return BuildPipeline.GetBuildTargetGroup(target) == BuildTargetGroup.Standalone
                && subtarget == (int)StandaloneBuildSubtarget.Server;
        }

        public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            // GetSubtarget<T> is platform-specific; never query a standalone
            // subtarget from a WebGL/mobile build report.
            if (BuildPipeline.GetBuildTargetGroup(report.summary.platform) != BuildTargetGroup.Standalone) return null;
            if (!AppliesTo(report.summary.platform, (int)report.summary.GetSubtarget<StandaloneBuildSubtarget>())) return null;
            string path = Path.GetFullPath(Path.Combine("Library", "FishMMO", "Linker", "ServerDatabase.link.xml"));
            WriteDescriptor(path);
            return path;
        }

        public static void WriteDescriptor(string path)
        {
            // Keep this outside Assets: global link.xml files also root assemblies in
            // clients. This callback is invoked for standard and dashboard server builds.
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            using (var writer = XmlWriter.Create(path, new XmlWriterSettings { Indent = true }))
            {
                writer.WriteStartElement("linker");
                foreach (string assembly in RuntimeAssemblies)
                {
                    writer.WriteStartElement("assembly");
                    writer.WriteAttributeString("fullname", assembly);
                    writer.WriteAttributeString("preserve", "all");
                    writer.WriteAttributeString("ignoreIfMissing", "1");
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
            }
        }

        private static readonly string[] RuntimeAssemblies =
        {
            "FishMMO-DB",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Abstractions",
            "Microsoft.EntityFrameworkCore.Relational",
            "Npgsql.EntityFrameworkCore.PostgreSQL",
            "Npgsql",
            "EFCore.NamingConventions",
            "Microsoft.Extensions.DependencyInjection",
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            "Microsoft.Extensions.Caching.Abstractions",
            "Microsoft.Extensions.Caching.Memory",
            "Microsoft.Extensions.Logging",
            "Microsoft.Extensions.Logging.Abstractions",
            "Microsoft.Extensions.Options",
            "Microsoft.Extensions.Primitives",
            "Microsoft.Extensions.Configuration",
            "Microsoft.Extensions.Configuration.Abstractions",
            "Microsoft.Extensions.Configuration.Binder"
        };
    }
}
#endif
