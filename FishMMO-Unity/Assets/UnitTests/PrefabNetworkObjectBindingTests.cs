using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs that a networked component points at the NetworkObject in its own prefab.
	/// </summary>
	/// <remarks>
	/// <para>
	/// FishNet caches the owning NetworkObject on each NetworkBehaviour as a serialized reference.
	/// Within a prefab that reference is local — a bare fileID. Duplicating a prefab can leave it
	/// pointing at the ORIGINAL asset instead, as a fileID plus that asset's guid, and Unity reports
	/// nothing: the field is populated and the type is right, it just names another prefab's object.
	/// </para>
	/// <para>
	/// Found through "I cannot damage the orc warrior". Its health bar never moved, and the server
	/// log showed why — every swing at it resolved to <c>target Elf(Clone)</c>, the caster, because
	/// the ability found no target and <c>TargetSelector</c> falls back to the initiator. The
	/// warrior's CharacterAttributeController, which owns Health, was bound to the orc mage's
	/// NetworkObject. Three prefabs had been duplicated from that mage and carried the same fault:
	/// the warrior and a lesser fire elemental on both fields, and a plain orc on one — and the orc
	/// stayed damageable with one of the two intact, which is what made this look like a warrior
	/// problem rather than a family one.
	/// </para>
	/// <para>
	/// Cheap to assert and impossible to see by eye in the inspector, so it is asserted here.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class PrefabNetworkObjectBindingTests
	{
		/// <summary>Fields FishNet uses to cache the owning NetworkObject.</summary>
		private static readonly string[] OwnerFields =
		{
			"_addedNetworkObject",
			"_networkObjectCache",
		};

		private static string PrefabRoot =>
			Path.Combine(Directory.GetCurrentDirectory(), "Assets/Prefabs");

		[Test]
		public void NoPrefabBindsItsComponentsToAnotherPrefabsNetworkObject()
		{
			LogAssert.IsTrue(Directory.Exists(PrefabRoot), $"prefabs must live at {PrefabRoot}");

			string[] prefabs = Directory.GetFiles(PrefabRoot, "*.prefab", SearchOption.AllDirectories);
			LogAssert.IsTrue(prefabs.Length > 0, "there must be prefabs to check");

			/* A guid on one of these fields is the whole tell. A reference inside the prefab is a
			 * bare fileID; the guid only appears when it reaches out to a different asset. */
			Regex foreign = new Regex(
				"_(?:" + string.Join("|", OwnerFields) + "): \\{fileID: -?\\d+, guid: ([0-9a-f]{32})");

			List<string> offenders = new List<string>();

			foreach (string prefab in prefabs)
			{
				foreach (Match match in foreign.Matches(File.ReadAllText(prefab)))
				{
					offenders.Add($"{Path.GetFileName(prefab)} -> guid {match.Groups[1].Value}");
				}
			}

			LogAssert.IsTrue(offenders.Count == 0,
				"a NetworkBehaviour must be bound to the NetworkObject in its own prefab; " +
				"these point at another asset, which silently breaks targeting and health on the " +
				"affected entity: " + string.Join("; ", offenders));
		}
	}
}
