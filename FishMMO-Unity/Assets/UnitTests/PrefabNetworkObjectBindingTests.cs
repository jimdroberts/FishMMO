using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs that a networked component points at the NetworkObject in its own asset.
	/// </summary>
	/// <remarks>
	/// <para>
	/// FishNet caches the owning NetworkObject on each NetworkBehaviour as a serialized reference.
	/// Within a prefab or scene that reference is local — a bare fileID. Anything that copies
	/// serialized fields between prefabs (Paste Component Values, CopySerialized, a
	/// SerializedObject migration) can leave it pointing at ANOTHER asset instead, as a fileID plus
	/// that asset's guid, and Unity reports nothing: the field is populated and the type is right,
	/// it just names another asset's object.
	/// </para>
	/// <para>
	/// Found through "I cannot damage the orc warrior". Its health bar never moved, and the server
	/// log showed why — every swing at it resolved to <c>target Elf(Clone)</c>, the caster, because
	/// the ability found no target and <c>TargetSelector</c> falls back to the initiator. The
	/// warrior's CharacterAttributeController, which owns Health, was bound to the orc mage's
	/// NetworkObject. The template-to-ID migration of 2026-03-27 had rewritten the field on three
	/// prefabs at once: the warrior and a lesser fire elemental on both fields, and a plain orc on
	/// one — and the orc stayed damageable with one of the two intact, which is what made this look
	/// like a warrior problem rather than a family one (PR #212).
	/// </para>
	/// <para>
	/// The scan lives in <see cref="NetworkObjectBindingValidator"/> so the import hook, the build
	/// hook, the repair menu and this test agree on what "foreign" means. The second test pins the
	/// scanner itself against the exact bytes that broke the warrior, so a regex regression cannot
	/// turn the first test into a silent pass.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class PrefabNetworkObjectBindingTests
	{
		private static string AssetRoot =>
			Path.Combine(Directory.GetCurrentDirectory(), "Assets");

		/// <summary>The warrior's CharacterAttributeController as committed on 2026-03-27.</summary>
		private const string BrokenWarrior =
			"  _componentIndexCache: 1\n" +
			"  _addedNetworkObject: {fileID: -4468559157413045786, guid: a7364fafb6a45174babebcc6a5fbdce4,\n" +
			"    type: 3}\n" +
			"  _networkObjectCache: {fileID: -4468559157413045786, guid: a7364fafb6a45174babebcc6a5fbdce4,\n" +
			"    type: 3}\n" +
			"  CharacterAttributeDatabase: {fileID: 11400000, guid: 8fe93c66f5bc53c45a7b0f8334be9016,\n" +
			"    type: 2}\n";

		/// <summary>The same block after PR #212.</summary>
		private const string FixedWarrior =
			"  _componentIndexCache: 1\n" +
			"  _addedNetworkObject: {fileID: -4468559157413045786}\n" +
			"  _networkObjectCache: {fileID: -4468559157413045786}\n" +
			"  CharacterAttributeDatabase: {fileID: 11400000, guid: 8fe93c66f5bc53c45a7b0f8334be9016,\n" +
			"    type: 2}\n";

		[Test]
		public void NoNetworkedComponentBindsToAnotherAssetsNetworkObject()
		{
			LogAssert.IsTrue(Directory.Exists(AssetRoot), $"assets must live at {AssetRoot}");

			int prefabs = Directory.GetFiles(AssetRoot, "*.prefab", SearchOption.AllDirectories).Length;
			int scenes = Directory.GetFiles(AssetRoot, "*.unity", SearchOption.AllDirectories).Length;
			LogAssert.IsTrue(prefabs > 0 && scenes > 0, "there must be prefabs and scenes to check");

			List<NetworkObjectBindingValidator.Finding> findings = NetworkObjectBindingValidator.ScanAll(AssetRoot);

			LogAssert.IsTrue(findings.Count == 0, NetworkObjectBindingValidator.Describe(findings));
		}

		[Test]
		public void ScannerRecognisesTheShapeThatBrokeTheWarrior()
		{
			List<NetworkObjectBindingValidator.Finding> findings =
				NetworkObjectBindingValidator.ScanText(BrokenWarrior, "an orc warrior.prefab");

			LogAssert.AreEqual(2, findings.Count, "both owner fields name the mage");
			LogAssert.AreEqual("_addedNetworkObject", findings[0].Field);
			LogAssert.AreEqual(2, findings[0].Line, "line of the first foreign field");
			LogAssert.AreEqual("_networkObjectCache", findings[1].Field);
			LogAssert.AreEqual(4, findings[1].Line, "line of the second foreign field");
			LogAssert.AreEqual("a7364fafb6a45174babebcc6a5fbdce4", findings[0].TargetGuid, "the mage's guid");

			// The template reference two lines later also carries a guid, and must not be reported.
			LogAssert.AreEqual(0, NetworkObjectBindingValidator.ScanText(FixedWarrior, "fixed").Count,
				"a bare fileID is the healthy shape");
		}

		[Test]
		public void RepairProducesExactlyThePr212Edit()
		{
			string repaired = NetworkObjectBindingValidator.RepairText(BrokenWarrior, out int count);

			LogAssert.AreEqual(2, count, "one rewrite per foreign field");
			LogAssert.AreEqual(FixedWarrior, repaired, "guid and type dropped, fileID kept, neighbours untouched");
			LogAssert.AreEqual(0, NetworkObjectBindingValidator.ScanText(repaired, "repaired").Count,
				"repair output is clean");

			NetworkObjectBindingValidator.RepairText(FixedWarrior, out int noop);
			LogAssert.AreEqual(0, noop, "a healthy asset is left alone");
		}
	}
}
