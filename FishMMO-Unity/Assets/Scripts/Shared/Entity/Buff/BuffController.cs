using FishNet.Connection;
using FishNet.Serializing;
using FishNet.Transporting;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Controls the application, ticking, and removal of buffs for a character, including network synchronization.
	/// </summary>
	public class BuffController : CharacterBehaviour, IBuffController
	{
		/// <summary>
		/// Internal dictionary mapping buff template IDs to active buff instances.
		/// </summary>
		private Dictionary<int, Buff> buffs = new Dictionary<int, Buff>();

		/// <summary>
		/// Public accessor for the character's active buffs.
		/// </summary>
		public Dictionary<int, Buff> Buffs { get { return buffs; } }

		/// <summary>
		/// Temporary list of keys to remove after update loop (avoids modifying dictionary during iteration).
		/// </summary>
		private List<int> keysToRemove = new List<int>();

		/// <summary>
		/// Reads the buff state from the network payload and applies each buff to the character.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="reader">The network reader to read from.</param>
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

		/// <summary>
		/// Writes the current buff state to the network payload for synchronization.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="writer">The network writer to write to.</param>
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

		/// <summary>
		/// Unity Update callback. Handles ticking, expiration, and removal of buffs each frame.
		/// </summary>
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
						// Remove a stack and reset duration if stacks remain
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

		/// <summary>
		/// Applies a buff to the character by template, creating a new instance if needed and handling stacking.
		/// </summary>
		/// <param name="template">The buff template to apply.</param>
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

			// Handle stacking logic
			if (template.MaxStacks > 0 && buffInstance.Stacks < template.MaxStacks)
			{
				buffInstance.AddStack(Character);
				buffInstance.ResetDuration();
			}
			else
			{
				buffInstance.ResetDuration();
			}

			template.OnApplyFX(buffInstance, Character);
		}

		/// <summary>
		/// Applies a buff instance to the character if not already present, invoking appropriate events.
		/// </summary>
		/// <param name="buff">The buff instance to apply.</param>
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

		/// <summary>
		/// Removes a buff by template ID, invoking removal events and cleaning up.
		/// </summary>
		/// <param name="buffID">The template ID of the buff to remove.</param>
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

		/// <summary>
		/// Removes a random buff or debuff from the character, with options to include buffs and/or debuffs.
		/// </summary>
		/// <param name="rng">The random number generator to use.</param>
		/// <param name="includeBuffs">Whether to include buffs in the selection.</param>
		/// <param name="includeDebuffs">Whether to include debuffs in the selection.</param>
		public void RemoveRandom(System.Random rng, bool includeBuffs = false, bool includeDebuffs = false)
		{
			if (rng == null)
			{
				return;
			}

			List<int> keys = new List<int>(buffs.Keys);

			if (keys.Count < 1)
			{
				return;
			}

			int key;

			int attempts = 0;

			// We can try a maximum of 10 times to remove a random buff
			while (attempts < 10)
			{
				key = keys[rng.Next(0, keys.Count)];

				// Get the buff instance
				if (buffs.TryGetValue(key, out Buff buffInstance) && !buffInstance.Template.IsPermanent)
				{
					// Check if the buff meets the conditions
					if ((includeBuffs && !buffInstance.Template.IsDebuff) || (includeDebuffs && buffInstance.Template.IsDebuff))
					{
						// Remove the buff
						Remove(key);
						return;
					}
				}

				// Increment the attempt counter
				attempts++;
			}
		}

		/// <summary>
		/// Removes all non-permanent buffs from the character, optionally suppressing removal events.
		/// </summary>
		/// <param name="ignoreInvokeRemove">If true, does not invoke OnRemoveBuff/OnRemoveDebuff events.</param>
		public void RemoveAll(bool ignoreInvokeRemove = false)
		{
			foreach (KeyValuePair<int, Buff> pair in new Dictionary<int, Buff>(buffs))
			{
				if (!pair.Value.Template.IsPermanent)
				{
					pair.Value.Remove(Character);
					buffs.Remove(pair.Key);

					if (!ignoreInvokeRemove)
					{
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
		}

		/// <summary>
		/// Resets the buff controller state, clearing all buffs.
		/// </summary>
		/// <param name="asServer">Whether the reset is being performed on the server.</param>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

			buffs.Clear();
		}

#if !UNITY_SERVER
		/// <summary>
		/// Called when the character is started on the client. Registers broadcast listeners for buff updates.
		/// </summary>
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

		/// <summary>
		/// Called when the character is stopped on the client. Unregisters buff update listeners.
		/// </summary>
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
		/// Handles a broadcast from the server to add a single buff.
		/// </summary>
		/// <param name="msg">The buff add message.</param>
		/// <param name="channel">The network channel.</param>
		private void OnClientBuffAddBroadcastReceived(BuffAddBroadcast msg, Channel channel)
		{
			BaseBuffTemplate template = BaseBuffTemplate.Get<BaseBuffTemplate>(msg.TemplateID);
			if (template != null)
			{
				Apply(template);
			}
		}

		/// <summary>
		/// Handles a broadcast from the server to add multiple buffs.
		/// </summary>
		/// <param name="msg">The multiple buff add message.</param>
		/// <param name="channel">The network channel.</param>
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
		/// Handles a broadcast from the server to remove a single buff.
		/// </summary>
		/// <param name="msg">The buff remove message.</param>
		/// <param name="channel">The network channel.</param>
		private void OnClientBuffRemoveBroadcastReceived(BuffRemoveBroadcast msg, Channel channel)
		{
			BaseBuffTemplate template = BaseBuffTemplate.Get<BaseBuffTemplate>(msg.TemplateID);
			if (template != null)
			{
				Remove(template.ID);
			}
		}

		/// <summary>
		/// Handles a broadcast from the server to remove multiple buffs.
		/// </summary>
		/// <param name="msg">The multiple buff remove message.</param>
		/// <param name="channel">The network channel.</param>
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