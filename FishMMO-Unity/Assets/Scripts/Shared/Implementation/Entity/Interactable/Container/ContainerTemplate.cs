using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace FishMMO.Shared
{
	/// <summary>
	/// ScriptableObject template defining configuration for container interactables (chests, wardrobes, etc.).
	/// </summary>
	[CreateAssetMenu(fileName = "New Container", menuName = "FishMMO/Character/Container/Container", order = 1)]
	public class ContainerTemplate : CachedScriptableObject<ContainerTemplate>, ICachedObject
	{
		/// <summary>
		/// Addressable reference to the icon sprite for this container.
		/// </summary>
		public AssetReferenceSprite IconReference;

		/// <summary>
		/// The loaded icon sprite. Only available on the client after OnLoad completes.
		/// </summary>
		[System.NonSerialized]
		private Sprite loadedIcon;

		/// <summary>
		/// The icon for this container (loaded at runtime on client).
		/// </summary>
		public Sprite Icon { get { return this.loadedIcon; } }
		public string Description;

		/// <summary>
		/// Number of item slots this container provides.
		/// </summary>
		public int SlotCount = 10;

		/// <summary>
		/// When true, the container despawns after all items have been taken.
		/// </summary>
		public bool DespawnWhenEmpty;

		public string Name { get { return this.name; } }

		/// <summary>
		/// Called when the container template is loaded into cache. Loads the icon on the client.
		/// </summary>
		public override void OnLoad(string typeName, string resourceName, int resourceID)
		{
			base.OnLoad(typeName, resourceName, resourceID);

			if (typeName != nameof(ContainerTemplate))
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
		/// Called when the container template is unloaded from cache. Releases the icon on the client.
		/// </summary>
		public override void OnUnload(string typeName, string resourceName, int resourceID)
		{
			if (typeName == nameof(ContainerTemplate))
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