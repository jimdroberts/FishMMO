using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;
using FishMMO.Shared.Weather;

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
		/// Rules scaling the ability's numbers by the weather where it is cast. Aggregated
		/// with the parent template's.
		/// </summary>
		/// <remarks>
		/// An effect added to an ability brings its own weather rules, the same way it brings its
		/// own cast time — so a Lightning Bolt effect can be the thing that makes an ability hit
		/// harder in a storm, whatever ability it is crafted onto.
		/// </remarks>
		[Tooltip("Scale the ability's numbers by the weather where it is cast. Empty: this effect does not care about the weather.")]
		[SerializeReference]
		[UnityEngine.Serialization.FormerlySerializedAs("WeatherModifiers")]
		public List<IAbilityModifier> Modifiers = new List<IAbilityModifier>();

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
		/// Whether this effect does anything on an ability built from <paramref name="template"/>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The one statement of which effects go on which abilities. Crafting, premade offers, the
		/// craft window and <see cref="Ability.AddEvent"/> all ask it, so an effect the server
		/// refuses is one the window never offers and a stale database row can never smuggle in.
		/// </para>
		/// <para>
		/// The default refuses summon spells. A <see cref="PetAbilityTemplate"/> spawns no ability
		/// object, so the tick, hit, spawn and destroy phases these effects run in never happen on
		/// one. Crafting one onto a summon used to be accepted all the same, and all it did was
		/// fold the effect's cast time, cooldown and cost into the spell: a player paid for a
		/// fireball and got a slower summon, or found a negative cast-time effect made a faster one.
		/// <see cref="AbilityOnSummonEvent"/> is the effect that belongs on a summon, and inverts this.
		/// </para>
		/// </remarks>
		/// <param name="template">The base ability the effect would be added to.</param>
		/// <returns>True when the effect can be part of that ability.</returns>
		public virtual bool IsCompatibleWith(AbilityTemplate template)
		{
			return template != null && !(template is PetAbilityTemplate);
		}

		/// <summary>
		/// Effect lines this event contributes beyond the wording of its own actions.
		/// </summary>
		/// <remarks>
		/// For effects whose result is data rather than an action, such as the abilities an
		/// <see cref="AbilityOnSummonEvent"/> teaches the pet. Shared by this event's own tooltip
		/// and by <see cref="AbilitySummary"/>, so the effect reads the same in both.
		/// </remarks>
		/// <param name="into">The list to append to.</param>
		public virtual void AppendEffectDescriptions(List<string> into)
		{
		}

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

			List<string> effects = new List<string>();
			if (OnConditionsMetActions != null)
			{
				foreach (BaseAction action in OnConditionsMetActions)
				{
					string effect = action?.GetTooltipContribution();
					if (!string.IsNullOrWhiteSpace(effect))
					{
						effects.Add(effect);
					}
				}
			}
			AppendEffectDescriptions(effects);

			if (effects.Count > 0)
			{
				content.AddHeader("Effects", TooltipPriority.Effects);
				for (int i = 0; i < effects.Count; ++i)
				{
					content.AddEffect(effects[i], TooltipPriority.Effects + 1 + i);
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