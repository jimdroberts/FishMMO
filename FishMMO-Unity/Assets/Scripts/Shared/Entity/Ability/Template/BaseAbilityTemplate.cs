using Cysharp.Text;
using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract base ScriptableObject for ability templates, providing common fields and tooltip logic.
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
		/// Price or cost of the ability.
		/// </summary>
		public int Price;

		/// <summary>
		/// Resources required to use the ability.
		/// </summary>
		public AbilityResourceDictionary Resources = new AbilityResourceDictionary();

		/// <summary>
		/// Attributes required to use the ability.
		/// </summary>
		public AbilityResourceDictionary RequiredAttributes = new AbilityResourceDictionary();

		/// <summary>
		/// Faction required to use the ability.
		/// </summary>
		public FactionTemplate RequiredFaction;

		/// <summary>
		/// Archetype required to use the ability.
		/// </summary>
		public ArchetypeTemplate RequiredArchetype;

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
		/// Builds the primary tooltip string for the ability, including name, description, and stats.
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

				AbilityResourceDictionary resources = new AbilityResourceDictionary();
				resources.CopyFrom(Resources);

				AbilityResourceDictionary requirements = new AbilityResourceDictionary();
				requirements.CopyFrom(RequiredAttributes);

				if (combineList != null &&
					combineList.Count > 0)
				{
					foreach (BaseAbilityTemplate template in combineList)
					{
						if (template == null)
						{
							continue;
						}

						string templateDescription = template.GetFormattedDescription();
						if (!string.IsNullOrWhiteSpace(templateDescription))
						{
							sb.Append(RichText.Format(templateDescription, true, "a66ef5FF"));
						}

						activationTime += template.ActivationTime;
						lifeTime += template.LifeTime;
						speed += template.Speed;
						cooldown += template.Cooldown;
						price += template.Price;

						foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in template.Resources)
						{
							if (!string.IsNullOrWhiteSpace(pair.Key.Name))
							{
								if (resources.ContainsKey(pair.Key))
								{
									resources[pair.Key] += pair.Value;
								}
								else
								{
									resources[pair.Key] = pair.Value;
								}
							}
						}

						foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in template.RequiredAttributes)
						{
							if (!string.IsNullOrWhiteSpace(pair.Key.Name))
							{
								if (requirements.ContainsKey(pair.Key))
								{
									requirements[pair.Key] += pair.Value;
								}
								else
								{
									requirements[pair.Key] = pair.Value;
								}
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

				if (resources != null && resources.Count > 0)
				{
					//sb.Append("\r\n______________________________\r\n\r\n");
					sb.Append("\r\n\r\n<color=#a66ef5>Resource Cost: </color>");

					foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in resources)
					{
						if (!string.IsNullOrWhiteSpace(pair.Key.Name))
						{
							sb.Append(RichText.Format(pair.Key.Name, pair.Value, true, "f5ad6eFF", "", "", "120%"));
						}
					}
				}
				if (requirements != null && requirements.Count > 0)
				{
					sb.Append("\r\n\r\n<color=#a66ef5>Requirements: </color>");

					foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in requirements)
					{
						if (!string.IsNullOrWhiteSpace(pair.Key.Name))
						{
							sb.Append(RichText.Format(pair.Key.Name, pair.Value, true, "f5ad6eFF", "", "", "120%"));
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