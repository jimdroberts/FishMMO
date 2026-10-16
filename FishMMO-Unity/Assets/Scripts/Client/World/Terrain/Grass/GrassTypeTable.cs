using System;
using System.Collections.Generic;

namespace FishMMO.Client
{
	/// <summary>
	/// The scene's blade type table: which entry a prototype's blades are drawn as, given its prefab and the signature of
	/// its biome's look of it (<see cref="FishMMO.Shared.Biomes.BiomeGrassType.Signature"/> with its tints). Pure, so the
	/// rules below are tested off the engine.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>One entry per distinct look of a prefab.</b> Two biomes whose looks of a prefab are equal share one entry (every
	/// biome's are, until the per-biome rounds tune them: the authoring writes the one old look everywhere), so a scene has
	/// exactly as many entries as it had types when a type was a prefab. Where they differ the prefab has an entry per look,
	/// and each prototype's blades take its own biome's (TerrainDetailBiomes), so the same grass is lush in one biome and
	/// straw in the next across one terrain.
	/// </para>
	/// <para>
	/// <b>The cap</b> is the GPU's (<see cref="GrassBladeRenderer.MaxTypes"/>, GrassMath.MaxTypes = 32: four bits of a blade
	/// record's packed word and one of its colour word, FishGrassBlades.compute GRASS_TYPES). Past it a look is MERGED into
	/// the first entry of the same prefab (drawn with that biome's look, said once in the log): the blades still grow where
	/// their biome paints them, only their look is another biome's. A prefab with no entry at all past the cap is refused
	/// (its prototypes stay meshes, as a type past the cap always did). Each terrain's own cap
	/// (<see cref="GrassTerrain.MaxChannels"/>, 16 density channels) is the builder's, as before: an entry is a channel.
	/// </para>
	/// </remarks>
	public static class GrassTypeTable
	{
		/// <summary>How a look was placed in the table.</summary>
		public enum Placement
		{
			/// <summary>An entry with this prefab and look was there already.</summary>
			Existing,
			/// <summary>A new entry was made for it.</summary>
			Added,
			/// <summary>The table was full: it takes the first entry of its prefab, another biome's look.</summary>
			Merged,
			/// <summary>The table was full and holds nothing of its prefab: its prototypes stay meshes.</summary>
			Refused,
		}

		/// <summary>
		/// The entry for <paramref name="prefab"/> with look <paramref name="signature"/> in <paramref name="keys"/> (the table's
		/// entries in order, each its prefab and look), adding one while there are fewer than <paramref name="capacity"/>;
		/// −1 when refused. <paramref name="keys"/> grows by one exactly when <paramref name="how"/> is <see cref="Placement.Added"/>.
		/// </summary>
		public static int Place<TPrefab>(List<(TPrefab Prefab, string Look)> keys, TPrefab prefab, string signature, int capacity, out Placement how)
			where TPrefab : class
		{
			int firstOfPrefab = -1;
			for (int i = 0; i < keys.Count; i++)
			{
				if (!ReferenceEquals(keys[i].Prefab, prefab) && !Equals(keys[i].Prefab, prefab))
				{
					continue;
				}
				if (string.Equals(keys[i].Look, signature, StringComparison.Ordinal))
				{
					how = Placement.Existing;
					return i;
				}
				if (firstOfPrefab < 0)
				{
					firstOfPrefab = i;
				}
			}
			if (keys.Count < capacity)
			{
				keys.Add((prefab, signature));
				how = Placement.Added;
				return keys.Count - 1;
			}
			how = firstOfPrefab >= 0 ? Placement.Merged : Placement.Refused;
			return firstOfPrefab;
		}
	}
}
