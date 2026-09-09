using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract base class for ability events. Extends <see cref="Trigger"/> to provide ECA-driven conditions and actions.
	/// Ability requirements (resources, faction, archetype, attributes) are defined as ECA conditions on the inherited
	/// <see cref="Trigger.Conditions"/> list or on the parent <see cref="BaseAbilityTemplate.ActivationConditions"/> list.
	/// </summary>
	public abstract class AbilityEvent : Trigger, ITooltip
	{
		/// <summary>
		/// The icon representing the ability event (set in the inspector).
		/// </summary>
		[SerializeField]
		private Sprite icon;

		/// <summary>
		/// Time required to activate the event (in seconds). Aggregated into the runtime ability.
		/// </summary>
		public float ActivationTime;

		/// <summary>
		/// Lifetime of the event effect (in seconds). Aggregated into the runtime ability.
		/// </summary>
		public float LifeTime;

		/// <summary>
		/// Speed of the event effect (units per second). Aggregated into the runtime ability.
		/// </summary>
		public float Speed;

		/// <summary>
		/// Cooldown time for the event (in seconds). Aggregated into the runtime ability.
		/// </summary>
		public float Cooldown;

		/// <summary>
		/// Crafting price of the event (in-game currency cost to add this event during ability crafting).
		/// </summary>
		public int Price;

		/// <summary>
		/// The name of the event (from the ScriptableObject name).
		/// </summary>
		public string Name { get { return this.name; } }

		/// <summary>
		/// The icon representing the event (property accessor).
		/// </summary>
		public Sprite Icon { get { return this.icon; } }

		/// <summary>
		/// Describes what this effect is and what adding it to an ability would do.
		/// </summary>
		/// <remarks>
		/// Written as the CHANGE it represents rather than as absolute numbers, because that is
		/// what the player is deciding about: an effect on its own has no cast time, it adds one.
		/// Signs are therefore always shown, and the tone says whether the change helps.
		/// </remarks>
		/// <param name="content">The content being assembled.</param>
		public virtual void BuildTooltip(TooltipContent content)
		{
			content.Icon = Icon;
			content.AddTitle(Name);
			content.AddSubtitle($"Ability Effect · {AbilitySummary.EventCategory(this)}");

			AddModifier(content, "Cast Time", ActivationTime, "s", moreIsBetter: false, TooltipPriority.Stats);
			AddModifier(content, "Cooldown", Cooldown, "s", moreIsBetter: false, TooltipPriority.Stats + 1);
			AddModifier(content, "Speed", Speed, "m/s", moreIsBetter: true, TooltipPriority.Stats + 2);
			AddModifier(content, "Duration", LifeTime, "s", moreIsBetter: true, TooltipPriority.Stats + 3);

			BaseAbilityTemplate.AppendConditions(content, Conditions);

			string targeting = TargetSelector?.GetTooltipContribution();
			if (!string.IsNullOrWhiteSpace(targeting))
			{
				content.AddHeader("Targeting", TooltipPriority.Targeting);
				content.AddEffect(targeting, TooltipPriority.Targeting + 1);
			}

			if (OnConditionsMetActions != null)
			{
				int order = 1;
				foreach (BaseAction action in OnConditionsMetActions)
				{
					string effect = action?.GetTooltipContribution();
					if (string.IsNullOrWhiteSpace(effect))
					{
						continue;
					}
					if (order == 1)
					{
						content.AddHeader("Effects", TooltipPriority.Effects);
					}
					content.AddEffect(effect, TooltipPriority.Effects + order++);
				}
			}

			if (Price > 0)
			{
				content.AddStat("Craft Cost", Price.ToString(), TooltipPriority.Price);
			}
		}

		/// <summary>Writes one modifier row, or nothing when this effect does not change that stat.</summary>
		private static void AddModifier(TooltipContent content, string label, float value, string suffix, bool moreIsBetter, int priority)
		{
			if (Mathf.Abs(value) < 0.0005f)
			{
				return;
			}

			string formatted = value > 0.0f ? $"+{value:0.##}{suffix}" : $"{value:0.##}{suffix}";
			content.AddStat(label, formatted, priority, tone: AbilitySummary.ToneForDelta(value, moreIsBetter));
		}

		/// <summary>
		/// Determines whether a condition should be evaluated when this ability event executes.
		/// Resource cost conditions are aggregated and processed by the owning ability during activation,
		/// so event execution skips them to avoid checking already-consumed resources again.
		/// </summary>
		/// <param name="condition">The condition being considered.</param>
		/// <returns>True when the condition should be evaluated during event execution; otherwise, false.</returns>
		protected override bool ShouldEvaluateCondition(BaseCondition condition)
		{
			return !(condition is IResourceCost);
		}
	}
}