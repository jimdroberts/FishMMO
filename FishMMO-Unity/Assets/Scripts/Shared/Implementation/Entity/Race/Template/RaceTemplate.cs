using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// ScriptableObject template for defining a playable race, including models, attributes, starting abilities, inventory, and equipment.
	/// </summary>
	[CreateAssetMenu(fileName = "New Race", menuName = "FishMMO/Character/Race/Race", order = 1)]
	public class RaceTemplate : CachedScriptableObject<RaceTemplate>, ICachedObject
	{
		/// <summary>
		/// The prefab for the race.
		/// </summary>
		public GameObject Prefab;

		/// <summary>
		/// This model is loaded during ClientPreboot as a static model reference for this Race. It will be replaced by the player's selected model at runtime.
		/// </summary>
		public AssetReference PlaceholderModel;

		/// <summary>
		/// The real character model references for this race.
		/// </summary>
		public List<AssetReference> Models;

		/// <summary>
		/// Description of the race.
		/// </summary>
		public string Description;

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
		/// The name of the race (from the ScriptableObject name).
		/// </summary>
		public string Name { get { return this.name; } }

		/// <summary>
		/// Gets the model reference for the given index, or the placeholder if out of range or models are missing.
		/// </summary>
		/// <param name="index">The model index.</param>
		/// <returns>The asset reference for the model.</returns>
		public AssetReference GetModelReference(int index)
		{
			if (Models == null || Models.Count == 0)
			{
				return PlaceholderModel;
			}

			// If index is out of range, return the first model as a fallback.
			if (index >= Models.Count || index < 0)
			{
				return Models[0];
			}
			return Models[index];
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
			LoadPlaceholderModel();
		}

		/// <summary>
		/// Called when the race is unloaded. Unloads the placeholder model.
		/// </summary>
		/// <param name="typeName">The type name of the resource.</param>
		/// <param name="resourceName">The resource name.</param>
		/// <param name="resourceID">The resource ID.</param>
		public override void OnUnload(string typeName, string resourceName, int resourceID)
		{
			UnloadPlaceholderModel();
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