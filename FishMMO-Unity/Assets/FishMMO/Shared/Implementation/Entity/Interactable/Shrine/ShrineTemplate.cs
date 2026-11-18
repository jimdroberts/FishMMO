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

		[Header("Cooldown")]
		/// <summary>
		/// Seconds a character must wait between uses of one shrine. 0 disables the cooldown.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Per character, per shrine — not shared. Two players never queue for the same stone, and
		/// using one shrine does not lock a player out of another.
		/// </para>
		/// <para>
		/// Without this a shrine was a full heal plus <see cref="BuffStackCount"/> stacks once per
		/// second, which is all the global interaction debounce allows and nothing else stood in
		/// the way. Held on the server only: the cooldown table lives on the
		/// <see cref="Shrine"/> instance and is never replicated, so a client cannot shorten it.
		/// </para>
		/// </remarks>
		[Tooltip("Seconds one character must wait between uses of this shrine. 0 = no cooldown.")]
		[Min(0.0f)]
		public float CooldownSeconds = 300.0f;

		/// <summary>
		/// Whether the shrine may be used while the character is in combat.
		/// </summary>
		/// <remarks>
		/// Off by default. A shrine is a restore point between fights; a full heal available
		/// mid-fight is a combat mechanic, and one worth opting into deliberately rather than
		/// inheriting by omission.
		/// </remarks>
		[Tooltip("Allow this shrine to be used while in combat.")]
		public bool UsableInCombat = false;

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