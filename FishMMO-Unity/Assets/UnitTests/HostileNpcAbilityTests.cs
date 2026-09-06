using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs that an NPC meant to fight knows at least one ability.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Reported as "I received no damage from any of the 3 mobs". Every combat trigger in the server
	/// log had the player as its initiator and not one had a mob, and the reason was simply that no
	/// NPC in the project knew any ability at all — all seven shipped with an empty list.
	/// </para>
	/// <para>
	/// The first suspect was the AI archetypes, every one of which has a null
	/// <c>AbilityRotation</c>. That is a red herring: a rotation is an OVERRIDE, and
	/// <c>AIController.PickBestAbility</c> falls through to its own scoring-based picker when there
	/// is none. The picker chooses from the NPC's known abilities, so with an empty list it returns
	/// null however the archetype is configured, and an NPC stands there.
	/// </para>
	/// <para>
	/// A vendor is exempt: a banker with no attack is correct. The test keys on whether the NPC has
	/// an AI archetype that makes it hostile, so a new merchant does not have to be added to a list
	/// here, while a new monster is covered the day it is created.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class HostileNpcAbilityTests
	{
		private static string NpcRoot =>
			Path.Combine(Directory.GetCurrentDirectory(), "Assets/Prefabs/Shared/Entity/NPCs");

		/// <summary>Names of AI archetypes whose NPCs are expected to fight.</summary>
		/// <remarks>
		/// Enemy and Pet both attack; only the vendor archetypes do not. Matched on the archetype
		/// asset's own name so this does not need a per-prefab exclusion list.
		/// </remarks>
		private static readonly string[] FightingArchetypePrefixes = { "Enemy - ", "Pet - " };

		[Test]
		public void EveryFightingNpcKnowsAnAbility()
		{
			LogAssert.IsTrue(Directory.Exists(NpcRoot), $"NPC prefabs must live at {NpcRoot}");

			Dictionary<string, string> archetypeNames = ArchetypeNamesByGuid();
			LogAssert.IsTrue(archetypeNames.Count > 0, "there must be AI archetypes to resolve");

			string[] prefabs = Directory.GetFiles(NpcRoot, "*.prefab", SearchOption.AllDirectories);
			LogAssert.IsTrue(prefabs.Length > 0, "there must be NPC prefabs to check");

			List<string> offenders = new List<string>();
			int checkedCount = 0;

			foreach (string prefab in prefabs)
			{
				string source = File.ReadAllText(prefab);

				/* The field was renamed to its camelCase backing field behind FormerlySerializedAs, so a
				 * prefab saved before the rename carries "Archetype:" and one saved after carries
				 * "archetype:". Both are the same slot. */
				Match archetype = Regex.Match(source, "[Aa]rchetype: \\{fileID: \\d+, guid: ([0-9a-f]{32})");
				if (!archetype.Success ||
					!archetypeNames.TryGetValue(archetype.Groups[1].Value, out string archetypeName) ||
					!Fights(archetypeName))
				{
					continue;
				}

				checkedCount++;

				/* An empty list serialises as "Abilities: []" and a populated one as "Abilities:"
				 * followed by entries, so the bracket is the whole test. */
				if (Regex.IsMatch(source, "\\n  Abilities: \\[\\]"))
				{
					offenders.Add($"{Path.GetFileNameWithoutExtension(prefab)} ({archetypeName})");
				}
			}

			LogAssert.IsTrue(checkedCount > 0,
				"no NPC resolved to a fighting archetype — the archetype lookup has stopped working");

			LogAssert.IsTrue(offenders.Count == 0,
				"an NPC with a fighting archetype and no abilities cannot attack anything, whatever " +
				"its AI is set to: " + string.Join(", ", offenders));
		}

		/// <summary>Maps each AI archetype asset's guid to its file name.</summary>
		private static Dictionary<string, string> ArchetypeNamesByGuid()
		{
			Dictionary<string, string> names = new Dictionary<string, string>();

			string templates = Path.Combine(Directory.GetCurrentDirectory(), "Assets/Templates");
			if (!Directory.Exists(templates))
			{
				return names;
			}

			foreach (string meta in Directory.GetFiles(templates, "*.asset.meta", SearchOption.AllDirectories))
			{
				string name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(meta));
				if (!Fights(name))
				{
					continue;
				}

				Match guid = Regex.Match(File.ReadAllText(meta), "guid: ([0-9a-f]{32})");
				if (guid.Success)
				{
					names[guid.Groups[1].Value] = name;
				}
			}

			return names;
		}

		private static bool Fights(string archetypeName)
		{
			foreach (string prefix in FightingArchetypePrefixes)
			{
				if (archetypeName.StartsWith(prefix, StringComparison.Ordinal))
				{
					return true;
				}
			}

			return false;
		}
	}
}
