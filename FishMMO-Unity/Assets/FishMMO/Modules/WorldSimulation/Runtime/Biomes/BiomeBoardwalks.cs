using System;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// Which of a biome's ways are raised boardwalks (<see cref="BiomeTemplate.Boardwalks"/>, 2026-10-18, phase I2i, decision 27):
	/// plank walks on piles with a pole handrail, laid along the way and walked as any deck. Stored by number: append only.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A biome setting, not a ground layer's.</b> The ways are planned and carved with the sites, before the biomes are painted
	/// (their pads shape the ground the splat then reads), so the ground layer under a way is not known when the planner, the carve
	/// and the path field must already treat a stretch as a deck. The biome is (the plan's biome field), and a biome that sets this
	/// is a marsh biome whose ground is wet throughout; steep stretches (over <see cref="BiomeBoardwalkRules.MaxSlopeDegrees"/>)
	/// stay on the ground, and rivers are crossed by their bridges and fords as anywhere.
	/// </para>
	/// <para>Until 2026-10-18 no way was a boardwalk; <see cref="None"/> (every biome's default) keeps it so.</para>
	/// </remarks>
	[Flags]
	public enum BiomeBoardwalks : byte
	{
		/// <summary>No boardwalks: every way lies on the ground.</summary>
		None = 0,
		/// <summary>The cart tracks, roads and highways across the biome's level ground.</summary>
		Roads = 1 << 0,
		/// <summary>The footpaths and trails across the biome's level ground too.</summary>
		Paths = 1 << 1,
		/// <summary>The streets of the settlements built in the biome, between their houses.</summary>
		Streets = 1 << 2,
	}

	/// <summary>
	/// How a biome's raised ways are built (<see cref="BiomeTemplate.BoardwalkStyle"/>, 2026-10-25, phase I2i-b): the plank walk on
	/// piles of I2i, or one of the country walks the biome drafts name. Stored by number: append only. The streets of a settlement
	/// keep the plank walk between its houses whatever the style; the style is the biome's routed ways' (its roads, paths and trails).
	/// </summary>
	/// <remarks>Until 2026-10-25 every boardwalk was planked; <see cref="Planked"/> (every biome's default) keeps it so.</remarks>
	public enum BiomeBoardwalkStyle : byte
	{
		/// <summary>The plank walk (I2i): split planks across stringers on bents of two driven piles, 0.9 m up, a pole handrail.</summary>
		Planked = 0,
		/// <summary>A passerella (Farmland): oak planks on stub piles 0.3 m over a wet meadow, one pole rail.</summary>
		Passerella = 1,
		/// <summary>A landing walk (Plains): split cottonwood planks on forked posts 0.8 m up, rawhide-lashed, no rail.</summary>
		LandingWalk = 2,
		/// <summary>A Bohlensteg (Valley): paired split-larch planks laid lengthwise on driven stakes, 0.6 m wide, low over a fen.</summary>
		Bohlensteg = 3,
		/// <summary>A pole-walk (Mangrove): two bark-stripped poles side by side along the way, end-lapped on forked stakes over the mud.</summary>
		PoleWalk = 4,
	}

	/// <summary>The boardwalk rules that do not depend on a template asset.</summary>
	public static class BiomeBoardwalkRules
	{
		/// <summary>The steepest ground a way is raised over, degrees: steeper, it climbs on the ground.</summary>
		public const float MaxSlopeDegrees = 8f;

		/// <summary>True when the biome's setting raises a routed way of this class (the ways a settlement's layout lays are <see cref="BiomeBoardwalks.Streets"/>).</summary>
		public static bool Raises(BiomeBoardwalks setting, ScenePathClass way)
		{
			switch (way)
			{
				case ScenePathClass.CartTrack:
				case ScenePathClass.Road:
				case ScenePathClass.Highway:
					return (setting & BiomeBoardwalks.Roads) != 0;
				case ScenePathClass.Footpath:
				case ScenePathClass.Trail:
					return (setting & BiomeBoardwalks.Paths) != 0;
				case ScenePathClass.Street:
					return (setting & BiomeBoardwalks.Streets) != 0;
				default:
					// An ancient road's paving and a site's yards stay on the ground.
					return false;
			}
		}
	}
}
