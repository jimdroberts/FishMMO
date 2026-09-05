using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using FishMMO.Logging;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared
{
	/// <summary>
	/// Race model references available for a specific generated character gender.
	/// </summary>
	[System.Serializable]
	public class GenderedRaceModelSet
	{
		/// <summary>
		/// Gender this model set applies to.
		/// </summary>
		public CharacterGender Gender = CharacterGender.Unspecified;

		/// <summary>
		/// Model references available for this gender.
		/// </summary>
		public List<AssetReference> ModelReferences = new List<AssetReference>();
	}

	/// <summary>
	/// Default body proportions for a race. These values are applied as bone scaling on character spawn.
	/// 1.0 = default human proportion. Values are multipliers on individual bone localScale.
	/// </summary>
	[System.Serializable]
	public struct RaceProportions
	{
		/// <summary>Overall height multiplier (affects spine and leg bones).</summary>
		[Range(0.5f, 1.5f)]
		[Tooltip("Overall height multiplier. Stacks with LegLength/TorsoLength for intentional compounding (e.g., Dwarf: Height=0.75, LegLength=0.70 → legs = 0.525x).")]
		public float Height;

		/// <summary>Arm length multiplier (affects upper/lower arm bones).</summary>
		[Range(0.7f, 1.3f)]
		public float ArmLength;

		/// <summary>Leg length multiplier (affects upper/lower leg bones). Stacks with Height.</summary>
		[Range(0.7f, 1.3f)]
		public float LegLength;

		/// <summary>Torso length multiplier (affects spine and chest bones). Stacks with Height.</summary>
		[Range(0.7f, 1.3f)]
		public float TorsoLength;

		/// <summary>Shoulder width multiplier (affects clavicle/shoulder bone offsets).</summary>
		[Range(0.7f, 1.3f)]
		public float ShoulderWidth;

		/// <summary>Head scale multiplier (affects head and neck bones).</summary>
		[Range(0.7f, 1.3f)]
		public float HeadScale;

		/// <summary>
		/// Returns the default human proportions (all 1.0).
		/// </summary>
		public static RaceProportions Default => new RaceProportions
		{
			Height = 1.0f,
			ArmLength = 1.0f,
			LegLength = 1.0f,
			TorsoLength = 1.0f,
			ShoulderWidth = 1.0f,
			HeadScale = 1.0f,
		};
	}

	/// <summary>
	/// ScriptableObject template for a race: models, attributes, starting abilities, inventory and
	/// equipment for playable races, and the naming data the name generator draws on for every race.
	/// A race with <see cref="Playable"/> off exists only to name things — it has no prefab or models,
	/// is skipped by character creation, and loads no placeholder model.
	/// </summary>
	[CreateAssetMenu(fileName = "New Race", menuName = "FishMMO/Character/Race/Race", order = 1)]
	public class RaceTemplate : CachedScriptableObject<RaceTemplate>, ICachedObject
	{
		/// <summary>
		/// True for a race players can create and NPCs can be built from. False for a naming-only race:
		/// character creation skips it and no placeholder model is loaded for it.
		/// </summary>
		[Tooltip("Off for a naming-only race: no prefab or models, hidden from character creation.")]
		public bool Playable = true;

		/// <summary>
		/// The prefab for the race.
		/// </summary>
		public GameObject Prefab;

		/// <summary>
		/// This model is loaded during ClientPreboot as a static model reference for this Race. It will be replaced by the player's selected model at runtime.
		/// </summary>
		public AssetReference PlaceholderModel;

		/// <summary>
		/// Gender-specific model references for this race. Use <see cref="CharacterGender.Unspecified"/> for generic/default models.
		/// </summary>
		public List<GenderedRaceModelSet> GenderedModels = new List<GenderedRaceModelSet>();

		/// <summary>
		/// Description of the race.
		/// </summary>
		public string Description;

		/// <summary>
		/// Default body proportions for this race. Applied via bone scaling on character spawn.
		/// Examples: Human (all 1.0), Dwarf (Height 0.75, LegLength 0.70, ArmLength 0.85),
		/// Elf (Height 1.05, LegLength 1.15, ArmLength 1.10).
		/// </summary>
		public RaceProportions DefaultProportions = RaceProportions.Default;

		/// <summary>
		/// Initial attribute database for the race.
		/// </summary>
		public CharacterAttributeTemplateDatabase InitialAttributes;

		/// <summary>
		/// The initial faction for the race.
		/// </summary>
		public FactionTemplate InitialFaction;

		/// <summary>
		/// List of starting abilities for the race.
		/// </summary>
		public List<AbilityTemplate> StartingAbilities = new List<AbilityTemplate>();

		/// <summary>
		/// List of starting inventory items for the race.
		/// </summary>
		public List<BaseItemTemplate> StartingInventoryItems = new List<BaseItemTemplate>();

		/// <summary>
		/// List of starting equipment for the race.
		/// </summary>
		public List<EquippableItemTemplate> StartingEquipment = new List<EquippableItemTemplate>();

		/// <summary>
		/// Phonology, cultures, titles, places and city suffixes the name generator uses for this race.
		/// </summary>
		/// <summary>
		/// Broad family the race belongs to — Humanoid, Undead, Aquatic, Elemental… Title pools
		/// serve categories, the Dashboard groups by it, and spawners can ask for a category. The
		/// asset's folder under Templates/Entity/Races mirrors it for browsing.
		/// </summary>
		[Header("Classification")]
		[Tooltip("Broad family: Humanoid, Giant, Fey, Beastfolk, Beast, Draconic, Aquatic, Undead, Construct, Elemental, Outsider, Plant, Aberration. Title pools serve categories.")]
		public string Category;

		[Header("Naming")]
		public RaceNamingData Naming = new RaceNamingData();

		/// <summary>
		/// Biomes this race realistically belongs in, weighted. Naming draws a home biome from
		/// these when none is given, and spawners can ask which races suit a place.
		/// </summary>
		[Header("Biomes")]
		[Tooltip("Where this race is at home. Weighted; empty means no preference.")]
		public List<BiomeAffinity> BiomeAffinities = new List<BiomeAffinity>();

		private string namingKey;

		/// <summary>
		/// The name of the race (from the ScriptableObject name).
		/// </summary>
		public string Name { get { return this.name; } }

		/// <summary>
		/// Key the name generator files this race under: the asset name lowercased with everything but
		/// letters removed, so "Wood Elf" is requested as "woodelf".
		/// </summary>
		public string NamingKey
		{
			get
			{
				if (namingKey == null)
				{
					namingKey = GeneratorUtility.NormalizeRace(this.name);
				}
				return namingKey;
			}
		}

		/// <summary>
		/// This race's affinity for a biome, or 0 when it has none.
		/// </summary>
		public float AffinityFor(int biomeID)
		{
			if (BiomeAffinities == null || biomeID == 0)
			{
				return 0f;
			}
			for (int i = 0; i < BiomeAffinities.Count; i++)
			{
				BiomeAffinity affinity = BiomeAffinities[i];
				if (affinity != null && affinity.BiomeID == biomeID)
				{
					return affinity.Weight;
				}
			}
			return 0f;
		}

		/// <summary>
		/// Draws one of this race's home biomes, weighted, with the given RNG; null when the race has
		/// no registered affinities. Deterministic for a seeded RNG, so both peers agree.
		/// </summary>
		public BiomeTemplate PickHomeBiome(DeterministicRNG rng)
		{
			if (BiomeAffinities == null || BiomeAffinities.Count == 0)
			{
				return null;
			}
			float total = 0f;
			for (int i = 0; i < BiomeAffinities.Count; i++)
			{
				BiomeAffinity affinity = BiomeAffinities[i];
				if (affinity != null && affinity.Weight > 0f && affinity.Biome != null)
				{
					total += affinity.Weight;
				}
			}
			if (total <= 0f)
			{
				return null;
			}
			float roll = (float)(rng.NextDouble() * total);
			for (int i = 0; i < BiomeAffinities.Count; i++)
			{
				BiomeAffinity affinity = BiomeAffinities[i];
				if (affinity == null || affinity.Weight <= 0f)
				{
					continue;
				}
				BiomeTemplate biome = affinity.Biome;
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

		/// <summary>
		/// Gets the model reference for the given index, or the placeholder if out of range or models are missing.
		/// </summary>
		/// <param name="index">The model index.</param>
		/// <returns>The asset reference for the model.</returns>
		public AssetReference GetModelReference(int index)
		{
			return GetModelReference(CharacterGender.Unspecified, index);
		}

		/// <summary>
		/// Gets the model reference for the given gender and index, or the placeholder if selection fails.
		/// </summary>
		/// <param name="gender">The selected character gender.</param>
		/// <param name="index">The model index.</param>
		/// <returns>The asset reference for the model.</returns>
		public AssetReference GetModelReference(CharacterGender gender, int index)
		{
			if (gender == CharacterGender.Unspecified)
			{
				return GetModelReferenceFromAllSets(index);
			}

			GenderedRaceModelSet modelSet = GetModelSet(gender);
			if (modelSet == null)
			{
				modelSet = GetModelSet(CharacterGender.Unspecified);
			}

			if (modelSet == null || modelSet.ModelReferences == null || modelSet.ModelReferences.Count == 0)
			{
				return PlaceholderModel;
			}

			if (index >= modelSet.ModelReferences.Count || index < 0)
			{
				return PlaceholderModel;
			}

			AssetReference modelReference = modelSet.ModelReferences[index];
			return modelReference == null ? PlaceholderModel : modelReference;
		}

		/// <summary>
		/// Gets the number of model references available for a gender, including fallback models.
		/// </summary>
		/// <param name="gender">The selected character gender.</param>
		/// <returns>The number of available model references.</returns>
		public int GetModelCount(CharacterGender gender)
		{
			if (gender == CharacterGender.Unspecified)
			{
				return GetAllModelCount();
			}

			GenderedRaceModelSet modelSet = GetModelSet(gender);
			if (modelSet == null)
			{
				modelSet = GetModelSet(CharacterGender.Unspecified);
			}

			return modelSet == null || modelSet.ModelReferences == null ? 0 : modelSet.ModelReferences.Count;
		}

		/// <summary>
		/// Gets model references for a specific gender.
		/// </summary>
		/// <param name="gender">The selected character gender.</param>
		/// <returns>The matching model list, or null when no matching gender list exists.</returns>
		public List<AssetReference> GetModels(CharacterGender gender)
		{
			GenderedRaceModelSet modelSet = GetModelSet(gender);
			return modelSet == null ? null : modelSet.ModelReferences;
		}

		/// <summary>
		/// Gets the display name for a model in the flattened gendered model list.
		/// </summary>
		/// <param name="index">The flattened model index.</param>
		/// <returns>The model asset name, or an empty string when unavailable.</returns>
		public string GetModelName(int index)
		{
			AssetReference modelReference = GetModelReference(index);
			return modelReference == null || modelReference.Asset == null ? string.Empty : modelReference.Asset.name;
		}

		/// <summary>
		/// Gets the model set matching a gender.
		/// </summary>
		/// <param name="gender">The selected character gender.</param>
		/// <returns>The matching model set, or null when unavailable.</returns>
		private GenderedRaceModelSet GetModelSet(CharacterGender gender)
		{
			if (GenderedModels == null)
			{
				return null;
			}

			for (int i = 0; i < GenderedModels.Count; i++)
			{
				GenderedRaceModelSet modelSet = GenderedModels[i];
				if (modelSet != null &&
					modelSet.Gender == gender &&
					modelSet.ModelReferences != null &&
					modelSet.ModelReferences.Count > 0)
				{
					return modelSet;
				}
			}

			return null;
		}

		/// <summary>
		/// Gets a model reference from all configured gendered model sets using a flattened index.
		/// </summary>
		/// <param name="index">The flattened model index.</param>
		/// <returns>The matching model reference, or the placeholder when unavailable.</returns>
		private AssetReference GetModelReferenceFromAllSets(int index)
		{
			if (GenderedModels == null || GenderedModels.Count == 0 || index < 0)
			{
				return PlaceholderModel;
			}

			int currentIndex = 0;
			for (int i = 0; i < GenderedModels.Count; i++)
			{
				GenderedRaceModelSet modelSet = GenderedModels[i];
				if (modelSet == null || modelSet.ModelReferences == null || modelSet.ModelReferences.Count == 0)
				{
					continue;
				}

				for (int modelIndex = 0; modelIndex < modelSet.ModelReferences.Count; modelIndex++)
				{
					if (currentIndex == index)
					{
						AssetReference modelReference = modelSet.ModelReferences[modelIndex];
						return modelReference == null ? PlaceholderModel : modelReference;
					}
					currentIndex++;
				}
			}

			return PlaceholderModel;
		}

		/// <summary>
		/// Gets the total number of models across all configured gendered model sets.
		/// </summary>
		/// <returns>The flattened model count.</returns>
		private int GetAllModelCount()
		{
			if (GenderedModels == null)
			{
				return 0;
			}

			int count = 0;
			for (int i = 0; i < GenderedModels.Count; i++)
			{
				GenderedRaceModelSet modelSet = GenderedModels[i];
				if (modelSet != null && modelSet.ModelReferences != null)
				{
					count += modelSet.ModelReferences.Count;
				}
			}

			return count;
		}

		/// <summary>
		/// Called when the race is loaded. Loads the placeholder model.
		/// </summary>
		/// <param name="typeName">The type name of the resource.</param>
		/// <param name="resourceName">The resource name.</param>
		/// <param name="resourceID">The resource ID.</param>
		public override void OnLoad(string typeName, string resourceName, int resourceID)
		{
			base.OnLoad(typeName, resourceName, resourceID);

			namingKey = null;
			RaceRegistry.Register(this);

#if !UNITY_SERVER
			// A naming-only race has no model to preload, and would only warn that it has none.
			if (Playable)
			{
				LoadPlaceholderModel();
			}
#endif
		}

		/// <summary>
		/// Called when the race is unloaded. Unloads the placeholder model.
		/// </summary>
		/// <param name="typeName">The type name of the resource.</param>
		/// <param name="resourceName">The resource name.</param>
		/// <param name="resourceID">The resource ID.</param>
		public override void OnUnload(string typeName, string resourceName, int resourceID)
		{
			RaceRegistry.Unregister(this);

#if !UNITY_SERVER
			if (Playable)
			{
				UnloadPlaceholderModel();
			}
#endif

			base.OnUnload(typeName, resourceName, resourceID);
		}

		/// <summary>
		/// Loads the placeholder model for the race using Addressables.
		/// </summary>
		public void LoadPlaceholderModel()
		{
			if (PlaceholderModel == null)
			{
				Log.Warning("RaceTemplate", $"'{Name}' has no valid PlaceholderModel assigned to load.");
				return;
			}
			AddressableLoadProcessor.LoadPrefabAsync(PlaceholderModel, null);
		}

		/// <summary>
		/// Unloads the placeholder model for the race using Addressables.
		/// </summary>
		public void UnloadPlaceholderModel()
		{
			AddressableLoadProcessor.UnloadPrefab(PlaceholderModel);
		}
	}
}