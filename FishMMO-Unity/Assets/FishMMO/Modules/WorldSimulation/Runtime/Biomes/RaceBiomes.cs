using FishMMO.Shared;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// A race's biomes as templates: the race template keeps its affinities as
	/// biome ids, and the World Simulation module resolves them.
	/// </summary>
	public static class RaceBiomes
	{
		/// <summary>The biome an affinity names, or null when it is not registered.</summary>
		public static BiomeTemplate Biome(this BiomeAffinity affinity) => affinity != null && BiomeRegistry.TryGetByID(affinity.BiomeID, out BiomeTemplate biome) ? biome : null;

		/// <summary>
		/// Draws one of this race's home biomes, weighted, with the given RNG; null when the race has
		/// no registered affinities. Deterministic for a seeded RNG, so both peers agree.
		/// </summary>
		public static BiomeTemplate PickHomeBiome(this RaceTemplate race, DeterministicRNG rng)
		{
			if (race.BiomeAffinities == null || race.BiomeAffinities.Count == 0)
			{
				return null;
			}
			float total = 0f;
			for (int i = 0; i < race.BiomeAffinities.Count; i++)
			{
				BiomeAffinity affinity = race.BiomeAffinities[i];
				if (affinity != null && affinity.Weight > 0f && affinity.Biome() != null)
				{
					total += affinity.Weight;
				}
			}
			if (total <= 0f)
			{
				return null;
			}
			float roll = (float)(rng.NextDouble() * total);
			for (int i = 0; i < race.BiomeAffinities.Count; i++)
			{
				BiomeAffinity affinity = race.BiomeAffinities[i];
				if (affinity == null || affinity.Weight <= 0f)
				{
					continue;
				}
				BiomeTemplate biome = affinity.Biome();
				if (biome == null)
				{
					continue;
				}
				roll -= affinity.Weight;
				if (roll <= 0f)
				{
					return biome;
				}
			}
			return null;
		}
	}
}
