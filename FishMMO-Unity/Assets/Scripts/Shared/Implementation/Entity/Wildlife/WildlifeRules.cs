using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// The rules wildlife lives by, outside the faction system. Every check that would otherwise ask a faction asks here
	/// first: the harm rule (<see cref="FactionHarm.MayHarm"/>) and the AI's search for something to fight or flee.
	/// </summary>
	public static class WildlifeRules
	{
		/// <summary>The character's wildlife role: <see cref="WildlifeRole.None"/> for anything that is not wildlife.</summary>
		public static WildlifeRole RoleOf(ICharacter character)
		{
			return character is NPC npc && npc.Wildlife != null ? npc.Wildlife.Role : WildlifeRole.None;
		}

		/// <summary>The character's pack (its spawner's group, server only); 0 for none.</summary>
		public static int PackOf(ICharacter character)
		{
			return character is NPC npc ? npc.PackId : 0;
		}

		/// <summary>True when either side is wildlife, so <see cref="MayHarm(WildlifeRole, int, WildlifeRole, int)"/> decides.</summary>
		public static bool Decides(WildlifeRole attacker, WildlifeRole defender)
		{
			return attacker != WildlifeRole.None || defender != WildlifeRole.None;
		}

		/// <summary>
		/// Whether harm between two characters, at least one of them wildlife, is allowed: always, except between members of
		/// one pack. Pure, so it is tested on its own.
		/// </summary>
		public static bool MayHarm(WildlifeRole attacker, int attackerPack, WildlifeRole defender, int defenderPack)
		{
			return !(attacker != WildlifeRole.None && defender != WildlifeRole.None && attackerPack != 0 && attackerPack == defenderPack);
		}

		/// <summary>
		/// Whether a wild animal's search should pick <paramref name="other"/>: a predator what it may hunt, a territorial
		/// animal what it charges, prey what it flees from. <paramref name="decided"/> is false when the searcher is not
		/// wildlife, and the faction rules answer instead. Pure, so it is tested on its own.
		/// </summary>
		/// <remarks>
		/// A predator hunts everything but its pack. Prey runs from everything but other prey and territorial animals (a boar
		/// grazing beside deer does not scatter them). A territorial animal ignores prey and its own kind and charges anything
		/// else that comes inside its (short) detection radius.
		/// </remarks>
		public static bool Notices(WildlifeRole searcher, int searcherPack, WildlifeRole other, int otherPack, out bool decided)
		{
			decided = searcher != WildlifeRole.None;
			switch (searcher)
			{
				case WildlifeRole.Predator:
					return MayHarm(searcher, searcherPack, other, otherPack);
				case WildlifeRole.Prey:
				case WildlifeRole.Territorial:
					return other != WildlifeRole.Prey && other != WildlifeRole.Territorial;
				default:
					return false;
			}
		}

		/// <inheritdoc cref="Notices(WildlifeRole, int, WildlifeRole, int, out bool)"/>
		public static bool Notices(ICharacter searcher, ICharacter other, out bool decided)
		{
			return Notices(RoleOf(searcher), PackOf(searcher), RoleOf(other), PackOf(other), out decided);
		}

		/// <summary>
		/// The flat direction prey should run: away from every threat at once, each weighted by how close it is (1/d), so a
		/// deer between two wolves runs out sideways rather than from one into the other. Pure, so it is tested on its own.
		/// </summary>
		/// <param name="position">The prey's position.</param>
		/// <param name="threats">Where the threats stand.</param>
		/// <param name="fallback">Used when the threats cancel out or stand on top of it: the way it already faces, reversed by the caller as it likes.</param>
		/// <returns>A unit vector on the ground plane.</returns>
		public static Vector3 FleeHeading(Vector3 position, IReadOnlyList<Vector3> threats, Vector3 fallback)
		{
			Vector3 sum = Vector3.zero;
			if (threats != null)
			{
				for (int i = 0; i < threats.Count; ++i)
				{
					Vector3 away = position - threats[i];
					away.y = 0f;
					float sqr = away.sqrMagnitude;
					if (sqr < 1e-6f)
					{
						continue;
					}
					// Unit vector over distance: (away / d) / d.
					sum += away / sqr;
				}
			}
			sum.y = 0f;
			if (sum.sqrMagnitude < 1e-8f)
			{
				sum = new Vector3(fallback.x, 0f, fallback.z);
			}
			if (sum.sqrMagnitude < 1e-8f)
			{
				sum = Vector3.forward;
			}
			return sum.normalized;
		}
	}
}
