using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace FishMMO.Shared
{
	/// <summary>
	/// Template defining a gathering node's loot table and interaction parameters.
	/// Each gathering interaction rolls the drop table and grants items to the player.
	/// </summary>
	[CreateAssetMenu(fileName = "New Gathering Node", menuName = "FishMMO/Interactable/Gathering Node", order = 1)]
	public class GatheringNodeTemplate : CachedScriptableObject<GatheringNodeTemplate>, ICachedObject
	{
		/// <summary>
		/// Addressable reference to the icon sprite for this gathering node.
		/// </summary>
		public AssetReferenceSprite IconReference;

		/// <summary>
		/// The loaded icon sprite. Only available on the client after OnLoad completes.
		/// </summary>
		[System.NonSerialized]
		private Sprite loadedIcon;

		/// <summary>
		/// The icon for this gathering node (loaded at runtime on client).
		/// </summary>
		public Sprite Icon { get { return this.loadedIcon; } }

		/// <summary>
		/// Description displayed in tooltips or UI.
		/// </summary>
		[TextArea(2, 4)]
		public string Description;

		/// <summary>
		/// The list of possible drops when gathering from this node.
		/// </summary>
		public List<GatheringDrop> Drops;

		/// <summary>
		/// Number of successful gathers before the node is depleted and despawns.
		/// </summary>
		[Min(1)]
		public int MaxUses = 3;

		/// <summary>
		/// Time in seconds the gathering action takes. Used by the client for a progress bar.
		/// Set to 0 for instant gathering.
		/// </summary>
		[Min(0f)]
		public float GatherTimeSeconds = 2.0f;

		/// <summary>
		/// The display name of this gathering node template.
		/// </summary>
		public string Name { get { return this.name; } }

		/// <summary>
		/// Called when the gathering node template is loaded into cache. Loads the icon on the client.
		/// </summary>
		public override void OnLoad(string typeName, string resourceName, int resourceID)
		{
			base.OnLoad(typeName, resourceName, resourceID);

			if (typeName != nameof(GatheringNodeTemplate))
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
		/// Called when the gathering node template is unloaded from cache. Releases the icon on the client.
		/// </summary>
		public override void OnUnload(string typeName, string resourceName, int resourceID)
		{
			if (typeName == nameof(GatheringNodeTemplate))
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