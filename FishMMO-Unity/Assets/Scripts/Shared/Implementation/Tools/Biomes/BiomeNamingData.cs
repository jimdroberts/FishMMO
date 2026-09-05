using System;
using UnityEngine;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// What the name generator knows about a biome, carried on its <see cref="BiomeTemplate"/>:
	/// the phonology dungeon and point-of-interest names are built from, and the vocabulary
	/// that flavours them. The runtime view is built once when the biome registers.
	/// </summary>
	[Serializable]
	public class BiomeNamingData
	{
		[Tooltip("Syllable tables and suffix pools for dungeon and point-of-interest names.")]
		public SerializableBiomePhonology Phonology = new();

		private BiomePhonology runtimePhonology;

		/// <summary>True when the phonology can produce a root at all.</summary>
		public bool IsUsable => Phonology != null && Phonology.IsUsable();

		public BiomePhonology RuntimePhonology
		{
			get
			{
				if (runtimePhonology == null)
				{
					BuildRuntime();
				}
				return runtimePhonology;
			}
		}

		/// <summary>Rebuilds the runtime view from the serialized fields.</summary>
		public void BuildRuntime()
		{
			runtimePhonology = (Phonology ?? new SerializableBiomePhonology()).ToRuntime();
		}
	}
}
