using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Guards the on-disk authoring of the region trigger assets under
	/// <c>Assets/Prefabs/Shared/Entity/Regions</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// These eight assets are <see cref="FishMMO.Shared.Core.Trigger"/> ScriptableObjects that the
	/// tutorial scenes reference from <c>Region.OnRegionEnter</c>. A previous commit rewrote every
	/// one of them with <c>Region.cs</c>'s script GUID — Region is a MonoBehaviour, not a
	/// ScriptableObject, so Unity deserialized them as a Region component living in an asset file
	/// and silently discarded the fog / display-name values they carried. The scenes kept
	/// resolving (the asset GUIDs were untouched) so nothing errored; the regions simply stopped
	/// having any effect.
	/// </para>
	/// <para>
	/// The GUIDs are read from the <c>.meta</c> files rather than hardcoded, so a re-import that
	/// legitimately reassigns a script GUID moves this guard with it instead of turning it into a
	/// false failure. The assets are read as text rather than through the asset database so the
	/// check sees exactly what is committed.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class RegionAssetIntegrityTests
	{
		/// <summary>Matches the <c>m_Script</c> line and captures the referenced script GUID.</summary>
		private static readonly Regex ScriptGuid = new Regex(@"^\s*m_Script:\s*{fileID:\s*\d+,\s*guid:\s*([0-9a-f]{32})", RegexOptions.Compiled);

		/// <summary>Matches the <c>guid:</c> line of a <c>.meta</c> file.</summary>
		private static readonly Regex MetaGuid = new Regex(@"^guid:\s*([0-9a-f]{32})\s*$", RegexOptions.Compiled);

		/// <summary>
		/// Serialized fields that only a <c>Region</c> MonoBehaviour has. Their presence in an
		/// asset body is proof the asset was written against the wrong script.
		/// </summary>
		private static readonly string[] RegionOnlyFields =
		{
			"OnRegionEnter:",
			"OnRegionStay:",
			"OnRegionExit:",
			"GizmoColor:",
			"_networkObjectCache:",
			"_addedNetworkObject:",
			"_componentIndexCache:",
		};

		private static string ProjectRoot => Directory.GetCurrentDirectory();

		private static string RegionAssetRoot => Path.Combine(ProjectRoot, "Assets", "Prefabs", "Shared", "Entity", "Regions");

		private static string TriggerScriptMeta => Path.Combine(
			ProjectRoot, "Assets", "Scripts", "Shared", "Core", "Entity", "ECA", "Core", "Trigger.cs.meta");

		private static string RegionScriptMeta => Path.Combine(
			ProjectRoot, "Assets", "Scripts", "Shared", "Implementation", "Entity", "Prediction", "Region", "Region.cs.meta");

		/// <summary>
		/// Reads the <c>guid:</c> value out of a Unity <c>.meta</c> file.
		/// </summary>
		private static string ReadMetaGuid(string metaPath)
		{
			LogAssert.IsTrue(File.Exists(metaPath), $"Meta file not found at {metaPath}.");
			foreach (string line in File.ReadLines(metaPath))
			{
				Match m = MetaGuid.Match(line);
				if (m.Success)
				{
					return m.Groups[1].Value;
				}
			}
			LogAssert.Fail($"No guid line found in {metaPath}.");
			return null;
		}

		private static IEnumerable<string> RegionAssetFiles()
		{
			LogAssert.IsTrue(Directory.Exists(RegionAssetRoot), $"Region asset root not found at {RegionAssetRoot}.");
			return Directory.EnumerateFiles(RegionAssetRoot, "*.asset", SearchOption.AllDirectories);
		}

		/// <summary>
		/// Every ScriptableObject asset under the Regions folder must point at Trigger.cs.
		/// </summary>
		[Test]
		public void EveryRegionAsset_ReferencesTheTriggerScript()
		{
			string triggerGuid = ReadMetaGuid(TriggerScriptMeta);
			string regionGuid = ReadMetaGuid(RegionScriptMeta);
			LogAssert.AreNotEqual(triggerGuid, regionGuid, "Trigger.cs and Region.cs resolved to the same GUID; the meta lookup is wrong.");

			int scanned = 0;
			List<string> offenders = new List<string>();

			foreach (string file in RegionAssetFiles())
			{
				string found = null;
				foreach (string line in File.ReadLines(file))
				{
					Match m = ScriptGuid.Match(line);
					if (!m.Success)
					{
						continue;
					}
					found = m.Groups[1].Value;
					break;
				}

				++scanned;
				if (found == null)
				{
					offenders.Add($"{Path.GetRelativePath(RegionAssetRoot, file)} (no m_Script line)");
					continue;
				}
				if (found != triggerGuid)
				{
					string what = found == regionGuid ? "Region.cs (a MonoBehaviour)" : found;
					offenders.Add($"{Path.GetRelativePath(RegionAssetRoot, file)} -> {what}");
				}
			}

			TestContext.WriteLine($"MEASURE region trigger assets scanned: {scanned}");
			LogAssert.IsTrue(scanned > 0, $"No .asset files found under {RegionAssetRoot}; this guard is checking nothing.");
			LogAssert.AreEqual(0, offenders.Count,
				"These region assets do not reference Trigger.cs, so Unity cannot deserialize them as triggers and " +
				"the regions that list them silently do nothing: " + string.Join(", ", offenders));
		}

		/// <summary>
		/// A correctly authored trigger asset can never contain Region MonoBehaviour fields.
		/// This catches the corruption even if the script GUID were somehow repaired in isolation.
		/// </summary>
		[Test]
		public void NoRegionAsset_ContainsRegionMonoBehaviourFields()
		{
			List<string> offenders = new List<string>();

			foreach (string file in RegionAssetFiles())
			{
				string text = File.ReadAllText(file);
				foreach (string field in RegionOnlyFields)
				{
					if (text.Contains(field))
					{
						offenders.Add($"{Path.GetRelativePath(RegionAssetRoot, file)} contains '{field}'");
					}
				}
			}

			LogAssert.AreEqual(0, offenders.Count,
				"These region assets carry serialized Region MonoBehaviour fields, which means they were written " +
				"against Region.cs instead of Trigger.cs and their authored values were lost: " + string.Join(", ", offenders));
		}

		/// <summary>
		/// A trigger asset with no actions has no observable effect. Every recovered asset must
		/// carry at least one action reference, which is what proves the fog / name values survived.
		/// </summary>
		[Test]
		public void EveryRegionAsset_DeclaresAtLeastOneAction()
		{
			List<string> offenders = new List<string>();

			foreach (string file in RegionAssetFiles())
			{
				string text = File.ReadAllText(file);
				bool hasMet = Regex.IsMatch(text, @"OnConditionsMetActions:\s*\r?\n\s*-\s*rid:");
				bool hasNotMet = Regex.IsMatch(text, @"OnConditionsNotMetActions:\s*\r?\n\s*-\s*rid:");
				if (!hasMet && !hasNotMet)
				{
					offenders.Add(Path.GetRelativePath(RegionAssetRoot, file));
				}
			}

			LogAssert.AreEqual(0, offenders.Count,
				"These region trigger assets declare no actions at all, so firing them does nothing: " +
				string.Join(", ", offenders));
		}
	}
}
