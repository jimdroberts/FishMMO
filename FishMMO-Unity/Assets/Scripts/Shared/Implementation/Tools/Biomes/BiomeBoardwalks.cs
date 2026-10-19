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
