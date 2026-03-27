using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace FishMMO.Shared
{
	/// <summary>
	/// Template defining a shrine's healing and buff effects.
	/// Shrines can heal health, mana, or both, and optionally apply a buff on interaction.
	/// </summary>
	[CreateAssetMenu(fileName = "New Shrine", menuName = "FishMMO/Interactable/Shrine", order = 1)]
	public class ShrineTemplate : CachedScriptableObject<ShrineTemplate>, ICachedObject
	{
		/// <summary>
		/// Addressable reference to the icon sprite for this shrine.
		/// </summary>
		public AssetReferenceSprite IconReference;

		/// <summary>
		/// The loaded icon sprite. Only available on the client after OnLoad completes.
		/// </summary>
		[System.NonSerialized]
		private Sprite loadedIcon;

		/// <summary>
		/// The icon for this shrine (loaded at runtime on client).
		/// </summary>
		public Sprite Icon { get { return this.loadedIcon; } }

		/// <summary>
		/// Description displayed in tooltips or UI.
		/// </summary>
		[TextArea(2, 4)]
		public string Description;

		[Header("Health")]
		/// <summary>
		/// When true, the shrine heals health on interaction.
		/// </summary>
		public bool HealHealth;

		/// <summary>
		/// Percentage of max health to restore (0.0–1.0). Only used when <see cref="HealHealth"/> is true.
		/// </summary>
		[Range(0f, 1f)]
		public float HealthHealPercent = 1.0f;

		[Header("Mana")]
		/// <summary>
		/// When true, the shrine heals mana on interaction.
		/// </summary>
		public bool HealMana;

		/// <summary>
		/// Percentage of max mana to restore (0.0–1.0). Only used when <see cref="HealMana"/> is true.
		/// </summary>
		[Range(0f, 1f)]
		public float ManaHealPercent = 1.0f;

		[Header("Buff")]
		/// <summary>
		/// Optional buff to apply on interaction. Null means no buff is applied.
		/// </summary>
		public BaseBuffTemplate Buff;

		/// <summary>
		/// Number of buff stacks to apply. Only used when <see cref="Buff"/> is not null.
		/// </summary>
		[Min(1)]
		public int BuffStackCount = 1;

		/// <summary>
		/// The display name of this shrine template.
		/// </summary>
		public string Name { get { return this.name; } }

		/// <summary>
		/// Called when the shrine template is loaded into cache. Loads the icon on the client.
		/// </summary>
		public override void OnLoad(string typeName, string resourceName, int resourceID)
		{
			base.OnLoad(typeName, resourceName, resourceID);

			if (typeName != nameof(ShrineTemplate))
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
		/// Called when the shrine template is unloaded from cache. Releases the icon on the client.
		/// </summary>
		public override void OnUnload(string typeName, string resourceName, int resourceID)
		{
			if (typeName == nameof(ShrineTemplate))
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