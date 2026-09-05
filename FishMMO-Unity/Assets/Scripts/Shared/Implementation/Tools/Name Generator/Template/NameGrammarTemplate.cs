using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.NameGeneration
{
	/// <summary>
	/// The universal vocabulary every generator mode shares: the meaning tables
	/// that turn syllables into a gloss ("silver-", "of ancient", "light"), the
	/// title grammar (deeds, places, outcomes), the item vocabulary, and the
	/// city and POI tables that are not tied to one race or biome.
	///
	/// <para>Exactly one of these is active; the last one loaded wins and
	/// registers itself with <see cref="NameGrammar"/>.</para>
	/// </summary>
	[CreateAssetMenu(fileName = "Name Grammar", menuName = "FishMMO/Naming/Name Grammar", order = 4)]
	public class NameGrammarTemplate : CachedScriptableObject<NameGrammarTemplate>, ICachedObject
	{
		[Header("Character meaning — scanned in order; first match wins")]
		[Tooltip("Name start → meaning fragment, e.g. 'Ael' → 'silver-'.")]
		public List<StringMapping> MeaningOnsets = new();
		[Tooltip("Name ending → meaning fragment, e.g. 'iel' → 'light'.")]
		public List<StringMapping> MeaningCodas = new();
		[Tooltip("Middle syllable → connective, e.g. 'al' → 'of ancient'.")]
		public List<StringMapping> MeaningMiddles = new();
		[Tooltip("Meaning keyword → title category it leans toward (honorific / epithet / rank / legend).")]
		public List<StringMapping> MeaningTitleBias = new();

		[Header("Cities")]
		[Tooltip("Leading words for city names, e.g. 'Old', 'North'.")]
		public string[] CityPrefixes;
		[Tooltip("City suffix → meaning, e.g. 'hold' → 'stronghold'.")]
		public List<StringMapping> CitySuffixMeanings = new();
		[Tooltip("Used when a race has no suffixes for the requested city type.")]
		public string[] FallbackCitySuffixes = { "hold", "haven", "gate", "fall", "stead" };

		[Header("Biome meaning — scanned in order; first match wins")]
		public List<StringMapping> BiomeMeaningOnsets = new();
		public List<StringMapping> BiomeMeaningCodas = new();

		[Header("Points of interest")]
		[Tooltip("POI type (lowercase POIType name) → type-specific suffixes, e.g. 'shrine' → Shrine, Altar, Fane.")]
		public List<StringListMapping> POITypeSuffixes = new();

		[Header("Titles — ordinals and pronouns")]
		public string[] Ordinals;
		public string[] PossessivePronouns;

		[Header("Titles — composition")]
		[Tooltip("{0} = place name. Example: 'of {0}', 'bane of {0}'.")]
		public string[] PlaceEpithetPatterns;
		public string[] ComposedAdjectives;
		public string[] ComposedQualifiers;

		[Header("Titles — legends")]
		public string[] DeedVerbs;
		public string[] DeedObjects;
		public string[] UniversalPlaces;
		public string[] BattleQualifiers;
		public string[] EraQualifiers;
		public string[] Outcomes;

		[Header("Items")]
		[Tooltip("Item type (lowercase ItemType name) → name endings, e.g. 'weapon' → bane, cleaver, fang.")]
		public List<StringListMapping> ItemTypeSuffixes = new();
		[Tooltip("Item type (lowercase ItemType name) → nouns for '<Noun> of <Root>' names.")]
		public List<StringListMapping> ItemTypeNouns = new();
		[Tooltip("Nouns used when the item type has no entry above.")]
		public string[] ItemGenericNouns = { "Relic", "Artifact", "Treasure", "Heirloom", "Token" };
		public string[] ItemEpithets;
		public string[] ItemLegendaryPrefixes;
		[Tooltip("Patterns relating an item to a person: {0} = item noun, {1} = character name.")]
		public string[] ItemHeroRelations;
		[Tooltip("Patterns relating an item to a place: {0} = item noun, {1} = place name.")]
		public string[] ItemPlaceRelations;

		public override void OnLoad(string typeName, string resourceName, int resourceID)
		{
			base.OnLoad(typeName, resourceName, resourceID);
			NameGrammar.Register(this);
		}

		public override void OnUnload(string typeName, string resourceName, int resourceID)
		{
			NameGrammar.Unregister(this);
			base.OnUnload(typeName, resourceName, resourceID);
		}
	}
}
