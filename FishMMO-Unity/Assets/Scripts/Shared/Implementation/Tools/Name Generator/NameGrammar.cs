using System;
using System.Collections.Generic;

namespace FishMMO.Shared.NameGeneration
{
	/// <summary>
	/// The active <see cref="NameGrammarTemplate"/>, exposed as runtime lookups.
	/// The meaning tables that are scanned for a prefix or suffix match are kept
	/// in authored order (first match wins, exactly as authored); the exact-match
	/// tables are dictionaries.
	/// </summary>
	public static class NameGrammar
	{
		private static readonly string[] EmptyStrings = Array.Empty<string>();

		/// <summary>Raised when the active grammar changes.</summary>
		public static event Action Changed;

		/// <summary>The template currently supplying the grammar, or null.</summary>
		public static NameGrammarTemplate Current { get; private set; }

		/// <summary>True once a grammar template has been registered.</summary>
		public static bool IsLoaded => Current != null;

		public static IReadOnlyList<StringMapping> MeaningOnsets { get; private set; } = new List<StringMapping>();
		public static IReadOnlyList<StringMapping> MeaningCodas { get; private set; } = new List<StringMapping>();
		public static IReadOnlyDictionary<string, string> MeaningMiddles { get; private set; } = new Dictionary<string, string>();
		public static IReadOnlyList<StringMapping> MeaningTitleBias { get; private set; } = new List<StringMapping>();

		public static string[] CityPrefixes { get; private set; } = EmptyStrings;
		public static IReadOnlyDictionary<string, string> CitySuffixMeanings { get; private set; } = new Dictionary<string, string>();
		public static string[] FallbackCitySuffixes { get; private set; } = EmptyStrings;

		public static IReadOnlyList<StringMapping> BiomeMeaningOnsets { get; private set; } = new List<StringMapping>();
		public static IReadOnlyList<StringMapping> BiomeMeaningCodas { get; private set; } = new List<StringMapping>();

		public static IReadOnlyDictionary<string, string[]> POITypeSuffixes { get; private set; } = new Dictionary<string, string[]>();

		/// <summary>Authored place-name compositions; empty when the asset has none (the builder then uses <see cref="PlaceNameDefaults.Templates"/>).</summary>
		public static IReadOnlyList<PlaceNameTemplate> PlaceNameTemplates { get; private set; } = new List<PlaceNameTemplate>();
		public static IReadOnlyDictionary<string, string[]> PlaceFusedSuffixes { get; private set; } = new Dictionary<string, string[]>();
		public static string[] PlaceAdjectives { get; private set; } = EmptyStrings;
		public static string[] PlaceNouns { get; private set; } = EmptyStrings;
		/// <summary>Authored kind weights for <see cref="POIType.Any"/>; a kind with no row uses <see cref="PlaceNameDefaults.KindWeight"/>.</summary>
		public static IReadOnlyDictionary<POIType, int> POIKindWeights { get; private set; } = new Dictionary<POIType, int>();
		/// <summary>The authored water phonology, or null when the asset has no usable one.</summary>
		public static BiomePhonology WaterPhonology { get; private set; }

		public static IReadOnlyList<TitleTemplate> TitleTemplates { get; private set; } = new List<TitleTemplate>();
		public static HashSet<string> PlaceTakingHonorifics { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		public static HashSet<string> OrdinalTakingHonorifics { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		public static string[] GenericOccupations { get; private set; } = EmptyStrings;

		public static string[] Ordinals { get; private set; } = EmptyStrings;
		public static string[] PossessivePronouns { get; private set; } = EmptyStrings;
		public static string[] PlaceEpithetPatterns { get; private set; } = EmptyStrings;
		public static string[] ComposedAdjectives { get; private set; } = EmptyStrings;
		public static string[] ComposedQualifiers { get; private set; } = EmptyStrings;
		public static string[] DeedVerbs { get; private set; } = EmptyStrings;
		public static string[] DeedObjects { get; private set; } = EmptyStrings;
		public static string[] UniversalPlaces { get; private set; } = EmptyStrings;
		public static string[] BattleQualifiers { get; private set; } = EmptyStrings;
		public static string[] EraQualifiers { get; private set; } = EmptyStrings;
		public static string[] Outcomes { get; private set; } = EmptyStrings;

		public static IReadOnlyDictionary<string, string[]> ItemTypeSuffixes { get; private set; } = new Dictionary<string, string[]>();
		public static IReadOnlyDictionary<string, string[]> ItemTypeNouns { get; private set; } = new Dictionary<string, string[]>();
		public static string[] ItemGenericNouns { get; private set; } = EmptyStrings;
		public static string[] ItemEpithets { get; private set; } = EmptyStrings;
		public static string[] ItemLegendaryPrefixes { get; private set; } = EmptyStrings;
		public static string[] ItemHeroRelations { get; private set; } = EmptyStrings;
		public static string[] ItemPlaceRelations { get; private set; } = EmptyStrings;

		/// <summary>Makes a template the active grammar. The last one registered wins.</summary>
		public static void Register(NameGrammarTemplate template)
		{
			if (template == null)
			{
				return;
			}
			Current = template;
			Rebuild();
		}

		/// <summary>Drops the active grammar, but only if it is still the given template.</summary>
		public static void Unregister(NameGrammarTemplate template)
		{
			if (template == null || Current != template)
			{
				return;
			}
			Current = null;
			Rebuild();
		}

		/// <summary>Drops the active grammar.</summary>
		public static void Clear()
		{
			Current = null;
			Rebuild();
		}

		/// <summary>Rebuilds every lookup from <see cref="Current"/>; call after editing it at runtime.</summary>
		public static void Rebuild()
		{
			NameGrammarTemplate t = Current;
			var ordinal = StringComparer.Ordinal;
			var ignoreCase = StringComparer.OrdinalIgnoreCase;

			MeaningOnsets = NamingTableUtility.ToOrdered(t?.MeaningOnsets);
			MeaningCodas = NamingTableUtility.ToOrdered(t?.MeaningCodas);
			MeaningMiddles = NamingTableUtility.ToDictionary(t?.MeaningMiddles, ignoreCase);
			MeaningTitleBias = NamingTableUtility.ToOrdered(t?.MeaningTitleBias);

			CityPrefixes = t?.CityPrefixes ?? EmptyStrings;
			CitySuffixMeanings = NamingTableUtility.ToDictionary(t?.CitySuffixMeanings, ignoreCase);
			FallbackCitySuffixes = t?.FallbackCitySuffixes ?? EmptyStrings;

			BiomeMeaningOnsets = NamingTableUtility.ToOrdered(t?.BiomeMeaningOnsets);
			BiomeMeaningCodas = NamingTableUtility.ToOrdered(t?.BiomeMeaningCodas);

			POITypeSuffixes = NamingTableUtility.ToListDictionary(t?.POITypeSuffixes, ignoreCase);
			PlaceNameTemplates = CleanPlaceTemplates(t?.PlaceNameTemplates);
			PlaceFusedSuffixes = NamingTableUtility.ToListDictionary(t?.PlaceFusedSuffixes, ignoreCase);
			PlaceAdjectives = t?.PlaceAdjectives ?? EmptyStrings;
			PlaceNouns = t?.PlaceNouns ?? EmptyStrings;
			POIKindWeights = KindWeights(t?.POIKindWeights);
			WaterPhonology = t?.WaterPhonology != null && t.WaterPhonology.IsUsable() ? t.WaterPhonology.ToRuntime() : null;

			TitleTemplates = CleanTemplates(t?.TitleTemplates);
			PlaceTakingHonorifics = new HashSet<string>(t?.PlaceTakingHonorifics ?? EmptyStrings, ignoreCase);
			OrdinalTakingHonorifics = new HashSet<string>(t?.OrdinalTakingHonorifics ?? EmptyStrings, ignoreCase);
			GenericOccupations = t?.GenericOccupations ?? EmptyStrings;

			Ordinals = t?.Ordinals ?? EmptyStrings;
			PossessivePronouns = t?.PossessivePronouns ?? EmptyStrings;
			PlaceEpithetPatterns = t?.PlaceEpithetPatterns ?? EmptyStrings;
			ComposedAdjectives = t?.ComposedAdjectives ?? EmptyStrings;
			ComposedQualifiers = t?.ComposedQualifiers ?? EmptyStrings;
			DeedVerbs = t?.DeedVerbs ?? EmptyStrings;
			DeedObjects = t?.DeedObjects ?? EmptyStrings;
			UniversalPlaces = t?.UniversalPlaces ?? EmptyStrings;
			BattleQualifiers = t?.BattleQualifiers ?? EmptyStrings;
			EraQualifiers = t?.EraQualifiers ?? EmptyStrings;
			Outcomes = t?.Outcomes ?? EmptyStrings;

			ItemTypeSuffixes = NamingTableUtility.ToListDictionary(t?.ItemTypeSuffixes, ignoreCase);
			ItemTypeNouns = NamingTableUtility.ToListDictionary(t?.ItemTypeNouns, ignoreCase);
			ItemGenericNouns = t?.ItemGenericNouns ?? EmptyStrings;
			ItemEpithets = t?.ItemEpithets ?? EmptyStrings;
			ItemLegendaryPrefixes = t?.ItemLegendaryPrefixes ?? EmptyStrings;
			ItemHeroRelations = t?.ItemHeroRelations ?? EmptyStrings;
			ItemPlaceRelations = t?.ItemPlaceRelations ?? EmptyStrings;

			Changed?.Invoke();
		}

		private static List<TitleTemplate> CleanTemplates(List<TitleTemplate> rows)
		{
			var result = new List<TitleTemplate>(rows?.Count ?? 0);
			if (rows == null)
			{
				return result;
			}
			for (int i = 0; i < rows.Count; i++)
			{
				TitleTemplate row = rows[i];
				if (row != null && !string.IsNullOrWhiteSpace(row.Pattern) && row.Weight > 0)
				{
					result.Add(row);
				}
			}
			return result;
		}

		private static List<PlaceNameTemplate> CleanPlaceTemplates(List<PlaceNameTemplate> rows)
		{
			var result = new List<PlaceNameTemplate>(rows?.Count ?? 0);
			if (rows == null)
			{
				return result;
			}
			for (int i = 0; i < rows.Count; i++)
			{
				PlaceNameTemplate row = rows[i];
				if (row != null && !string.IsNullOrWhiteSpace(row.Pattern) && row.Weight > 0)
				{
					result.Add(row);
				}
			}
			return result;
		}

		/// <summary>Kind → weight; later rows for the same kind win, as in the other tables.</summary>
		private static Dictionary<POIType, int> KindWeights(List<POIKindWeight> rows)
		{
			var result = new Dictionary<POIType, int>();
			if (rows == null)
			{
				return result;
			}
			for (int i = 0; i < rows.Count; i++)
			{
				if (rows[i] != null && rows[i].Kind != POIType.Any)
				{
					result[rows[i].Kind] = Math.Max(0, rows[i].Weight);
				}
			}
			return result;
		}

		/// <summary>First row whose key the text starts with, or null. Case-insensitive.</summary>
		public static string MatchPrefix(IReadOnlyList<StringMapping> rows, string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return null;
			}
			for (int i = 0; i < rows.Count; i++)
			{
				if (text.StartsWith(rows[i].Key, StringComparison.OrdinalIgnoreCase))
				{
					return rows[i].Value;
				}
			}
			return null;
		}

		/// <summary>First row whose key the text ends with, or null. Case-insensitive.</summary>
		public static string MatchSuffix(IReadOnlyList<StringMapping> rows, string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return null;
			}
			for (int i = 0; i < rows.Count; i++)
			{
				if (text.EndsWith(rows[i].Key, StringComparison.OrdinalIgnoreCase))
				{
					return rows[i].Value;
				}
			}
			return null;
		}
	}
}
