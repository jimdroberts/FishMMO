using System.Collections.Generic;

namespace FishMMO.Shared
{
	public class Ability
	{
		public long ID;
		public float ActivationTime;
		public float LifeTime;
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
		public AbilityTypeOverrideEventType TypeOverride { get;	private set; }

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
			LifeTime += template.LifeTime;
			Cooldown += template.Cooldown;
			Range += template.Range;
			Speed += template.Speed;

			if (Resources == null)
			{
				Resources = new AbilityResourceDictionary();
			}

			if (Requirements == null)
			{
				Requirements = new AbilityResourceDictionary();
			}

			foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in template.Resources)
			{
				if (!Resources.ContainsKey(pair.Key))
				{
					Resources[pair.Key] = pair.Value;
				}
				else
				{
					Resources[pair.Key] += pair.Value;
				}
			}

			foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in template.Requirements)
			{
				if (!Requirements.ContainsKey(pair.Key))
				{
					Requirements[pair.Key] = pair.Value;
				}
				else
				{
					Requirements[pair.Key] += pair.Value;
				}
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
			if (AbilityEvents == null)
			{
				AbilityEvents = new Dictionary<int, AbilityEvent>();
			}
			if (!AbilityEvents.ContainsKey(abilityEvent.ID))
			{
				CachedTooltip = null;

				AbilityEvents.Add(abilityEvent.ID, abilityEvent);

				SpawnEvent spawnEvent = abilityEvent as SpawnEvent;
				if (spawnEvent != null)
				{
					if (PreSpawnEvents == null)
					{
						PreSpawnEvents = new Dictionary<int, SpawnEvent>();
					}
					if (SpawnEvents == null)
					{
						SpawnEvents = new Dictionary<int, SpawnEvent>();
					}
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
						if (HitEvents == null)
						{
							HitEvents = new Dictionary<int, HitEvent>();
						}
						HitEvents.Add(abilityEvent.ID, hitEvent);
					}
					else
					{
						MoveEvent moveEvent = abilityEvent as MoveEvent;
						if (moveEvent != null)
						{
							if (MoveEvents == null)
							{
								MoveEvents = new Dictionary<int, MoveEvent>();
							}
							MoveEvents.Add(abilityEvent.ID, moveEvent);
						}
						else
						{
							AbilityTypeOverrideEventType overrideTypeEvent = abilityEvent as AbilityTypeOverrideEventType;
							if (overrideTypeEvent != null)
							{
								TypeOverride = overrideTypeEvent;
							}
						}
					}
				}

				ActivationTime += abilityEvent.ActivationTime;
				LifeTime += abilityEvent.LifeTime;
				Cooldown += abilityEvent.Cooldown;
				Range += abilityEvent.Range;
				Speed += abilityEvent.Speed;
				foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in abilityEvent.Resources)
				{
					if (!Resources.ContainsKey(pair.Key))
					{
						Resources[pair.Key] = pair.Value;
					}
					else
					{
						Resources[pair.Key] += pair.Value;
					}
				}
				foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in abilityEvent.Requirements)
				{
					if (!Requirements.ContainsKey(pair.Key))
					{
						Requirements[pair.Key] = pair.Value;
					}
					else
					{
						Requirements[pair.Key] += pair.Value;
					}
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
						else
						{
							AbilityTypeOverrideEventType overrideTypeEvent = abilityEvent as AbilityTypeOverrideEventType;
							if (overrideTypeEvent != null)
							{
								TypeOverride = null;
							}
						}
					}
				}

				ActivationTime -= abilityEvent.ActivationTime;
				LifeTime -= abilityEvent.LifeTime;
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
			if (!character.TryGet(out ICharacterAttributeController attributeController))
			{
				return false;
			}
			foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in Requirements)
			{
				if (!attributeController.TryGetResourceAttribute(pair.Key.ID, out CharacterResourceAttribute requirement) ||
					requirement.CurrentValue < pair.Value)
				{
					return false;
				}
			}
			return true;
		}

		public bool HasResource(Character character, AbilityEvent bloodResourceConversion, CharacterAttributeTemplate bloodResource)
		{
			if (!character.TryGet(out ICharacterAttributeController attributeController))
			{
				return false;
			}
			if (AbilityEvents != null &&
				bloodResourceConversion != null &&
				bloodResource != null &&
				AbilityEvents.ContainsKey(bloodResourceConversion.ID))
			{
				int totalCost = TotalResourceCost;

				CharacterResourceAttribute resource;
				if (!attributeController.TryGetResourceAttribute(bloodResource.ID, out resource) ||
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
					if (!attributeController.TryGetResourceAttribute(pair.Key.ID, out resource) ||
						resource.CurrentValue < pair.Value)
					{
						return false;
					}
				}
			}
			return true;
		}

		public void ConsumeResources(Character character, AbilityEvent bloodResourceConversion, CharacterAttributeTemplate bloodResource)
		{
			if (!character.TryGet(out ICharacterAttributeController attributeController))
			{
				return;
			}
			if (AbilityEvents != null &&
				bloodResourceConversion != null &&
				bloodResource != null &&
				AbilityEvents.ContainsKey(bloodResourceConversion.ID))
			{
				int totalCost = TotalResourceCost;

				CharacterResourceAttribute resource;
				if (attributeController.TryGetResourceAttribute(bloodResource.ID, out resource) &&
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
						resource.CurrentValue >= pair.Value)
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
			if (!string.IsNullOrWhiteSpace(CachedTooltip))
			{
				return CachedTooltip;
			}

			CachedTooltip = Template.Tooltip(new List<ITooltip>(AbilityEvents.Values));

			if (TypeOverride != null)
			{
				CachedTooltip += RichText.Format($"Type: {TypeOverride.OverrideAbilityType}", true, "f5ad6eFF", "120%");
			}
			else
			{
				CachedTooltip += RichText.Format($"Type: {Template.Type}", true, "f5ad6eFF", "120%");
			}

			return CachedTooltip;
		}
	}
}