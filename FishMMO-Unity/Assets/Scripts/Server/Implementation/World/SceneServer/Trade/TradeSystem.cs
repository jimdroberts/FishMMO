using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Player-to-player trading of items and currency (issue #144).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Nothing of value comes from the client.</b> Every request names an inventory slot, a
	/// quantity, a character id or a state version; the server resolves what is in the slot,
	/// what the balance is, and whether the two characters may trade at all. The client's own
	/// checks exist to save round trips and to close its window promptly — they decide nothing.
	/// </para>
	/// <para>
	/// <b>An offered item is reserved by the inventory's own slot lock.</b> The moment an item
	/// goes on the table its slot is locked on the server, and every path that could move,
	/// split, merge, sell, mail, equip or consume it refuses a locked slot — including the
	/// equip path, which runs inside the replicate tick and has no broadcast handler to
	/// intercept, and the consumable finder, which now skips locked slots for the same reason.
	/// Withdrawing the offer, or the session ending for any reason, unlocks it. A trade never
	/// needs to guard those paths itself, and the completion check that re-reads every slot is
	/// a second lock on the same door.
	/// </para>
	/// <para>
	/// <b>Any change clears both acceptances</b> (see <see cref="TradeSession"/>), and an accept
	/// must quote the state version it consents to. <b>Range and state are the server's</b>: a
	/// tick re-checks that both characters are present, able to act, in the same scene
	/// instance and within <see cref="MaxTradeDistance"/>, and closes the session the moment
	/// any of that stops being true. The same rule is applied once more at completion.
	/// </para>
	/// <para>
	/// <b>The exchange is one database commit.</b> Both characters' item rows, both currency
	/// balances and the ledger rows travel in one unit of work through
	/// <see cref="ICharacterInventorySystem.TryPersistExchange"/>, under both characters'
	/// session row locks. The in-memory application that precedes it is itself all-or-nothing
	/// (<see cref="TradeExchange"/>), so the database can only ever see either the whole trade
	/// or none of it, and memory never holds half of one.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "TradeSystem", menuName = "FishMMO/Server/SceneServer/Trade System", order = 1)]
	[RequiresDataContainer(typeof(TradeSystemMainThreadQueueData))]
	[RequiresDataContainer(typeof(TradeSystemRuntimeData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public partial class TradeSystem : ServerBehaviour, ITradeSystem
	{
		[Header("Main Thread Dispatch")]
		[Tooltip("Max trade-system actions drained from the main-thread queue per frame")]
		[SerializeField] private int maxMainThreadActionsPerFrame = 100;

		[Header("Trade Rules")]
		[Tooltip("How far apart, in metres, two characters may be and still trade. The trade closes the moment they separate further. Issue #144 asks for 15-20.")]
		[SerializeField] private float maxTradeDistance = TradeRules.DefaultMaxDistance;

		[Tooltip("Seconds between server-side range and state checks of every open trade.")]
		[SerializeField] private float rangeCheckIntervalSeconds = 0.25f;

		[Tooltip("How many inventory slots one side may put on the table.")]
		[SerializeField] private int maxOfferSlots = TradeRules.DefaultMaxOfferSlots;

		[Tooltip("Seconds an invitation waits for an answer before it lapses.")]
		[SerializeField] private float inviteTtlSeconds = TradeRules.DefaultInviteTtlSeconds;

		[Tooltip("Seconds a character must wait before inviting the same target again after a decline or expiry.")]
		[SerializeField] private float perTargetInviteCooldownSeconds = 10.0f;

		[Header("Ingress Protection")]
		[Tooltip("Minimum milliseconds between trade requests of one kind per connection")]
		[SerializeField] private int ingressDebounceMilliseconds = 50;

		[Tooltip("Seconds between bounded ingress guard cleanup sweeps")]
		[SerializeField] private float ingressSweepIntervalSeconds = 5.0f;

		[Tooltip("Seconds before stale ingress guard entries are removed")]
		[SerializeField] private float ingressEntryTtlSeconds = 30.0f;

		[Tooltip("Maximum stale ingress guard entries removed per sweep")]
		[SerializeField] private int ingressSweepMaxRemovals = 128;

		[Header("Currency")]
		[Tooltip("The attribute that is the tradeable currency. The same template the merchant and mailbox use.")]
		[SerializeField] private CharacterAttributeTemplate currencyTemplate;

		/// <summary>Ingress guard operation codes.</summary>
		private enum IngressOperation : byte
		{
			Request = 1,
			Respond = 2,
			Offer = 3,
			Withdraw = 4,
			Currency = 5,
			Accept = 6,
			Cancel = 7,
		}

		/// <summary>An invitation waiting for the target's answer.</summary>
		private sealed class PendingInvite
		{
			public long RequesterID;
			public long TargetID;
			public double ExpiresAt;
		}

		/// <summary>Open sessions, one entry per PARTY, so a lookup by either character is O(1).</summary>
		private readonly Dictionary<long, TradeSession> sessionsByCharacter = new Dictionary<long, TradeSession>();

		/// <summary>Every open session once, for the range tick.</summary>
		private readonly List<TradeSession> sessions = new List<TradeSession>();

		/// <summary>Invitations keyed by TARGET: a character can hold at most one.</summary>
		private readonly Dictionary<long, PendingInvite> invitesByTarget = new Dictionary<long, PendingInvite>();

		/// <summary>The same invitations keyed by REQUESTER: a character can have at most one out.</summary>
		private readonly Dictionary<long, PendingInvite> invitesByRequester = new Dictionary<long, PendingInvite>();

		/// <summary>
		/// (requester, target) → time before which a repeat invitation is refused. Keeps a
		/// declined invitation from being re-sent every debounce interval.
		/// </summary>
		private readonly Dictionary<(long, long), double> inviteCooldowns = new Dictionary<(long, long), double>();

		/// <summary>Sessions scheduled to close during the tick, so the tick never mutates the list it walks.</summary>
		private readonly List<(TradeSession session, TradeCloseReason first, TradeCloseReason second)> pendingCloses = new List<(TradeSession, TradeCloseReason, TradeCloseReason)>();

		private long nextSessionID = 1;
		private double nextInviteSweep;

		/// <inheritdoc />
		public float MaxTradeDistance => maxTradeDistance;

		/// <summary>Number of open sessions, for diagnostics.</summary>
		public int OpenSessionCount => sessions.Count;

		private static double Now => Time.unscaledTimeAsDouble;

		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("TradeSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.DataContainerRegistry.TryGet<ITradeSystemMainThreadQueueData>(out _))
			{
				Log.Error("TradeSystem", "InitializeOnce: ITradeSystemMainThreadQueueData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			if (!Server.DataContainerRegistry.TryGet<ITradeSystemRuntimeData>(out _))
			{
				Log.Error("TradeSystem", "InitializeOnce: ITradeSystemRuntimeData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			if (!Server.BehaviourRegistry.TryGet(out ICharacterInventorySystem _))
			{
				Log.Error("TradeSystem", "InitializeOnce: ICharacterInventorySystem not found; it must be registered before the trade system.");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (currencyTemplate == null)
			{
				// Trading items still works; only currency offers are refused. Said once here
				// rather than per request.
				Log.Warning("TradeSystem", "No currency template assigned; currency offers will be refused.");
			}

			maxMainThreadActionsPerFrame = Mathf.Max(1, maxMainThreadActionsPerFrame);
			maxTradeDistance = TradeRules.ClampMaxDistance(maxTradeDistance);
			rangeCheckIntervalSeconds = Mathf.Max(0.05f, rangeCheckIntervalSeconds);
			maxOfferSlots = TradeRules.ClampMaxOfferSlots(maxOfferSlots);
			inviteTtlSeconds = Mathf.Max(1.0f, inviteTtlSeconds);
			perTargetInviteCooldownSeconds = Mathf.Max(0.0f, perTargetInviteCooldownSeconds);
			ingressDebounceMilliseconds = Mathf.Max(0, ingressDebounceMilliseconds);
			ingressSweepIntervalSeconds = Mathf.Max(0.25f, ingressSweepIntervalSeconds);
			ingressEntryTtlSeconds = Mathf.Max(1.0f, ingressEntryTtlSeconds);
			ingressSweepMaxRemovals = Mathf.Max(1, ingressSweepMaxRemovals);

			RegisterBroadcasts();
			SubscribeToCharacterLifecycle();

			Log.Debug("TradeSystem", $"Initialized (MaxDistance={maxTradeDistance}m, MaxOfferSlots={maxOfferSlots}, InviteTtl={inviteTtlSeconds}s)");
			return ServerComponentInitializationStatus.Initialized;
		}

		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				return;
			}

			DrainMainThreadQueue(drainAll: true);

			UnregisterBroadcasts();
			UnsubscribeFromCharacterLifecycle();

			// Every open table is closed with its items still in their owners' bags: nothing has
			// moved until a commit lands, and a commit that has already been handed to the
			// worker completes on its own.
			for (int i = sessions.Count - 1; i >= 0; --i)
			{
				CloseSession(sessions[i], TradeCloseReason.ServerShutdown, TradeCloseReason.ServerShutdown);
			}
			sessions.Clear();
			sessionsByCharacter.Clear();
			invitesByTarget.Clear();
			invitesByRequester.Clear();
			inviteCooldowns.Clear();
			pendingCloses.Clear();

			if (Server.DataContainerRegistry.TryGet<ITradeSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard?.Clear();
			}
		}

		protected override void OnUpdate(float deltaTime)
		{
			DrainMainThreadQueue(drainAll: false);

			if (Server.DataContainerRegistry.TryGet<ITradeSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard.Sweep(ingressSweepIntervalSeconds, ingressEntryTtlSeconds, ingressSweepMaxRemovals);
			}

			TickSessions();
			SweepInvites();
		}

		// ── Registration ────────────────────────────────────────────────────────────────────

		private void RegisterBroadcasts()
		{
			Server.NetworkWrapper.RegisterBroadcast<TradeRequestBroadcast>(OnServerTradeRequestReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<TradeRequestResponseBroadcast>(OnServerTradeRequestResponseReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<TradeOfferItemBroadcast>(OnServerTradeOfferItemReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<TradeWithdrawItemBroadcast>(OnServerTradeWithdrawItemReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<TradeSetCurrencyBroadcast>(OnServerTradeSetCurrencyReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<TradeAcceptBroadcast>(OnServerTradeAcceptReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<TradeCancelBroadcast>(OnServerTradeCancelReceived, true);
		}

		private void UnregisterBroadcasts()
		{
			Server.NetworkWrapper.UnregisterBroadcast<TradeRequestBroadcast>(OnServerTradeRequestReceived);
			Server.NetworkWrapper.UnregisterBroadcast<TradeRequestResponseBroadcast>(OnServerTradeRequestResponseReceived);
			Server.NetworkWrapper.UnregisterBroadcast<TradeOfferItemBroadcast>(OnServerTradeOfferItemReceived);
			Server.NetworkWrapper.UnregisterBroadcast<TradeWithdrawItemBroadcast>(OnServerTradeWithdrawItemReceived);
			Server.NetworkWrapper.UnregisterBroadcast<TradeSetCurrencyBroadcast>(OnServerTradeSetCurrencyReceived);
			Server.NetworkWrapper.UnregisterBroadcast<TradeAcceptBroadcast>(OnServerTradeAcceptReceived);
			Server.NetworkWrapper.UnregisterBroadcast<TradeCancelBroadcast>(OnServerTradeCancelReceived);
		}

		/// <summary>
		/// A disconnect is a player leaving; a despawn is a character being taken out of the
		/// world without one, which is what a hand-off to another scene server looks like from
		/// here. Either ends a trade and drops any invitation.
		/// </summary>
		private void SubscribeToCharacterLifecycle()
		{
			if (!Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem))
			{
				Log.Warning("TradeSystem", "No character system found; trades will only close on the range tick.");
				return;
			}

			characterSystem.OnDisconnect -= CharacterSystem_OnCharacterLeft;
			characterSystem.OnDespawnCharacter -= CharacterSystem_OnCharacterLeft;
			characterSystem.OnDisconnect += CharacterSystem_OnCharacterLeft;
			characterSystem.OnDespawnCharacter += CharacterSystem_OnCharacterLeft;
		}

		private void UnsubscribeFromCharacterLifecycle()
		{
			if (!Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem))
			{
				return;
			}

			characterSystem.OnDisconnect -= CharacterSystem_OnCharacterLeft;
			characterSystem.OnDespawnCharacter -= CharacterSystem_OnCharacterLeft;
		}

		private void CharacterSystem_OnCharacterLeft(NetworkConnection conn, IPlayerCharacter character)
		{
			if (character == null)
			{
				return;
			}

			if (sessionsByCharacter.TryGetValue(character.ID, out TradeSession session))
			{
				CloseSessionFor(session, character.ID, TradeCloseReason.PartnerLeft, TradeCloseReason.PartnerLeft);
			}

			DropInvitesInvolving(character.ID, notifyRequester: true);
		}

		// ── Main thread queue ───────────────────────────────────────────────────────────────

		private void DrainMainThreadQueue(bool drainAll)
		{
			DrainMainThreadQueue<ITradeSystemMainThreadQueueData>(maxMainThreadActionsPerFrame, drainAll);
		}

		private bool TryEnqueueMainThread(Action action)
		{
			return TryEnqueueMainThread<ITradeSystemMainThreadQueueData>(action);
		}

		// ── Ingress guard ───────────────────────────────────────────────────────────────────

		private bool TryBeginIngressGuard(int connectionId, IngressOperation operation, out long guardKey)
		{
			if (!Server.DataContainerRegistry.TryGet<ITradeSystemRuntimeData>(out var runtimeData))
			{
				guardKey = 0;
				return false;
			}
			return runtimeData.IngressGuard.TryBegin(connectionId, (byte)operation, ingressDebounceMilliseconds, out guardKey);
		}

		private void EndIngressGuard(long guardKey)
		{
			if (Server?.DataContainerRegistry.TryGet<ITradeSystemRuntimeData>(out var runtimeData) == true)
			{
				runtimeData.IngressGuard.End(guardKey);
			}
		}

		// ── Lookups ─────────────────────────────────────────────────────────────────────────

		/// <inheritdoc />
		public bool IsTrading(long characterID)
		{
			return sessionsByCharacter.ContainsKey(characterID);
		}

		/// <inheritdoc />
		public bool CloseTradeFor(IPlayerCharacter character, TradeCloseReason reason)
		{
			if (character == null || !sessionsByCharacter.TryGetValue(character.ID, out TradeSession session))
			{
				return false;
			}
			CloseSessionFor(session, character.ID, reason, reason);
			return true;
		}

		/// <summary>The character the server holds for <paramref name="characterID"/>, if it is resident here.</summary>
		private bool TryGetResidentCharacter(long characterID, out IPlayerCharacter character)
		{
			character = null;
			return Server != null &&
				Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> mappingData) &&
				mappingData.CharactersByID.TryGetValue(characterID, out character) &&
				character != null;
		}

		private bool TryGetInventory(IPlayerCharacter character, out IInventoryController inventory)
		{
			inventory = null;
			return character != null && character.TryGet(out inventory) && inventory != null;
		}

		private void Send<T>(IPlayerCharacter character, T message) where T : struct, FishNet.Broadcast.IBroadcast
		{
			if (character?.Owner == null)
			{
				return;
			}
			Server.NetworkWrapper.Broadcast(character.Owner, message, true, Channel.Reliable);
		}

		private void Send<T>(NetworkConnection conn, T message) where T : struct, FishNet.Broadcast.IBroadcast
		{
			if (conn == null)
			{
				return;
			}
			Server.NetworkWrapper.Broadcast(conn, message, true, Channel.Reliable);
		}

		// ── Invitations ─────────────────────────────────────────────────────────────────────

		/// <summary>Drops every invitation the character sent or received.</summary>
		private void DropInvitesInvolving(long characterID, bool notifyRequester)
		{
			if (invitesByTarget.TryGetValue(characterID, out PendingInvite asTarget))
			{
				RemoveInvite(asTarget);
				if (notifyRequester && TryGetResidentCharacter(asTarget.RequesterID, out IPlayerCharacter requester))
				{
					Send(requester, new TradeRequestResultBroadcast { TargetCharacterID = asTarget.TargetID, Failure = TradeRequestFailure.TargetUnavailable });
				}
			}

			if (invitesByRequester.TryGetValue(characterID, out PendingInvite asRequester))
			{
				RemoveInvite(asRequester);
			}
		}

		private void RemoveInvite(PendingInvite invite)
		{
			if (invite == null)
			{
				return;
			}
			if (invitesByTarget.TryGetValue(invite.TargetID, out PendingInvite byTarget) && ReferenceEquals(byTarget, invite))
			{
				invitesByTarget.Remove(invite.TargetID);
			}
			if (invitesByRequester.TryGetValue(invite.RequesterID, out PendingInvite byRequester) && ReferenceEquals(byRequester, invite))
			{
				invitesByRequester.Remove(invite.RequesterID);
			}
		}

		/// <summary>Lapses invitations nobody answered, telling the requester.</summary>
		private void SweepInvites()
		{
			double now = Now;
			if (now < nextInviteSweep)
			{
				return;
			}
			nextInviteSweep = now + 1.0;

			if (invitesByTarget.Count == 0 && inviteCooldowns.Count == 0)
			{
				return;
			}

			List<PendingInvite> expired = null;
			foreach (PendingInvite invite in invitesByTarget.Values)
			{
				if (invite.ExpiresAt <= now)
				{
					(expired ??= new List<PendingInvite>()).Add(invite);
				}
			}

			if (expired != null)
			{
				for (int i = 0; i < expired.Count; ++i)
				{
					PendingInvite invite = expired[i];
					RemoveInvite(invite);
					ArmInviteCooldown(invite.RequesterID, invite.TargetID);
					if (TryGetResidentCharacter(invite.RequesterID, out IPlayerCharacter requester))
					{
						Send(requester, new TradeRequestResultBroadcast { TargetCharacterID = invite.TargetID, Failure = TradeRequestFailure.Expired });
					}
				}
			}

			if (inviteCooldowns.Count > 0)
			{
				List<(long, long)> lapsed = null;
				foreach (KeyValuePair<(long, long), double> kvp in inviteCooldowns)
				{
					if (kvp.Value <= now)
					{
						(lapsed ??= new List<(long, long)>()).Add(kvp.Key);
					}
				}
				if (lapsed != null)
				{
					for (int i = 0; i < lapsed.Count; ++i)
					{
						inviteCooldowns.Remove(lapsed[i]);
					}
				}
			}
		}

		private void ArmInviteCooldown(long requesterID, long targetID)
		{
			if (perTargetInviteCooldownSeconds <= 0.0f)
			{
				return;
			}
			inviteCooldowns[(requesterID, targetID)] = Now + perTargetInviteCooldownSeconds;
		}

		private bool IsInviteOnCooldown(long requesterID, long targetID)
		{
			return inviteCooldowns.TryGetValue((requesterID, targetID), out double until) && until > Now;
		}

		// ── Sessions ────────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Opens a session between two characters that have both agreed, and tells them.
		/// </summary>
		private TradeSession OpenSession(IPlayerCharacter first, IPlayerCharacter second)
		{
			var session = new TradeSession(nextSessionID++, first.ID, second.ID, maxOfferSlots)
			{
				NextRangeCheckTime = Now + rangeCheckIntervalSeconds,
			};

			sessions.Add(session);
			sessionsByCharacter[first.ID] = session;
			sessionsByCharacter[second.ID] = session;

			SubscribeToInventory(first);
			SubscribeToInventory(second);

			Send(first, new TradeOpenedBroadcast { PartnerCharacterID = second.ID, PartnerName = second.CharacterName, MaxDistance = maxTradeDistance });
			Send(second, new TradeOpenedBroadcast { PartnerCharacterID = first.ID, PartnerName = first.CharacterName, MaxDistance = maxTradeDistance });
			BroadcastState(session);

			Log.Debug("TradeSystem", $"Session {session.ID} opened between {first.ID} and {second.ID}.");
			return session;
		}

		/// <summary>
		/// Ends a session from the point of view of <paramref name="actorID"/>: they get
		/// <paramref name="actorReason"/>, the other party gets <paramref name="partnerReason"/>.
		/// </summary>
		private void CloseSessionFor(TradeSession session, long actorID, TradeCloseReason actorReason, TradeCloseReason partnerReason)
		{
			if (session.First.CharacterID == actorID)
			{
				CloseSession(session, actorReason, partnerReason);
			}
			else
			{
				CloseSession(session, partnerReason, actorReason);
			}
		}

		/// <summary>
		/// Ends a session: unlocks every offered slot, forgets it, and tells both parties.
		/// </summary>
		/// <remarks>
		/// Idempotent. A session that reached the commit has already unlocked its slots and
		/// moved its items; closing it here only tells the parties and forgets it.
		/// </remarks>
		private void CloseSession(TradeSession session, TradeCloseReason firstReason, TradeCloseReason secondReason)
		{
			if (session == null)
			{
				return;
			}

			bool wasOpen = session.Phase == TradePhase.Open;
			session.Close();

			IPlayerCharacter first = null;
			IPlayerCharacter second = null;
			TryGetResidentCharacter(session.First.CharacterID, out first);
			TryGetResidentCharacter(session.Second.CharacterID, out second);

			if (wasOpen)
			{
				// Only an open session still holds locks; a committing one released them first.
				UnlockOffers(first, session.First);
				UnlockOffers(second, session.Second);
			}

			UnsubscribeFromInventory(first);
			UnsubscribeFromInventory(second);

			if (sessionsByCharacter.TryGetValue(session.First.CharacterID, out TradeSession byFirst) && ReferenceEquals(byFirst, session))
			{
				sessionsByCharacter.Remove(session.First.CharacterID);
			}
			if (sessionsByCharacter.TryGetValue(session.Second.CharacterID, out TradeSession bySecond) && ReferenceEquals(bySecond, session))
			{
				sessionsByCharacter.Remove(session.Second.CharacterID);
			}
			sessions.Remove(session);

			Send(first, new TradeClosedBroadcast { Reason = firstReason });
			Send(second, new TradeClosedBroadcast { Reason = secondReason });

			Log.Debug("TradeSystem", $"Session {session.ID} closed ({firstReason}/{secondReason}).");
		}

		/// <summary>Releases the slot locks one side's offers hold.</summary>
		private void UnlockOffers(IPlayerCharacter character, TradeSession.Party party)
		{
			if (!TryGetInventory(character, out IInventoryController inventory))
			{
				return;
			}
			for (int i = 0; i < party.Offers.Count; ++i)
			{
				inventory.UnlockSlot(party.Offers[i].Slot);
			}
		}

		/// <summary>Sends each party its own view of the table.</summary>
		private void BroadcastState(TradeSession session)
		{
			if (TryGetResidentCharacter(session.First.CharacterID, out IPlayerCharacter first))
			{
				Send(first, session.BuildStateFor(first.ID));
			}
			if (TryGetResidentCharacter(session.Second.CharacterID, out IPlayerCharacter second))
			{
				Send(second, session.BuildStateFor(second.ID));
			}
		}

		/// <summary>
		/// The server's own range and state check, run on every open session at the configured
		/// interval. The client closes its window on its own check first; this is the one that
		/// decides.
		/// </summary>
		private void TickSessions()
		{
			if (sessions.Count == 0)
			{
				return;
			}

			double now = Now;
			pendingCloses.Clear();

			for (int i = 0; i < sessions.Count; ++i)
			{
				TradeSession session = sessions[i];
				if (session.Phase != TradePhase.Open || session.NextRangeCheckTime > now)
				{
					continue;
				}
				session.NextRangeCheckTime = now + rangeCheckIntervalSeconds;

				if (!TryGetResidentCharacter(session.First.CharacterID, out IPlayerCharacter first))
				{
					pendingCloses.Add((session, TradeCloseReason.PartnerLeft, TradeCloseReason.PartnerLeft));
					continue;
				}
				if (!TryGetResidentCharacter(session.Second.CharacterID, out IPlayerCharacter second))
				{
					pendingCloses.Add((session, TradeCloseReason.PartnerLeft, TradeCloseReason.PartnerLeft));
					continue;
				}

				if (!CharacterStateValidation.CanAct(first) || !CharacterStateValidation.CanAct(second))
				{
					pendingCloses.Add((session, TradeCloseReason.CannotAct, TradeCloseReason.CannotAct));
					continue;
				}

				if (!TradeRules.AreInSameScene(first, second) ||
					first.Transform == null || second.Transform == null ||
					!TradeRules.IsWithinRange(first.Transform.position, second.Transform.position, maxTradeDistance))
				{
					pendingCloses.Add((session, TradeCloseReason.OutOfRange, TradeCloseReason.OutOfRange));
					continue;
				}
			}

			for (int i = 0; i < pendingCloses.Count; ++i)
			{
				(TradeSession session, TradeCloseReason firstReason, TradeCloseReason secondReason) = pendingCloses[i];
				CloseSession(session, firstReason, secondReason);
			}
			pendingCloses.Clear();
		}

		// ── Inventory watch ─────────────────────────────────────────────────────────────────

		/// <summary>
		/// Watches a party's inventory for an offered slot changing under the offer.
		/// </summary>
		/// <remarks>
		/// The slot lock makes this unreachable through every request path; it stays as a
		/// last line so a future mutation path that forgets the lock withdraws the offer and
		/// clears both acceptances instead of letting the table describe an item that is no
		/// longer there. Completion re-reads every slot regardless.
		/// </remarks>
		private void SubscribeToInventory(IPlayerCharacter character)
		{
			if (!TryGetInventory(character, out IInventoryController inventory))
			{
				return;
			}
			inventory.OnSlotUpdated -= Inventory_OnSlotUpdated;
			inventory.OnSlotUpdated += Inventory_OnSlotUpdated;
		}

		private void UnsubscribeFromInventory(IPlayerCharacter character)
		{
			if (!TryGetInventory(character, out IInventoryController inventory))
			{
				return;
			}
			inventory.OnSlotUpdated -= Inventory_OnSlotUpdated;
		}

		private void Inventory_OnSlotUpdated(IItemContainer container, Item item, int slot)
		{
			if (container is not ICharacterBehaviour behaviour || behaviour.Character == null)
			{
				return;
			}

			long characterID = behaviour.Character.ID;
			if (!sessionsByCharacter.TryGetValue(characterID, out TradeSession session) || session.Phase != TradePhase.Open)
			{
				return;
			}

			TradeSession.Party party = session.PartyOf(characterID);
			int index = party?.IndexOfSlot(slot) ?? -1;
			if (index < 0)
			{
				return;
			}

			TradeOffer offer = party.Offers[index];
			if (TradeRules.OfferStillHolds(item, offer.ItemID, offer.TemplateID, offer.Amount))
			{
				return;
			}

			Log.Warning("TradeSystem", $"Session {session.ID}: offered slot {slot} of character {characterID} changed under its lock; withdrawing the offer.");
			if (session.TryRemoveOffer(characterID, slot, out _) == TradeOfferRefusal.None)
			{
				container.UnlockSlot(slot);
				BroadcastState(session);
			}
		}
	}
}
