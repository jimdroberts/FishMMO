using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace FishMMO.Shared
{
	/// <summary>
	/// ScriptableObject template representing an achievement, including icon, category, description, and tiers.
	/// </summary>
	[CreateAssetMenu(fileName = "New Achievement", menuName = "FishMMO/Character/Achievement/Achievement", order = 1)]
	public class AchievementTemplate : CachedScriptableObject<AchievementTemplate>, ICachedObject
	{
		/// <summary>
		/// Addressable reference to the icon sprite for this achievement.
		/// </summary>
		public AssetReferenceSprite IconReference;

		/// <summary>
		/// The loaded icon sprite. Only available on the client after OnLoad completes.
		/// </summary>
		[System.NonSerialized]
		private Sprite loadedIcon;

		/// <summary>
		/// The icon for this achievement (loaded at runtime on client).
		/// </summary>
		public Sprite Icon { get { return this.loadedIcon; } }

		/// <summary>
		/// The category this achievement belongs to (e.g., Combat, Exploration).
		/// </summary>
		public AchievementCategory Category;

		/// <summary>
		/// The description of the achievement, shown to the player.
		/// </summary>
		public string Description;

		/// <summary>
		/// The list of tiers for this achievement, each representing a milestone or level.
		/// </summary>
		public List<AchievementTier> Tiers;

		/// <summary>
		/// The name of this achievement template (from the ScriptableObject's name).
		/// </summary>
		public string Name { get { return this.name; } }

		/// <summary>
		/// Called when the achievement template is loaded into cache. Loads the icon on the client.
		/// </summary>
		public override void OnLoad(string typeName, string resourceName, int resourceID)
		{
			base.OnLoad(typeName, resourceName, resourceID);

			if (typeName != nameof(AchievementTemplate))
				return;

#if !UNITY_SERVER
			if (IconReference != null && IconReference.RuntimeKeyIsValid())
			{
				IconReference.LoadAssetAsync<Sprite>().Completed += (handle) =>
				{
					if (handle.Status == AsyncOperationStatus.Succeeded)
						loadedIcon = handle.Result;
				};
			}
#endif
		}

		/// <summary>
		/// Called when the achievement template is unloaded from cache. Releases the icon on the client.
		/// </summary>
		public override void OnUnload(string typeName, string resourceName, int resourceID)
		{
			if (typeName == nameof(AchievementTemplate))
			{
#if !UNITY_SERVER
				if (IconReference != null && IconReference.IsValid())
				{
					IconReference.ReleaseAsset();
				}
				loadedIcon = null;
#endif
			}

			base.OnUnload(typeName, resourceName, resourceID);
		}
	}
}