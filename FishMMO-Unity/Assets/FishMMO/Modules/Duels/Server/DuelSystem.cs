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
	/// Open-world duels (issue #250): challenges, the duel ground, the countdown, the fight's rules and the record.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>One duel per player, from either end.</b> A player in a duel, with a challenge out or a challenge to answer is
	/// busy, so nobody can accept two duels or be in two conversations at once (as trading).
	/// </para>
	/// <para>
	/// <b>The ground.</b> On acceptance the server finds level ground for a box of <see cref="arenaSize"/> (15 m each way by
	/// default) between the two, turned so its length runs from one to the other, and sets each down
	/// <see cref="DuelRules.StartOffset"/> from its centre facing the other. Being outside it (by more than
	/// <see cref="leaveTolerance"/>) at a check forfeits, during the countdown too.
	/// </para>
	/// <para>
	/// <b>Nobody dies.</b> While the fight is on the two sides may harm each other whatever their party, guild or toggles
	/// (<see cref="DuelHarmRelation"/>), and a blow that would kill leaves the loser on their last point of health: the
	/// harm rule spares it and <see cref="DuelRegistry.OnFinishingBlow"/> ends the duel here. Death from anything else is
	/// a loss too.
	/// </para>
	/// <para>
	/// <b>The record.</b> Every finished duel is written in one transaction (<c>IDuelRecordService</c>) that moves both
	/// ratings and adds the history row, through the persistence queue, which never drops work; the Duel Rating and Duel
	/// Wins boards read it. A duel the server calls off (shutdown) counts for nothing.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "DuelSystem", menuName = "FishMMO/Server/SceneServer/Duel System", order = 1)]
	[RequiresDataContainer(typeof(DuelSystemMainThreadQueueData))]
	[RequiresDataContainer(typeof(DuelSystemRuntimeData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public partial class DuelSystem : ServerBehaviour
	{
		private const string LOG = "DuelSystem";

		[Header("Duel Ground")]
		[Tooltip("Width (x), height (y) and length (z) of the duel ground, in metres. Each side is kept between 6 and 60.")]
		[SerializeField] private Vector3 arenaSize = DuelRules.DefaultArenaSize;

		[Tooltip("Metres a duellist may stand past the ground's edge before it counts as leaving it.")]
		[SerializeField] private float leaveTolerance = DuelRules.DefaultLeaveTolerance;

		[Header("Rules")]
		[Tooltip("Seconds between the teleport and the first blow.")]
		[SerializeField] private float countdownSeconds = DuelRules.DefaultCountdownSeconds;

		[Tooltip("Seconds a fight may last before it is a draw. Zero: no limit.")]
		[SerializeField] private float timeLimitSeconds = DuelRules.DefaultTimeLimitSeconds;

		[Tooltip("How far apart, in metres, two players may be for one to challenge the other.")]
		[SerializeField] private float challengeRange = DuelRules.DefaultChallengeRange;

		[Tooltip("Seconds a challenge waits for an answer.")]
		[SerializeField] private float inviteTtlSeconds = DuelRules.DefaultInviteTtlSeconds;

		[Tooltip("Seconds before a player may challenge the same player again after a decline or no answer.")]
		[SerializeField] private float perTargetCooldownSeconds = 15f;

		[Tooltip("Seconds between the server's checks of every duel (presence, ground, time).")]
		[SerializeField] private float checkIntervalSeconds = 0.1f;

		[Header("Rating")]
		[Tooltip("The most one duel moves a rating (Elo K factor).")]
		[SerializeField] private int ratingKFactor = 32;

		[Header("Main Thread Dispatch")]
		[Tooltip("Database replies drained from the main-thread queue per frame.")]
		[SerializeField] private int maxMainThreadActionsPerFrame = 100;

		[Header("Ingress Protection")]
		[Tooltip("Minimum milliseconds between duel requests of one kind per connection.")]
		[SerializeField] private int ingressDebounceMilliseconds = 250;

		[Tooltip("Seconds between bounded ingress guard cleanup sweeps.")]
		[SerializeField] private float ingressSweepIntervalSeconds = 5f;

		[Tooltip("Seconds before stale ingress guard entries are removed.")]
		[SerializeField] private float ingressEntryTtlSeconds = 30f;

		[Tooltip("Maximum stale ingress guard entries removed per sweep.")]
		[SerializeField] private int ingressSweepMaxRemovals = 128;

		private enum IngressOperation : byte
		{
			Request = 1,
			Respond = 2,
			Yield = 3,
		}

		private enum DuelPhase : byte
		{
			Countdown = 0,
			Live = 1,
		}

		/// <summary>A challenge waiting for its answer.</summary>
		private sealed class PendingInvite
		{
			public long ChallengerID;
			public long TargetID;
			public double ExpiresAt;
		}

		/// <summary>One duel, from the teleport to its end.</summary>
		private sealed class Duel
		{
			public long ID;
			public long FirstID;
			public long SecondID;
			public string FirstName;
			public string SecondName;
			public Scene Scene;
			public DuelArena Arena;
			public DuelPhase Phase;
			public double LiveAt;
			public double EndsAt;
			public double NextCheckAt;
		}

		private readonly Dictionary<long, Duel> duelsByCharacter = new Dictionary<long, Duel>();
		private readonly List<Duel> duels = new List<Duel>();
		private readonly Dictionary<long, PendingInvite> invitesByTarget = new Dictionary<long, PendingInvite>();
		private readonly Dictionary<long, PendingInvite> invitesByChallenger = new Dictionary<long, PendingInvite>();
		private readonly Dictionary<(long, long), double> inviteCooldowns = new Dictionary<(long, long), double>();
		private readonly List<(Duel duel, long loserID, bool draw, DuelEndReason reason)> pendingEnds = new List<(Duel, long, bool, DuelEndReason)>();

		private long nextDuelID = 1;
		private double nextInviteSweep;

		private static double Now => MonotonicClock.NowSeconds;

		/// <summary>Duels being fought or counted down, for diagnostics and tests.</summary>
		public int ActiveDuelCount => duels.Count;

		/// <summary>The configured ground size, made safe.</summary>
		public Vector3 ArenaSize => DuelRules.ClampSize(arenaSize);

		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error(LOG, "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}
			if (!Server.DataContainerRegistry.TryGet<IDuelSystemMainThreadQueueData>(out _) ||
				!Server.DataContainerRegistry.TryGet<IDuelSystemRuntimeData>(out _))
			{
				Log.Error(LOG, "InitializeOnce: its data containers are missing");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			arenaSize = DuelRules.ClampSize(arenaSize);
			leaveTolerance = Mathf.Clamp(leaveTolerance, 0f, 2f);
			countdownSeconds = Mathf.Clamp(countdownSeconds, 0f, 60f);
			timeLimitSeconds = Mathf.Max(0f, timeLimitSeconds);
			challengeRange = Mathf.Max(1f, challengeRange);
			inviteTtlSeconds = Mathf.Max(5f, inviteTtlSeconds);
			perTargetCooldownSeconds = Mathf.Max(0f, perTargetCooldownSeconds);
			checkIntervalSeconds = Mathf.Clamp(checkIntervalSeconds, 0.02f, 1f);
			ratingKFactor = Mathf.Clamp(ratingKFactor, 1, 100);
			maxMainThreadActionsPerFrame = Mathf.Max(1, maxMainThreadActionsPerFrame);
			ingressDebounceMilliseconds = Mathf.Max(0, ingressDebounceMilliseconds);
			ingressSweepIntervalSeconds = Mathf.Max(0.25f, ingressSweepIntervalSeconds);
			ingressEntryTtlSeconds = Mathf.Max(1f, ingressEntryTtlSeconds);
			ingressSweepMaxRemovals = Mathf.Max(1, ingressSweepMaxRemovals);

			DuelRegistry.Clear();
			Server.NetworkWrapper.RegisterBroadcast<DuelRequestBroadcast>(OnDuelRequest, true);
			Server.NetworkWrapper.RegisterBroadcast<DuelInviteResponseBroadcast>(OnDuelInviteResponse, true);
			Server.NetworkWrapper.RegisterBroadcast<DuelYieldBroadcast>(OnDuelYield, true);
			SubscribeToCharacterLifecycle();
			DuelRegistry.OnFinishingBlow -= OnFinishingBlow;
			DuelRegistry.OnFinishingBlow += OnFinishingBlow;
			ICharacterDamageController.OnKilled -= OnCharacterKilled;
			ICharacterDamageController.OnKilled += OnCharacterKilled;

			Log.Debug(LOG, $"Initialized (ground {arenaSize.x}x{arenaSize.y}x{arenaSize.z} m, countdown {countdownSeconds}s, limit {timeLimitSeconds}s)");
			return ServerComponentInitializationStatus.Initialized;
		}

		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				return;
			}

			// Called off, not decided: nobody's record moves for a duel the server ended.
			for (int i = duels.Count - 1; i >= 0; --i)
			{
				EndDuel(duels[i], 0, false, DuelEndReason.Shutdown);
			}
			DrainMainThreadQueue<IDuelSystemMainThreadQueueData>(maxMainThreadActionsPerFrame, true);

			Server.NetworkWrapper.UnregisterBroadcast<DuelRequestBroadcast>(OnDuelRequest);
			Server.NetworkWrapper.UnregisterBroadcast<DuelInviteResponseBroadcast>(OnDuelInviteResponse);
			Server.NetworkWrapper.UnregisterBroadcast<DuelYieldBroadcast>(OnDuelYield);
			UnsubscribeFromCharacterLifecycle();
			DuelRegistry.OnFinishingBlow -= OnFinishingBlow;
			ICharacterDamageController.OnKilled -= OnCharacterKilled;

			duels.Clear();
			duelsByCharacter.Clear();
			invitesByTarget.Clear();
			invitesByChallenger.Clear();
			inviteCooldowns.Clear();
			pendingEnds.Clear();
			DuelRegistry.Clear();
			if (Server.DataContainerRegistry.TryGet<IDuelSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard?.Clear();
			}
		}

		protected override void OnUpdate(float deltaTime)
		{
			DrainMainThreadQueue<IDuelSystemMainThreadQueueData>(maxMainThreadActionsPerFrame, false);
			if (Server.DataContainerRegistry.TryGet<IDuelSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard.Sweep(ingressSweepIntervalSeconds, ingressEntryTtlSeconds, ingressSweepMaxRemovals);
			}
			TickDuels();
			SweepInvites();
		}

		// ── Lifecycle ─────────────────────────────────────────────────────────────────────

		private void SubscribeToCharacterLifecycle()
		{
			if (!Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem))
			{
				Log.Warning(LOG, "No character system found; a duellist who leaves is only noticed by the duel check.");
				return;
			}
			characterSystem.OnDisconnect -= OnCharacterLeft;
			characterSystem.OnDespawnCharacter -= OnCharacterLeft;
			characterSystem.OnDisconnect += OnCharacterLeft;
			characterSystem.OnDespawnCharacter += OnCharacterLeft;
			characterSystem.OnSpawnCharacter -= OnCharacterSpawned;
			characterSystem.OnSpawnCharacter += OnCharacterSpawned;
		}

		private void UnsubscribeFromCharacterLifecycle()
		{
			if (Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem))
			{
				characterSystem.OnDisconnect -= OnCharacterLeft;
				characterSystem.OnDespawnCharacter -= OnCharacterLeft;
				characterSystem.OnSpawnCharacter -= OnCharacterSpawned;
			}
		}

		/// <summary>
		/// A character arrived (logged in, or crossed from another scene): tell them their record, for the character sheet.
		/// Read through the persistence queue under their id, so it lands after any result still being written.
		/// </summary>
		private void OnCharacterSpawned(NetworkConnection conn, IPlayerCharacter character, Scene scene)
		{
			if (character == null || character.ID <= 0)
			{
				return;
			}
			long characterID = character.ID;
			EnqueuePersistence(async () =>
			{
				if (!TryGetDbService(out FishMMO.Database.Npgsql.Services.Interfaces.IDuelRecordService service))
				{
					return;
				}
				FishMMO.Database.DatabaseResult<FishMMO.Database.Data.DuelRecordData> result = await service.FetchRecordAsync(characterID);
				if (!result.IsSuccess)
				{
					await Log.Warning(LOG, $"Reading the duel record of {characterID} failed: [{result.ErrorCode}] {result.ErrorMessage}");
					return;
				}
				FishMMO.Database.Data.DuelRecordData record = result.Data;
				// DuelID 0: no duel to announce, only the numbers.
				TryEnqueueMainThread<IDuelSystemMainThreadQueueData>(() => SendRecord(0, record));
			}, characterID);
		}

		/// <summary>A disconnect, or a despawn (a hand-off to another scene): the leaver forfeits, and their challenges lapse.</summary>
		private void OnCharacterLeft(NetworkConnection conn, IPlayerCharacter character)
		{
			if (character == null)
			{
				return;
			}
			if (duelsByCharacter.TryGetValue(character.ID, out Duel duel))
			{
				EndDuel(duel, character.ID, false, DuelEndReason.Left);
			}
			DropInvitesInvolving(character.ID);
		}

		/// <summary>A duellist killed by anything at all (the opponent's blows are spared, so this is a monster, a fall, lava): a loss.</summary>
		private void OnCharacterKilled(ICharacter killer, ICharacter victim)
		{
			if (victim is IPlayerCharacter && duelsByCharacter.TryGetValue(victim.ID, out Duel duel))
			{
				EndDuel(duel, victim.ID, false, DuelEndReason.Died);
			}
		}

		/// <summary>The harm rule spared a blow between two duellists: the striker wins.</summary>
		private void OnFinishingBlow(long winnerID, long loserID)
		{
			if (duelsByCharacter.TryGetValue(loserID, out Duel duel) && duel.Phase == DuelPhase.Live &&
				(duel.FirstID == winnerID || duel.SecondID == winnerID))
			{
				EndDuel(duel, loserID, false, DuelEndReason.Defeated);
			}
		}

		// ── Lookups ───────────────────────────────────────────────────────────────────────

		/// <summary>Whether a character is duelling, or counting down to one.</summary>
		public bool IsDuelling(long characterID) => duelsByCharacter.ContainsKey(characterID);

		private bool TryGetResidentCharacter(long characterID, out IPlayerCharacter character)
		{
			character = null;
			return Server != null &&
				Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> mappingData) &&
				mappingData.CharactersByID.TryGetValue(characterID, out character) &&
				character != null;
		}

		private void Send<T>(IPlayerCharacter character, T message) where T : struct, FishNet.Broadcast.IBroadcast
		{
			if (character?.Owner != null && character.Owner.IsActive)
			{
				Server.NetworkWrapper.Broadcast(character.Owner, message, true, Channel.Reliable);
			}
		}

		private void Send<T>(NetworkConnection conn, T message) where T : struct, FishNet.Broadcast.IBroadcast
		{
			if (conn != null && conn.IsActive)
			{
				Server.NetworkWrapper.Broadcast(conn, message, true, Channel.Reliable);
			}
		}

		private bool TryBeginIngressGuard(NetworkConnection conn, IngressOperation operation, out long guardKey)
		{
			guardKey = 0;
			return Server.DataContainerRegistry.TryGet<IDuelSystemRuntimeData>(out var runtimeData) &&
				runtimeData.IngressGuard.TryBegin(conn.ClientId, (byte)operation, ingressDebounceMilliseconds, out guardKey);
		}

		private void EndIngressGuard(long guardKey)
		{
			if (Server?.DataContainerRegistry.TryGet<IDuelSystemRuntimeData>(out var runtimeData) == true)
			{
				runtimeData.IngressGuard?.End(guardKey);
			}
		}

		// ── The fight ─────────────────────────────────────────────────────────────────────

		/// <summary>Starts an agreed duel: the ground, the teleport, the countdown.</summary>
		private void StartDuel(IPlayerCharacter first, IPlayerCharacter second, in DuelArena arena, Vector3 firstStart, Vector3 secondStart)
		{
			double now = Now;
			Duel duel = new Duel
			{
				ID = nextDuelID++,
				FirstID = first.ID,
				SecondID = second.ID,
				FirstName = first.CharacterName,
				SecondName = second.CharacterName,
				Scene = first.GameObject.scene,
				Arena = arena,
				Phase = DuelPhase.Countdown,
				LiveAt = now + countdownSeconds,
				EndsAt = 0,
				NextCheckAt = now + checkIntervalSeconds,
			};
			duels.Add(duel);
			duelsByCharacter[first.ID] = duel;
			duelsByCharacter[second.ID] = duel;
			DuelRegistry.Publish(duel.ID, first.ID, second.ID, false);

			// Through the motor, never the transform: prediction reconciles against the motor, and the owner's client
			// takes the move on its next reconcile, as it does a respawn.
			first.Motor.SetPositionAndRotationAndVelocity(firstStart, DuelRules.FirstFacing(arena.Yaw), Vector3.zero);
			second.Motor.SetPositionAndRotationAndVelocity(secondStart, DuelRules.SecondFacing(arena.Yaw), Vector3.zero);

			Send(first, new DuelStartBroadcast
			{
				DuelID = duel.ID,
				OpponentCharacterID = second.ID,
				OpponentName = second.CharacterName,
				Arena = arena,
				StartYaw = arena.Yaw,
				CountdownSeconds = countdownSeconds,
				TimeLimitSeconds = timeLimitSeconds,
			});
			Send(second, new DuelStartBroadcast
			{
				DuelID = duel.ID,
				OpponentCharacterID = first.ID,
				OpponentName = first.CharacterName,
				Arena = arena,
				StartYaw = arena.Yaw + 180f,
				CountdownSeconds = countdownSeconds,
				TimeLimitSeconds = timeLimitSeconds,
			});
			Log.Debug(LOG, $"Duel {duel.ID}: {first.ID} vs {second.ID} at {arena.Center} (yaw {arena.Yaw:0}).");
		}

		/// <summary>The countdown's end, then each duel's presence, ground and time, at the check interval.</summary>
		private void TickDuels()
		{
			if (duels.Count == 0)
			{
				return;
			}

			double now = Now;
			pendingEnds.Clear();
			for (int i = 0; i < duels.Count; ++i)
			{
				Duel duel = duels[i];
				if (duel.Phase == DuelPhase.Countdown && now >= duel.LiveAt)
				{
					duel.Phase = DuelPhase.Live;
					duel.EndsAt = timeLimitSeconds > 0f ? now + timeLimitSeconds : 0;
					DuelRegistry.SetLive(duel.ID, true);
					if (TryGetResidentCharacter(duel.FirstID, out IPlayerCharacter a)) Send(a, new DuelLiveBroadcast { DuelID = duel.ID });
					if (TryGetResidentCharacter(duel.SecondID, out IPlayerCharacter b)) Send(b, new DuelLiveBroadcast { DuelID = duel.ID });
				}

				if (now < duel.NextCheckAt)
				{
					continue;
				}
				duel.NextCheckAt = now + checkIntervalSeconds;

				bool firstOut = !IsStillInDuel(duel, duel.FirstID, out bool firstGone, out bool firstDead);
				bool secondOut = !IsStillInDuel(duel, duel.SecondID, out bool secondGone, out bool secondDead);

				if (firstGone || secondGone)
				{
					pendingEnds.Add((duel, firstGone ? duel.FirstID : duel.SecondID, false, DuelEndReason.Left));
				}
				else if (firstDead || secondDead)
				{
					pendingEnds.Add((duel, firstDead ? duel.FirstID : duel.SecondID, false, DuelEndReason.Died));
				}
				else if (firstOut && secondOut)
				{
					pendingEnds.Add((duel, 0, true, DuelEndReason.LeftGround));
				}
				else if (firstOut || secondOut)
				{
					pendingEnds.Add((duel, firstOut ? duel.FirstID : duel.SecondID, false, DuelEndReason.LeftGround));
				}
				else if (duel.Phase == DuelPhase.Live && duel.EndsAt > 0 && now >= duel.EndsAt)
				{
					pendingEnds.Add((duel, 0, true, DuelEndReason.TimeUp));
				}
			}

			for (int i = 0; i < pendingEnds.Count; ++i)
			{
				(Duel duel, long loserID, bool draw, DuelEndReason reason) = pendingEnds[i];
				EndDuel(duel, loserID, draw, reason);
			}
			pendingEnds.Clear();
		}

		/// <summary>
		/// Whether a duellist is still here, alive, in the duel's scene and on its ground. <paramref name="gone"/>: not on
		/// this server, or in another scene; <paramref name="dead"/>: dead.
		/// </summary>
		private bool IsStillInDuel(Duel duel, long characterID, out bool gone, out bool dead)
		{
			gone = false;
			dead = false;
			if (!TryGetResidentCharacter(characterID, out IPlayerCharacter character) || character.Transform == null ||
				character.GameObject == null || character.GameObject.scene != duel.Scene)
			{
				gone = true;
				return false;
			}
			if (character.IsFlagged(CharacterFlags.IsDead))
			{
				dead = true;
				return false;
			}
			return DuelRules.Contains(duel.Arena, character.Transform.position, leaveTolerance);
		}

		/// <summary>
		/// Ends a duel: <paramref name="loserID"/> loses (0 with <paramref name="draw"/> for a draw, 0 without it for a duel
		/// called off). Forgets it, tells both sides, and records the result.
		/// </summary>
		private void EndDuel(Duel duel, long loserID, bool draw, DuelEndReason reason)
		{
			if (duel == null || !duels.Remove(duel))
			{
				return;
			}
			if (duelsByCharacter.TryGetValue(duel.FirstID, out Duel byFirst) && ReferenceEquals(byFirst, duel))
			{
				duelsByCharacter.Remove(duel.FirstID);
			}
			if (duelsByCharacter.TryGetValue(duel.SecondID, out Duel bySecond) && ReferenceEquals(bySecond, duel))
			{
				duelsByCharacter.Remove(duel.SecondID);
			}
			DuelRegistry.Remove(duel.ID);

			bool cancelled = !draw && loserID == 0;
			long winnerID = cancelled || draw ? duel.FirstID : (loserID == duel.FirstID ? duel.SecondID : duel.FirstID);
			long resultLoserID = cancelled || draw ? duel.SecondID : loserID;

			SendEnd(duel, duel.FirstID, cancelled ? DuelOutcome.Cancelled : draw ? DuelOutcome.Draw : duel.FirstID == winnerID ? DuelOutcome.Won : DuelOutcome.Lost, reason);
			SendEnd(duel, duel.SecondID, cancelled ? DuelOutcome.Cancelled : draw ? DuelOutcome.Draw : duel.SecondID == winnerID ? DuelOutcome.Won : DuelOutcome.Lost, reason);
			Log.Debug(LOG, $"Duel {duel.ID} ended: {(cancelled ? "called off" : draw ? "draw" : $"{winnerID} beat {resultLoserID}")} ({reason}).");

			if (!cancelled)
			{
				RecordResult(duel.ID, winnerID, resultLoserID, draw, reason);
			}
		}

		private void SendEnd(Duel duel, long characterID, DuelOutcome outcome, DuelEndReason reason)
		{
			if (TryGetResidentCharacter(characterID, out IPlayerCharacter character))
			{
				Send(character, new DuelEndBroadcast { DuelID = duel.ID, Outcome = outcome, Reason = reason });
			}
		}

		/// <summary>Writes a result through the persistence queue, then tells each side still here their new record.</summary>
		private void RecordResult(long duelID, long winnerID, long loserID, bool draw, DuelEndReason reason)
		{
			Guid requestKey = Guid.NewGuid();
			int k = ratingKFactor;
			EnqueuePersistence(async () =>
			{
				if (!TryGetDbService(out FishMMO.Database.Npgsql.Services.Interfaces.IDuelRecordService service))
				{
					await Log.Warning(LOG, $"Duel {duelID}: no duel record service; the result was not recorded.");
					return;
				}
				FishMMO.Database.DatabaseResult<FishMMO.Database.Data.DuelResultData> result =
					await service.RecordResultAsync(requestKey, winnerID, loserID, draw, (short)reason, k);
				if (!result.IsSuccess)
				{
					await Log.Warning(LOG, $"Duel {duelID}: recording {winnerID} vs {loserID} failed: [{result.ErrorCode}] {result.ErrorMessage}");
					return;
				}
				FishMMO.Database.Data.DuelResultData data = result.Data;
				TryEnqueueMainThread<IDuelSystemMainThreadQueueData>(() =>
				{
					SendRecord(duelID, data.Winner);
					SendRecord(duelID, data.Loser);
				});
			}, winnerID);
		}

		private void SendRecord(long duelID, FishMMO.Database.Data.DuelRecordData record)
		{
			if (TryGetResidentCharacter(record.CharacterID, out IPlayerCharacter character))
			{
				Send(character, new DuelRecordBroadcast
				{
					DuelID = duelID,
					Rating = record.Rating,
					RatingChange = record.RatingChange,
					Wins = record.Wins,
					Losses = record.Losses,
					Draws = record.Draws,
				});
			}
		}
	}
}
