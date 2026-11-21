using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// ScriptableObject for overriding the ability type in an event.
	/// </summary>
	[CreateAssetMenu(fileName = "New Ability Type Override Event", menuName = "FishMMO/Character/Ability/Override Event/Ability Type Override", order = 1)]
	public sealed class AbilityTypeOverrideEventType : BaseAbilityTemplate
	{
		/// <summary>
		/// The ability type to override with this event.
		/// </summary>
		public AbilityType OverrideAbilityType;

		/// <summary>
		/// Describes what the override does, which is the one thing it is for.
		/// </summary>
		/// <remarks>
		/// It inherited <see cref="BaseAbilityTemplate.BuildTooltip"/>, which describes stats and
		/// costs — so an override's tooltip listed a cooldown and never mentioned that it changes
		/// the ability's type, the only reason to craft one.
		/// </remarks>
		/// <param name="content">The content being assembled.</param>
		public override void BuildTooltip(TooltipContent content)
		{
			base.BuildTooltip(content);
			content.AddSubtitle("Ability Type Override");
			content.AddEffect($"Casts the ability as {OverrideAbilityType}", TooltipPriority.Effects);
		}
	}
}