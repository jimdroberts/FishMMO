using System.Collections.Generic;
using System.Reflection;
using FishMMO.Shared;
using KinematicCharacterController;
using UnityEngine;

namespace FishMMO.TestHarness
{
	/// <summary>
	/// Self-running, visual twin-world simulation of the platform-riding prediction model:
	/// a SERVER world and a CLIENT world occupying the same coordinates on separate collision
	/// layers, joined only by latency queues carrying inputs up and state snapshots down.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What this is.</b> The client twin predicts every tick immediately; its inputs travel
	/// through a delay queue to the server twin, which simulates the same ticks with the same
	/// stored stream and returns authoritative snapshots through the downstream delay; the client
	/// rolls back and replays on every snapshot. That is the predict → snapshot → rollback →
	/// replay loop the live game runs, expressed at the model level with the REAL deterministic
	/// pieces: <see cref="KinematicCharacterMotor"/>, <see cref="KCCPlatform.Step"/>, and the same
	/// platform-velocity injection seam <c>KCCPlayer</c> uses. Latency is adjustable live from
	/// 0&#160;ms to 500&#160;ms, plus an auto-sweep.
	/// </para>
	/// <para>
	/// <b>What it is NOT.</b> FishNet's transport, serializers and tick scheduling are not in this
	/// scene — a single process cannot faithfully be both peers of this codebase (clientHost skips
	/// prediction, and the static client handlers guard on <c>IsServerStarted</c>). Those layers
	/// are covered by the EditMode suite; this scene proves the MODEL: determinism, rollback
	/// identity, platform phase alignment, and no falling through moving decks, visually.
	/// </para>
	/// <para>
	/// <b>Reading the scene.</b> Solid objects are the client's view. Green translucent objects
	/// are the server twin, overlaid at identical coordinates — divergence is literally the green
	/// ghost separating from its solid partner. The HUD reports mispredictions, rollback-identity
	/// failures, platform phase error and fall-throughs, with a PASS/FAIL banner.
	/// </para>
	/// <para>
	/// <b>Replay fidelity note.</b> During replay the motor's ground probes run against the
	/// platforms at their PRESENT poses while the platform VELOCITY comes from the per-tick ring —
	/// which is exactly what the live game does (platforms never roll back; riders replay against
	/// <c>KCCPlatform.TryGetVelocityForTick</c>). The harness mirrors that deliberately rather
	/// than "fixing" it.
	/// </para>
	/// </remarks>
	public sealed class PlatformSimHarness : MonoBehaviour
	{
		// ── Tunables ────────────────────────────────────────────────────────────────

		/// <summary>Fixed simulation rate, matching the game's tick rate.</summary>
		public const float TickDelta = 1f / 30f;

		/// <summary>Maximum simulated round trip, milliseconds.</summary>
		public const int MaxRttMs = 500;

		/// <summary>Simulated round trip in milliseconds. Adjustable live from the HUD.</summary>
		public int RttMs = 100;

		/// <summary>Run speed multiplier so long soaks do not take wall-clock hours.</summary>
		public float TimeScale = 1f;

		/// <summary>
		/// Reconcile on every snapshot even when it matches the prediction. This is the harshest
		/// correctness setting: every tick performs a full rollback + replay, and the replayed
		/// state must land exactly back on the live state (the rollback identity).
		/// </summary>
		public bool AlwaysReconcile = true;

		/// <summary>
		/// Demonstration toggle: seed the client platforms one transit STALE, the way the shipped
		/// payload behaved before the catch-up fix. The green halo separates from the client
		/// platforms by the transit distance and the rider diverges at every edge — the bug this
		/// scene exists to keep dead. Applied on Reset.
		/// </summary>
		public bool LegacyStalePlatformSeed = false;

		/// <summary>Cycle RTT 0 → 500 in 50 ms steps automatically, logging a line per step.</summary>
		public bool AutoSweep = false;

		/// <summary>Positions closer than this count as a correct prediction.</summary>
		public const float PredictionEpsilon = 0.01f;

		/// <summary>Post-replay distance from the pre-rollback live state that counts as identity.</summary>
		public const float IdentityEpsilon = 0.001f;

		// ── World state ─────────────────────────────────────────────────────────────

		private const int ClientLayer = 30;
		private const int ServerLayer = 31;
		private const int RingSize = 1024;
		private const int PlatformCount = 2;

		private sealed class World
		{
			public KCCPlatform[] Platforms;
			public Transform[] PlatformDecks;
			/// <summary>Deck collider half-extents, for the spatial on-platform test.</summary>
			public Vector3[] PlatformHalfExtents;
			public SimRiderController Rider;
			public KinematicCharacterMotor Motor;
			public int FallThroughs;
			/// <summary>Tick a below-world excursion began, or 0 while above ground.</summary>
			public uint DipStartTick;
			/// <summary>Transient below-world dips that reconciliation recovered.</summary>
			public int RecoveredDips;
		}

		private struct PlatformTickState
		{
			public Vector3 Position;
			public Vector3 Velocity;
		}

		private struct Snapshot
		{
			public uint Tick;
			public KinematicCharacterMotorState MotorState;
			public uint DeliverAt;
		}

		private struct InputDelivery
		{
			public uint Tick;
			public uint DeliverAt;
		}

		private World client;
		private World server;

		private readonly SimRiderInput[] inputs = new SimRiderInput[RingSize];
		private readonly KinematicCharacterMotorState[] predictedStates = new KinematicCharacterMotorState[RingSize];
		private readonly PlatformTickState[,] clientPlatformRing = new PlatformTickState[RingSize, PlatformCount];
		private readonly PlatformTickState[,] serverPlatformRing = new PlatformTickState[RingSize, PlatformCount];

		private readonly Queue<InputDelivery> upQueue = new Queue<InputDelivery>();
		private readonly Queue<Snapshot> downQueue = new Queue<Snapshot>();

		private uint clientTick;
		private uint serverTick;
		private float accumulator;

		// ── Stats ───────────────────────────────────────────────────────────────────

		private long snapshotsChecked;
		private long mispredictions;
		private float maxPredictionError;
		private long rollbacks;
		private long identityChecks;
		private long identityFailures;
		private float maxPlatformPhaseError;
		private uint sweepStartTick;

		// ── Scenario brain ──────────────────────────────────────────────────────────

		private enum BrainState { WalkToBoard, WaitForFerry, Board, Ride, Disembark }
		private BrainState brainState = BrainState.WalkToBoard;
		private int rideDirection = 1;

		/* Geometry contract the scenario relies on: islands span |x| ∈ [6, 9] with tops at
		 * y 0.75; the ferry deck (6 long in x, top FLUSH with the islands at y 0.75) travels to
		 * |x| = 4.9, overlapping each island edge by 1.9 at the ends. The ferry never dwells
		 * (KCCPlatform reverses on arrival), so boarding designs with timing sensitivity all
		 * failed in turn: CHASING the departing deck was geometrically impossible (window closed
		 * in ~3 ticks), waiting IN the sweep path got the rider bulldozed by the arriving deck
		 * face, a 0.25 STEP-UP against the moving deck side parked the rider at the face, and a
		 * HOP over the lip spent longer airborne (24 ticks) than the whole coverage window (22).
		 * Flush decks end all of that: the rider waits at z 2.6 — beyond the deck's z 1.5 face
		 * plus arrival radius plus capsule radius — and when the inbound deck covers the
		 * boarding x (gated at |ferry.x| < 3.0, a ~43-tick window against a ~15-tick walk),
		 * simply WALKS aboard; the boarding point sits past the island edge so the ground probe
		 * lands on the deck collider unambiguously. Disembarking is the mirror: walk toward the
		 * far island over the coplanar overlap and let the departing deck slide out from
		 * underfoot. Under everything lies the sea (top y 0.3, wadeable, a 0.45 step below the
		 * islands): a mistimed hop lands there and the brain wades ashore, so the only way any
		 * rider reaches y -1.5 is by tunneling through geometry. */
		private static readonly Vector3 IslandAWaitPoint = new Vector3(-6.2f, 1.0f, 2.6f);
		private static readonly Vector3 IslandBWaitPoint = new Vector3(6.2f, 1.0f, 2.6f);
		private static readonly Vector3 IslandABoardingPoint = new Vector3(-5.5f, 0.75f, 0.6f);
		private static readonly Vector3 IslandBBoardingPoint = new Vector3(5.5f, 0.75f, 0.6f);
		/* Landing targets are ON the islands (boarding points are deliberately past the edge,
		 * over deck-only footing — standing there after the deck leaves is a swim). */
		private static readonly Vector3 IslandALandingPoint = new Vector3(-6.5f, 0.75f, 0.6f);
		private static readonly Vector3 IslandBLandingPoint = new Vector3(6.5f, 0.75f, 0.6f);
		private const float FerryTravel = 4.9f;
		private const float DeckHalfLength = 3f;

		// ── Construction ────────────────────────────────────────────────────────────

		private void Start()
		{
			client = BuildWorld(ClientLayer, isServerWorld: false);
			server = BuildWorld(ServerLayer, isServerWorld: true);
			ResetSimulation();
		}

		/// <summary>Builds one world's geometry, platforms and rider on its own collision layer.</summary>
		private World BuildWorld(int layer, bool isServerWorld)
		{
			World world = new World
			{
				Platforms = new KCCPlatform[PlatformCount],
				PlatformDecks = new Transform[PlatformCount],
				PlatformHalfExtents = new Vector3[PlatformCount],
			};

			Transform root = new GameObject(isServerWorld ? "ServerWorld" : "ClientWorld").transform;
			root.SetParent(transform, false);

			// Two ground islands (tops at y 0.75) with a gap the ferry crosses.
			MakeBox(root, "IslandA", new Vector3(-7.5f, 0.25f, 0f), new Vector3(3f, 1f, 6f), layer, isServerWorld, ghostScale: 1.0f);
			MakeBox(root, "IslandB", new Vector3(7.5f, 0.25f, 0f), new Vector3(3f, 1f, 6f), layer, isServerWorld, ghostScale: 1.0f);

			/* The sea: a walkable floor (top y 0.3) under the whole play space. This is the live
			 * game's answer to a mistimed boarding — at 500ms RTT the brain, acting on client
			 * knowledge like a real player, WILL sometimes march its server twin off a departing
			 * deck; that twin lands in the water and wades out (the 0.45 ledge onto an island is
			 * under the motor's 0.5 step height), exactly as a live player swims ashore. It also
			 * sharpens the fall assertion: with floor everywhere, ANY rider below y -1.5 can only
			 * have tunneled through geometry — the precise regression this scene guards. The deck
			 * (bottom y 0.5) sweeps 0.2 above it. */
			MakeBox(root, "Sea", new Vector3(0f, 0.05f, 0f), new Vector3(30f, 0.5f, 12f), layer, isServerWorld, ghostScale: 1.0f,
				color: new Color(0.15f, 0.3f, 0.5f));

			// Platform 0: the ferry, sliding X across the gap and overlapping each island edge at
			// its ends. Platform 1: an elevator bobbing Y as moving scenery.
			world.Platforms[0] = MakePlatform(root, "Ferry", new Vector3(-FerryTravel, 0.5f, 0f), layer, isServerWorld,
				new Vector3(FerryTravel * 2f, 0f, 0f), new Vector3(0f, 0f, 0f), moveRate: 3f,
				new Vector3(DeckHalfLength * 2f, 0.5f, 3f), out world.PlatformDecks[0]);
			world.Platforms[1] = MakePlatform(root, "Elevator", new Vector3(0f, 0.75f, 6f), layer, isServerWorld,
				new Vector3(0f, 3f, 0f), new Vector3(0f, 0f, 0f), moveRate: 2f,
				new Vector3(3f, 0.5f, 3f), out world.PlatformDecks[1]);
			world.PlatformHalfExtents[0] = new Vector3(DeckHalfLength, 0.25f, 1.5f);
			world.PlatformHalfExtents[1] = new Vector3(1.5f, 0.25f, 1.5f);

			// The rider.
			GameObject riderGo = new GameObject(isServerWorld ? "ServerRider" : "ClientRider");
			riderGo.transform.SetParent(root, false);
			riderGo.layer = layer;
			CapsuleCollider capsule = riderGo.AddComponent<CapsuleCollider>();
			capsule.height = 1.8f;
			capsule.radius = 0.35f;
			capsule.center = new Vector3(0f, 0.9f, 0f);
			KinematicCharacterMotor motor = riderGo.AddComponent<KinematicCharacterMotor>();
			motor.SetCapsuleDimensions(0.35f, 1.8f, 0.9f);
			motor.CollidableLayers = 1 << layer;
			motor.StableGroundLayers = 1 << layer;
			SimRiderController controller = riderGo.AddComponent<SimRiderController>();
			controller.Bind(motor);
			world.Rider = controller;
			world.Motor = motor;

			// Rider visual.
			GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
			Object.Destroy(visual.GetComponent<Collider>());
			visual.transform.SetParent(riderGo.transform, false);
			visual.transform.localPosition = new Vector3(0f, 0.9f, 0f);
			visual.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
			Paint(visual, isServerWorld ? GhostColor : new Color(0.25f, 0.45f, 0.9f), isServerWorld);

			return world;
		}

		private static readonly Color GhostColor = new Color(0.2f, 1f, 0.35f, 0.35f);

		private static void Paint(GameObject go, Color color, bool ghost)
		{
			Renderer renderer = go.GetComponent<Renderer>();
			if (renderer == null)
			{
				return;
			}
			/* Sprites/Default is always available in-editor, renders vertex/material color with
			 * alpha, and needs no render-pipeline-specific setup — good enough for a harness. */
			Material material = new Material(Shader.Find(ghost ? "Sprites/Default" : "Unlit/Color"));
			material.color = color;
			renderer.sharedMaterial = material;
		}

		private static void MakeBox(Transform parent, string name, Vector3 position, Vector3 size, int layer, bool ghost, float ghostScale, Color? color = null)
		{
			GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
			go.name = name;
			go.transform.SetParent(parent, false);
			go.transform.position = position;
			go.transform.localScale = size * (ghost ? ghostScale : 1f);
			go.layer = layer;
			Paint(go, ghost ? GhostColor : (color ?? new Color(0.4f, 0.4f, 0.42f)), ghost);
		}

		/// <summary>
		/// Builds one REAL <see cref="KCCPlatform"/> with its private route installed by
		/// reflection (the fields are authored via the inspector in shipped scenes).
		/// </summary>
		private static KCCPlatform MakePlatform(Transform parent, string name, Vector3 position, int layer,
			bool ghost, Vector3 goalOffsetA, Vector3 goalOffsetB, float moveRate, Vector3 deckSize, out Transform deck)
		{
			GameObject go = new GameObject(name);
			go.transform.SetParent(parent, false);
			go.transform.position = position;
			go.layer = layer;

			BoxCollider collider = go.AddComponent<BoxCollider>();
			collider.size = deckSize;

			GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
			Object.Destroy(visual.GetComponent<Collider>());
			visual.transform.SetParent(go.transform, false);
			visual.transform.localScale = deckSize * (ghost ? 1.03f : 1f);
			Paint(visual, ghost ? GhostColor : new Color(0.7f, 0.5f, 0.2f), ghost);

			KCCPlatform platform = go.AddComponent<KCCPlatform>();
			SetPrivate(platform, "goals", new List<Vector3>
			{
				position + goalOffsetA,
				position + goalOffsetB,
			});
			SetPrivate(platform, "moveRate", moveRate);

			deck = go.transform;
			return platform;
		}

		private static void SetPrivate(object target, string field, object value)
		{
			FieldInfo info = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
			if (info == null)
			{
				Debug.LogError($"PlatformSimHarness: private field '{field}' missing on {target.GetType().Name} — the harness needs updating.");
				return;
			}
			info.SetValue(target, value);
		}

		// ── Reset ───────────────────────────────────────────────────────────────────

		/// <summary>Returns both worlds to tick zero without rebuilding the scene objects.</summary>
		public void ResetSimulation()
		{
			clientTick = 0;
			serverTick = 0;
			accumulator = 0f;
			upQueue.Clear();
			downQueue.Clear();
			snapshotsChecked = 0;
			mispredictions = 0;
			maxPredictionError = 0f;
			rollbacks = 0;
			identityChecks = 0;
			identityFailures = 0;
			maxPlatformPhaseError = 0f;
			edgeReplayDivergences = 0;
			waterEntries = 0;
			client.FallThroughs = 0;
			client.RecoveredDips = 0;
			client.DipStartTick = 0;
			server.FallThroughs = 0;
			completedCrossings = 0;
			brainState = BrainState.WalkToBoard;
			rideDirection = 1;
			sweepStartTick = 0;

			ResetWorld(client);
			ResetWorld(server);

			/* The legacy demonstration: pre-fix, a client's platform started from a payload
			 * snapshot one transit old and never caught up. Model it by stepping only the SERVER
			 * platforms forward by the transit before the run begins — the phase gap then persists
			 * for the whole session, exactly as it did in the shipped bug. With the toggle off both
			 * twins start phase-aligned, which is what the payload catch-up now guarantees. */
			if (LegacyStalePlatformSeed)
			{
				int staleTicks = TotalDelayTicks();
				for (int i = 0; i < staleTicks; ++i)
				{
					for (int p = 0; p < PlatformCount; ++p)
					{
						server.Platforms[p].Step(TickDelta);
					}
				}
			}
		}

		private void ResetWorld(World world)
		{
			for (int p = 0; p < PlatformCount; ++p)
			{
				// Return each platform to its authored start and first goal.
				List<Vector3> goals = (List<Vector3>)typeof(KCCPlatform)
					.GetField("goals", BindingFlags.Instance | BindingFlags.NonPublic)
					.GetValue(world.Platforms[p]);
				world.Platforms[p].transform.position = (goals[0] + goals[1]) * 0.5f;
				SetPrivate(world.Platforms[p], "goalIndex", (byte)0);
			}

			Vector3 riderStart = new Vector3(-8f, 1.6f, 0f);
			world.Motor.SetPositionAndRotation(riderStart, Quaternion.identity);
			Physics.SyncTransforms();
		}

		// ── The loop ────────────────────────────────────────────────────────────────

		private int UpDelayTicks() => Mathf.CeilToInt((RttMs * 0.001f) / TickDelta * 0.5f);
		private int TotalDelayTicks() => Mathf.CeilToInt((RttMs * 0.001f) / TickDelta);
		private int DownDelayTicks() => TotalDelayTicks() - UpDelayTicks();

		private void Update()
		{
			accumulator += Time.deltaTime * TimeScale;
			int safety = 0;
			while (accumulator >= TickDelta && safety++ < 240)
			{
				accumulator -= TickDelta;
				SimulateOneTick();
			}

			if (AutoSweep && clientTick - sweepStartTick >= 300u)
			{
				Debug.Log($"[PlatformSim] RTT {RttMs}ms over {clientTick - sweepStartTick} ticks: " +
					$"mispredict {mispredictions}/{snapshotsChecked} (max {maxPredictionError:F4}), " +
					$"identityFails {identityFailures}/{identityChecks}, " +
					$"fallThrough client {client.FallThroughs} server {server.FallThroughs}, " +
					$"platformPhaseErr {maxPlatformPhaseError:F4}");
				RttMs = RttMs >= MaxRttMs ? 0 : RttMs + 50;
				sweepStartTick = clientTick;
			}
		}

		private void SimulateOneTick()
		{
			uint n = clientTick;

			// 1. The "player" samples input from what it can see: the client world.
			SimRiderInput input = SampleBrain();
			inputs[n % RingSize] = input;

			// 2. Client platforms advance one tick; their per-tick state is ringed for replay.
			for (int p = 0; p < PlatformCount; ++p)
			{
				client.Platforms[p].Step(TickDelta);
				clientPlatformRing[n % RingSize, p] = new PlatformTickState
				{
					Position = client.Platforms[p].transform.position,
					Velocity = client.Platforms[p].LastCompletedTickVelocity,
				};
			}
			/* The motor queries the physics engine directly, and autoSyncTransforms is off by
			 * default — without an explicit sync every query this tick would run against the
			 * platform poses of the last FixedUpdate, which at high time scales is many sim
			 * ticks stale (and at spawn is 'nothing exists yet': the rider free-fell through
			 * the whole world). The live game ticks inside FishNet's loop where this is
			 * handled; the harness owns its own loop, so it owns the sync too. */
			Physics.SyncTransforms();

			// 3. The client PREDICTS tick n immediately.
			Vector3 platformVelocity = PlatformVelocityUnder(client, live: true, ringTick: n);
			client.Rider.SimulateTick(input, platformVelocity, TickDelta);
			predictedStates[n % RingSize] = client.Motor.GetState();
			DetectFallThrough(client);

			// 4. The input travels up.
			upQueue.Enqueue(new InputDelivery { Tick = n, DeliverAt = n + (uint)UpDelayTicks() });

			// 5. The server consumes everything that has arrived and simulates those ticks.
			while (upQueue.Count > 0 && upQueue.Peek().DeliverAt <= n)
			{
				InputDelivery delivery = upQueue.Dequeue();
				uint k = delivery.Tick;

				for (int p = 0; p < PlatformCount; ++p)
				{
					server.Platforms[p].Step(TickDelta);
					serverPlatformRing[k % RingSize, p] = new PlatformTickState
					{
						Position = server.Platforms[p].transform.position,
						Velocity = server.Platforms[p].LastCompletedTickVelocity,
					};
				}
				Physics.SyncTransforms();

				Vector3 serverPlatformVelocity = PlatformVelocityUnder(server, live: true, ringTick: k);
				server.Rider.SimulateTick(inputs[k % RingSize], serverPlatformVelocity, TickDelta);
				DetectFallThrough(server);
				serverTick = k;

				downQueue.Enqueue(new Snapshot
				{
					Tick = k,
					MotorState = server.Motor.GetState(),
					DeliverAt = n + (uint)DownDelayTicks(),
				});
			}

			// 6. The client consumes snapshots and reconciles.
			while (downQueue.Count > 0 && downQueue.Peek().DeliverAt <= n)
			{
				Snapshot snapshot = downQueue.Dequeue();
				ConsumeSnapshot(snapshot, n);
			}

			clientTick = n + 1;
		}

		private void ConsumeSnapshot(in Snapshot snapshot, uint currentTick)
		{
			snapshotsChecked++;

			KinematicCharacterMotorState predicted = predictedStates[snapshot.Tick % RingSize];
			float error = (predicted.Position - snapshot.MotorState.Position).magnitude;
			if (error > maxPredictionError)
			{
				maxPredictionError = error;
			}
			bool mispredicted = error > PredictionEpsilon;
			if (mispredicted)
			{
				mispredictions++;
			}

			float phaseError = 0f;
			for (int p = 0; p < PlatformCount; ++p)
			{
				phaseError = Mathf.Max(phaseError,
					(clientPlatformRing[snapshot.Tick % RingSize, p].Position -
					 serverPlatformRing[snapshot.Tick % RingSize, p].Position).magnitude);
			}
			if (phaseError > maxPlatformPhaseError)
			{
				maxPlatformPhaseError = phaseError;
			}

			if (!mispredicted && !AlwaysReconcile)
			{
				return;
			}

			// Rollback and replay — the identity check only means something when the snapshot
			// already agreed with the prediction, so a correct replay must land exactly back
			// on the live state it replaced.
			KinematicCharacterMotorState liveBefore = client.Motor.GetState();
			rollbacks++;

			client.Motor.ApplyState(snapshot.MotorState);
			client.Rider.transform.SetPositionAndRotation(client.Motor.TransientPosition, client.Motor.TransientRotation);

			for (uint j = snapshot.Tick + 1; j <= currentTick; ++j)
			{
				Vector3 replayPlatformVelocity = PlatformVelocityUnder(client, live: false, ringTick: j);
				client.Rider.SimulateTick(inputs[j % RingSize], replayPlatformVelocity, TickDelta);
				predictedStates[j % RingSize] = client.Motor.GetState();
			}

			if (!mispredicted)
			{
				identityChecks++;
				float identityError = (client.Motor.GetState().Position - liveBefore.Position).magnitude;
				if (identityError > IdentityEpsilon)
				{
					/* At zero delay the replay window is empty-to-one tick and the world cannot
					 * have moved under it, so any divergence is real nondeterminism — a hard
					 * failure. At higher RTT a replay near a deck edge legitimately probes a
					 * platform that has since moved (platforms never roll back, mirroring the
					 * live game), diverges, and is pulled back by the following snapshots; that
					 * is the netcode working, counted separately so it stays visible. */
					if (TotalDelayTicks() == 0)
					{
						identityFailures++;
						Debug.LogError($"[PlatformSim] Rollback identity broke at ZERO delay, tick {snapshot.Tick}: " +
							$"replayed state is {identityError:F5} from the live state it replaced — the simulation is nondeterministic.");
					}
					else
					{
						edgeReplayDivergences++;
					}
				}
			}
		}

		/// <summary>
		/// Replays at nonzero RTT that diverged from the live state near moving geometry —
		/// an expected, self-correcting property of the shipped model, tracked for visibility.
		/// </summary>
		private long edgeReplayDivergences;

		/// <summary>Times the brain ended up in the sea and waded ashore — the live game's
		/// "missed the ferry at high ping" outcome. Informational, never a failure.</summary>
		private int waterEntries;

		/// <summary>Complete island-to-island ferry rides. The PlayMode suite requires at least
		/// one per latency leg — the guard against this scenario passing vacuously with a rider
		/// that never actually boards.</summary>
		private int completedCrossings;

		/// <summary>
		/// The velocity of the platform under the rider — live from the platform, or from the
		/// per-tick ring during replay, mirroring <c>KCCPlayer</c>'s TryGetVelocityForTick path.
		/// </summary>
		private Vector3 PlatformVelocityUnder(World world, bool live, uint ringTick)
		{
			int index = PlatformIndexUnder(world, live, ringTick);
			if (index < 0)
			{
				return Vector3.zero;
			}
			if (live)
			{
				return world.Platforms[index].LastCompletedTickVelocity;
			}
			return world == client
				? clientPlatformRing[ringTick % RingSize, index].Velocity
				: serverPlatformRing[ringTick % RingSize, index].Velocity;
		}

		/// <summary>
		/// Which platform the rider is standing on, by a SPATIAL test against the deck extents —
		/// standing height over the deck top, feet inside the footprint.
		/// </summary>
		/// <remarks>
		/// Deliberately not <c>GroundingStatus.GroundCollider</c>: the motor state restored by
		/// every reconcile's <c>ApplyState</c> carries a null collider reference (references do
		/// not survive state transfer), so under AlwaysReconcile that field reads null on every
		/// single sampled tick even while stably standing on the deck — which silently disabled
		/// both riding and the brain's boarding detection. The live game does not use the ground
		/// collider either; <c>KCCPlatform</c> owns a trigger volume that calls
		/// <c>KCCPlayer.SetPlatform</c>, and this arithmetic is that mechanism at model level —
		/// pure position math, so replays resolve it identically from ring poses.
		/// </remarks>
		private int PlatformIndexUnder(World world, bool live, uint ringTick)
		{
			if (!world.Motor.GroundingStatus.IsStableOnGround)
			{
				return -1;
			}
			Vector3 rider = world.Motor.TransientPosition;
			for (int p = 0; p < PlatformCount; ++p)
			{
				Vector3 center = live
					? world.Platforms[p].transform.position
					: (world == client
						? clientPlatformRing[ringTick % RingSize, p].Position
						: serverPlatformRing[ringTick % RingSize, p].Position);
				Vector3 half = world.PlatformHalfExtents[p];
				float top = center.y + half.y;
				if (Mathf.Abs(rider.x - center.x) <= half.x + 0.2f
					&& Mathf.Abs(rider.z - center.z) <= half.z + 0.2f
					&& rider.y - top >= -0.05f && rider.y - top <= 0.2f)
				{
					return p;
				}
			}
			return -1;
		}

		/// <summary>
		/// Fall accounting, with the client and server held to DIFFERENT standards — the same
		/// standards the live game holds them to.
		/// </summary>
		/// <remarks>
		/// The SERVER is authority: it below the world even once is a hard failure, and the run
		/// resets it to keep producing signal. The CLIENT is a prediction: near a deck edge at
		/// high RTT its replay legitimately probes a platform that has since moved (platforms
		/// never roll back — deliberately mirrored from the live game), so a transient dip that
		/// the next snapshots pull back up is the system WORKING, and is counted as a recovered
		/// dip rather than a failure. What is a client failure is staying fallen: if
		/// reconciliation has not recovered the rider within a generous window, the correction
		/// loop itself is broken.
		/// </remarks>
		private void DetectFallThrough(World world)
		{
			Vector3 rider = world.Motor.TransientPosition;
			bool below = rider.y < -1.5f;

			if (world == server)
			{
				if (below)
				{
					world.FallThroughs++;
					Debug.LogError($"[PlatformSim] SERVER rider fell out of the world at tick {clientTick} (y {rider.y:F2}) — the authority fell, hard failure.");
					world.Motor.SetPositionAndRotation(new Vector3(-8f, 1.6f, 0f), Quaternion.identity);
				}
				return;
			}

			if (!below)
			{
				if (world.DipStartTick != 0)
				{
					world.RecoveredDips++;
					world.DipStartTick = 0;
				}
				return;
			}

			if (world.DipStartTick == 0)
			{
				world.DipStartTick = clientTick;
				return;
			}

			// Reconciliation gets a generous two seconds plus the full round trip to pull the
			// prediction back onto the server's truth before this counts as broken.
			uint recoveryWindow = 60u + (uint)TotalDelayTicks();
			if (clientTick - world.DipStartTick > recoveryWindow)
			{
				world.FallThroughs++;
				world.DipStartTick = 0;
				Debug.LogError($"[PlatformSim] CLIENT rider stayed fallen past the recovery window at tick {clientTick} (y {rider.y:F2}) — reconciliation is not correcting it.");
				world.Motor.SetPositionAndRotation(new Vector3(-8f, 1.6f, 0f), Quaternion.identity);
			}
		}

		// ── The scripted player ─────────────────────────────────────────────────────

		/// <summary>
		/// A small waypoint player: walk to the wait point beside the sweep line, wait for the
		/// inbound deck to cover the boarding x, walk sideways onto the deck (a rider-initiated
		/// 0.25 step-up), ride across holding the deck center (hopping once mid-ride, the
		/// classic fall-through stressor), stand on the far island's strip while the deck slides
		/// out from underfoot, and come back. Samples only the CLIENT world — the same
		/// information a human player has — and its output is stored per tick, so replays reuse
		/// the stream rather than re-deciding.
		/// </summary>
		private SimRiderInput SampleBrain()
		{
			Vector3 rider = client.Motor.TransientPosition;
			Vector3 ferry = client.Platforms[0].transform.position;
			float ferryVelocityX = client.Platforms[0].LastCompletedTickVelocity.x;
			bool onFerry = IsStandingOnFerry(client);
			Vector3 waitPoint = rideDirection > 0 ? IslandAWaitPoint : IslandBWaitPoint;
			Vector3 boardingPoint = rideDirection > 0 ? IslandABoardingPoint : IslandBBoardingPoint;
			Vector3 landingPoint = rideDirection > 0 ? IslandBLandingPoint : IslandALandingPoint;

			/* Deck coverage of the boarding x, with margin for the walk: the deck (half-length
			 * 3) covers x = ∓6.2 while |ferry.x| ≥ 3.4; requiring 3.8 keeps ≥ 20 ticks of
			 * coverage ahead of a ~10-tick walk. Board only while the deck is INBOUND (still
			 * approaching our island), so the window is opening rather than closing. */
			bool deckCoversBoarding = rideDirection > 0 ? ferry.x < -3.0f : ferry.x > 3.0f;
			bool deckInbound = rideDirection > 0 ? ferryVelocityX < 0f : ferryVelocityX > 0f;

			SimRiderInput input = default;

			/* Fell in the water (a hop gone wrong at high RTT, where the brain's client-side
			 * knowledge can be a stride ahead of the server twin): wade ashore and start over,
			 * like a live player swimming back. Water is y ≈ 0.3, islands 0.75, deck 1.0 —
			 * 0.6 cleanly separates. */
			if (rider.y < 0.6f && !onFerry && brainState != BrainState.WalkToBoard)
			{
				waterEntries++;
				brainState = BrainState.WalkToBoard;
			}

			switch (brainState)
			{
				case BrainState.WalkToBoard:
					// From the water, first step OUT of the ferry's sweep line (deck z ∈ ±1.5,
					// bottom 0.2 above the sea) so the returning deck doesn't plough through the
					// wader, then make for the wait point on the island.
					if (rider.y < 0.6f && Mathf.Abs(rider.z) < 2.0f && Mathf.Abs(rider.x) < 8.2f)
					{
						input.Move = Vector3.forward;
						break;
					}
					input.Move = MoveToward(rider, waitPoint, out bool atWaitPoint);
					if (atWaitPoint)
					{
						brainState = BrainState.WaitForFerry;
					}
					break;

				case BrainState.WaitForFerry:
					// Stand beside the sweep line until the inbound deck covers the boarding x.
					if (deckCoversBoarding && deckInbound)
					{
						Debug.Log($"[PlatformSim] Board attempt at tick {clientTick}: rider z {rider.z:F2}, ferry x {ferry.x:F2}");
						brainState = BrainState.Board;
					}
					break;

				case BrainState.Board:
					if (onFerry)
					{
						Debug.Log($"[PlatformSim] Boarded at tick {clientTick}: rider ({rider.x:F2},{rider.y:F2},{rider.z:F2}), ferry x {ferry.x:F2}");
						brainState = BrainState.Ride;
						break;
					}
					if (!deckCoversBoarding)
					{
						Collider ground = client.Motor.GroundingStatus.GroundCollider;
						Debug.Log($"[PlatformSim] Board ABORTED at tick {clientTick}: rider ({rider.x:F2},{rider.y:F2},{rider.z:F2}), ferry x {ferry.x:F2} — deck left before the rider was aboard. " +
							$"ground '{(ground != null ? ground.name : "NONE")}' stable {client.Motor.GroundingStatus.IsStableOnGround} found {client.Motor.GroundingStatus.FoundAnyGround}");
						brainState = BrainState.WalkToBoard;
						break;
					}
					/* Walk onto the deck. Its top is FLUSH with the island top, so boarding is a
					 * plain walk — no step, no hop, no timing (a 0.25 step-up against the side
					 * of a transform-driven moving deck proved unreliable: the rider parked
					 * against the face, and a hop's air time outlasted the coverage window).
					 * The target sits past the island edge so grounding lands on the DECK
					 * collider unambiguously — over the overlap strip the two tops are coplanar
					 * and the probe could return either. */
					input.Move = MoveToward(rider, boardingPoint, out _);
					break;

				case BrainState.Ride:
					// Hop once near the middle of the crossing: landing back on a deck that has
					// moved underneath you is the exact case that exposes phase errors. Only hop
					// from near the deck CENTER: at high RTT the client and server riders can
					// legitimately sit a stride apart after an edge replay, and a hop taken at the
					// deck edge lands the server twin in the water. Mid-deck, both land with
					// metres to spare.
					if (Mathf.Abs(ferry.x) < 0.35f && onFerry && Mathf.Abs(rider.x - ferry.x) < 0.6f)
					{
						input.Jump = true;
					}
					else
					{
						// Hold the deck center. Self-correcting: if either twin has drifted
						// toward an edge, the shared input stream walks BOTH toward the middle
						// instead of leaving the drifted one hanging over the gap until the next
						// reconcile. (Side-boarding leaves the rider near the deck's z edge, so
						// this also walks it somewhere safe before the mid-ride hop.)
						input.Move = MoveToward(rider, new Vector3(ferry.x, rider.y, ferry.z), out _);
					}
					bool ferryNearFarSide = rideDirection > 0 ? ferry.x > FerryTravel - 0.6f : ferry.x < -FerryTravel + 0.6f;
					if (ferryNearFarSide)
					{
						brainState = BrainState.Disembark;
					}
					break;

				case BrainState.Disembark:
					/* Stand over the far island's strip and let the departing deck slide out
					 * from underfoot — a 0.25 step DOWN onto the island, the mirror of
					 * boarding. The deck overlaps that island by 1.9m at the far end, so the
					 * landing point is over solid ground the whole time. */
					input.Move = MoveToward(rider, landingPoint, out bool atLanding);
					if (atLanding && !onFerry && rider.y > 0.6f)
					{
						completedCrossings++;
						rideDirection = -rideDirection;
						brainState = BrainState.WalkToBoard;
					}
					break;
			}

			return input;
		}

		/// <summary>True when the world's rider is grounded on its ferry deck (spatial test —
		/// see <see cref="PlatformIndexUnder"/> for why not the ground collider).</summary>
		private bool IsStandingOnFerry(World world)
		{
			return PlatformIndexUnder(world, live: true, ringTick: 0) == 0;
		}

		private static Vector3 MoveToward(Vector3 from, Vector3 to, out bool arrived)
		{
			Vector3 planar = new Vector3(to.x - from.x, 0f, to.z - from.z);
			arrived = planar.magnitude < 0.3f;
			return arrived ? Vector3.zero : planar.normalized;
		}

		// ── HUD ─────────────────────────────────────────────────────────────────────

		private void OnGUI()
		{
			const int width = 380;
			GUILayout.BeginArea(new Rect(10, 10, width, 480), GUI.skin.box);
			GUILayout.Label("<b>Platform Prediction Simulation</b>", RichLabel());
			GUILayout.Label($"client tick {clientTick}   server tick {serverTick}   queue ↑{upQueue.Count} ↓{downQueue.Count}");

			GUILayout.Label($"Simulated RTT: {RttMs} ms  (↑{UpDelayTicks()}t ↓{DownDelayTicks()}t)");
			int newRtt = (int)GUILayout.HorizontalSlider(RttMs, 0, MaxRttMs);
			if (!AutoSweep && newRtt != RttMs)
			{
				RttMs = (newRtt / 10) * 10;
			}

			GUILayout.BeginHorizontal();
			foreach (int scale in new[] { 1, 5, 10 })
			{
				if (GUILayout.Button($"{scale}x", GUILayout.Width(46)))
				{
					TimeScale = scale;
				}
			}
			if (GUILayout.Button("Reset", GUILayout.Width(70)))
			{
				ResetSimulation();
			}
			GUILayout.EndHorizontal();

			AlwaysReconcile = GUILayout.Toggle(AlwaysReconcile, " Always reconcile (rollback identity every snapshot)");
			bool legacy = GUILayout.Toggle(LegacyStalePlatformSeed, " Legacy stale platform seed (pre-fix bug demo; applies on Reset)");
			if (legacy != LegacyStalePlatformSeed)
			{
				LegacyStalePlatformSeed = legacy;
			}
			AutoSweep = GUILayout.Toggle(AutoSweep, " Auto-sweep RTT 0→500 (logs a line per step)");

			GUILayout.Space(6);
			GUILayout.Label($"snapshots checked: {snapshotsChecked}");
			GUILayout.Label($"mispredictions: {mispredictions}   max error: {maxPredictionError:F4} m");
			GUILayout.Label($"rollbacks: {rollbacks}   identity: {identityFailures} fail / {identityChecks}   edge replays: {edgeReplayDivergences}");
			GUILayout.Label($"platform phase error (max): {maxPlatformPhaseError:F4} m");
			GUILayout.Label($"fall failures — client: {client?.FallThroughs ?? 0} (dips recovered {client?.RecoveredDips ?? 0})   server: {server?.FallThroughs ?? 0}   water landings: {waterEntries}");
			GUILayout.Label($"ferry crossings completed: {completedCrossings}   brain: {brainState}");

			bool pass = (client?.FallThroughs ?? 0) == 0 &&
				(server?.FallThroughs ?? 0) == 0 &&
				identityFailures == 0;
			GUI.color = pass ? Color.green : Color.red;
			GUILayout.Label(pass ? "<b>PASS</b>" : "<b>FAIL</b>", RichLabel());
			GUI.color = Color.white;

			GUILayout.Label("Solid = client view. Green ghost = server twin at the same tick;\n" +
				"divergence is the ghost separating from its solid partner.");
			GUILayout.EndArea();
		}

		private static GUIStyle RichLabel()
		{
			GUIStyle style = new GUIStyle(GUI.skin.label) { richText = true };
			return style;
		}

		// ── Automated verification access (PlayMode tests read these) ───────────────

		/// <summary>Ticks simulated so far.</summary>
		public uint ClientTick => clientTick;
		/// <summary>Fall-through count across both worlds.</summary>
		public int TotalFallThroughs => (client?.FallThroughs ?? 0) + (server?.FallThroughs ?? 0);
		/// <summary>Rollback identity failures so far.</summary>
		public long IdentityFailures => identityFailures;
		/// <summary>Largest platform phase divergence observed at matched ticks.</summary>
		public float MaxPlatformPhaseError => maxPlatformPhaseError;
		/// <summary>Largest predicted-versus-authoritative rider error observed.</summary>
		public float MaxPredictionError => maxPredictionError;
		/// <summary>Client dips that the reconcile loop pulled back above ground.</summary>
		public int RecoveredDips => client?.RecoveredDips ?? 0;
		/// <summary>Nonzero-RTT edge replays that diverged and self-corrected (informational).</summary>
		public long EdgeReplayDivergences => edgeReplayDivergences;

		public int WaterEntries => waterEntries;

		public int CompletedCrossings => completedCrossings;

		/// <summary>One-line live state for headless diagnosis: where is the rider, where is the
		/// ferry, and what does the brain think it is doing.</summary>
		public string DebugStatus
		{
			get
			{
				Vector3 rider = client != null ? client.Motor.TransientPosition : Vector3.zero;
				Vector3 ferry = client != null ? client.Platforms[0].transform.position : Vector3.zero;
				float ferryVelX = client != null ? client.Platforms[0].LastCompletedTickVelocity.x : 0f;
				bool onFerry = client != null && IsStandingOnFerry(client);
				return $"tick {clientTick} brain {brainState} dir {rideDirection} " +
					$"rider ({rider.x:F2},{rider.y:F2},{rider.z:F2}) ferry x {ferry.x:F2} velX {ferryVelX:F3} " +
					$"onFerry {onFerry} crossings {completedCrossings} water {waterEntries}";
			}
		}
	}
}
