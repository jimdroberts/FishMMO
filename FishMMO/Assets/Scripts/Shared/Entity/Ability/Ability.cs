using System.Collections.Generic;
using System.Text;

public class Ability
{
	public int abilityID;
	public int templateID;
	public float activationTime = 0.0f;
	public float cooldown = 0.0f;
	public float range = 0.0f;
	public float speed = 0.0f;
	public AbilityResourceDictionary resources = new AbilityResourceDictionary();
	public AbilityResourceDictionary requirements = new AbilityResourceDictionary();

	// cache of the ability objects this ability has spawned
	public Dictionary<int, AbilityObject> objects = new Dictionary<int, AbilityObject>();

	// cache of all ability events
	public Dictionary<int, AbilityEvent> AbilityEvents = new Dictionary<int, AbilityEvent>();
	public Dictionary<int, SpawnEvent> PreSpawnEvents = new Dictionary<int, SpawnEvent>();
	public Dictionary<int, SpawnEvent> SpawnEvents = new Dictionary<int, SpawnEvent>();
	public Dictionary<int, MoveEvent> MoveEvents = new Dictionary<int, MoveEvent>();
	public Dictionary<int, HitEvent> HitEvents = new Dictionary<int, HitEvent>();

	private AbilityTemplate cachedTemplate;
	public AbilityTemplate Template { get { return cachedTemplate; } }

	public int TotalResourceCost
	{
		get
		{
			int totalCost = 0;
			foreach (int cost in resources.Values)
			{
				totalCost += cost;
			}
			return totalCost;
		}
	}

	public Ability(int abilityID, int templateID) : this(abilityID, templateID, null)
	{
	}

	public Ability(int abilityID, int templateID, List<AbilityEvent> events)
	{
		this.abilityID = abilityID;
		this.templateID = templateID;
		this.cachedTemplate = AbilityTemplate.Get<AbilityTemplate>(templateID);

		InternalAddTemplateModifiers(Template);

		if (events != null)
		{
			for (int i = 0; i < events.Count; ++i)
			{
				AddAbilityEvent(events[i]);
			}
		}
	}

	internal void InternalAddTemplateModifiers(AbilityTemplate template)
	{
		activationTime += template.ActivationTime;
		cooldown += template.Cooldown;
		range += template.Range;
		speed += template.Speed;

		foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in template.Resources)
		{
			if (!resources.ContainsKey(pair.Key))
			{
				resources.Add(pair.Key, pair.Value);

			}
			else
			{
				resources[pair.Key] += pair.Value;
			}
		}

		foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in template.Requirements)
		{
			if (!requirements.ContainsKey(pair.Key))
			{
				requirements.Add(pair.Key, pair.Value);

			}
			else
			{
				requirements[pair.Key] += pair.Value;
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
		if (!AbilityEvents.ContainsKey(abilityEvent.ID))
		{
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

			activationTime += abilityEvent.ActivationTime;
			cooldown += abilityEvent.Cooldown;
			range += abilityEvent.Range;
			speed += abilityEvent.Speed;
			foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in abilityEvent.Resources)
			{
				if (!resources.ContainsKey(pair.Key))
				{
					resources.Add(pair.Key, pair.Value);

				}
				else
				{
					resources[pair.Key] += pair.Value;
				}
			}
			foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in abilityEvent.Requirements)
			{
				if (!requirements.ContainsKey(pair.Key))
				{
					requirements.Add(pair.Key, pair.Value);
				}
				else
				{
					requirements[pair.Key] += pair.Value;
				}
			}
		}
	}

	public void RemoveAbilityEvent(AbilityEvent abilityEvent)
	{
		if (AbilityEvents.ContainsKey(abilityEvent.ID))
		{
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

			activationTime -= abilityEvent.ActivationTime;
			cooldown -= abilityEvent.Cooldown;
			range -= abilityEvent.Range;
			speed -= abilityEvent.Speed;
			foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in abilityEvent.Resources)
			{
				if (resources.ContainsKey(pair.Key))
				{
					resources[pair.Key] -= pair.Value;
				}
			}
			foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in abilityEvent.Requirements)
			{
				if (requirements.ContainsKey(pair.Key))
				{
					requirements[pair.Key] += pair.Value;
				}
			}
		}
	}

	public bool MeetsRequirements(Character character)
	{
		foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in requirements)
		{
			if (!character.AttributeController.TryGetResourceAttribute(pair.Key.Name, out CharacterResourceAttribute requirement) ||
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
			if (!character.AttributeController.TryGetResourceAttribute(bloodResource.Name, out resource) ||
				resource.CurrentValue < totalCost)
			{
				return false;
			}
		}
		else
		{
			foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in resources)
			{
				CharacterResourceAttribute resource;
				if (!character.AttributeController.TryGetResourceAttribute(pair.Key.Name, out resource) ||
					resource.CurrentValue < pair.Value)
				{
					return false;
				}
			}
		}
		return true;
	}

	public string Tooltip()
	{
		StringBuilder sb = new StringBuilder();
		sb.Append("<size=120%><color=#f5ad6e>");
		sb.Append(Template.Name);
		sb.Append("</color></size>");
		sb.AppendLine();
		sb.Append("<color=#a66ef5>AbilityID: ");
		sb.Append(abilityID);
		sb.Append("</color>");
		sb.AppendLine();
		sb.Append("<color=#a66ef5>TemplateID: ");
		sb.Append(templateID);
		sb.Append("</color>");
		sb.AppendLine();
		sb.Append("<color=#a66ef5>Activation Time: ");
		sb.Append(activationTime);
		sb.Append("</color>");
		sb.AppendLine();
		sb.Append("<color=#a66ef5>Cooldown: ");
		sb.Append(cooldown);
		sb.Append("</color>");
		sb.AppendLine();
		sb.Append("<color=#a66ef5>Range: ");
		sb.Append(range);
		sb.Append("</color>");
		sb.AppendLine();
		sb.Append("<color=#a66ef5>Speed: ");
		sb.Append(speed);
		sb.Append("</color>");
		return sb.ToString();
	}
}