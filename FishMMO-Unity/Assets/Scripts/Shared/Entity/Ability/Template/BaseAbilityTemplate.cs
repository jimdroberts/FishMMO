using Cysharp.Text;
using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	public abstract class BaseAbilityTemplate : CachedScriptableObject<BaseAbilityTemplate>, ITooltip, ICachedObject
	{
		public Sprite icon;
		public string Description;
		public float ActivationTime;
		public float LifeTime;
		public float Speed;
		public float Cooldown;
		public int Price;
		public AbilityResourceDictionary Resources = new AbilityResourceDictionary();
		public AbilityResourceDictionary RequiredAttributes = new AbilityResourceDictionary();
		public FactionTemplate RequiredFaction;
		public ArcheTypeTemplate RequiredArchetype;

		public string Name { get { return this.name; } }

		public Sprite Icon { get { return this.icon; } }

		public virtual string Tooltip()
		{
			return PrimaryTooltip(null);
		}

		public virtual string Tooltip(List<ITooltip> combineList)
		{
			return PrimaryTooltip(combineList);
		}

		public virtual string GetFormattedDescription()
		{
			return Description;
		}

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