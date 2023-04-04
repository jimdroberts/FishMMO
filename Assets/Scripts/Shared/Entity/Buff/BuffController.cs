using FishNet.Object;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Character))]
public class BuffController : NetworkBehaviour
{
	private Dictionary<string, Buff> buffs = new Dictionary<string, Buff>();

	public Character character;

	public override void OnStartClient()
	{
		base.OnStartClient();

		if (character == null || !base.IsOwner)
		{
			enabled = false;
			return;
		}
	}

	void Update()
	{
		float dt = Time.deltaTime;
		foreach (KeyValuePair<string, Buff> pair in new Dictionary<string, Buff>(buffs))
		{
			pair.Value.SubtractTime(dt);
			if (pair.Value.remainingTime > 0.0f)
			{
				pair.Value.SubtractTickTime(dt);
				pair.Value.TryTick(character);
			}
			else
			{
				/*if (pair.Value.Stacks.Count > 0 && pair.Value.Buff.IndependantStackTimer)
				{
					pair.Value.RemoveStack(this.gameObject);

				}*/
				foreach (Buff stack in pair.Value.Stacks)
				{
					stack.RemoveStack(character);
				}
				pair.Value.Remove(character);
				buffs.Remove(pair.Key);
			}
		}
	}

	public void Apply(BuffTemplate template, Character caster = null)
	{
		Buff buffInstance;
		if (!buffs.TryGetValue(template.Name, out buffInstance))
		{
			buffInstance = new Buff(template.ID, caster);
			buffInstance.Apply(character);
			buffs.Add(template.Name, buffInstance);
		}
		else if (template.MaxStacks > 0 && buffInstance.Stacks.Count < template.MaxStacks)
		{
			Buff newStack = new Buff(template.ID, caster);
			buffInstance.AddStack(newStack, character);
			buffInstance.ResetDuration();
		}
		else
		{
			buffInstance.ResetDuration();
		}
	}

	public void Remove(string buffName)
	{
		Buff buffInstance;
		if (buffs.TryGetValue(buffName, out buffInstance))
		{
			foreach (Buff stack in buffInstance.Stacks)
			{
				stack.RemoveStack(character);
			}
			buffInstance.Remove(character);
			buffs.Remove(buffName);
		}
	}

	public void RemoveAll()
	{
		foreach (KeyValuePair<string, Buff> pair in new Dictionary<string, Buff>(buffs))
		{
			if (!pair.Value.Template.IsPermanent)
			{
				foreach (Buff stack in pair.Value.Stacks)
				{
					stack.RemoveStack(character);
				}
				pair.Value.Remove(character);
				buffs.Remove(pair.Key);
			}
		}
	}
}