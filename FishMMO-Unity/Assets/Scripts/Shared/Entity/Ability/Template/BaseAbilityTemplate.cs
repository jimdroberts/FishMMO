using Cysharp.Text;
using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract base ScriptableObject for ability templates, providing common fields, activation conditions, and tooltip logic.
	/// Ability requirements (resources, faction, archetype, attributes) are defined as ECA conditions
	/// on the <see cref="ActivationConditions"/> list rather than as hardcoded fields.
	/// </summary>
	public abstract class BaseAbilityTemplate : CachedScriptableObject<BaseAbilityTemplate>, ITooltip, ICachedObject
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
			return PrimaryTooltip(null);
		}

		/// <summary>
		/// Returns the tooltip string for the ability, optionally combining with other tooltips.
		/// </summary>
		/// <param name="combineList">List of tooltips to combine.</param>
		public virtual string Tooltip(List<ITooltip> combineList)
		{
			return PrimaryTooltip(combineList);
		}

		/// <summary>
		/// Returns the formatted description for the ability.
		/// </summary>
		public virtual string GetFormattedDescription()
		{
			return Description;
		}

		/// <summary>
		/// Builds the primary tooltip string for the ability, including name, description, stats,
		/// and requirement/cost information gathered from <see cref="ActivationConditions"/>.
		/// </summary>
		/// <param name="combineList">List of tooltips to combine.</param>
		/// <returns>Formatted tooltip string.</returns>
		private string PrimaryTooltip(List<ITooltip> combineList)
		{
			using (var sb = ZString.CreateStringBuilder())
			{
				sb.Append(RichText.Format(Name, true, "f5ad6e", "140%"));

				string description = GetFormattedDescription();
				if (!string.IsNullOrWhiteSpace(description))
				{
					sb.AppendLine();
					sb.Append(RichText.Format(description, true, "a66ef5FF"));
				}

				float activationTime = ActivationTime;
				float lifeTime = LifeTime;
				float speed = Speed;
				float cooldown = Cooldown;
				float price = Price;

				// Collect all conditions for tooltip display.
				List<BaseCondition> allConditions = new List<BaseCondition>();
				if (ActivationConditions != null)
				{
					allConditions.AddRange(ActivationConditions);
				}

				if (combineList != null && combineList.Count > 0)
				{
					foreach (ITooltip tooltip in combineList)
					{
						if (tooltip == null)
						{
							continue;
						}

						string templateDescription = tooltip.GetFormattedDescription();
						if (!string.IsNullOrWhiteSpace(templateDescription))
						{
							sb.Append(RichText.Format(templateDescription, true, "a66ef5FF"));
						}

						if (tooltip is AbilityEvent abilityEvent)
						{
							activationTime += abilityEvent.ActivationTime;
							lifeTime += abilityEvent.LifeTime;
							speed += abilityEvent.Speed;
							cooldown += abilityEvent.Cooldown;
							price += abilityEvent.Price;

							// Gather event conditions for tooltip contributions.
							if (abilityEvent.Conditions != null)
							{
								allConditions.AddRange(abilityEvent.Conditions);
							}
						}
						else if (tooltip is BaseAbilityTemplate template)
						{
							activationTime += template.ActivationTime;
							lifeTime += template.LifeTime;
							speed += template.Speed;
							cooldown += template.Cooldown;
							price += template.Price;

							if (template.ActivationConditions != null)
							{
								allConditions.AddRange(template.ActivationConditions);
							}
						}
					}
				}

				if (activationTime > 0 ||
					lifeTime > 0 ||
					cooldown > 0 ||
					speed > 0)
				{
					sb.AppendLine();
					sb.Append(RichText.Format("Activation Time", activationTime, true, "FFFFFFFF", "", "s"));
					sb.Append(RichText.Format("Life Time", lifeTime, true, "FFFFFFFF", "", "s"));
					sb.Append(RichText.Format("Speed", speed, true, "FFFFFFFF", "", "m/s"));
					sb.Append(RichText.Format("Range", speed * lifeTime, true, "FFFFFFFF", "", "m"));
					sb.Append(RichText.Format("Cooldown", cooldown, true, "FFFFFFFF", "", "s"));
				}

				// Build resource cost and requirement sections from conditions.
				bool hasResources = false;
				bool hasRequirements = false;

				foreach (BaseCondition condition in allConditions)
				{
					if (condition == null) continue;

					if (condition is IResourceCost resourceCost &&
						resourceCost.ResourceTemplate != null &&
						resourceCost.ResourceAmount > 0)
					{
						if (!hasResources)
						{
							sb.Append("\r\n\r\n<color=#a66ef5>Resource Cost: </color>");
							hasResources = true;
						}
						sb.Append(RichText.Format(resourceCost.ResourceTemplate.Name, resourceCost.ResourceAmount, true, "f5ad6eFF", "", "", "120%"));
					}
					else if (condition is ITooltipContributor contributor)
					{
						string contribution = contributor.GetTooltipContribution();
						if (!string.IsNullOrWhiteSpace(contribution))
						{
							if (!hasRequirements)
							{
								sb.Append("\r\n\r\n<color=#a66ef5>Requirements: </color>");
								hasRequirements = true;
							}
							sb.Append(contribution);
						}
					}
				}

				if (price > 0)
				{
					sb.AppendLine();
					sb.Append(RichText.Format("Price", price, true, "FFFFFFFF"));
				}
				return sb.ToString();
			}
		}
	}
}