using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Core;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared
{
	/// <summary>Which generator a <see cref="SceneObjectNamer"/> draws its name from.</summary>
	public enum SceneObjectNamingMode : byte
	{
		/// <summary>A person: given name, optional family name and title, from the object's race.</summary>
		Character = 0,
		/// <summary>A settlement name from the object's race, optionally flavoured by a biome.</summary>
		City = 1,
		/// <summary>A dungeon name from a biome.</summary>
		Dungeon = 2,
		/// <summary>A landmark, shrine, camp or similar from a biome.</summary>
		PointOfInterest = 3,
		/// <summary>A legendary item name from the object's race.</summary>
		Item = 4,
		/// <summary>
		/// No generated name: the object keeps the name it was authored with. For interactables such
		/// as chests and crates that have neither a race nor a place to be named after.
		/// </summary>
		Authored = 5,
	}

	/// <summary>How the gender behind a character name is chosen.</summary>
	public enum NamingGenderPolicy : byte
	{
		/// <summary>Random among the genders the race has models for; random between Male and Female when it has no gendered sets.</summary>
		RaceModels = 0,
		/// <summary>Random between Male and Female regardless of models.</summary>
		Random = 1,
		Male = 2,
		Female = 3,
		/// <summary>No gender lean; the name takes no gendered suffix.</summary>
		Unspecified = 4,
	}

	/// <summary>Whether a character name carries a title, and of which kind.</summary>
	public enum NamingTitlePolicy : byte
	{
		None = 0,
		/// <summary>A category chosen by the generator from the name's meaning.</summary>
		Random = 1,
		Honorific = 2,
		Epithet = 3,
		Rank = 4,
		Legend = 5,
	}

	/// <summary>Shape of a character name.</summary>
	public enum CharacterNameFormat : byte
	{
		/// <summary>"Toriton"</summary>
		Given = 0,
		/// <summary>"Toriton Feruald" — a second, ungendered draw from the same phonology.</summary>
		GivenAndFamily = 1,
	}

	/// <summary>
	/// Everything a designer sets on a <see cref="SceneObjectNamer"/>. Authored on
	/// the prefab, so it is identical on both peers and never travels over the wire.
	/// </summary>
	[Serializable]
	public class SceneObjectNamingSettings
	{
		[Tooltip("Which generator the name comes from.")]
		public SceneObjectNamingMode Mode = SceneObjectNamingMode.Character;

		[Header("Race")]
		[Tooltip("Race to name from. Leave unset to use the NPC's own race.")]
		[TemplateReference(typeof(RaceTemplate))]
		public int RaceOverrideID;
		[Tooltip("Optional culture key within the race, e.g. 'nordic'. Unknown keys fall back to the race's own phonology.")]
		public string Culture;
		[Tooltip("Optional cross-race flavour layered on the phonology.")]
		[TemplateReference("FishMMO.Shared.NameGeneration.NameModifierTemplate")]
		public int ModifierID;

		[Header("Character")]
		public NamingGenderPolicy GenderPolicy = NamingGenderPolicy.RaceModels;
		public CharacterNameFormat NameFormat = CharacterNameFormat.GivenAndFamily;
		public NamingTitlePolicy TitlePolicy = NamingTitlePolicy.None;

		[Header("Title")]
		[Tooltip("Social register of the title: Civil for townsfolk and traders, Martial for soldiers, Mythic for legends.")]
		public TitleRegister Register = TitleRegister.Civil;
		[Tooltip("What this character does, e.g. 'Banker'. Empty uses the Interactable's title when the object has one.")]
		public string Profession;
		[Tooltip("Longest title allowed on the nameplate; 0 is unlimited.")]
		public int MaxTitleLength = 32;
		[Tooltip("Whether a second clause may be appended ('Lord of Ashford, the Bold'). Off keeps nameplates short.")]
		public bool AllowCompoundTitle;

		[Header("Places and items")]
		[Tooltip("Biome for Dungeon and Point of Interest names and flavour for City names. Leave unset to read the scene's biome map at the object's position.")]
		[TemplateReference("BiomeTemplate")]
		public int BiomeID;
		public CityType CityType = CityType.Any;
		public POIType POIType = POIType.Any;
		public ItemType ItemType = ItemType.Any;

		[Header("Determinism")]
		[Tooltip("When set, the name is derived from this and the object seed, so it is the same every time the server starts. Leave empty for a fresh name per spawn.")]
		public string RegionSeed;
		[Tooltip("Distinguishes objects sharing a region seed. Defaults to the object's authored name.")]
		public string ObjectSeed;

		/// <summary>True for the modes that name things from a race rather than a biome.</summary>
		public bool UsesRace =>
			Mode == SceneObjectNamingMode.Character
			|| Mode == SceneObjectNamingMode.City
			|| Mode == SceneObjectNamingMode.Item;

		/// <summary>True for the modes that cannot produce a name without a biome.</summary>
		public bool RequiresBiome =>
			Mode == SceneObjectNamingMode.Dungeon
			|| Mode == SceneObjectNamingMode.PointOfInterest;

		/// <summary>True for the modes whose name is coloured by a biome, required or not.</summary>
		public bool UsesBiome => RequiresBiome || Mode == SceneObjectNamingMode.City;
	}

	/// <summary>
	/// Where a scene object's generated name comes from (<see cref="SceneObjectNamer"/>): the Naming module's name generator,
	/// installed into <see cref="SceneObjectNames.Source"/>. The server
	/// resolves the seed, the gender and the place; both peers then build the same name from them.
	/// </summary>
	public interface ISceneObjectNameSource
	{
		/// <summary>The name's seed: from the region and object seeds when set, else fresh. Never 0 (0 means "keep the authored name").</summary>
		int DeriveSeed(SceneObjectNamingSettings settings, string fallbackObjectSeed);

		/// <summary>The gender a character's name is drawn for, from the settings' policy and the race (an NPC's own unless overridden).</summary>
		CharacterGender PickGender(SceneObjectNamingSettings settings, RaceTemplate ownRace, int seed);

		/// <summary>The biome (its registry ID) and climate variant (its index) a place name is drawn from, at the object's position.</summary>
		void ResolvePlace(SceneObjectNamingSettings settings, Vector3 position, Scene scene, out int biomeID, out byte variantIndex);

		/// <summary>Builds the name from what the server resolved; false, with the reason, when the settings cannot name the object.</summary>
		bool TryBuild(SceneObjectNamingSettings settings, RaceTemplate ownRace, int seed, CharacterGender gender, string autoProfession,
			int biomeID, byte variantIndex, Scene scene, out string name, out string error);
	}

	/// <summary>The installed scene object name source; null without the Naming module, when every object keeps its authored name.</summary>
	public static class SceneObjectNames
	{
		public static ISceneObjectNameSource Source { get; set; }
	}
}
