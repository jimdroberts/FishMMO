using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Race", menuName = "FishMMO/Character/Race/Race", order = 1)]
	public class RaceTemplate : CachedScriptableObject<RaceTemplate>, ICachedObject
	{
		public GameObject Prefab;
		/// <summary>
		/// This model is loaded during ClientPreboot as a static model reference for this Race. It will be replaced by the players selected model at runtime.
		/// </summary>
		public AssetReference PlaceholderModel;
		/// <summary>
		/// The real character model references.
		/// </summary>
		public List<AssetReference> Models;
		public string Description;
		public CharacterAttributeTemplateDatabase InitialAttributes;
		public FactionTemplate InitialFaction;
		public List<AbilityTemplate> StartingAbilities = new List<AbilityTemplate>();
		public List<BaseItemTemplate> StartingInventoryItems = new List<BaseItemTemplate>();
		public List<EquippableItemTemplate> StartingEquipment = new List<EquippableItemTemplate>();

		public string Name { get { return this.name; } }

		public AssetReference GetModelReference(int index)
		{
			if (Models == null || Models.Count == 0)
			{
				return PlaceholderModel;
			}

			if (index >= Models.Count || index < 0)
			{
				return Models[0];
			}
			return Models[index];
		}

		public override void OnLoad(string typeName, string resourceName, int resourceID)
		{
			base.OnLoad(typeName, resourceName, resourceID);

			LoadPlaceholderModel();
		}

		public override void OnUnload(string typeName, string resourceName, int resourceID)
		{
			UnloadPlaceholderModel();

			base.OnUnload(typeName, resourceName, resourceID);
		}

		public void LoadPlaceholderModel()
		{
			if (PlaceholderModel == null)
			{
				Log.Warning("RaceTemplate", $"'{Name}' has no valid PlaceholderModel assigned to load.");
				return;
			}

			AddressableLoadProcessor.LoadPrefabAsync(PlaceholderModel, null);
		}

		public void UnloadPlaceholderModel()
		{
			AddressableLoadProcessor.UnloadPrefab(PlaceholderModel);
		}
	}
}