using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing.Timing;
using FishNet.Serializing;
using FishMMO.Logging;
using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>A resource value restored onto a mercenary as it comes back out.</summary>
	public struct MercenaryPersistedResource
	{
		/// <summary>The resource attribute (health, mana, stamina).</summary>
		public int TemplateID;
		/// <summary>Its current value.</summary>
		public float CurrentValue;
	}

	/// <summary>A buff restored onto a mercenary as it comes back out.</summary>
	public struct MercenaryPersistedBuff
	{
		/// <summary>The buff template.</summary>
		public int TemplateID;
		/// <summary>Seconds it had left.</summary>
		public float RemainingTime;
		/// <summary>Seconds to its next tick.</summary>
		public float TickTime;
		/// <summary>Its stacks.</summary>
		public int Stacks;
		/// <summary>Ticks it had done.</summary>
		public int TickCount;
	}

	/// <summary>
	/// A hired mercenary (issue #135): an NPC that follows, guards and fights for the player who hired it, driven by the
	/// pet AI (<see cref="IAICompanion"/>), with its kit's abilities and the gear its owner keeps for it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A companion, not a pet.</b> It does not take a pet slot and the pet system never sees it; it takes a party seat
	/// instead (<see cref="PartySeatCompanions"/>). Through <see cref="CompanionOwner"/> the harm rule, loot rights, kill
	/// credit and the party meters see through it to its owner, as they do for a pet.
	/// </para>
	/// <para>
	/// <b>Spawned without an owning connection</b>, as a pet is: its brain runs on the server. Its owner and kit ride the
	/// spawn payload; its stance is the owner's business and is sent to the owner only.
	/// </para>
	/// <para>
	/// <b>Gear is worn, not held.</b> The items stay in the owner's mercenary gear container; the mercenary only wears them
	/// (<see cref="ItemEquippable.Equip"/>), so their attributes apply to it, and taking them off (or the mercenary
	/// leaving) gives them back their wearer and nothing else.
	/// </para>
	/// </remarks>
	public class Mercenary : NPC, IAICompanion
	{
		/// <summary>Live mercenaries on this peer, by their owner's character ID.</summary>
		private static readonly Dictionary<long, Mercenary> byOwner = new Dictionary<long, Mercenary>();

		/// <summary>Raised on a client when a mercenary appears or leaves for <c>ownerID</c> (null when it left).</summary>
		public static event Action<long, Mercenary> OnClientMercenaryChanged;

		/// <summary>The live mercenary hired by <paramref name="ownerID"/> on this peer, or null.</summary>
		public static Mercenary GetByOwner(long ownerID)
		{
			return ownerID > 0 && byOwner.TryGetValue(ownerID, out Mercenary mercenary) && mercenary != null ? mercenary : null;
		}

		/// <summary>The kit this mercenary was hired with. Server: set before the spawn; clients: from the payload.</summary>
		public MercenaryKitTemplate Kit { get; set; }

		/// <summary>Clients: the owner's character ID from the payload; 0 for none.</summary>
		private long payloadOwnerID;

		/// <summary>The kit's ID as read from the payload, resolved lazily (templates may load after the object).</summary>
		private int payloadKitID;

		private ICharacter owner;

		/// <summary>The character that hired it.</summary>
		public ICharacter MercenaryOwner
		{
			get
			{
#if !UNITY_SERVER
				// Clients: resolved from the payload's ID, lazily, and re-checked so a pooled reuse is never kept (as Pet does).
				if (payloadOwnerID != 0 && (owner == null || owner.ID != payloadOwnerID))
				{
					owner = BaseCharacter.ClientCharacters.TryGetValue(payloadOwnerID, out ICharacter found) && !ReferenceEquals(found, this)
						? found
						: null;
				}
#endif
				return owner;
			}
			set
			{
				if (ReferenceEquals(owner, value))
				{
					return;
				}
				owner = value;
				// The threat dispatcher pairs a companion with its owner: hit either and both are in the fight.
				AICompanions.RaiseOwnerChanged(this, value);
			}
		}

		/// <summary>The owner's character ID: the server's assignment, or the payload's on a client.</summary>
		public long OwnerID => owner != null ? owner.ID : payloadOwnerID;

		/// <inheritdoc />
		/// <remarks>A mercenary fights for its owner: the core's companion seam.</remarks>
		public override ICharacter CompanionOwner => MercenaryOwner;

		/// <inheritdoc />
		ICharacter IAICompanion.PetOwner => MercenaryOwner;

		/// <summary>When it fights. Server-authoritative; its owner learns it from <c>MercenaryStateBroadcast</c>.</summary>
		public PetStance Stance { get; set; } = PetStance.Defensive;

		/// <inheritdoc />
		/// <remarks>Always at its owner's heel: a mercenary takes no Stay order.</remarks>
		public PetMovementOrder MovementOrder => PetMovementOrder.Follow;

		/// <summary>Shown under its name.</summary>
		public override string Title => IsFlagged(CharacterFlags.IsDead) ? base.Title : "Mercenary";

		/// <summary>Goldenrod, as the Mercenary who was hired.</summary>
		public override Color TitleColor => IsFlagged(CharacterFlags.IsDead) ? base.TitleColor : TinyColor.ToUnityColor(TinyColor.goldenrod);

		/// <summary>Server: gear to wear as it spawns, from its owner's container. Worn in <see cref="OnStartServer"/>.</summary>
		public List<Item> PendingGear { get; set; }

		/// <summary>Server: resource values restored as it spawns (null for a fresh hire, which arrives whole).</summary>
		public List<MercenaryPersistedResource> PersistedResources { get; set; }

		/// <summary>Server: buffs restored as it spawns.</summary>
		public List<MercenaryPersistedBuff> PersistedBuffs { get; set; }

		/// <summary>The items it is wearing.</summary>
		private readonly List<Item> wornGear = new List<Item>();

		/// <summary>The items it is wearing (its owner's, worn).</summary>
		public IReadOnlyList<Item> WornGear => wornGear;

		/// <summary>
		/// Server spawn: the kit's abilities first (the NPC learns its list in its own start), then the gear, then the
		/// restored resources and buffs, all before the spawn payload is written.
		/// </summary>
		/// <remarks>
		/// Gear before resources: a saved health value is written against a maximum that already includes the gear, so a
		/// mercenary that left at full health comes back at full health.
		/// </remarks>
		public override void OnStartServer()
		{
			if (Kit != null)
			{
				// The kit is the whole spellbook: nothing a prefab authored, and nothing else, ever.
				Abilities.Clear();
				Abilities.AddRange(Kit.Abilities);
			}

			base.OnStartServer();

			if (PendingGear != null)
			{
				for (int i = 0; i < PendingGear.Count; ++i)
				{
					Wear(PendingGear[i]);
				}
				PendingGear = null;
			}

			ApplyPersistedResources();
			ApplyPersistedBuffs();
			PersistedResources = null;
			PersistedBuffs = null;
		}

		/// <inheritdoc />
		public override void OnStartClient()
		{
			base.OnStartClient();
			if (!base.IsServerStarted && payloadOwnerID > 0)
			{
				byOwner[payloadOwnerID] = this;
				OnClientMercenaryChanged?.Invoke(payloadOwnerID, this);
			}
		}

		/// <inheritdoc />
		public override void OnStopClient()
		{
			base.OnStopClient();
			if (!base.IsServerStarted && payloadOwnerID > 0 &&
				byOwner.TryGetValue(payloadOwnerID, out Mercenary registered) && ReferenceEquals(registered, this))
			{
				byOwner.Remove(payloadOwnerID);
				OnClientMercenaryChanged?.Invoke(payloadOwnerID, null);
			}
		}

		/// <summary>Server: lists this mercenary as <paramref name="ownerID"/>'s. The mercenary system calls it.</summary>
		public static void RegisterServer(long ownerID, Mercenary mercenary)
		{
			if (ownerID > 0 && mercenary != null)
			{
				byOwner[ownerID] = mercenary;
			}
		}

		/// <summary>Server: unlists <paramref name="ownerID"/>'s mercenary when it is <paramref name="mercenary"/>.</summary>
		public static void UnregisterServer(long ownerID, Mercenary mercenary)
		{
			if (byOwner.TryGetValue(ownerID, out Mercenary registered) && ReferenceEquals(registered, mercenary))
			{
				byOwner.Remove(ownerID);
			}
		}

		/// <summary>Wears one of its owner's items, applying its attributes. A null item, or one it does not wear, is ignored.</summary>
		public void Wear(Item item)
		{
			if (item == null || !item.IsEquippable || MercenaryRules.GearSlotFor(item.Template) < 0 || wornGear.Contains(item))
			{
				return;
			}
			item.Equippable.Equip(this);
			wornGear.Add(item);
		}

		/// <summary>Takes an item off, releasing its attributes; the item stays its owner's.</summary>
		public void TakeOff(Item item)
		{
			if (item == null || !wornGear.Remove(item))
			{
				return;
			}
			if (item.IsEquippable && ReferenceEquals(item.Equippable.Character, this))
			{
				item.Equippable.Unequip();
			}
		}

		/// <summary>Takes everything off. Runs as it leaves, so the owner's items never keep a wearer that is gone.</summary>
		public void TakeOffAll()
		{
			for (int i = wornGear.Count - 1; i >= 0; --i)
			{
				TakeOff(wornGear[i]);
			}
			wornGear.Clear();
		}

		/// <summary>Writes the restored resource values (health, mana, stamina) onto its attribute controller.</summary>
		private void ApplyPersistedResources()
		{
			if (PersistedResources == null || PersistedResources.Count < 1 ||
				!this.TryGet(out ICharacterAttributeController attributeController))
			{
				return;
			}
			for (int i = 0; i < PersistedResources.Count; ++i)
			{
				MercenaryPersistedResource resource = PersistedResources[i];
				if (attributeController.TryGetResourceAttribute(resource.TemplateID, out CharacterResourceAttribute attribute))
				{
					// At least one point: a mercenary saved at zero health is a dead one, and the dead are not restored.
					attribute.SetCurrentValue(Mathf.Clamp(resource.CurrentValue, 1f, attribute.FinalValue));
				}
			}
		}

		/// <summary>Rebuilds saved buffs against this server's clock, as a restored pet's are.</summary>
		private void ApplyPersistedBuffs()
		{
			if (PersistedBuffs == null || PersistedBuffs.Count < 1 || !this.TryGet(out IBuffController buffController))
			{
				return;
			}
			TimeManager timeManager = base.TimeManager;
			if (timeManager == null)
			{
				return;
			}
			float tickDelta = (float)timeManager.TickDelta;
			if (tickDelta <= 0f)
			{
				return;
			}
			uint currentTick = buffController.ResolveAuthoritativeTick(timeManager.LocalTick);
			for (int i = 0; i < PersistedBuffs.Count; ++i)
			{
				MercenaryPersistedBuff persisted = PersistedBuffs[i];
				uint expiryTick = currentTick + (uint)Math.Max(1.0, Math.Ceiling(persisted.RemainingTime / tickDelta));
				uint nextTickTick = currentTick + (uint)Math.Max(1.0, Math.Ceiling(persisted.TickTime / tickDelta));
				buffController.Apply(new Buff(persisted.TemplateID, expiryTick, nextTickTick, tickDelta, persisted.Stacks, persisted.TickCount));
			}
		}

		/// <inheritdoc />
		public override void ResetState(bool asServer)
		{
			TakeOffAll();
			base.ResetState(asServer);

			payloadOwnerID = 0;
			payloadKitID = 0;
			MercenaryOwner = null;
			Kit = null;
			Stance = PetStance.Defensive;
			PendingGear = null;
			PersistedResources = null;
			PersistedBuffs = null;
		}

		/// <inheritdoc />
		/// <remarks>The owner's ID and the kit's, after the NPC's own payload.</remarks>
		public override void ReadPayload(NetworkConnection connection, Reader reader)
		{
			base.ReadPayload(connection, reader);
			payloadOwnerID = reader.ReadInt64();
			payloadKitID = reader.ReadInt32();
			Kit = payloadKitID != 0 ? MercenaryKitTemplate.Get<MercenaryKitTemplate>(payloadKitID) : null;
		}

		/// <inheritdoc />
		public override void WritePayload(NetworkConnection connection, Writer writer)
		{
			base.WritePayload(connection, writer);
			// The owner may be gone for a frame (despawned between spawn and write): 0 reads as "no owner".
			writer.WriteInt64(MercenaryOwner != null ? MercenaryOwner.ID : 0);
			writer.WriteInt32(Kit != null ? Kit.ID : 0);
		}

		/// <summary>
		/// Back to the pool at once, with no corpse: a mercenary that dies leaves its owner's service, and one sent to rest
		/// simply goes. Pooled out of the world scene, as a pet is (<see cref="PersistentPool"/>).
		/// </summary>
		public override void Despawn()
		{
			TakeOffAll();
			if (NetworkObject != null && NetworkObject.IsSpawned && base.IsServerStarted)
			{
				PersistentPool.Despawn(NetworkManager, NetworkObject);
				return;
			}
			ReturnToPool();
		}
	}
}
