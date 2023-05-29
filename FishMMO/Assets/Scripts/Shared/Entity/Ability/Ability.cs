using System.Collections.Generic;
using UnityEngine;

public class Ability
{
	public const string BLOOD_RESOURCE = "Health";
	public const string BLOOD_RESOURCE_CONVERSION = "Blood Magic";

	private string name = null;
	private AbilityResourceDictionary resources = new AbilityResourceDictionary();
	private AbilityResourceDictionary requirements = new AbilityResourceDictionary();

	public int templateID;
	public float activationTime = 0.0f;
	public float cooldown = 0.0f;
	public float range = 0.0f;
	public float speed = 0.0f;

	public Dictionary<string, AbilityNode> Nodes = new Dictionary<string, AbilityNode>();

	public AbilityTemplate Template { get { return AbilityTemplate.Cache[templateID]; } }

	public string Name
	{
		get
		{
			return string.IsNullOrWhiteSpace(name) ? Template.name : name;
		}
	}

	public Ability(int templateID)
	{
		this.templateID = templateID;

		InternalAddTemplateModifiers(Template);
	}

	public Ability(int templateID, List<AbilityNode> nodes)
	{
		this.templateID = templateID;

		InternalAddTemplateModifiers(Template);

		for (int i = 0; i < nodes.Count; ++i)
		{
			AddAbilityNode(nodes[i]);
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

	public bool HasAbilityNode(AbilityNode node)
	{
		if (node == null) return false;
		return Nodes.ContainsKey(node.Name);
	}

	public void AddAbilityNode(AbilityNode node)
	{
		if (!Nodes.ContainsKey(node.Name))
		{
			Nodes.Add(node.Name, node);
			InternalAddAbilityNode(node);
		}
	}

	public void RemoveAbilityNode(AbilityNode node)
	{
		if (Nodes.ContainsKey(node.Name))
		{
			Nodes.Remove(node.Name);
			InternalRemoveAbilityNode(node);
		}
	}

	internal void InternalAddAbilityNode(AbilityNode node)
	{
		activationTime += node.ActivationTime;
		cooldown += node.Cooldown;
		range += node.Range;
		speed += node.Speed;
		foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in node.Resources)
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
		foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in node.Requirements)
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

	internal void InternalRemoveAbilityNode(AbilityNode node)
	{
		activationTime -= node.ActivationTime;
		cooldown -= node.Cooldown;
		range -= node.Range;
		speed -= node.Speed;
		foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in node.Resources)
		{
			if (resources.ContainsKey(pair.Key))
			{
				resources[pair.Key] -= pair.Value;
			}
		}
		foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in node.Requirements)
		{
			if (requirements.ContainsKey(pair.Key))
			{
				requirements[pair.Key] += pair.Value;
			}
		}
	}

	public void Start(Character self, TargetInfo targetInfo)
	{
		Template.OnStartEvent?.Invoke(this, self, targetInfo, null);
	}

	public void Update(Character self, TargetInfo targetInfo)
	{
		Template.OnUpdateEvent?.Invoke(this, self, targetInfo, null);
	}

	public void Hit(Character attacker, TargetInfo hitTarget, GameObject abilityObject)
	{
		if (hitTarget.target != null)
		{
			Character hitCharacter = hitTarget.target.GetComponent<Character>();
			if (hitCharacter != null)
			{
				hitCharacter.AbilityController?.Interrupt(attacker);
			}
		}
		Template.OnHitEvent?.Invoke(this, attacker, hitTarget, abilityObject);
	}

	public void Finish(Character self, TargetInfo targetInfo)
	{
		Template.OnFinishEvent?.Invoke(this, self, targetInfo, null);
	}

	public void Interrupt(Character self, TargetInfo attacker)
	{
		Template.OnInterruptEvent?.Invoke(this, self, attacker, null);
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

	public bool HasResource(Character character)
	{
		if (Nodes.ContainsKey(BLOOD_RESOURCE_CONVERSION))
		{
			int totalCost = 0;
			foreach (int cost in resources.Values)
			{
				totalCost += cost;
			}
			CharacterResourceAttribute resource;
			if (!character.AttributeController.TryGetResourceAttribute(BLOOD_RESOURCE, out resource) ||
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

	public void ConsumeResource(Character character)
	{
		if (Nodes.ContainsKey(BLOOD_RESOURCE_CONVERSION))
		{
			int totalCost = 0;
			foreach (int cost in resources.Values)
			{
				totalCost += cost;
			}
			CharacterResourceAttribute resource;
			if (character.AttributeController.TryGetResourceAttribute(BLOOD_RESOURCE, out resource) &&
				resource.CurrentValue >= totalCost)
			{
				resource.Consume(totalCost);
			}
		}
		else if (HasResource(character)) // consume is handled after we check all the resources exist
		{
			foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in resources)
			{
				CharacterResourceAttribute resource;
				if (character.AttributeController.TryGetResourceAttribute(pair.Key.Name, out resource) &&
					resource.CurrentValue < pair.Value)
				{
					resource.Consume(pair.Value);
				}
			}
		}
	}
}