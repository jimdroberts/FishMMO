using FishNet.Object;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	[RequireComponent(typeof(Character))]
	public class BuffController : NetworkBehaviour
	{
		private Dictionary<int, Buff> buffs = new Dictionary<int, Buff>();

		public Character Character;

		public Dictionary<int, Buff> Buffs { get { return buffs; } }

		void Update()
		{
			float dt = Time.deltaTime;
			foreach (KeyValuePair<int, Buff> pair in new Dictionary<int, Buff>(buffs))
			{
				pair.Value.SubtractTime(dt);
				if (pair.Value.RemainingTime > 0.0f)
				{
					pair.Value.SubtractTickTime(dt);
					pair.Value.TryTick(Character);
				}
				else
				{
					if (pair.Value.Stacks.Count > 0 && pair.Value.Template.IndependantStackTimer)
					{
						pair.Value.RemoveStack(Character);

					}
					foreach (Buff stack in pair.Value.Stacks)
					{
						stack.RemoveStack(Character);
					}
					pair.Value.Remove(Character);
					buffs.Remove(pair.Key);
				}
			}
		}

		public void Apply(BuffTemplate template)
		{
			Buff buffInstance;
			if (!buffs.TryGetValue(template.ID, out buffInstance))
			{
				buffInstance = new Buff(template.ID);
				buffInstance.Apply(Character);
				buffs.Add(template.ID, buffInstance);
			}
			else if (template.MaxStacks > 0 && buffInstance.Stacks.Count < template.MaxStacks)
			{
				Buff newStack = new Buff(template.ID);
				buffInstance.AddStack(newStack, Character);
				buffInstance.ResetDuration();
			}
			else
			{
				buffInstance.ResetDuration();
			}
		}

		public void Apply(Buff buff)
		{
			if (!buffs.ContainsKey(buff.Template.ID))
			{
				buffs.Add(buff.Template.ID, buff);
			}
		}

		public void Remove(int buffID)
		{
			if (buffs.TryGetValue(buffID, out Buff buffInstance))
			{
				foreach (Buff stack in buffInstance.Stacks)
				{
					stack.RemoveStack(Character);
				}
				buffInstance.Remove(Character);
				buffs.Remove(buffID);
			}
		}

		public void RemoveAll()
		{
			foreach (KeyValuePair<int, Buff> pair in new Dictionary<int, Buff>(buffs))
			{
				if (!pair.Value.Template.IsPermanent)
				{
					foreach (Buff stack in pair.Value.Stacks)
					{
						stack.RemoveStack(Character);
					}
					pair.Value.Remove(Character);
					buffs.Remove(pair.Key);
				}
			}
		}

#if !UNITY_SERVER
		public override void OnStartClient()
		{
			base.OnStartClient();

			if (!base.IsOwner)
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

		/// <summary>
		/// Server sent a buff add broadcast.
		/// </summary>
		private void OnClientBuffAddBroadcastReceived(BuffAddBroadcast msg)
		{
			BuffTemplate template = BuffTemplate.Get<BuffTemplate>(msg.templateID);
			if (template != null)
			{
				Apply(template);
			}
		}

		/// <summary>
		/// Server sent a multiple buff add broadcast.
		/// </summary>
		private void OnClientBuffAddMultipleBroadcastReceived(BuffAddMultipleBroadcast msg)
		{
			foreach (BuffAddBroadcast subMsg in msg.buffs)
			{
				BuffTemplate template = BuffTemplate.Get<BuffTemplate>(subMsg.templateID);
				if (template != null)
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
			BuffTemplate template = BuffTemplate.Get<BuffTemplate>(msg.templateID);
			if (template != null)
			{
				Remove(template.ID);
			}
		}

		/// <summary>
		/// Server sent a remove multiple buff add broadcast.
		/// </summary>
		private void OnClientBuffRemoveMultipleBroadcastReceived(BuffRemoveMultipleBroadcast msg)
		{
			foreach (BuffRemoveBroadcast subMsg in msg.buffs)
			{
				BuffTemplate template = BuffTemplate.Get<BuffTemplate>(subMsg.templateID);
				if (template != null)
				{
					Remove(template.ID);
				}
			}
		}
#endif
	}
}