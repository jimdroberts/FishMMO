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

	// cache of all ability events
	public Dictionary<string, AbilityEvent> AbilityEvents = new Dictionary<string, AbilityEvent>();
	public Dictionary<string, SpawnEvent> PreSpawnEvents = new Dictionary<string, SpawnEvent>();
	public Dictionary<string, SpawnEvent> SpawnEvents = new Dictionary<string, SpawnEvent>();
	public Dictionary<string, MoveEvent> MoveEvents = new Dictionary<string, MoveEvent>();
	public Dictionary<string, HitEvent> HitEvents = new Dictionary<string, HitEvent>();

	public AbilityTemplate Template { get { return AbilityTemplate.Cache[templateID]; } }

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

	public bool TryGetAbilityEvent<T>(string name, out T modifier) where T : AbilityEvent
	{
		if (AbilityEvents.TryGetValue(name, out AbilityEvent result))
		{
			if ((modifier = result as T) != null)
			{
				return true;
			}
		}
		modifier = null;
		return false;
	}

	public bool HasAbilityEvent(string name)
	{
		if (string.IsNullOrWhiteSpace(name)) return false;
		return AbilityEvents.ContainsKey(name);
	}

	public void AddAbilityEvent(AbilityEvent abilityEvent)
	{
		if (!AbilityEvents.ContainsKey(abilityEvent.Name))
		{
			AbilityEvents.Add(abilityEvent.Name, abilityEvent);

			SpawnEvent spawnEvent = abilityEvent as SpawnEvent;
			if (spawnEvent != null)
			{
				switch (spawnEvent.SpawnEventType)
				{
					case SpawnEventType.OnPreSpawn:
						if (!PreSpawnEvents.ContainsKey(spawnEvent.Name))
						{
							PreSpawnEvents.Add(spawnEvent.Name, spawnEvent);
						}
						break;
					case SpawnEventType.OnSpawn:
						if (!SpawnEvents.ContainsKey(spawnEvent.Name))
						{
							SpawnEvents.Add(spawnEvent.Name, spawnEvent);
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
					HitEvents.Add(abilityEvent.name, hitEvent);
				}
				else
				{
					MoveEvent moveEvent = abilityEvent as MoveEvent;
					if (moveEvent != null)
					{
						MoveEvents.Add(abilityEvent.name, moveEvent);
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
		if (AbilityEvents.ContainsKey(abilityEvent.Name))
		{
			AbilityEvents.Remove(abilityEvent.Name);

			SpawnEvent spawnEvent = abilityEvent as SpawnEvent;
			if (spawnEvent != null)
			{
				switch (spawnEvent.SpawnEventType)
				{
					case SpawnEventType.OnPreSpawn:
						PreSpawnEvents.Remove(spawnEvent.Name);
						break;
					case SpawnEventType.OnSpawn:
						SpawnEvents.Remove(spawnEvent.Name);
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
					HitEvents.Remove(abilityEvent.name);
				}
				else
				{
					MoveEvent moveEvent = abilityEvent as MoveEvent;
					if (moveEvent != null)
					{
						MoveEvents.Remove(abilityEvent.name);
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
		if (AbilityEvents.ContainsKey(bloodResourceConversion.Name))
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