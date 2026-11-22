using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared
{
	/// <summary>How a wild animal treats everything around it.</summary>
	public enum WildlifeRole : byte
	{
		/// <summary>Not wildlife: the faction rules apply.</summary>
		None = 0,
		/// <summary>Never fights. Flees from anything that is not prey, for as long as it is near, and the whole herd with it.</summary>
		Prey = 1,
		/// <summary>Hunts everything (players, other NPCs, prey, other packs) except its own pack.</summary>
		Predator = 2,
		/// <summary>Never hunts, never flees: charges whatever comes too close or hits it (a boar).</summary>
		Territorial = 3,
	}

	/// <summary>Whether a species lives out in the open, in caves, or both (2026-11-21: deer were spawning in caves).</summary>
	/// <remarks>Append only; the ordinal is stored on the species templates.</remarks>
	public enum WildlifeShelter : byte
	{
		/// <summary>Out under the sky only: never in a cave or a mine (deer, elk, wolves).</summary>
		Open = 0,
		/// <summary>In caves and mines only (giant bats).</summary>
		Cave = 1,
		/// <summary>Either (a bear dens in a cave mouth and roams the woods).</summary>
		Anywhere = 2,
	}

	/// <summary>The grounds a species lives on, one bit per <see cref="SurfaceKind"/>; none set means any ground.</summary>
	[System.Flags]
	public enum SurfaceMask
	{
		Any = 0,
		Dirt = 1 << (int)SurfaceKind.Dirt,
		Grass = 1 << (int)SurfaceKind.Grass,
		Leaves = 1 << (int)SurfaceKind.Leaves,
		Mud = 1 << (int)SurfaceKind.Mud,
		Sand = 1 << (int)SurfaceKind.Sand,
		Gravel = 1 << (int)SurfaceKind.Gravel,
		Stone = 1 << (int)SurfaceKind.Stone,
		Wood = 1 << (int)SurfaceKind.Wood,
		Snow = 1 << (int)SurfaceKind.Snow,
		Ice = 1 << (int)SurfaceKind.Ice,
		Water = 1 << (int)SurfaceKind.Water,
		Metal = 1 << (int)SurfaceKind.Metal,
	}

	/// <summary>
	/// What the core reads of a wild animal's species (2026-11-20: the species template is the Wildlife module's): its role
	/// toward others, how long its carcass lies, and where and in what numbers a spawner puts it.
	/// </summary>
	public interface IWildlifeSpecies
	{
		/// <summary>Prey, predator or territorial.</summary>
		WildlifeRole Role { get; }

		/// <summary>How long the carcass lies before it is reclaimed, in seconds.</summary>
		float CorpseSeconds { get; }

		/// <summary>The largest herd or pack a spawner gathers of it.</summary>
		int GroupMax { get; }

		/// <summary>The ground it lives on.</summary>
		BiomeHabitat Habitats { get; }

		/// <summary>Out in the open, in caves, or both.</summary>
		WildlifeShelter Shelter { get; }

		/// <summary>The grounds (by their painted texture) it may appear on; <see cref="SurfaceMask.Any"/> for all.</summary>
		SurfaceMask Surfaces { get; }

		/// <summary>Metres per second at a walk.</summary>
		float WalkSpeed { get; }

		/// <summary>Metres per second at a run (fleeing, hunting, charging).</summary>
		float RunSpeed { get; }
	}

	/// <summary>
	/// The rules wildlife lives by, outside the faction system. Every check that would otherwise ask a faction asks here
	/// first: the harm rule (<see cref="HarmRule.MayHarm"/>) and the AI's search for something to fight or flee.
	/// </summary>
	public static class WildlifeRules
	{
		/// <summary>Whether the species lives on <paramref name="habitat"/>: one of its habitats, never a settlement or the sea.</summary>
		/// <remarks>
		/// A cave dweller asks nothing of the biome: the map's value at a cave is the land over it (a sea cave under the coast's
		/// sea cell, a cave under a town's fields), not the cave.
		/// </remarks>
		public static bool Lives(IWildlifeSpecies species, BiomeHabitat habitat)
		{
			if (species == null)
			{
				return false;
			}
			if (species.Shelter == WildlifeShelter.Cave)
			{
				return true;
			}
			return (habitat & species.Habitats) != 0 && (habitat & (BiomeHabitat.Settlement | BiomeHabitat.Sea)) == 0;
		}

		/// <summary>
		/// Whether the species may appear on the ground a spawn's cast struck: in the open or underground as its shelter says, and on
		/// one of its grounds (an unknown ground passes). Pure, so it is tested on its own.
		/// </summary>
		public static bool LivesOn(IWildlifeSpecies species, in GroundReading ground)
		{
			if (species == null)
			{
				return false;
			}
			switch (species.Shelter)
			{
				case WildlifeShelter.Open:
					if (ground.Underground)
					{
						return false;
					}
					break;
				case WildlifeShelter.Cave:
					if (!ground.Underground)
					{
						return false;
					}
					break;
			}
			return species.Surfaces == SurfaceMask.Any || !ground.SurfaceKnown || (species.Surfaces & MaskOf(ground.Surface)) != 0;
		}

		/// <summary>The mask bit of one ground.</summary>
		public static SurfaceMask MaskOf(SurfaceKind kind) => (SurfaceMask)(1 << (int)kind);

		/// <summary>Whether <paramref name="point"/> is inside one of the keep-out circles, each (x, radius, z).</summary>
		public static bool InsideKeepOut(IReadOnlyList<Vector3> circles, Vector3 point)
		{
			if (circles == null)
			{
				return false;
			}
			for (int i = 0; i < circles.Count; ++i)
			{
				Vector3 c = circles[i];
				float dx = point.x - c.x, dz = point.z - c.z;
				if (dx * dx + dz * dz < c.y * c.y)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>The character's wildlife role: <see cref="WildlifeRole.None"/> for anything that is not wildlife.</summary>
		public static WildlifeRole RoleOf(ICharacter character)
		{
			return character is NPC npc && npc.WildlifeSpecies != null ? npc.WildlifeSpecies.Role : WildlifeRole.None;
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
