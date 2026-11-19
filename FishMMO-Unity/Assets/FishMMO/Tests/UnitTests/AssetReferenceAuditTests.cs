using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishMMO.Shared;
using NUnit.Framework;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The project-wide reference audit (<see cref="AssetReferenceAudit"/>) as a ratchet: no new missing
	/// script, dangling asset reference or unresolved serialized type may appear, and a fixed one must leave
	/// the baseline.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The baseline (<c>AssetReferenceAuditBaseline.txt</c> beside this file) lists the findings the project
	/// carried when the audit was introduced. A finding not in it fails the test, which is how a file moved
	/// without its <c>.meta</c>, an asset deleted while still in use, or a serialized type moved without
	/// <c>[MovedFrom]</c> is caught. A baseline line that no longer occurs also fails, so the list only shrinks.
	/// </para>
	/// <para>
	/// Every run writes the current findings to <c>Temp/AssetReferenceAudit.txt</c>, ready to compare or, after a
	/// deliberate change, to copy over the baseline.
	/// </para>
	/// </remarks>
	public class AssetReferenceAuditTests
	{
		private const string BaselineFile = "Assets/FishMMO/Tests/UnitTests/AssetReferenceAuditBaseline.txt";
		private const string ReportFile = "Temp/AssetReferenceAudit.txt";

		[Test]
		public void NoReferenceBreaksBeyondTheBaseline()
		{
			AssetReferenceAudit.ResetTypeTables();
			AssetReferenceAudit.Report report = AssetReferenceAudit.Scan(ProjectFiles.ProjectRoot, AssetReferenceAudit.ResolveLoadedType);
			List<string> current = report.All().ToList();
			Directory.CreateDirectory("Temp");
			File.WriteAllLines(ReportFile, current);

			Assert.That(report.FilesScanned, Is.GreaterThan(1000), "the audit read almost nothing: is the project root right?");
			Assert.That(report.ManagedReferencesChecked, Is.GreaterThan(100), "the audit checked almost no managed references: has the YAML format changed?");

			string baselinePath = ProjectFiles.Path(BaselineFile);
			Assert.That(ProjectFiles.Exists(baselinePath), Is.True, $"no baseline at {BaselineFile}; the current findings are in {ReportFile}.");
			var baseline = new HashSet<string>(ProjectFiles.ReadAllLines(baselinePath)
				.Select(line => line.Trim())
				.Where(line => line.Length > 0 && !line.StartsWith("#")));

			List<string> added = current.Where(line => !baseline.Contains(line) && !IntoARemovedModule(line)).ToList();
			// A baseline entry inside a module that is not installed is not fixed, only absent (a module gate).
			List<string> fixedLines = baseline.Where(line => !current.Contains(line) && !InAnAbsentModule(line)).ToList();

			Assert.That(added, Is.Empty,
				$"(removed-module GUIDs read: {RemovedGuidCount()} from '{System.Environment.GetEnvironmentVariable(RemovedGuidsVariable)}' or '{Path.GetFullPath(RemovedGuidsFile)}', exists {File.Exists(RemovedGuidsFile)}) " +
				"New broken references (a file moved without its .meta, an asset deleted while in use, or a serialized type moved without [MovedFrom]):\n  " +
				string.Join("\n  ", added));
			Assert.That(fixedLines, Is.Empty,
				$"These baseline entries are fixed; remove them from {BaselineFile}:\n  " + string.Join("\n  ", fixedLines));
		}

		/// <summary>The environment variable a module gate sets: a file listing the removed modules' asset GUIDs, one a line.</summary>
		public const string RemovedGuidsVariable = "FISHMMO_GATE_REMOVED_GUIDS";

		/// <summary>Where a module gate also leaves that list, under the project (removed again when the gate restores the modules).</summary>
		public const string RemovedGuidsFile = "Library/FishMMOGate/RemovedGuids.txt";

		private static HashSet<string> removedGuids;

		/// <summary>
		/// Whether a finding is a world scene's reference into a module the gate removed (Jim's rule for the World Simulation
		/// gate, 2026-11-18): a scene is the modules' content, so a scene made with a module names its scripts and assets, and a project
		/// without that module cannot load it whole. Only scenes (anything under Assets/Scenes/), and only GUIDs of the removed
		/// modules' own files (a missing script or a dangling asset reference); every other broken reference still fails the audit.
		/// </summary>
		/// <summary>Whether a finding lies in a module folder that is not installed.</summary>
		internal static bool InAnAbsentModule(string line)
		{
			const string Modules = " in Assets/FishMMO/Modules/";
			int at = line.IndexOf(Modules, System.StringComparison.Ordinal);
			if (at < 0)
			{
				return false;
			}
			string rest = line.Substring(at + Modules.Length);
			int slash = rest.IndexOf('/');
			return slash > 0 && !InstalledModules.Has(rest.Substring(0, slash));
		}

		private static int RemovedGuidCount()
		{
			IntoARemovedModule(string.Empty);
			return removedGuids.Count;
		}

		internal static bool IntoARemovedModule(string line)
		{
			if (removedGuids == null)
			{
				removedGuids = new HashSet<string>();
				string path = System.Environment.GetEnvironmentVariable(RemovedGuidsVariable);
				if (string.IsNullOrEmpty(path) || !File.Exists(path))
				{
					path = RemovedGuidsFile;
				}
				if (File.Exists(path))
				{
					foreach (string guid in File.ReadAllLines(path))
					{
						if (guid.Trim().Length > 0)
						{
							removedGuids.Add(guid.Trim());
						}
					}
				}
			}
			if (removedGuids.Count == 0 || !(line.StartsWith("missing-script ") || line.StartsWith("dangling ")))
			{
				return false;
			}
			string[] parts = line.Split(new[] { ' ' }, 4);
			return parts.Length == 4 && parts[2] == "in" && parts[3].StartsWith("Assets/Scenes/") && removedGuids.Contains(parts[1]);
		}

		[Test]
		public void ExclusionsMatchFoldersTheirFilesAndTheirSiblingsOfTheSameName()
		{
			Assert.That(AssetReferenceAudit.IsExcluded("Assets/LOCAL"), Is.True);
			Assert.That(AssetReferenceAudit.IsExcluded("Assets/LOCAL/x.asset"), Is.True);
			Assert.That(AssetReferenceAudit.IsExcluded("Assets/FishMMO/Modules/WorldSimulation/Templates/World/Atlas.asset"), Is.True);
			Assert.That(AssetReferenceAudit.IsExcluded("Assets/Generated/WorldMaps/A.asset"), Is.True);
			Assert.That(AssetReferenceAudit.IsExcluded("Assets/GeneratedContent/A.asset"), Is.False, "a different folder that only shares a prefix is scanned");
			Assert.That(AssetReferenceAudit.IsExcluded("Assets/FishMMO/Modules/WorldSimulation/Templates/World/Atlases/A.asset"), Is.False);
		}
	}
}
