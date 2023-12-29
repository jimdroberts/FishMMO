using System.Collections.Generic;
using Cysharp.Text;

namespace FishMMO.Shared
{
	public class Ability
	{
		public long ID;
		public float ActivationTime;
		public float ActiveTime;
		public float Cooldown;
		public float Range;
		public float Speed;

		public AbilityTemplate Template { get; private set; }
		public string Name { get; set; }
		public string CachedTooltip { get; private set; }
		public AbilityResourceDictionary Resources { get; private set; }
		public AbilityResourceDictionary Requirements { get; private set; }

		public Dictionary<int, AbilityEvent> AbilityEvents { get; private set; }
		public Dictionary<int, SpawnEvent> PreSpawnEvents { get; private set; }
		public Dictionary<int, SpawnEvent> SpawnEvents { get; private set; }
		public Dictionary<int, MoveEvent> MoveEvents { get; private set; }
		public Dictionary<int, HitEvent> HitEvents { get; private set; }

		/// <summary>
		/// Cache of all active ability Objects. <ContainerID, <AbilityObjectID, AbilityObject>>
		/// </summary>
		public Dictionary<int, Dictionary<int, AbilityObject>> Objects { get; set; }

		public int TotalResourceCost
		{
			get
			{
				int totalCost = 0;
				foreach (int cost in Resources.Values)
				{
					totalCost += cost;
				}
				return totalCost;
			}
		}

		public Ability(AbilityTemplate template, List<int> abilityEvents = null)
		{
			ID = -1;
			Template = template;
			Name = Template.Name;
			CachedTooltip = null;

			InternalAddTemplateModifiers(Template);

			if (abilityEvents != null)
			{
				for (int i = 0; i < abilityEvents.Count; ++i)
				{
					AbilityEvent abilityEvent = AbilityEvent.Get<AbilityEvent>(abilityEvents[i]);
					if (abilityEvent == null)
					{
						continue;
					}
					AddAbilityEvent(abilityEvent);
				}
			}
		}

		public Ability(long abilityID, int templateID, List<int> abilityEvents = null)
		{
			ID = abilityID;
			Template = AbilityTemplate.Get<AbilityTemplate>(templateID);
			Name = Template.Name;
			CachedTooltip = null;

			InternalAddTemplateModifiers(Template);

			if (abilityEvents != null)
			{
				for (int i = 0; i < abilityEvents.Count; ++i)
				{
					AbilityEvent abilityEvent = AbilityEvent.Get<AbilityEvent>(abilityEvents[i]);
					if (abilityEvent == null)
					{
						continue;
					}
					AddAbilityEvent(abilityEvent);
				}
			}
		}

		public Ability(long abilityID, AbilityTemplate template, List<int> abilityEvents = null)
		{
			ID = abilityID;
			Template = template;
			Name = Template.Name;
			CachedTooltip = null;

			InternalAddTemplateModifiers(Template);

			if (abilityEvents != null)
			{
				for (int i = 0; i < abilityEvents.Count; ++i)
				{
					AbilityEvent abilityEvent = AbilityEvent.Get<AbilityEvent>(abilityEvents[i]);
					if (abilityEvent == null)
					{
						continue;
					}
					AddAbilityEvent(abilityEvent);
				}
			}
		}

		internal void InternalAddTemplateModifiers(AbilityTemplate template)
		{
			ActivationTime += template.ActivationTime;
			ActiveTime += template.ActiveTime;
			Cooldown += template.Cooldown;
			Range += template.Range;
			Speed += template.Speed;

			foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in template.Resources)
			{
				Resources[pair.Key] += pair.Value;
			}

			foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in template.Requirements)
			{
				Requirements[pair.Key] += pair.Value;
			}
		}

		public bool TryGetAbilityEvent<T>(int templateID, out T modifier) where T : AbilityEvent
		{
			if (AbilityEvents.TryGetValue(templateID, out AbilityEvent result))
			{
				if ((modifier = result as T) != null)
				{
					return true;
				}
			}
			modifier = null;
			return false;
		}

		public bool HasAbilityEvent(int templateID)
		{
			return AbilityEvents.ContainsKey(templateID);
		}

		public void AddAbilityEvent(AbilityEvent abilityEvent)
		{
			if (!AbilityEvents.ContainsKey(abilityEvent.ID))
			{
				CachedTooltip = null;

				AbilityEvents.Add(abilityEvent.ID, abilityEvent);

				SpawnEvent spawnEvent = abilityEvent as SpawnEvent;
				if (spawnEvent != null)
				{
					switch (spawnEvent.SpawnEventType)
					{
						case SpawnEventType.OnPreSpawn:
							if (!PreSpawnEvents.ContainsKey(spawnEvent.ID))
							{
								PreSpawnEvents.Add(spawnEvent.ID, spawnEvent);
							}
							break;
						case SpawnEventType.OnSpawn:
							if (!SpawnEvents.ContainsKey(spawnEvent.ID))
							{
								SpawnEvents.Add(spawnEvent.ID, spawnEvent);
							}
							break;
						default:
							break;
					}
				}
				else
				{
					HitEvent hitEvent = abilityEvent as HitEvent;
					if (hitEvent != null)
					{
						HitEvents.Add(abilityEvent.ID, hitEvent);
					}
					else
					{
						MoveEvent moveEvent = abilityEvent as MoveEvent;
						if (moveEvent != null)
						{
							MoveEvents.Add(abilityEvent.ID, moveEvent);
						}
					}
				}

				ActivationTime += abilityEvent.ActivationTime;
				ActiveTime += abilityEvent.ActiveTime;
				Cooldown += abilityEvent.Cooldown;
				Range += abilityEvent.Range;
				Speed += abilityEvent.Speed;
				foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in abilityEvent.Resources)
				{
					Resources[pair.Key] += pair.Value;
				}
				foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in abilityEvent.Requirements)
				{
					Requirements[pair.Key] += pair.Value;
				}
			}
		}

		public void RemoveAbilityEvent(AbilityEvent abilityEvent)
		{
			if (AbilityEvents.ContainsKey(abilityEvent.ID))
			{
				CachedTooltip = null;

				AbilityEvents.Remove(abilityEvent.ID);

				SpawnEvent spawnEvent = abilityEvent as SpawnEvent;
				if (spawnEvent != null)
				{
					switch (spawnEvent.SpawnEventType)
					{
						case SpawnEventType.OnPreSpawn:
							PreSpawnEvents.Remove(spawnEvent.ID);
							break;
						case SpawnEventType.OnSpawn:
							SpawnEvents.Remove(spawnEvent.ID);
							break;
						default:
							break;
					}
				}
				else
				{
					HitEvent hitEvent = abilityEvent as HitEvent;
					if (hitEvent != null)
					{
						HitEvents.Remove(abilityEvent.ID);
					}
					else
					{
						MoveEvent moveEvent = abilityEvent as MoveEvent;
						if (moveEvent != null)
						{
							MoveEvents.Remove(abilityEvent.ID);
						}
					}
				}

				ActivationTime -= abilityEvent.ActivationTime;
				ActiveTime -= abilityEvent.ActiveTime;
				Cooldown -= abilityEvent.Cooldown;
				Range -= abilityEvent.Range;
				Speed -= abilityEvent.Speed;
				foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in abilityEvent.Resources)
				{
					if (Resources.ContainsKey(pair.Key))
					{
						Resources[pair.Key] -= pair.Value;
					}
				}
				foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in abilityEvent.Requirements)
				{
					if (Requirements.ContainsKey(pair.Key))
					{
						Requirements[pair.Key] += pair.Value;
					}
				}
			}
		}

		public bool MeetsRequirements(Character character)
		{
			foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in Requirements)
			{
				if (!character.AttributeController.TryGetResourceAttribute(pair.Key.ID, out CharacterResourceAttribute requirement) ||
					requirement.CurrentValue < pair.Value)
				{
					return false;
				}
			}
			return true;
		}

		public bool HasResource(Character character, AbilityEvent bloodResourceConversion, CharacterAttributeTemplate bloodResource)
		{
			if (AbilityEvents.ContainsKey(bloodResourceConversion.ID))
			{
				int totalCost = TotalResourceCost;

				CharacterResourceAttribute resource;
				if (!character.AttributeController.TryGetResourceAttribute(bloodResource.ID, out resource) ||
					resource.CurrentValue < totalCost)
				{
					return false;
				}
			}
			else
			{
				foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in Resources)
				{
					CharacterResourceAttribute resource;
					if (!character.AttributeController.TryGetResourceAttribute(pair.Key.ID, out resource) ||
						resource.CurrentValue < pair.Value)
					{
						return false;
					}
				}
			}
			return true;
		}

		public void ConsumeResources(CharacterAttributeController attributeController, AbilityEvent bloodResourceConversion, CharacterAttributeTemplate bloodResource)
		{
			if (bloodResourceConversion != null && AbilityEvents.ContainsKey(bloodResourceConversion.ID))
			{
				int totalCost = TotalResourceCost;

				CharacterResourceAttribute resource;
				if (bloodResource != null && attributeController.TryGetResourceAttribute(bloodResource.ID, out resource) &&
					resource.CurrentValue >= totalCost)
				{
					resource.Consume(totalCost);
				}
			}
			else
			{
				foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in Resources)
				{
					CharacterResourceAttribute resource;
					if (attributeController.TryGetResourceAttribute(pair.Key.ID, out resource) &&
						resource.CurrentValue < pair.Value)
					{
						resource.Consume(pair.Value);
					}
				}
			}
		}

		public void RemoveAbilityObject(int containerID, int objectID)
		{
			if (Objects.TryGetValue(containerID, out Dictionary<int, AbilityObject> container))
			{
				container.Remove(objectID);
			}
		}

		public string Tooltip()
		{
			if (CachedTooltip != null)
			{
				return CachedTooltip;
			}
			using (var sb = ZString.CreateStringBuilder())
			{
				sb.Append("<size=120%><color=#f5ad6e>");
				sb.Append(Template.Name);
				sb.Append("</color></size>");
				sb.AppendLine();
				sb.Append("<color=#a66ef5>ID: ");
				sb.Append(ID);
				sb.Append("</color>");
				sb.AppendLine();
				sb.Append("<color=#a66ef5>TemplateID: ");
				sb.Append(Template.ID);
				sb.Append("</color>");
				sb.AppendLine();
				sb.Append("<color=#a66ef5>Activation Time: ");
				sb.Append(ActivationTime);
				sb.Append("</color>");
				sb.AppendLine();
				sb.Append("<color=#a66ef5>Active Time: ");
				sb.Append(ActiveTime);
				sb.Append("</color>");
				sb.AppendLine();
				sb.Append("<color=#a66ef5>Cooldown: ");
				sb.Append(Cooldown);
				sb.Append("</color>");
				sb.AppendLine();
				sb.Append("<color=#a66ef5>Range: ");
				sb.Append(Range);
				sb.Append("</color>");
				sb.AppendLine();
				sb.Append("<color=#a66ef5>Speed: ");
				sb.Append(Speed);
				sb.Append("</color>");
				if (Resources != null && Resources.Count > 0)
				{
					sb.AppendLine();
					sb.Append("<color=#a66ef5>Resources: </color>");

					foreach (CharacterAttributeTemplate attribute in Resources.Keys)
					{
						if (!string.IsNullOrWhiteSpace(attribute.Name))
						{
							sb.AppendLine();
							sb.Append("<size=120%><color=#f5ad6e>");
							sb.Append(attribute.Name);
							sb.Append("</color></size>");
						}
					}
				}
				if (Requirements != null && Requirements.Count > 0)
				{
					sb.AppendLine();
					sb.Append("<color=#a66ef5>Requirements: </color>");

					foreach (CharacterAttributeTemplate attribute in Requirements.Keys)
					{
						if (!string.IsNullOrWhiteSpace(attribute.Name))
						{
							sb.AppendLine();
							sb.Append("<size=120%><color=#f5ad6e>");
							sb.Append(attribute.Name);
							sb.Append("</color></size>");
						}
					}
				}
				if (AbilityEvents != null && AbilityEvents.Count > 0)
				{
					sb.AppendLine();
					sb.Append("<color=#a66ef5>Ability Events: </color>");

					foreach (AbilityEvent abilityEvent in AbilityEvents.Values)
					{
						sb.AppendLine();
						sb.Append(abilityEvent.Name);
						sb.AppendLine();
						sb.Append(abilityEvent.Description);
					}
				}
				CachedTooltip = sb.ToString();
				return CachedTooltip;
			}
		}
	}
}