using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// What an ability actually <em>does</em>, derived from the ECA actions attached to its events.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A flags enum rather than a single value, because one ability routinely does several things —
	/// a nuke that also applies a burn is Damage and Debuff; a heal that cleanses is Heal and
	/// Dispel. Collapsing that to one category would force an arbitrary choice about which half of
	/// the ability the AI is allowed to notice.
	/// </para>
	/// <para>
	/// Distinct from <see cref="AbilityCategory"/>, which describes <em>delivery</em> — melee
	/// versus ranged versus area. The two are orthogonal: a ranged AOE can be a heal or a nuke, and
	/// an NPC needs both answers. Delivery decides where it stands; intent decides who it points at.
	/// </para>
	/// </remarks>
	[Flags]
	public enum AIAbilityIntent
	{
		/// <summary>Nothing recognisable was found in the ability's action graph.</summary>
		None = 0,

		/// <summary>Reduces a target's health, directly or over time.</summary>
		Damage = 1 << 0,

		/// <summary>Restores a target's health, directly or over time.</summary>
		Heal = 1 << 1,

		/// <summary>Applies a beneficial effect to a target.</summary>
		Buff = 1 << 2,

		/// <summary>Applies a detrimental effect to a target.</summary>
		Debuff = 1 << 3,

		/// <summary>Prevents a target from acting or moves it against its will.</summary>
		Control = 1 << 4,

		/// <summary>Removes existing effects from a target.</summary>
		Dispel = 1 << 5,

		/// <summary>Returns a dead target to life.</summary>
		Revive = 1 << 6,

		/// <summary>Manipulates an NPC's threat table.</summary>
		Threat = 1 << 7,

		/// <summary>Brings another entity into the world.</summary>
		Summon = 1 << 8,

		/// <summary>Recognised, but none of the combat roles above — movement, interaction, and so on.</summary>
		Utility = 1 << 9,

		/// <summary>Anything an NPC would point at an enemy.</summary>
		Offensive = Damage | Debuff | Control | Threat,

		/// <summary>Anything an NPC would point at a friend, including itself.</summary>
		Supportive = Heal | Buff | Revive,
	}

	/// <summary>
	/// Helpers for reasoning about an ability's intent.
	/// </summary>
	public static class AIAbilityIntentExtensions
	{
		/// <summary>
		/// True when any of the given flags are present.
		/// </summary>
		/// <param name="intent">The intent to test.</param>
		/// <param name="flags">Flags to look for.</param>
		/// <returns>True if at least one flag is set.</returns>
		public static bool HasAny(this AIAbilityIntent intent, AIAbilityIntent flags)
		{
			return (intent & flags) != AIAbilityIntent.None;
		}

		/// <summary>
		/// True when this ability is something to point at an enemy.
		/// </summary>
		/// <param name="intent">The intent to test.</param>
		/// <returns>True if the ability is offensive.</returns>
		public static bool IsOffensive(this AIAbilityIntent intent)
		{
			return intent.HasAny(AIAbilityIntent.Offensive);
		}

		/// <summary>
		/// True when this ability is something to point at a friend.
		/// </summary>
		/// <remarks>
		/// <see cref="AIAbilityIntent.Dispel"/> is deliberately absent: a dispel that strips buffs
		/// is offensive and one that strips debuffs is supportive, and which it is cannot be read
		/// from the flag alone. <see cref="AIAbilityClassifier"/> resolves that at classification
		/// time from the dispel action's own configuration and sets the appropriate flag alongside.
		/// </remarks>
		/// <param name="intent">The intent to test.</param>
		/// <returns>True if the ability is supportive.</returns>
		public static bool IsSupportive(this AIAbilityIntent intent)
		{
			return intent.HasAny(AIAbilityIntent.Supportive);
		}
	}
}
