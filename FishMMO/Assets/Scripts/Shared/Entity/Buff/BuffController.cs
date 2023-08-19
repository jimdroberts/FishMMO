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

		ClientManager.RegisterBroadcast<BuffAddBroadcast>(OnClientBuffAddBroadcastReceived);
		ClientManager.RegisterBroadcast<BuffAddMultipleBroadcast>(OnClientBuffAddMultipleBroadcastReceived);
		ClientManager.RegisterBroadcast<BuffRemoveBroadcast>(OnClientBuffRemoveBroadcastReceived);
		ClientManager.RegisterBroadcast<BuffRemoveMultipleBroadcast>(OnClientBuffRemoveMultipleBroadcastReceived);
	}

	public override void OnStopClient()
	{
		base.OnStopClient();

		if (base.IsOwner)
		{
			ClientManager.UnregisterBroadcast<BuffAddBroadcast>(OnClientBuffAddBroadcastReceived);
			ClientManager.UnregisterBroadcast<BuffAddMultipleBroadcast>(OnClientBuffAddMultipleBroadcastReceived);
			ClientManager.UnregisterBroadcast<BuffRemoveBroadcast>(OnClientBuffRemoveBroadcastReceived);
			ClientManager.UnregisterBroadcast<BuffRemoveMultipleBroadcast>(OnClientBuffRemoveMultipleBroadcastReceived);
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

	public void Apply(BuffTemplate template)
	{
		Buff buffInstance;
		if (!buffs.TryGetValue(template.Name, out buffInstance))
		{
			buffInstance = new Buff(template.ID);
			buffInstance.Apply(character);
			buffs.Add(template.Name, buffInstance);
		}
		else if (template.MaxStacks > 0 && buffInstance.Stacks.Count < template.MaxStacks)
		{
			Buff newStack = new Buff(template.ID);
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
		if (buffs.TryGetValue(buffName, out Buff buffInstance))
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

	/// <summary>
	/// Server sent a buff add broadcast.
	/// </summary>
	private void OnClientBuffAddBroadcastReceived(BuffAddBroadcast msg)
	{
		if (BuffTemplate.Cache.TryGetValue(msg.templateID, out BuffTemplate template))
		{
			Apply(template);
		}
	}

	/// <summary>
	/// Server sent a multiple buff add broadcasts.
	/// </summary>
	private void OnClientBuffAddMultipleBroadcastReceived(BuffAddMultipleBroadcast msg)
	{
		foreach (BuffAddBroadcast subMsg in msg.buffs)
		{
			if (BuffTemplate.Cache.TryGetValue(subMsg.templateID, out BuffTemplate template))
			{
				Apply(template);
			}
		}
	}

	/// <summary>
	/// Server sent a remove buff add broadcast.
	/// </summary>
	private void OnClientBuffRemoveBroadcastReceived(BuffRemoveBroadcast msg)
	{
		if (BuffTemplate.Cache.TryGetValue(msg.templateID, out BuffTemplate template))
		{
			Remove(template.Name);
		}
	}

	/// <summary>
	/// Server sent a remove multiple buff add broadcasts.
	/// </summary>
	private void OnClientBuffRemoveMultipleBroadcastReceived(BuffRemoveMultipleBroadcast msg)
	{
		foreach (BuffRemoveBroadcast subMsg in msg.buffs)
		{
			if (BuffTemplate.Cache.TryGetValue(subMsg.templateID, out BuffTemplate template))
			{
				Remove(template.Name);
			}
		}
	}
}