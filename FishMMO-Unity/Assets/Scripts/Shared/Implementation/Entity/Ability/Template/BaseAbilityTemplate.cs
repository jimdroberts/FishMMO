using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract base ScriptableObject for ability templates, providing common fields, activation conditions, and tooltip logic.
	/// Ability requirements (resources, faction, archetype, attributes) are defined as ECA conditions
	/// on the <see cref="ActivationConditions"/> list rather than as hardcoded fields.
	/// </summary>
	public abstract class BaseAbilityTemplate : CachedScriptableObject<BaseAbilityTemplate>, ITooltip
	{
		/// <summary>
		/// The icon representing the ability.
		/// </summary>
		public Sprite icon;

		/// <summary>
		/// Description of the ability.
		/// </summary>
		public string Description;

		/// <summary>
		/// Time required to activate the ability.
		/// </summary>
		public float ActivationTime;

		/// <summary>
		/// Lifetime of the ability effect.
		/// </summary>
		public float LifeTime;

		/// <summary>
		/// Speed of the ability effect.
		/// </summary>
		public float Speed;

		/// <summary>
		/// Cooldown time for the ability.
		/// </summary>
		public float Cooldown;

		/// <summary>
		/// Crafting price of the ability (in-game currency).
		/// </summary>
		public int Price;

		/// <summary>
		/// Conditions that must be met to activate this ability.
		/// Use ECA conditions such as <see cref="HasResourceCondition"/>, <see cref="HasRequiredAttribute"/>,
		/// <see cref="HasFactionCondition"/>, and <see cref="IsArchetypeCondition"/> to define activation requirements.
		/// Resource conditions implementing <see cref="IResourceCost"/> are aggregated for total cost validation.
		/// </summary>
		[Header("Activation Conditions")]
		[Tooltip("Conditions that must be met to activate this ability (resource costs, attribute requirements, faction, archetype, etc.).")]
		[SerializeReference, SubclassSelector]
		public List<BaseCondition> ActivationConditions = new List<BaseCondition>();

		/// <summary>
		/// The name of the ability (from the ScriptableObject name).
		/// </summary>
		public string Name { get { return this.name; } }

		/// <summary>
		/// The icon representing the ability (property accessor).
		/// </summary>
		public Sprite Icon { get { return this.icon; } }

		/// <summary>
		/// Returns the tooltip string for the ability.
		/// </summary>
		public virtual string Tooltip()
		{
			using (var builder = new TooltipBuilder())
			{
				BuildTooltip(builder);
				return builder.Build();
			}
		}

		/// <summary>
		/// Populates the tooltip builder with this ability's tooltip lines.
		/// Includes name, description, stats, conditions, and price.
		/// When a combine list is provided, aggregates stats and descriptions from combined events/templates.
		/// </summary>
		/// <param name="builder">The tooltip builder to populate.</param>
		/// <param name="combineList">Optional list of tooltips to combine (for ability crafting).</param>
		public virtual void BuildTooltip(TooltipBuilder builder, List<ITooltip> combineList = null)
		{
			builder.AddLine(Name, 0, TooltipColors.Title, false, "140%");

			if (!string.IsNullOrWhiteSpace(Description))
			{
				builder.AddLine(Description, 10, TooltipColors.Label);
			}

			float activationTime = ActivationTime;
			float lifeTime = LifeTime;
			float speed = Speed;
			float cooldown = Cooldown;
			float price = Price;

			if (combineList != null && combineList.Count > 0)
			{
				foreach (ITooltip tooltip in combineList)
				{
					if (tooltip == null) continue;

					if (tooltip is AbilityEvent abilityEvent)
					{
						builder.AddLine("Ability Event: " + abilityEvent.Name, 15, TooltipColors.Label);
						activationTime += abilityEvent.ActivationTime;
						lifeTime += abilityEvent.LifeTime;
						speed += abilityEvent.Speed;
						cooldown += abilityEvent.Cooldown;
						price += abilityEvent.Price;
					}
					else if (tooltip is BaseAbilityTemplate template)
					{
						if (!string.IsNullOrWhiteSpace(template.Description))
						{
							builder.AddLine(template.Description, 15, TooltipColors.Label);
						}
						activationTime += template.ActivationTime;
						lifeTime += template.LifeTime;
						speed += template.Speed;
						cooldown += template.Cooldown;
						price += template.Price;
					}
				}
			}

			if (activationTime > 0) builder.AddLine($"Activation Time: {activationTime}s", 30, TooltipColors.Stat);
			if (lifeTime > 0) builder.AddLine($"Life Time: {lifeTime}s", 31, TooltipColors.Stat);
			if (speed > 0) builder.AddLine($"Speed: {speed}m/s", 32, TooltipColors.Stat);
			if (speed > 0 && lifeTime > 0) builder.AddLine($"Range: {speed * lifeTime}m", 33, TooltipColors.Stat);
			if (cooldown > 0) builder.AddLine($"Cooldown: {cooldown}s", 34, TooltipColors.Stat);

			// Append resource cost and requirement sections from all condition sources.
			bool hasResources = false;
			bool hasRequirements = false;

			AppendConditionLines(builder, ActivationConditions, ref hasResources, ref hasRequirements);

			if (combineList != null)
			{
				foreach (ITooltip tooltip in combineList)
				{
					if (tooltip is AbilityEvent abilityEvent && abilityEvent.Conditions != null)
					{
						AppendConditionLines(builder, abilityEvent.Conditions, ref hasResources, ref hasRequirements);
					}
					else if (tooltip is BaseAbilityTemplate template && template.ActivationConditions != null)
					{
						AppendConditionLines(builder, template.ActivationConditions, ref hasResources, ref hasRequirements);
					}
				}
			}

			if (price > 0)
			{
				builder.AddLine($"Price: {price}", 80, TooltipColors.Stat);
			}
		}

		/// <summary>
		/// Appends resource cost and requirement tooltip lines from a list of conditions to the builder.
		/// </summary>
		/// <param name="builder">The tooltip builder to populate.</param>
		/// <param name="conditions">The conditions to process.</param>
		/// <param name="hasResources">Tracks whether the resource cost header has been written.</param>
		/// <param name="hasRequirements">Tracks whether the requirements header has been written.</param>
		private static void AppendConditionLines(TooltipBuilder builder, List<BaseCondition> conditions, ref bool hasResources, ref bool hasRequirements)
		{
			if (conditions == null) return;

			foreach (BaseCondition condition in conditions)
			{
				if (condition == null) continue;

				if (condition is IResourceCost resourceCost &&
					resourceCost.ResourceTemplate != null &&
					resourceCost.ResourceAmount > 0)
				{
					if (!hasResources)
					{
						builder.AddLine("Resource Cost:", 50, TooltipColors.Label);
						hasResources = true;
					}
					builder.AddLine($"{resourceCost.ResourceTemplate.Name}: {resourceCost.ResourceAmount}", 51, TooltipColors.Title, false, "120%");
				}
				else if (condition is ITooltipContributor contributor)
				{
					string contribution = contributor.GetTooltipContribution();
					if (!string.IsNullOrWhiteSpace(contribution))
					{
						if (!hasRequirements)
						{
							builder.AddLine("Requirements:", 60, TooltipColors.Label);
							hasRequirements = true;
						}
						builder.AddLine(contribution, 61, TooltipColors.Title, false, "120%");
					}
				}
			}
		}
	}
}