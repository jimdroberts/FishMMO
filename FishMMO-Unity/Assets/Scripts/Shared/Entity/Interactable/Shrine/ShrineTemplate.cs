using UnityEngine;

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
		/// Optional icon for the shrine in the UI.
		/// </summary>
		public Sprite Icon;

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
	}
}