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
		private const string BaselineFile = "Assets/UnitTests/AssetReferenceAuditBaseline.txt";
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

			List<string> added = current.Where(line => !baseline.Contains(line)).ToList();
			List<string> fixedLines = baseline.Where(line => !current.Contains(line)).ToList();

			Assert.That(added, Is.Empty,
				"New broken references (a file moved without its .meta, an asset deleted while in use, or a serialized type moved without [MovedFrom]):\n  " +
				string.Join("\n  ", added));
			Assert.That(fixedLines, Is.Empty,
				$"These baseline entries are fixed; remove them from {BaselineFile}:\n  " + string.Join("\n  ", fixedLines));
		}

		[Test]
		public void ExclusionsMatchFoldersTheirFilesAndTheirSiblingsOfTheSameName()
		{
			Assert.That(AssetReferenceAudit.IsExcluded("Assets/LOCAL"), Is.True);
			Assert.That(AssetReferenceAudit.IsExcluded("Assets/LOCAL/x.asset"), Is.True);
			Assert.That(AssetReferenceAudit.IsExcluded("Assets/Templates/World/Atlas.asset"), Is.True);
			Assert.That(AssetReferenceAudit.IsExcluded("Assets/Generated/WorldMaps/A.asset"), Is.True);
			Assert.That(AssetReferenceAudit.IsExcluded("Assets/GeneratedContent/A.asset"), Is.False, "a different folder that only shares a prefix is scanned");
			Assert.That(AssetReferenceAudit.IsExcluded("Assets/Templates/World/Atlases/A.asset"), Is.False);
		}
	}
}
