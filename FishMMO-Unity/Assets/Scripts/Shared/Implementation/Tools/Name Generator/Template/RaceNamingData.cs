using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.NameGeneration
{
	/// <summary>
	/// Everything the name generator knows about one race, carried on its
	/// <see cref="RaceTemplate"/>: phonology, cultural variants, the places its
	/// titles refer to, its title vocabulary, and its city-name endings.
	///
	/// <para>The serialized fields are what designers edit in the Race
	/// inspector; the runtime views are built once when the race registers with
	/// <see cref="RaceRegistry"/>, or lazily on first use.</para>
	/// </summary>
	[Serializable]
	public class RaceNamingData
	{
		[Tooltip("Syllable tables names are assembled from.")]
		public SerializableRacePhonology Phonology = new();

		[Tooltip("Optional cultural variants, each with its own phonology under this race's titles and places.")]
		public List<SerializableCultureVariant> Cultures = new();

		[Tooltip("Place names titles can refer to, e.g. 'of Ashford'.")]
		public string[] Places;

		public SerializableRaceTitles Titles = new();

		public SerializableRaceCitySuffixes CitySuffixes = new();

		[Tooltip("Whether Civil titles may draw trades from the grammar's generic list when this race lists none. Off for monsters: a slime is never a potter.")]
		public bool AllowGenericOccupations = true;

		private RacePhonology runtimePhonology;
		private Dictionary<string, RacePhonology> runtimeCultures;
		private RaceTitles runtimeTitles;
		private RaceCitySuffixes runtimeCitySuffixes;
		private string[] runtimePlaces;

		/// <summary>True when the phonology can produce a name at all.</summary>
		public bool IsUsable => Phonology != null && Phonology.IsUsable();

		public RacePhonology RuntimePhonology
		{
			get
			{
				EnsureRuntime();
				return runtimePhonology;
			}
		}

		public IReadOnlyDictionary<string, RacePhonology> RuntimeCultures
		{
			get
			{
				EnsureRuntime();
				return runtimeCultures;
			}
		}

		public RaceTitles RuntimeTitles
		{
			get
			{
				EnsureRuntime();
				return runtimeTitles;
			}
		}

		public RaceCitySuffixes RuntimeCitySuffixes
		{
			get
			{
				EnsureRuntime();
				return runtimeCitySuffixes;
			}
		}

		public string[] RuntimePlaces
		{
			get
			{
				EnsureRuntime();
				return runtimePlaces;
			}
		}

		/// <summary>
		/// Rebuilds the runtime views from the serialized fields. Called on
		/// registration; call again after editing the fields at runtime.
		/// </summary>
		public void BuildRuntime()
		{
			runtimePhonology = (Phonology ?? new SerializableRacePhonology()).ToRuntime();
			runtimeTitles = (Titles ?? new SerializableRaceTitles()).ToRuntime();
			runtimeCitySuffixes = (CitySuffixes ?? new SerializableRaceCitySuffixes()).ToRuntime();
			runtimePlaces = Places ?? Array.Empty<string>();

			runtimeCultures = new Dictionary<string, RacePhonology>(StringComparer.Ordinal);
			if (Cultures != null)
			{
				for (int i = 0; i < Cultures.Count; i++)
				{
					SerializableCultureVariant culture = Cultures[i];
					if (culture == null || string.IsNullOrWhiteSpace(culture.CultureKey) || culture.Phonology == null)
					{
						continue;
					}
					runtimeCultures[GeneratorUtility.NormalizeRace(culture.CultureKey)] = culture.Phonology.ToRuntime();
				}
			}
		}

		/// <summary>Copies every serialized field from another block, so a race can adopt a preset.</summary>
		public void CopyFrom(RaceNamingData source)
		{
			if (source == null)
			{
				return;
			}
			Phonology = source.Phonology == null ? new SerializableRacePhonology() : SerializableRacePhonology.From(source.Phonology.ToRuntime());
			Cultures = new List<SerializableCultureVariant>();
			if (source.Cultures != null)
			{
				for (int i = 0; i < source.Cultures.Count; i++)
				{
					SerializableCultureVariant culture = source.Cultures[i];
					if (culture == null)
					{
						continue;
					}
					Cultures.Add(new SerializableCultureVariant
					{
						CultureKey = culture.CultureKey,
						Phonology = culture.Phonology == null
							? new SerializableRacePhonology()
							: SerializableRacePhonology.From(culture.Phonology.ToRuntime()),
					});
				}
			}
			Places = source.Places == null ? null : (string[])source.Places.Clone();
			Titles = source.Titles == null ? new SerializableRaceTitles() : SerializableRaceTitles.From(source.Titles.ToRuntime());
			AllowGenericOccupations = source.AllowGenericOccupations;
			CitySuffixes = source.CitySuffixes == null ? new SerializableRaceCitySuffixes() : SerializableRaceCitySuffixes.From(source.CitySuffixes.ToRuntime());
			runtimePhonology = null;
		}

		private void EnsureRuntime()
		{
			if (runtimePhonology == null)
			{
				BuildRuntime();
			}
		}
	}
}
