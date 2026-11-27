using System;
using System.Collections.Generic;
using FishNet.Transporting;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>A player's hired mercenary, as this peer knows it: which kit, its name, whether it is resting, its stance.</summary>
	public interface IMercenaryController : ICharacterBehaviour
	{
		/// <summary>True while the player has a mercenary, out or resting.</summary>
		bool HasMercenary { get; }

		/// <summary>The kit hired, or 0.</summary>
		int KitID { get; }

		/// <summary>The kit hired, or null.</summary>
		MercenaryKitTemplate Kit { get; }

		/// <summary>The mercenary's name.</summary>
		string MercenaryName { get; }

		/// <summary>True while it rests: dismissed for now, holding no party seat.</summary>
		bool Resting { get; }

		/// <summary>When it fights.</summary>
		PetStance Stance { get; }

		/// <summary>The live mercenary on this peer, or null (resting, none, or not observed here).</summary>
		Mercenary Live { get; }

		/// <summary>Raised whenever any of the above changes.</summary>
		event Action<IMercenaryController> OnChanged;

		/// <summary>Sets the state (the server's, or what the server sent the owner). Raises <see cref="OnChanged"/>.</summary>
		void SetState(int kitID, string mercenaryName, bool resting, PetStance stance);
	}

	/// <summary>
	/// The player's mercenary controller (a character part every player gets). The server holds the truth and the parts of
	/// it that are saved; the owner holds what <see cref="MercenaryStateBroadcast"/> told it.
	/// </summary>
	public class MercenaryController : CharacterPart, IMercenaryController
	{
		/// <inheritdoc />
		public int KitID { get; private set; }

		/// <inheritdoc />
		public bool HasMercenary => KitID != 0;

		/// <inheritdoc />
		public MercenaryKitTemplate Kit => KitID != 0 ? MercenaryKitTemplate.Get<MercenaryKitTemplate>(KitID) : null;

		/// <inheritdoc />
		public string MercenaryName { get; private set; } = string.Empty;

		/// <inheritdoc />
		public bool Resting { get; private set; }

		/// <inheritdoc />
		public PetStance Stance { get; private set; } = PetStance.Defensive;

		/// <inheritdoc />
		public Mercenary Live => Character != null ? Mercenary.GetByOwner(Character.ID) : null;

		/// <inheritdoc />
		public event Action<IMercenaryController> OnChanged;

		// ── Server-held state that is saved with the mercenary ─────────────────────────────

		/// <summary>Server: the seed its look is drawn from.</summary>
		public int AppearanceSeed { get; set; }

		/// <summary>Server: what its name is drawn from.</summary>
		public SceneObjectNamer.NameState NameState { get; set; }

		/// <summary>Server: the race of the one hired (its models and its name); 0 for the kit's own.</summary>
		public int RaceTemplateID { get; set; }

		/// <summary>Server: resource values to restore when it next comes out (a resting or arriving mercenary's).</summary>
		public List<MercenaryPersistedResource> SavedResources { get; set; }

		/// <summary>Server: buffs to restore when it next comes out.</summary>
		public List<MercenaryPersistedBuff> SavedBuffs { get; set; }

		/// <summary>Server: the party its seat is in while it is out; 0 when it holds none.</summary>
		public long SeatedPartyID { get; set; }

		/// <summary>Server: true while a hire, rest, return or release is being answered (one at a time).</summary>
		public bool RequestInFlight { get; set; }

		/// <summary>Server: the record changed since it was last written.</summary>
		public bool PersistenceDirty { get; set; }

		/// <summary>Server: counts changes, so a save that landed clears <see cref="PersistenceDirty"/> only if none came after it.</summary>
		public int ChangeCount { get; private set; }

		/// <summary>Server: records a change to be saved.</summary>
		public void MarkDirty()
		{
			PersistenceDirty = true;
			unchecked { ++ChangeCount; }
		}

		/// <inheritdoc />
		public void SetState(int kitID, string mercenaryName, bool resting, PetStance stance)
		{
			KitID = kitID;
			MercenaryName = kitID != 0 ? (mercenaryName ?? string.Empty) : string.Empty;
			Resting = kitID != 0 && resting;
			Stance = stance;
			OnChanged?.Invoke(this);
		}

		/// <summary>Server: forgets the mercenary (released or died). Gear is not touched: it is the player's.</summary>
		public void Clear()
		{
			AppearanceSeed = 0;
			NameState = default;
			RaceTemplateID = 0;
			SavedResources = null;
			SavedBuffs = null;
			SeatedPartyID = 0;
			SetState(0, null, false, PetStance.Defensive);
		}

		/// <inheritdoc />
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);
			KitID = 0;
			MercenaryName = string.Empty;
			Resting = false;
			Stance = PetStance.Defensive;
			AppearanceSeed = 0;
			NameState = default;
			RaceTemplateID = 0;
			SavedResources = null;
			SavedBuffs = null;
			SeatedPartyID = 0;
			RequestInFlight = false;
			PersistenceDirty = false;
			ChangeCount = 0;
			OnChanged = null;
		}

#if !UNITY_SERVER
		/// <inheritdoc />
		public override void OnStartCharacter()
		{
			base.OnStartCharacter();
			if (!base.IsOwner)
			{
				enabled = false;
				return;
			}
			ClientManager.RegisterBroadcast<MercenaryStateBroadcast>(OnClientStateBroadcastReceived);
			Mercenary.OnClientMercenaryChanged += OnClientMercenaryChanged;
		}

		/// <inheritdoc />
		public override void OnStopCharacter()
		{
			base.OnStopCharacter();
			if (base.IsOwner)
			{
				ClientManager.UnregisterBroadcast<MercenaryStateBroadcast>(OnClientStateBroadcastReceived);
				Mercenary.OnClientMercenaryChanged -= OnClientMercenaryChanged;
			}
		}

		private void OnClientStateBroadcastReceived(MercenaryStateBroadcast msg, Channel channel)
		{
			SetState(msg.KitID, msg.Name, msg.Resting, msg.Stance);
		}

		/// <summary>The owner's own mercenary came into view or left it: the window rebinds.</summary>
		private void OnClientMercenaryChanged(long ownerID, Mercenary mercenary)
		{
			if (Character != null && ownerID == Character.ID)
			{
				OnChanged?.Invoke(this);
			}
		}
#endif
	}
}
