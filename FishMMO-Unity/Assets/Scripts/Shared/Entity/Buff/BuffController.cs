using FishNet.Connection;
using FishNet.Serializing;
using FishNet.Transporting;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace FishMMO.Shared
{
	public class BuffController : CharacterBehaviour, IBuffController
	{
		private Dictionary<int, Buff> buffs = new Dictionary<int, Buff>();

		public Dictionary<int, Buff> Buffs { get { return buffs; } }

		private List<int> keysToRemove = new List<int>();

		public override void ReadPayload(NetworkConnection conn, Reader reader)
		{
			int buffCount = reader.ReadInt32();
			for (int i = 0; i < buffCount; ++i)
			{
				int templateID = reader.ReadInt32();
				float remainingTime = reader.ReadSingle();
				float tickTime = reader.ReadSingle();
				int stacks = reader.ReadInt32();

				Buff buff = new Buff(templateID, remainingTime, tickTime, stacks);

				Apply(buff);
			}
		}

		public override void WritePayload(NetworkConnection conn, Writer writer)
		{
			if (Buffs == null ||
				Buffs.Count < 1)
			{
				writer.WriteUInt32(0);
				return;
			}

			writer.WriteInt32(Buffs.Count);
			foreach (Buff buff in buffs.Values)
			{
				if (buff == null)
				{
					continue;
				}
				writer.WriteInt32(buff.Template.ID);
				writer.WriteSingle(buff.RemainingTime);
				writer.WriteSingle(buff.TickTime);
				writer.WriteInt32(buff.Stacks);
			}
		}

		void Update()
		{
			float dt = Time.deltaTime;

			foreach (var pair in buffs)
			{
				var buff = pair.Value;
				buff.SubtractTime(dt);

				IBuffController.OnSubtractTime?.Invoke(buff);

				if (buff.RemainingTime > 0.0f)
				{
					buff.SubtractTickTime(dt);
					buff.TryTick(Character);
				}
				else
				{
					if (buff.Stacks > 0)
					{
						buff.RemoveStack(Character);
						buff.ResetDuration();
					}
					else
					{
						// Add the key to the list for later removal
						keysToRemove.Add(pair.Key);
					}
				}
			}

			// Remove keys outside the loop to avoid modifying the dictionary during iteration
			foreach (var key in keysToRemove)
			{
				Remove(key);
			}
			keysToRemove.Clear();
		}

		public void Apply(BaseBuffTemplate template)
		{
			if (!buffs.TryGetValue(template.ID, out Buff buffInstance))
			{
				buffInstance = new Buff(template.ID);
				buffInstance.Apply(Character);
				buffs.Add(template.ID, buffInstance);

				if (template.IsDebuff)
				{
					IBuffController.OnAddDebuff?.Invoke(buffInstance);
				}
				else
				{
					IBuffController.OnAddBuff?.Invoke(buffInstance);
				}
			}
			
			if (template.MaxStacks > 0 && buffInstance.Stacks < template.MaxStacks)
			{
				buffInstance.AddStack(Character);
				buffInstance.ResetDuration();
			}
			else
			{
				buffInstance.ResetDuration();
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Apply(Buff buff)
		{
			if (!buffs.ContainsKey(buff.Template.ID))
			{
				buffs.Add(buff.Template.ID, buff);

				if (buff.Template.IsDebuff)
				{
					IBuffController.OnAddDebuff?.Invoke(buff);
				}
				else
				{
					IBuffController.OnAddBuff?.Invoke(buff);
				}
			}
		}

		public void Remove(int buffID)
		{
			if (buffs.TryGetValue(buffID, out Buff buffInstance))
			{
				buffInstance.Remove(Character);
				buffs.Remove(buffID);

				if (buffInstance.Template.IsDebuff)
				{
					IBuffController.OnRemoveDebuff?.Invoke(buffInstance);
				}
				else
				{
					IBuffController.OnRemoveBuff?.Invoke(buffInstance);
				}
			}
		}

		public void RemoveAll()
		{
			foreach (KeyValuePair<int, Buff> pair in new Dictionary<int, Buff>(buffs))
			{
				if (!pair.Value.Template.IsPermanent)
				{
					pair.Value.Remove(Character);
					buffs.Remove(pair.Key);

					if (pair.Value.Template.IsDebuff)
					{
						IBuffController.OnRemoveDebuff?.Invoke(pair.Value);
					}
					else
					{
						IBuffController.OnRemoveBuff?.Invoke(pair.Value);
					}
				}
			}
		}

		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

			buffs.Clear();
		}

#if !UNITY_SERVER
		public override void OnStartCharacter()
		{
			base.OnStartCharacter();

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

		public override void OnStopCharacter()
		{
			base.OnStopCharacter();

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
		private void OnClientBuffAddBroadcastReceived(BuffAddBroadcast msg, Channel channel)
		{
			BaseBuffTemplate template = BaseBuffTemplate.Get<BaseBuffTemplate>(msg.TemplateID);
			if (template != null)
			{
				Apply(template);
			}
		}

		/// <summary>
		/// Server sent a multiple buff add broadcast.
		/// </summary>
		private void OnClientBuffAddMultipleBroadcastReceived(BuffAddMultipleBroadcast msg, Channel channel)
		{
			foreach (BuffAddBroadcast subMsg in msg.Buffs)
			{
				BaseBuffTemplate template = BaseBuffTemplate.Get<BaseBuffTemplate>(subMsg.TemplateID);
				if (template != null)
				{
					Apply(template);
				}
			}
		}

		/// <summary>
		/// Server sent a remove buff add broadcast.
		/// </summary>
		private void OnClientBuffRemoveBroadcastReceived(BuffRemoveBroadcast msg, Channel channel)
		{
			BaseBuffTemplate template = BaseBuffTemplate.Get<BaseBuffTemplate>(msg.TemplateID);
			if (template != null)
			{
				Remove(template.ID);
			}
		}

		/// <summary>
		/// Server sent a remove multiple buff add broadcast.
		/// </summary>
		private void OnClientBuffRemoveMultipleBroadcastReceived(BuffRemoveMultipleBroadcast msg, Channel channel)
		{
			foreach (BuffRemoveBroadcast subMsg in msg.Buffs)
			{
				BaseBuffTemplate template = BaseBuffTemplate.Get<BaseBuffTemplate>(subMsg.TemplateID);
				if (template != null)
				{
					Remove(template.ID);
				}
			}
		}
#endif
	}
}