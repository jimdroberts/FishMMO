using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FishNet.Connection;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;

[assembly: FishMMO.Server.Implementation.ModuleServerSystem(typeof(FishMMO.Server.Implementation.World.SceneServer.GateSystem),
	FishMMO.Server.Implementation.ModuleServerSystemAttribute.SceneServer)]

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The scene server's side of the Gate and Call to Arms spells (issue #241).
	/// </summary>
	/// <remarks>
	/// <para><b>Gate.</b> The cast (<see cref="OpenGateAction"/>) starts a wait for the caster's Gate Stone choice
	/// (<see cref="BeginChoice"/>); the map sends it. Damage, the time running out or a cancel ends the wait. A chosen stone opens
	/// a portal in front of the caster for the template's lifetime; anyone the gate's access lets in who walks into it is sent
	/// to the stone (a local move in the same scene instance, the world server's hand-off otherwise) with a signed return
	/// ticket (<see cref="TravelTicket"/>) naming the casting spot.</para>
	/// <para><b>The stone is the way home.</b> A Gate Stone lights while a gate known here leads to it: one cast on this server
	/// to a stone in the same scene instance, or one a traveller's ticket names when it arrives. A ticket holder who uses the
	/// lit stone goes back to its ticket's origin. Nothing is spawned at the destination and nothing is stored: scene servers
	/// learn of a gate only from the tickets their arrivals carry, which is why using a stone is for token holders only.</para>
	/// <para><b>Call to Arms.</b> A signed summons (<see cref="TravelTicketKind.CallToArms"/>) goes to the caster's party:
	/// directly to members on this server, through the party chat pump (<see cref="PartySignals"/>) to the rest. Each member
	/// who accepts is checked again and moved to the caster's side.</para>
	/// <para><b>The key.</b> Tickets are signed with a key derived from the deployment's signing-key KEK
	/// (<see cref="SigningKeyKekProvider"/>), loaded once at start. Without it, only gates within one scene instance work (their
	/// returns are kept here, never sent) and Call to Arms is refused.</para>
	/// <para>Declared in code (<see cref="ModuleServerSystemAttribute"/>): the module ships no asset for it.</para>
	/// </remarks>
	public class GateSystem : ServerBehaviour, IGateHost
	{
		/// <summary>Seconds between walk-in checks of the open portals.</summary>
		public const float WalkInIntervalSeconds = 0.1f;

		/// <summary>Seconds a character is not pulled into a portal after arriving anywhere (no bounce back through).</summary>
		public const float ArrivalGraceSeconds = 3.0f;

		/// <summary>Most return tickets kept per character on this server.</summary>
		public const int MaxReturnsPerCharacter = TravelTicketWallet.MaxPerCharacter;

		/// <summary>
		/// Tickets one connection may present per <see cref="PresentWindowSeconds"/>: a client presents all it holds at once on
		/// arrival (at most <see cref="TravelTicketWallet.MaxPerCharacter"/>), and verifying each costs a MAC.
		/// </summary>
		public const int PresentBudget = TravelTicketWallet.MaxPerCharacter * 2;

		/// <summary>The window <see cref="PresentBudget"/> is counted over, in seconds.</summary>
		public const float PresentWindowSeconds = 10.0f;

		/// <summary>The <see cref="PartySignals"/> kind a Call to Arms travels under.</summary>
		public const string CallToArmsSignal = "cta";

		private sealed class Choice
		{
			public IPlayerCharacter Caster;
			public GateSettings Settings;
			public float Deadline;
		}

		private sealed class Gate
		{
			public long ID;
			public long CasterID;
			public long PartyID;
			public long GuildID;
			public GateAccess Access;
			public SceneHandle Scene;
			public Vector3 Position;
			public float Yaw;
			public float Radius;
			public long ExpiresUnixMs;
			public float ExpiresAt;
			public TravelTicket Return;
			public Vector3 StoneArrival;
			public float StoneYaw;
			public int Arrivals;
		}

		private struct StoneKey : IEquatable<StoneKey>
		{
			public SceneHandle Scene;
			public int Index;

			public bool Equals(StoneKey other) => Scene.Equals(other.Scene) && Index == other.Index;
			public override bool Equals(object obj) => obj is StoneKey other && Equals(other);
			public override int GetHashCode() => (Scene.GetHashCode() * 397) ^ Index;
		}

		private sealed class LitStone
		{
			public Vector3 Position;
			public float Yaw;
			public long ExpiresUnixMs;
		}

		private readonly Dictionary<long, Choice> choices = new Dictionary<long, Choice>();
		private readonly Dictionary<long, Gate> gates = new Dictionary<long, Gate>();
		private readonly Dictionary<StoneKey, LitStone> litStones = new Dictionary<StoneKey, LitStone>();
		private readonly Dictionary<long, List<TravelTicket>> returns = new Dictionary<long, List<TravelTicket>>();
		private readonly Dictionary<long, float> arrivalGrace = new Dictionary<long, float>();
		private readonly Dictionary<NetworkConnection, (float Start, int Count)> presentWindows = new Dictionary<NetworkConnection, (float, int)>();
		private readonly ConcurrentQueue<Action> mainThread = new ConcurrentQueue<Action>();
		private readonly List<long> scratchIDs = new List<long>();
		private readonly List<StoneKey> scratchStones = new List<StoneKey>();
		private readonly List<IPlayerCharacter> scratchPlayers = new List<IPlayerCharacter>();

		private ICharacterSystem<NetworkConnection, Scene> characterSystem;
		private byte[] ticketKey;
		private float walkInTimer;

		/// <summary>Whether tickets can be signed and verified here (the KEK loaded).</summary>
		public bool HasTicketKey => ticketKey != null;

		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}
			if (!Server.BehaviourRegistry.TryGet(out characterSystem) || characterSystem == null)
			{
				Log.Error("GateSystem", "InitializeOnce: ICharacterSystem not found; the Gate and Call to Arms spells are off on this server.");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			characterSystem.OnSpawnCharacter += CharacterSystem_OnSpawnCharacter;
			characterSystem.OnDisconnect += CharacterSystem_OnDisconnect;
			ICharacterDamageController.OnDamaged += CharacterDamageController_OnDamaged;
			Server.NetworkWrapper.RegisterBroadcast<GateChoiceBroadcast>(OnGateChoiceReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<GateChoiceCancelBroadcast>(OnGateChoiceCancelReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<TravelTicketPresentBroadcast>(OnTicketPresented, true);
			Server.NetworkWrapper.RegisterBroadcast<CallToArmsAnswerBroadcast>(OnCallToArmsAnswered, true);
			PartySignals.Register(CallToArmsSignal, OnCallToArmsSignal);
			GateHost.Current = this;
			LoadTicketKey();
			return ServerComponentInitializationStatus.Initialized;
		}

		public override void OnDeinitialize()
		{
			if (characterSystem != null)
			{
				characterSystem.OnSpawnCharacter -= CharacterSystem_OnSpawnCharacter;
				characterSystem.OnDisconnect -= CharacterSystem_OnDisconnect;
				characterSystem = null;
			}
			ICharacterDamageController.OnDamaged -= CharacterDamageController_OnDamaged;
			if (Server?.NetworkWrapper != null)
			{
				Server.NetworkWrapper.UnregisterBroadcast<GateChoiceBroadcast>(OnGateChoiceReceived);
				Server.NetworkWrapper.UnregisterBroadcast<GateChoiceCancelBroadcast>(OnGateChoiceCancelReceived);
				Server.NetworkWrapper.UnregisterBroadcast<TravelTicketPresentBroadcast>(OnTicketPresented);
				Server.NetworkWrapper.UnregisterBroadcast<CallToArmsAnswerBroadcast>(OnCallToArmsAnswered);
			}
			PartySignals.Unregister(CallToArmsSignal, OnCallToArmsSignal);
			if (ReferenceEquals(GateHost.Current, this))
			{
				GateHost.Current = null;
			}
			choices.Clear();
			gates.Clear();
			litStones.Clear();
			returns.Clear();
			arrivalGrace.Clear();
			presentWindows.Clear();
			ticketKey = null;
		}

		private void LoadTicketKey()
		{
			if (!TryGetDbService(out IDeploymentSecretService secrets))
			{
				Log.Warning("GateSystem", "IDeploymentSecretService unavailable: gates reach only the caster's own scene, and Call to Arms is off.");
				return;
			}
			Task.Run(async () =>
			{
				SigningKeyKekProvider.KekLoadResult result = await SigningKeyKekProvider.LoadFromDatabaseAsync(secrets).ConfigureAwait(false);
				byte[] key = result.Success ? TravelTicket.DeriveKey(result.Kek) : null;
				mainThread.Enqueue(() =>
				{
					ticketKey = key;
					if (key == null)
					{
						Log.Warning("GateSystem", $"No travel ticket key ({result.Error}): gates reach only the caster's own scene, and Call to Arms is off.");
					}
				});
			});
		}

		protected override void OnUpdate(float deltaTime)
		{
			while (mainThread.TryDequeue(out Action action))
			{
				try
				{
					action();
				}
				catch (Exception ex)
				{
					Log.Error("GateSystem", $"Main-thread work failed: {ex}");
				}
			}

			float now = Time.time;
			ExpireChoices(now);
			ExpireGates(now);
			ExpireStones();

			walkInTimer += deltaTime;
			if (walkInTimer >= WalkInIntervalSeconds)
			{
				walkInTimer = 0.0f;
				CheckWalkIns(now);
			}
		}

		private static long NowUnixMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		// ── Choosing a stone ────────────────────────────────────────────────────────────

		/// <inheritdoc />
		public string BeginChoice(IPlayerCharacter caster, in GateSettings settings)
		{
			if (caster?.Owner == null)
			{
				return null;
			}
			// Nothing to choose: say so now rather than open a map with no destination on it.
			WorldSceneDetailsCache cache = Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> sceneServer)
				? sceneServer.WorldSceneDetailsCache
				: null;
			if (!caster.TryGet(out IWaypointController waypoints) || !GateStoneDirectory.AnyDiscovered(cache, waypoints))
			{
				return NoStonesText;
			}
			choices[caster.ID] = new Choice()
			{
				Caster = caster,
				Settings = settings,
				Deadline = Time.time + settings.ChooseSeconds,
			};
			Send(caster.Owner, new GateChooseBroadcast() { Seconds = settings.ChooseSeconds });
			Toast(caster, ChooseText, ToastSeverity.Info);
			return null;
		}

		/// <summary>What a caster who has discovered no Gate Stone is told.</summary>
		public const string NoStonesText = "You know of no Gate Stone. Find one and use it (E), then cast Gate.";

		/// <summary>What the caster is told when the wait for its choice begins.</summary>
		public const string ChooseText = "Choose a Gate Stone on the map.";

		private void EndChoice(IPlayerCharacter caster, string text, ToastSeverity severity = ToastSeverity.Warning)
		{
			if (caster == null || !choices.Remove(caster.ID))
			{
				return;
			}
			if (caster.Owner != null && caster.Owner.IsActive)
			{
				Send(caster.Owner, new GateChooseBroadcast() { Seconds = 0.0f });
				Toast(caster, text, severity);
			}
		}

		private void ExpireChoices(float now)
		{
			if (choices.Count == 0)
			{
				return;
			}
			scratchIDs.Clear();
			foreach (KeyValuePair<long, Choice> entry in choices)
			{
				if (now >= entry.Value.Deadline || entry.Value.Caster == null || !entry.Value.Caster.IsSpawned)
				{
					scratchIDs.Add(entry.Key);
				}
			}
			foreach (long id in scratchIDs)
			{
				EndChoice(choices[id].Caster, "The Gate spell fades: no Gate Stone was chosen.");
			}
		}

		private void CharacterDamageController_OnDamaged(ICharacter attacker, ICharacter defender, int amount, DamageAttributeTemplate damageAttribute)
		{
			if (amount > 0 && defender is IPlayerCharacter player && choices.ContainsKey(player.ID))
			{
				EndChoice(player, "Your concentration breaks, and the gate does not open.");
			}
		}

		private void OnGateChoiceCancelReceived(NetworkConnection conn, GateChoiceCancelBroadcast msg, Channel channel)
		{
			if (TryGetPlayer(conn, out IPlayerCharacter player))
			{
				EndChoice(player, null);
			}
		}

		private void OnGateChoiceReceived(NetworkConnection conn, GateChoiceBroadcast msg, Channel channel)
		{
			if (!TryGetPlayer(conn, out IPlayerCharacter caster))
			{
				return;
			}

			bool choosing = choices.TryGetValue(caster.ID, out Choice choice);
			SceneWaypointDetails stone = null;
			bool known = !string.IsNullOrEmpty(msg.SceneName) && msg.SceneName.Length <= 128 &&
				TryGetStoneDetails(msg.SceneName, msg.WaypointIndex, out stone);
			bool discovered = known && caster.TryGet(out IWaypointController waypoints) && waypoints.IsUnlocked(msg.SceneName, msg.WaypointIndex);
			bool canOpenHere = caster.GameObject != null && !ArenaTeamRegistry.IsArena(caster.GameObject.scene.handle) &&
				!NoTravelZone.Blocks(caster) && !caster.IsInInstance();

			GateChoiceRefusal refusal = GateRules.DecideChoice(choosing, known, known && stone.IsGateStone, discovered,
				CharacterStateValidation.CanAct(caster), canOpenHere);
			if (refusal != GateChoiceRefusal.None)
			{
				// A refused choice leaves the wait running: the caster may pick another stone.
				Toast(caster, GateRules.Describe(refusal));
				return;
			}

			bool sameInstance = string.Equals(msg.SceneName, caster.CurrentSceneName(), StringComparison.Ordinal);
			if (!sameInstance && ticketKey == null)
			{
				Toast(caster, "Gate magic cannot reach another land from here right now.");
				return;
			}

			OpenGate(caster, choice.Settings, msg.SceneName, msg.WaypointIndex, stone, sameInstance);
			EndChoice(caster, $"A gate opens to {stone.Name}.", ToastSeverity.Success);
		}

		private bool TryGetStoneDetails(string sceneName, int waypointIndex, out SceneWaypointDetails details)
		{
			details = null;
			return Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> sceneServer) &&
				sceneServer.WorldSceneDetailsCache?.Scenes != null &&
				sceneServer.WorldSceneDetailsCache.Scenes.TryGetValue(sceneName, out WorldSceneDetails scene) &&
				scene?.Waypoints != null &&
				scene.Waypoints.TryGetValue(waypointIndex, out details) &&
				details != null;
		}

		// ── Gates ───────────────────────────────────────────────────────────────────────

		private void OpenGate(IPlayerCharacter caster, in GateSettings settings, string stoneScene, int stoneIndex,
			SceneWaypointDetails stone, bool sameInstance)
		{
			// One gate per caster: a new one closes the last.
			scratchIDs.Clear();
			foreach (KeyValuePair<long, Gate> entry in gates)
			{
				if (entry.Value.CasterID == caster.ID)
				{
					scratchIDs.Add(entry.Key);
				}
			}
			foreach (long id in scratchIDs)
			{
				CloseGate(id);
			}

			Transform casterTransform = caster.Transform;
			float yaw = casterTransform.eulerAngles.y;
			long expiresUnixMs = NowUnixMs + (long)(settings.LifetimeSeconds * 1000.0f);
			Vector3 arrival = stone.HasArrival ? stone.ArrivalPosition : stone.Position + Vector3.forward * 2.0f;
			float arrivalYaw = stone.HasArrival ? stone.ArrivalYaw : 0.0f;

			caster.TryGet(out IPartyController party);
			caster.TryGet(out IGuildController guild);
			var gate = new Gate()
			{
				ID = NewID(),
				CasterID = caster.ID,
				PartyID = party != null ? party.ID : 0L,
				GuildID = guild != null ? guild.ID : 0L,
				Access = settings.Access,
				Scene = caster.GameObject.scene.handle,
				Position = GateRules.PortalPosition(casterTransform.position, yaw, settings.ForwardDistance),
				Yaw = yaw + 180.0f,
				Radius = settings.Radius,
				ExpiresUnixMs = expiresUnixMs,
				ExpiresAt = Time.time + settings.LifetimeSeconds,
				StoneArrival = arrival,
				StoneYaw = arrivalYaw,
				Return = new TravelTicket()
				{
					Kind = TravelTicketKind.GateReturn,
					ExpiresUnixMs = expiresUnixMs,
					OriginScene = caster.CurrentSceneName(),
					OriginSceneID = caster.SceneHandle,
					OriginPosition = casterTransform.position,
					OriginYaw = yaw + 180.0f,
					StoneScene = stoneScene,
					StoneIndex = stoneIndex,
					CasterName = string.Empty,
				},
			};
			gate.Return.ID = gate.ID;
			gates[gate.ID] = gate;
			BroadcastPortal(gate, open: true);

			// A stone in the caster's own scene instance lights at once; anywhere else, the first traveller's ticket lights it.
			if (sameInstance && WaypointRegistry.TryGet(gate.Scene, stoneIndex, out IWaypoint live) && live.Kind == WaypointKind.GateStone)
			{
				LightStone(gate.Scene, live, expiresUnixMs);
			}
		}

		private void CloseGate(long gateID)
		{
			if (gates.Remove(gateID, out Gate gate))
			{
				BroadcastPortal(gate, open: false);
			}
		}

		private void ExpireGates(float now)
		{
			if (gates.Count == 0)
			{
				return;
			}
			scratchIDs.Clear();
			foreach (KeyValuePair<long, Gate> entry in gates)
			{
				if (now >= entry.Value.ExpiresAt)
				{
					scratchIDs.Add(entry.Key);
				}
			}
			foreach (long id in scratchIDs)
			{
				CloseGate(id);
			}
		}

		private void CheckWalkIns(float now)
		{
			if (gates.Count == 0 || !Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> mapping))
			{
				return;
			}
			scratchIDs.Clear();
			scratchIDs.AddRange(gates.Keys);
			foreach (long gateID in scratchIDs)
			{
				if (!gates.TryGetValue(gateID, out Gate gate))
				{
					continue;
				}
				scratchPlayers.Clear();
				foreach (IPlayerCharacter player in mapping.ConnectionCharacters.Values)
				{
					if (player?.GameObject != null && player.GameObject.scene.handle.Equals(gate.Scene) &&
						player.Transform != null && GateRules.IsInside(gate.Position, gate.Radius, player.Transform.position))
					{
						scratchPlayers.Add(player);
					}
				}
				foreach (IPlayerCharacter player in scratchPlayers)
				{
					TryEnter(gate, player, now);
				}
			}
		}

		private void TryEnter(Gate gate, IPlayerCharacter player, float now)
		{
			if (arrivalGrace.TryGetValue(player.ID, out float graceUntil) && now < graceUntil)
			{
				return;
			}
			if (!CharacterStateValidation.CanAct(player))
			{
				return;
			}
			player.TryGet(out IPartyController party);
			player.TryGet(out IGuildController guild);
			bool sameParty = gate.PartyID > 0 && party != null && party.ID == gate.PartyID;
			bool sameGuild = gate.GuildID > 0 && guild != null && guild.ID == gate.GuildID;
			if (!GateRules.CanEnter(gate.Access, player.ID == gate.CasterID, sameParty, sameGuild))
			{
				return;
			}

			TravelTicket ticket = gate.Return;
			ticket.CharacterID = player.ID;
			string stoneScene = ticket.StoneScene;
			bool local = CharacterTransfer.IsLocalMove(player.CurrentSceneName(), player.SceneHandle, player.IsInInstance(),
				stoneScene, GateRules.StoneChannel(player.CurrentSceneName(), player.SceneHandle, player.IsInInstance(), stoneScene));

			byte[] bytes = ticketKey != null ? TravelTicket.Encode(ticket, ticketKey) : null;
			if (!local && bytes == null)
			{
				return;
			}

			Vector3 spot = GateRules.ArrivalSpot(gate.StoneArrival, gate.StoneYaw, gate.Arrivals);
			var destination = new TransferDestination(stoneScene,
				GateRules.StoneChannel(player.CurrentSceneName(), player.SceneHandle, player.IsInInstance(), stoneScene),
				spot, Quaternion.Euler(0.0f, gate.StoneYaw, 0.0f));
			TransferOutcome outcome = CharacterTransfer.Transfer(player, destination, out TransferRefusal refusal);
			if (outcome == TransferOutcome.Refused)
			{
				Log.Debug("GateSystem", $"Character {player.ID} was not sent through gate {gate.ID}: {refusal}.");
				arrivalGrace[player.ID] = now + ArrivalGraceSeconds;
				return;
			}
			++gate.Arrivals;
			arrivalGrace[player.ID] = now + ArrivalGraceSeconds;
			if (outcome == TransferOutcome.MovedLocally)
			{
				KeepReturn(player.ID, ticket);
			}
			/* The client keeps the ticket for every later scene server. A hand-off is deferred to the next frame and its
			 * disconnect is graceful, so this reliable message reaches the client before it leaves. */
			if (bytes != null && player.Owner != null)
			{
				Send(player.Owner, new TravelTicketBroadcast() { Ticket = bytes, Seconds = ticket.RemainingSeconds(NowUnixMs) });
			}
		}

		private void BroadcastPortal(Gate gate, bool open)
		{
			var msg = new GatePortalBroadcast()
			{
				GateID = gate.ID,
				Position = gate.Position,
				Yaw = gate.Yaw,
				Radius = gate.Radius,
				RemainingSeconds = open ? Mathf.Max(0.1f, gate.ExpiresAt - Time.time) : 0.0f,
			};
			ForEachPlayerIn(gate.Scene, player => Send(player.Owner, msg));
		}

		// ── Stones ──────────────────────────────────────────────────────────────────────

		private void LightStone(SceneHandle scene, IWaypoint stone, long expiresUnixMs)
		{
			var key = new StoneKey() { Scene = scene, Index = stone.WaypointIndex };
			if (litStones.TryGetValue(key, out LitStone lit))
			{
				if (expiresUnixMs <= lit.ExpiresUnixMs)
				{
					return;
				}
				lit.ExpiresUnixMs = expiresUnixMs;
			}
			else
			{
				Transform transform = stone.GameObject.transform;
				lit = new LitStone() { Position = transform.position, Yaw = transform.eulerAngles.y, ExpiresUnixMs = expiresUnixMs };
				litStones[key] = lit;
			}
			BroadcastStone(key, lit, NowUnixMs);
		}

		private void BroadcastStone(StoneKey key, LitStone lit, long nowUnixMs)
		{
			var msg = new GateStoneLitBroadcast()
			{
				WaypointIndex = key.Index,
				Position = lit.Position,
				Yaw = lit.Yaw,
				RemainingSeconds = Mathf.Max(0.0f, (lit.ExpiresUnixMs - nowUnixMs) / 1000.0f),
			};
			ForEachPlayerIn(key.Scene, player => Send(player.Owner, msg));
		}

		private void ExpireStones()
		{
			if (litStones.Count == 0)
			{
				return;
			}
			long now = NowUnixMs;
			scratchStones.Clear();
			foreach (KeyValuePair<StoneKey, LitStone> entry in litStones)
			{
				if (now >= entry.Value.ExpiresUnixMs)
				{
					scratchStones.Add(entry.Key);
				}
			}
			foreach (StoneKey key in scratchStones)
			{
				LitStone lit = litStones[key];
				litStones.Remove(key);
				lit.ExpiresUnixMs = now;
				BroadcastStone(key, lit, now);
			}
		}

		/// <inheritdoc />
		public bool UseStone(IPlayerCharacter player, IWaypoint stone)
		{
			if (player == null || stone == null || !returns.TryGetValue(player.ID, out List<TravelTicket> held))
			{
				return false;
			}
			long now = NowUnixMs;
			int found = -1;
			for (int i = held.Count - 1; i >= 0; --i)
			{
				TravelTicket ticket = held[i];
				if (!ticket.IsLive(now))
				{
					held.RemoveAt(i);
					continue;
				}
				if (found < 0 && ticket.StoneIndex == stone.WaypointIndex &&
					string.Equals(ticket.StoneScene, stone.SceneName, StringComparison.Ordinal))
				{
					found = i;
				}
			}
			if (found < 0)
			{
				return false;
			}
			if (!CharacterStateValidation.CanAct(player))
			{
				return true;
			}

			TravelTicket home = held[found];
			var destination = new TransferDestination(home.OriginScene, home.OriginSceneID, home.OriginPosition,
				Quaternion.Euler(0.0f, home.OriginYaw, 0.0f));
			TransferOutcome outcome = CharacterTransfer.Transfer(player, destination, out TransferRefusal refusal);
			if (outcome == TransferOutcome.Refused)
			{
				Log.Debug("GateSystem", $"Character {player.ID} was not sent home through a Gate Stone: {refusal}.");
				Toast(player, "The Gate Stone flickers, but does not carry you.");
			}
			else if (outcome == TransferOutcome.MovedLocally)
			{
				arrivalGrace[player.ID] = Time.time + ArrivalGraceSeconds;
			}
			return true;
		}

		// ── Tickets ─────────────────────────────────────────────────────────────────────

		private void KeepReturn(long characterID, in TravelTicket ticket)
		{
			if (!returns.TryGetValue(characterID, out List<TravelTicket> held))
			{
				held = new List<TravelTicket>();
				returns[characterID] = held;
			}
			for (int i = held.Count - 1; i >= 0; --i)
			{
				if (held[i].ID == ticket.ID)
				{
					held.RemoveAt(i);
				}
			}
			while (held.Count >= MaxReturnsPerCharacter)
			{
				held.RemoveAt(0);
			}
			held.Add(ticket);
		}

		private void OnTicketPresented(NetworkConnection conn, TravelTicketPresentBroadcast msg, Channel channel)
		{
			if (!TryGetPlayer(conn, out IPlayerCharacter player) || ticketKey == null)
			{
				return;
			}
			float now = Time.time;
			if (!presentWindows.TryGetValue(conn, out (float Start, int Count) window) || now - window.Start >= PresentWindowSeconds)
			{
				window = (now, 0);
			}
			if (window.Count >= PresentBudget)
			{
				return;
			}
			presentWindows[conn] = (window.Start, window.Count + 1);

			if (!TravelTicket.TryDecode(msg.Ticket, ticketKey, out TravelTicket ticket) ||
				ticket.Kind != TravelTicketKind.GateReturn ||
				ticket.CharacterID != player.ID ||
				!ticket.IsLive(NowUnixMs))
			{
				return;
			}
			KeepReturn(player.ID, ticket);

			// A ticket that leads here lights its stone in the channel the traveller landed in.
			if (player.GameObject != null && !player.IsInInstance() &&
				string.Equals(ticket.StoneScene, player.CurrentSceneName(), StringComparison.Ordinal) &&
				WaypointRegistry.TryGet(player.GameObject.scene.handle, ticket.StoneIndex, out IWaypoint stone) &&
				stone.Kind == WaypointKind.GateStone)
			{
				LightStone(player.GameObject.scene.handle, stone, ticket.ExpiresUnixMs);
			}
		}

		// ── Call to Arms ────────────────────────────────────────────────────────────────

		/// <inheritdoc />
		public string CallToArms(IPlayerCharacter caster, in CallToArmsSettings settings)
		{
			if (caster?.Transform == null || !caster.TryGet(out IPartyController party) || party.ID < 1)
			{
				return "You are not in a party.";
			}
			if (ticketKey == null)
			{
				return "Call to Arms cannot reach your party right now.";
			}

			var summons = new TravelTicket()
			{
				Kind = TravelTicketKind.CallToArms,
				ID = NewID(),
				CharacterID = caster.ID,
				PartyID = party.ID,
				ExpiresUnixMs = NowUnixMs + (long)(settings.AcceptSeconds * 1000.0f),
				OriginScene = caster.CurrentSceneName(),
				OriginSceneID = caster.SceneHandle,
				OriginPosition = caster.Transform.position,
				OriginYaw = caster.Transform.eulerAngles.y,
				StoneScene = string.Empty,
				StoneIndex = -1,
				CasterName = caster.CharacterName ?? string.Empty,
			};
			byte[] bytes = TravelTicket.Encode(summons, ticketKey);
			if (bytes == null)
			{
				return "Call to Arms cannot reach your party right now.";
			}

			int local = DeliverSummons(summons, bytes);
			bool posted = PartySignals.Post(caster, party.ID, CallToArmsSignal, Convert.ToBase64String(bytes));
			Toast(caster, posted || local > 0 ? "You call your party to arms." : "No member of your party can hear the call.",
				posted || local > 0 ? ToastSeverity.Success : ToastSeverity.Warning);
			return null;
		}

		/// <summary>Prompts this server's members of the summoned party (not the caster). Returns how many.</summary>
		private int DeliverSummons(in TravelTicket summons, byte[] bytes)
		{
			if (!Server.DataContainerRegistry.TryGet(out IPartyCharacterMappingData partyData) ||
				partyData.PartyCharacterTracker == null ||
				!partyData.PartyCharacterTracker.TryGetValue(summons.PartyID, out HashSet<long> members) ||
				!Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> mapping))
			{
				return 0;
			}
			var msg = new CallToArmsBroadcast()
			{
				Ticket = bytes,
				CasterName = summons.CasterName,
				SceneName = summons.OriginScene,
				Seconds = summons.RemainingSeconds(NowUnixMs),
			};
			int count = 0;
			foreach (long memberID in members)
			{
				if (memberID == summons.CharacterID || !mapping.CharactersByID.TryGetValue(memberID, out IPlayerCharacter member) ||
					member?.Owner == null || !member.TryGet(out IPartyController memberParty) || memberParty.ID != summons.PartyID)
				{
					continue;
				}
				Send(member.Owner, msg);
				++count;
			}
			return count;
		}

		private void OnCallToArmsSignal(long partyID, string payload)
		{
			if (ticketKey == null || string.IsNullOrEmpty(payload) || payload.Length > TravelTicket.MaxBytes * 2)
			{
				return;
			}
			byte[] bytes;
			try
			{
				bytes = Convert.FromBase64String(payload);
			}
			catch (FormatException)
			{
				return;
			}
			if (TravelTicket.TryDecode(bytes, ticketKey, out TravelTicket summons) &&
				summons.Kind == TravelTicketKind.CallToArms && summons.PartyID == partyID && summons.IsLive(NowUnixMs))
			{
				DeliverSummons(summons, bytes);
			}
		}

		private void OnCallToArmsAnswered(NetworkConnection conn, CallToArmsAnswerBroadcast msg, Channel channel)
		{
			if (!TryGetPlayer(conn, out IPlayerCharacter member) || ticketKey == null ||
				!TravelTicket.TryDecode(msg.Ticket, ticketKey, out TravelTicket summons) ||
				summons.Kind != TravelTicketKind.CallToArms || member.ID == summons.CharacterID)
			{
				return;
			}
			if (!summons.IsLive(NowUnixMs))
			{
				Toast(member, "The call to arms has passed.");
				return;
			}
			if (!member.TryGet(out IPartyController party) || party.ID != summons.PartyID)
			{
				Toast(member, "You are no longer in that party.");
				return;
			}
			TravelSpellRefusal refusal = TravelSpellRules.Decide(CharacterStateValidation.CanAct(member),
				member.GameObject != null && ArenaTeamRegistry.IsArena(member.GameObject.scene.handle),
				NoTravelZone.Blocks(member), hasDestination: true);
			if (refusal != TravelSpellRefusal.None)
			{
				Toast(member, TravelSpellRules.Describe(refusal, null));
				return;
			}

			int slot = 1 + (int)((ulong)member.ID % (GateRules.ArrivalSpots - 1));
			var destination = new TransferDestination(summons.OriginScene, summons.OriginSceneID,
				GateRules.ArrivalSpot(summons.OriginPosition, summons.OriginYaw, slot),
				Quaternion.Euler(0.0f, summons.OriginYaw, 0.0f));
			if (CharacterTransfer.Transfer(member, destination, out TransferRefusal transferRefusal) == TransferOutcome.Refused)
			{
				Log.Debug("GateSystem", $"Character {member.ID} was not brought to {summons.CasterName}: {transferRefusal}.");
				Toast(member, TravelSpellRules.Describe(TravelSpellRefusal.TransferFailed, null));
			}
		}

		// ── Characters ──────────────────────────────────────────────────────────────────

		private void CharacterSystem_OnSpawnCharacter(NetworkConnection conn, IPlayerCharacter character, Scene scene)
		{
			if (character == null || conn == null)
			{
				return;
			}
			arrivalGrace[character.ID] = Time.time + ArrivalGraceSeconds;

			// What stands open where it arrived: portals and lit stones in its scene instance.
			long now = NowUnixMs;
			foreach (Gate gate in gates.Values)
			{
				if (gate.Scene.Equals(scene.handle))
				{
					Send(conn, new GatePortalBroadcast()
					{
						GateID = gate.ID,
						Position = gate.Position,
						Yaw = gate.Yaw,
						Radius = gate.Radius,
						RemainingSeconds = Mathf.Max(0.1f, gate.ExpiresAt - Time.time),
					});
				}
			}
			foreach (KeyValuePair<StoneKey, LitStone> entry in litStones)
			{
				if (entry.Key.Scene.Equals(scene.handle))
				{
					Send(conn, new GateStoneLitBroadcast()
					{
						WaypointIndex = entry.Key.Index,
						Position = entry.Value.Position,
						Yaw = entry.Value.Yaw,
						RemainingSeconds = Mathf.Max(0.0f, (entry.Value.ExpiresUnixMs - now) / 1000.0f),
					});
				}
			}
		}

		private void CharacterSystem_OnDisconnect(NetworkConnection conn, IPlayerCharacter character)
		{
			if (conn != null)
			{
				presentWindows.Remove(conn);
			}
			if (character == null)
			{
				return;
			}
			choices.Remove(character.ID);
			returns.Remove(character.ID);
			arrivalGrace.Remove(character.ID);
		}

		// ── Helpers ─────────────────────────────────────────────────────────────────────

		private bool TryGetPlayer(NetworkConnection conn, out IPlayerCharacter player)
		{
			player = null;
			return conn != null &&
				Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> mapping) &&
				mapping.ConnectionCharacters.TryGetValue(conn, out player) &&
				player != null && player.IsSpawned;
		}

		private void ForEachPlayerIn(SceneHandle scene, Action<IPlayerCharacter> action)
		{
			if (!Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> mapping))
			{
				return;
			}
			scratchPlayers.Clear();
			foreach (IPlayerCharacter player in mapping.ConnectionCharacters.Values)
			{
				if (player?.Owner != null && player.GameObject != null && player.GameObject.scene.handle.Equals(scene))
				{
					scratchPlayers.Add(player);
				}
			}
			for (int i = 0; i < scratchPlayers.Count; ++i)
			{
				action(scratchPlayers[i]);
			}
			scratchPlayers.Clear();
		}

		private void Send<T>(NetworkConnection conn, T msg) where T : struct, FishNet.Broadcast.IBroadcast
		{
			if (conn != null && conn.IsActive)
			{
				Server.NetworkWrapper.Broadcast(conn, msg, true, Channel.Reliable);
			}
		}

		private void Toast(IPlayerCharacter player, string text, ToastSeverity severity = ToastSeverity.Warning)
		{
			if (!string.IsNullOrEmpty(text) && player?.Owner != null)
			{
				Send(player.Owner, new ToastBroadcast() { Text = text, Severity = severity });
			}
		}

		private static long NewID()
		{
			byte[] bytes = new byte[8];
			using (var rng = RandomNumberGenerator.Create())
			{
				rng.GetBytes(bytes);
			}
			return BitConverter.ToInt64(bytes, 0) & long.MaxValue;
		}
	}
}
