using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs for issue #232: NPCs aggroed, pathed, surrounded the player and never dealt damage.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The AI was right all along. It picked Punch, closed, held position and called
	/// <c>Activate</c>; the server drained the queue into the NPC's replicate; the cast ran to
	/// completion, started its cooldown and armed the AI's pacing timer. Then
	/// <c>AbilityController.ResolveTargetAndSpawn</c> looked for the <c>ITargetController</c> it
	/// resolves every cast through, found none, and spawned nothing. The shipped NPC prefabs
	/// carried an <c>AbilityController</c> and no <c>TargetController</c>; the combat simulation
	/// never noticed because it added one itself.
	/// </para>
	/// <para>
	/// Two rules are pinned here. Every NPC prefab that can cast carries a target controller with
	/// a mask that can hit something, checked against the prefab YAML so a prefab saved without
	/// the component fails here rather than in a play session. And the attacking state arms its
	/// pacing timer only when <c>Activate</c> reports that it queued something, so the next
	/// activation failure presents as a retry on the following brain tick rather than as an NPC
	/// standing beside its target waiting out a cooldown for an attack it never made.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class NpcTargetControllerTests
	{
		private static string NpcRoot =>
			Path.Combine(Directory.GetCurrentDirectory(), "Assets/Prefabs/Shared/Entity/NPCs");

		private static string Scripts =>
			Path.Combine(Directory.GetCurrentDirectory(), "Assets/Scripts/Shared");

		/// <summary>The text of a source file, with its line endings normalised to \n.</summary>
		/// <remarks>
		/// Several proofs below match a pattern that spans a line break. Whether the working tree
		/// stores a file LF or CRLF is decided by git on checkout and by each developer's
		/// core.autocrlf, so a bound written with \n silently stops matching on a Windows
		/// checkout — the assertion then reports the code as missing when it is present and
		/// unchanged. Reading through here makes the fixture depend on the source rather than on
		/// how it was checked out.
		/// </remarks>
		private static string ReadSource(string path)
		{
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}


		/// <summary>
		/// Every NPC prefab whose root carries an AbilityController also carries a TargetController
		/// whose layer mask is not empty.
		/// </summary>
		[Test]
		public void EveryCastingNpcPrefabHasATargetController()
		{
			LogAssert.IsTrue(Directory.Exists(NpcRoot), $"NPC prefabs must live at {NpcRoot}");

			string abilityControllerGuid = ScriptGuid("Implementation/Entity/Prediction/Ability/AbilityController.cs");
			string targetControllerGuid = ScriptGuid("Implementation/Entity/Target/TargetController.cs");
			string damageControllerGuid = ScriptGuid("Implementation/Entity/Prediction/CharacterAttribute/CharacterDamageController.cs");

			string[] prefabs = Directory.GetFiles(NpcRoot, "*.prefab", SearchOption.AllDirectories);
			LogAssert.IsTrue(prefabs.Length > 0, "there must be NPC prefabs to check");

			List<string> offenders = new List<string>();
			int checkedCount = 0;

			foreach (string prefab in prefabs)
			{
				string source = File.ReadAllText(prefab);
				if (!source.Contains(abilityControllerGuid))
				{
					continue;
				}

				string name = Path.GetFileNameWithoutExtension(prefab);

				/* An NPC authored immortal has no reason to target anything, and the controller
				 * short-circuits for it at runtime whatever the prefab says. */
				Match damage = Regex.Match(source,
					"m_Script: \\{fileID: 11500000, guid: " + damageControllerGuid + ", type: 3\\}[\\s\\S]*?(?=\\n--- !u!)");
				if (damage.Success && Regex.IsMatch(damage.Value, "\\n  immortal: 1"))
				{
					continue;
				}

				checkedCount++;

				/* The component block: its script line, then its serialized fields until the next
				 * document separator. The LayerMask serialises as "m_Bits: N" inside it. */
				Match block = Regex.Match(source,
					"m_Script: \\{fileID: 11500000, guid: " + targetControllerGuid + ", type: 3\\}[\\s\\S]*?(?=\\n--- !u!)");
				if (!block.Success)
				{
					offenders.Add($"{name}: no TargetController");
					continue;
				}

				Match bits = Regex.Match(block.Value, "LayerMask:\\s*\\n\\s*serializedVersion: \\d+\\s*\\n\\s*m_Bits: (\\d+)");
				if (!bits.Success || bits.Groups[1].Value == "0")
				{
					offenders.Add($"{name}: TargetController LayerMask is empty");
				}
			}

			LogAssert.IsTrue(checkedCount > 0,
				"no NPC prefab carries an AbilityController — the script guid lookup has stopped working");

			LogAssert.IsTrue(offenders.Count == 0,
				"an NPC that casts without a TargetController completes every cast and spawns " +
				"nothing (issue #232). Run FishMMO > AI > Repair NPC Prefabs For Combat: " +
				string.Join(", ", offenders));
		}

		/// <summary>
		/// The NPC class itself requires the component, so a prefab created from scratch cannot
		/// be saved without one.
		/// </summary>
		[Test]
		public void NpcRequiresATargetController()
		{
			string npc = ReadSource(Path.Combine(Scripts, "Implementation/Entity/NPC/NPC.cs"));
			LogAssert.IsTrue(npc.Contains("[RequireComponent(typeof(TargetController))]"),
				"NPC must RequireComponent a TargetController; without it every NPC cast spawns nothing");
		}

		/// <summary>
		/// An immortal NPC's acquisition trace short-circuits: it has no reason to target anything.
		/// </summary>
		[Test]
		public void ImmortalNpcTargetingShortCircuits()
		{
			string controller = ReadSource(Path.Combine(Scripts, "Implementation/Entity/Target/TargetController.cs"));

			int shortCircuit = controller.IndexOf("if (IsImmortalNpc)", System.StringComparison.Ordinal);
			int resolveScene = controller.IndexOf("PhysicsScene physicsScene = ResolvePhysicsScene();", System.StringComparison.Ordinal);

			LogAssert.IsTrue(shortCircuit >= 0, "TargetController.UpdateTarget must short-circuit for an immortal NPC");
			LogAssert.IsTrue(resolveScene > shortCircuit,
				"the short-circuit must come before the physics scene is resolved and traced");
			LogAssert.IsTrue(controller.Contains("if (PlayerCharacter != null)\n\t\t\t\t{\n\t\t\t\t\treturn false;"),
				"the short-circuit is for NPCs only — a teleporting player is briefly immortal and keeps its hover targeting");
		}

		/// <summary>
		/// The attacking state arms its pacing timer only on an activation that was queued.
		/// </summary>
		[Test]
		public void AttackCooldownIsArmedOnlyWhenActivateQueued()
		{
			string state = ReadSource(Path.Combine(Scripts,
				"Implementation/Entity/NPC/AI/States/BaseAttackingState.cs"));

			int activate = state.IndexOf("if (!abilityController.Activate(ability.ID, held))", System.StringComparison.Ordinal);
			int arm = state.IndexOf("controller.AttackCooldownTimer = AttackCooldown + jitter;", System.StringComparison.Ordinal);

			LogAssert.IsTrue(activate >= 0,
				"BaseAttackingState.ActivateAbility must branch on Activate's result");
			LogAssert.IsTrue(arm > activate,
				"the pacing timer must be armed after, and only after, Activate reported success");

			string contract = ReadSource(Path.Combine(Scripts,
				"Core/Entity/Prediction/Ability/IAbilityController.cs"));
			LogAssert.IsTrue(contract.Contains("bool Activate(long referenceID, bool isHeld);"),
				"IAbilityController.Activate must report whether it queued anything");
		}

		/// <summary>Reads the guid of a script under Assets/Scripts/Shared from its .meta.</summary>
		private static string ScriptGuid(string relativePath)
		{
			string meta = Path.Combine(Scripts, relativePath) + ".meta";
			LogAssert.IsTrue(File.Exists(meta), $"expected {meta}");
			Match guid = Regex.Match(File.ReadAllText(meta), "guid: ([0-9a-f]{32})");
			LogAssert.IsTrue(guid.Success, $"no guid in {meta}");
			return guid.Groups[1].Value;
		}
	}
}
