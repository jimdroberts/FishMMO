using FishNet.Connection;
using FishNet.Serializing;
using FishNet.Transporting;
using System.Collections.Generic;
#if UNITY_SERVER
using FishNet.Broadcast;
#endif
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Controls the application, ticking, and removal of buffs for a character, including network synchronization.
	/// Uses FishNet TimeManager.OnTick for deterministic tick-aligned simulation.
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
		/// Reusable list of keys to remove after update loop (avoids allocation each frame).
		/// </summary>
		private readonly List<int> keysToRemove = new List<int>();

		public override void OnStartNetwork()
		{
			base.OnStartNetwork();

			if (base.TimeManager != null)
			{
				base.TimeManager.OnTick += TimeManager_OnTick;
			}
		}

		public override void OnStopNetwork()
		{
			base.OnStopNetwork();

			if (base.TimeManager != null)
			{
				base.TimeManager.OnTick -= TimeManager_OnTick;
			}
		}

		/// <summary>
		/// TimeManager tick callback. Drives buff ticking using the fixed network tick delta.
		/// </summary>
		private void TimeManager_OnTick()
		{
			Tick((float)base.TimeManager.TickDelta);
		}

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
				int tickCount = reader.ReadInt32();

				Buff buff = new Buff(templateID, remainingTime, tickTime, stacks, tickCount);
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
			if (buffs.Count < 1)
			{
				writer.WriteInt32(0);
				return;
			}

			writer.WriteInt32(buffs.Count);
			foreach (Buff buff in buffs.Values)
			{
				writer.WriteInt32(buff.Template.ID);
				writer.WriteSingle(buff.RemainingTime);
				writer.WriteSingle(buff.TickTime);
				writer.WriteInt32(buff.Stacks);
				writer.WriteInt32(buff.TickCount);
			}
		}

		/// <summary>
		/// Deterministic buff tick. Advances all buff timers by the given delta, triggers ticks,
		/// removes expired stacks, and queues fully expired buffs for removal.
		/// </summary>
		/// <param name="deltaTime">The time step to advance (seconds).</param>
		public void Tick(float deltaTime)
		{
			foreach (var pair in buffs)
			{
				Buff buff = pair.Value;
				buff.SubtractTime(deltaTime);

				IBuffController.OnSubtractTime?.Invoke(buff);

				if (buff.RemainingTime > 0.0f)
				{
					buff.SubtractTickTime(deltaTime);
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
						keysToRemove.Add(pair.Key);
					}
				}
			}

			for (int i = 0; i < keysToRemove.Count; i++)
			{
				Remove(keysToRemove[i]);
			}
			keysToRemove.Clear();
		}

		/// <summary>
		/// Applies a buff to the character by template, creating a new instance if needed and handling stacking.
		/// </summary>
		/// <param name="template">The buff template to apply.</param>
		public void Apply(BaseBuffTemplate template)
		{
			if (template == null) return;

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

			template.OnApplyFX(buffInstance, Character);

#if UNITY_SERVER
			if (base.IsServerStarted)
			{
				SendBuffAddUpdate(template.ID);
			}
#endif
		}

		/// <summary>
		/// Applies a pre-constructed buff instance to the character if not already present.
		/// Restores attribute modifiers for the base application and each existing stack
		/// (e.g., from DB or network payload). Stacks are not incremented because they are already set.
		/// </summary>
		/// <param name="buff">The buff instance to apply.</param>
		public void Apply(Buff buff)
		{
			if (buff == null) return;

			if (!buffs.ContainsKey(buff.Template.ID))
			{
				buff.Apply(Character);
				buffs.Add(buff.Template.ID, buff);

				for (int i = 0; i < buff.Stacks; ++i)
				{
					buff.Template.OnApplyStack(buff, Character);
				}

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
		/// Removes a buff by template ID, cleaning up all stack modifiers and the base application,
		/// then invoking removal events.
		/// </summary>
		/// <param name="buffID">The template ID of the buff to remove.</param>
		public void Remove(int buffID)
		{
			if (buffs.TryGetValue(buffID, out Buff buffInstance))
			{
				// Remove all remaining stack modifiers before removing the base effect
				while (buffInstance.Stacks > 0)
				{
					buffInstance.RemoveStack(Character);
				}

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

#if UNITY_SERVER
				if (base.IsServerStarted)
				{
					SendBuffRemoveUpdate(buffID);
				}
#endif
			}
		}

		/// <summary>
		/// Removes a random non-permanent buff or debuff, filtered by inclusion flags.
		/// Uses a single pass to build eligible candidates, avoiding retry loops.
		/// </summary>
		/// <param name="rng">The random number generator to use.</param>
		/// <param name="includeBuffs">Whether to include buffs in the selection.</param>
		/// <param name="includeDebuffs">Whether to include debuffs in the selection.</param>
		public void RemoveRandom(System.Random rng, bool includeBuffs = false, bool includeDebuffs = false)
		{
			if (rng == null || buffs.Count < 1) return;

			// Build list of eligible buff IDs in a single pass
			keysToRemove.Clear();
			foreach (var pair in buffs)
			{
				Buff buff = pair.Value;
				if (buff.Template.IsPermanent) continue;
				if (includeBuffs && !buff.Template.IsDebuff)
				{
					keysToRemove.Add(pair.Key);
				}
				else if (includeDebuffs && buff.Template.IsDebuff)
				{
					keysToRemove.Add(pair.Key);
				}
			}

			if (keysToRemove.Count > 0)
			{
				int index = rng.Next(0, keysToRemove.Count);
				int key = keysToRemove[index];
				keysToRemove.Clear();
				Remove(key);
			}
			else
			{
				keysToRemove.Clear();
			}
		}

		/// <summary>
		/// Removes all non-permanent buffs from the character, cleaning up all stack modifiers.
		/// </summary>
		/// <param name="ignoreInvokeRemove">If true, does not invoke OnRemoveBuff/OnRemoveDebuff events.</param>
		public void RemoveAll(bool ignoreInvokeRemove = false)
		{
			// Collect keys to remove (reuse keysToRemove to avoid allocation)
			keysToRemove.Clear();
			foreach (var pair in buffs)
			{
				if (!pair.Value.Template.IsPermanent)
				{
					keysToRemove.Add(pair.Key);
				}
			}

			for (int i = 0; i < keysToRemove.Count; i++)
			{
				int key = keysToRemove[i];
				if (buffs.TryGetValue(key, out Buff buff))
				{
					// Remove all stack modifiers
					while (buff.Stacks > 0)
					{
						buff.RemoveStack(Character);
					}

					buff.Remove(Character);
					buffs.Remove(key);

					if (!ignoreInvokeRemove)
					{
						if (buff.Template.IsDebuff)
						{
							IBuffController.OnRemoveDebuff?.Invoke(buff);
						}
						else
						{
							IBuffController.OnRemoveBuff?.Invoke(buff);
						}
					}
				}
			}
			keysToRemove.Clear();
		}

		/// <summary>
		/// Resets the buff controller state, clearing all buffs without invoking removal events.
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
			ClientManager.RegisterBroadcast<CharacterObserverBuffAddBroadcast>(OnClientCharacterObserverBuffAddBroadcastReceived);
			ClientManager.RegisterBroadcast<CharacterObserverBuffRemoveBroadcast>(OnClientCharacterObserverBuffRemoveBroadcastReceived);
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
				ClientManager.UnregisterBroadcast<CharacterObserverBuffAddBroadcast>(OnClientCharacterObserverBuffAddBroadcastReceived);
				ClientManager.UnregisterBroadcast<CharacterObserverBuffRemoveBroadcast>(OnClientCharacterObserverBuffRemoveBroadcastReceived);
			}
		}

		/// <summary>
		/// Resolves a target buff controller from the client character cache.
		/// </summary>
		private static bool TryGetCachedBuffController(long characterID, out IBuffController buffController)
		{
			buffController = null;
			if (characterID <= 0) return false;

			if (!BaseCharacter.ClientCharacters.TryGetValue(characterID, out ICharacter character) ||
				character == null)
			{
				return false;
			}

			return character.TryGet(out buffController);
		}

		/// <summary>
		/// Handles a broadcast from the server to add a single buff.
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
		/// Handles a broadcast from the server to add multiple buffs.
		/// </summary>
		private void OnClientBuffAddMultipleBroadcastReceived(BuffAddMultipleBroadcast msg, Channel channel)
		{
			if (msg.Buffs == null) return;
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
		private void OnClientBuffRemoveBroadcastReceived(BuffRemoveBroadcast msg, Channel channel)
		{
			Remove(msg.TemplateID);
		}

		/// <summary>
		/// Handles a broadcast from the server to remove multiple buffs.
		/// </summary>
		private void OnClientBuffRemoveMultipleBroadcastReceived(BuffRemoveMultipleBroadcast msg, Channel channel)
		{
			if (msg.Buffs == null) return;
			foreach (BuffRemoveBroadcast subMsg in msg.Buffs)
			{
				Remove(subMsg.TemplateID);
			}
		}

		/// <summary>
		/// Handles observer-targeted add buff updates for a specific character.
		/// </summary>
		private void OnClientCharacterObserverBuffAddBroadcastReceived(CharacterObserverBuffAddBroadcast msg, Channel channel)
		{
			if (!TryGetCachedBuffController(msg.CharacterID, out IBuffController buffController) ||
				msg.Buffs == null)
			{
				return;
			}

			foreach (BuffAddBroadcast subMsg in msg.Buffs)
			{
				BaseBuffTemplate template = BaseBuffTemplate.Get<BaseBuffTemplate>(subMsg.TemplateID);
				if (template != null)
				{
					buffController.Apply(template);
				}
			}
		}

		/// <summary>
		/// Handles observer-targeted remove buff updates for a specific character.
		/// </summary>
		private void OnClientCharacterObserverBuffRemoveBroadcastReceived(CharacterObserverBuffRemoveBroadcast msg, Channel channel)
		{
			if (!TryGetCachedBuffController(msg.CharacterID, out IBuffController buffController) ||
				msg.Buffs == null)
			{
				return;
			}

			foreach (BuffRemoveBroadcast subMsg in msg.Buffs)
			{
				buffController.Remove(subMsg.TemplateID);
			}
		}
#endif

#if UNITY_SERVER
		/// <summary>
		/// Sends an add-buff update to owner and observers.
		/// </summary>
		private void SendBuffAddUpdate(int templateID)
		{
			if (Character == null) return;

			BroadcastToOwnerOnly(Character, new BuffAddBroadcast()
			{
				TemplateID = templateID,
			}, Channel.Reliable);

			CharacterObserverBuffAddBroadcast observerBroadcast = new CharacterObserverBuffAddBroadcast()
			{
				CharacterID = Character.ID,
				Buffs = new List<BuffAddBroadcast>(1)
				{
					new BuffAddBroadcast() { TemplateID = templateID },
				},
			};
			BroadcastToObserversOnly(Character, observerBroadcast, Channel.Reliable);
		}

		/// <summary>
		/// Sends a remove-buff update to owner and observers.
		/// </summary>
		private void SendBuffRemoveUpdate(int templateID)
		{
			if (Character == null) return;

			BroadcastToOwnerOnly(Character, new BuffRemoveBroadcast()
			{
				TemplateID = templateID,
			}, Channel.Reliable);

			CharacterObserverBuffRemoveBroadcast observerBroadcast = new CharacterObserverBuffRemoveBroadcast()
			{
				CharacterID = Character.ID,
				Buffs = new List<BuffRemoveBroadcast>(1)
				{
					new BuffRemoveBroadcast() { TemplateID = templateID },
				},
			};
			BroadcastToObserversOnly(Character, observerBroadcast, Channel.Reliable);
		}

		/// <summary>
		/// Broadcasts the payload to only the owner of the character.
		/// </summary>
		private static void BroadcastToOwnerOnly<T>(ICharacter character, T broadcast, Channel channel)
			where T : struct, IBroadcast
		{
			if (character?.Owner != null)
			{
				character.Owner.Broadcast(broadcast, true, channel);
			}
		}

		/// <summary>
		/// Broadcasts the payload to all current observers of the character, excluding the owner.
		/// </summary>
		private static void BroadcastToObserversOnly<T>(ICharacter character, T broadcast, Channel channel)
			where T : struct, IBroadcast
		{
			if (character == null || character.Observers == null) return;

			NetworkConnection owner = character.Owner;
			foreach (NetworkConnection observer in character.Observers)
			{
				if (observer == null || observer == owner) continue;
				observer.Broadcast(broadcast, true, channel);
			}
		}
#endif
	}
}