using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Race", menuName = "FishMMO/Character/Race/Race", order = 1)]
	public class RaceTemplate : CachedScriptableObject<RaceTemplate>, ICachedObject
	{
		public GameObject Prefab;
		/// <summary>
		/// This model is loaded during ClientPreboot as a static model reference for this Race. It will be replaced by the players selected model at runtime.
		/// </summary>
		public AssetReference StandardModel;
		/// <summary>
		/// The real character model references.
		/// </summary>
		public List<AssetReference> Models;
		public string Description;
		public CharacterAttributeTemplateDatabase InitialAttributes;
		public FactionTemplate InitialFaction;

		public string Name { get { return this.name; } }

		public AssetReference GetModelReference(int index)
		{
			if (Models == null || Models.Count == 0)
			{
				return StandardModel;
			}

			if (index >= Models.Count || index < 0)
			{
				return Models[0];
			}
			return Models[index];
		}
	}
}