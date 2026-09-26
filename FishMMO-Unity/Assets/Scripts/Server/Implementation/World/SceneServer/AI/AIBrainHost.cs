using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Managing.Timing;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer.AI
{
	/// <summary>
	/// Attaches, drives and detaches the server-side brain of every NPC spawned under one
	/// <see cref="FishNet.Managing.NetworkManager"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why a host.</b> NPC prefabs are shared with clients and carry no AI. The server adds an
	/// <see cref="AIController"/> the first time it spawns an NPC, keeps it for the life of the
	/// pooled instance, and gives it the archetype the <see cref="AIBrainCatalogue"/> (or the
	/// spawner) names on every spawn. One network-tick subscription drives every brain rather than
	/// one per NPC.
	/// </para>
	/// <para>
	/// <b>Two ways in.</b> A spawn path that places the NPC — a spawner, the pet system — calls
	/// <see cref="Prepare"/> before <c>ServerManager.Spawn</c>, so the agent is warped onto the mesh
	/// while the object is active and in its final scene. Anything else that spawns an NPC (a boss
	/// calling in adds, a harness) is caught by <see cref="NPC.OnServerSpawned"/> and prepared where
	/// it stands.
	/// </para>
	/// <para>
	/// <b>Packs tick here too.</b> An <see cref="NPCGroup"/> is handed to the host of its first
	/// member and ticked after the brains on every network tick, gating itself onto the same AI
	/// tick and its members' LOD tiers; it has no <c>Update</c> of its own.
	/// </para>
	/// <para>
	/// A plain class rather than a server behaviour so a simulation can host brains on a bare
	/// FishNet server; <see cref="AISystem"/> is the production wrapper.
	/// </para>
	/// </remarks>
	public sealed class AIBrainHost
	{
		/// <summary>
		/// Running hosts by network manager.
		/// </summary>
		private static readonly Dictionary<NetworkManager, AIBrainHost> hosts = new Dictionary<NetworkManager, AIBrainHost>();

		/// <summary>
		/// The network manager whose NPCs this host drives.
		/// </summary>
		public NetworkManager NetworkManager { get; }

		/// <summary>
		/// Which brain each NPC prefab runs. Null leaves every NPC without an archetype unless its
		/// spawner names one.
		/// </summary>
		public AIBrainCatalogue Catalogue { get; set; }

		/// <summary>
		/// True between <see cref="Start"/> and <see cref="Stop"/>.
		/// </summary>
		public bool Running { get; private set; }

		/// <summary>
		/// Failures in a row, with no completed brain run between them, after which a brain is
		/// taken off the tick.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Thirty: one second of body steps at the project's 30 Hz network tick, or four seconds of
		/// Active-tier brain runs. Every transient cause seen so far — a target despawned mid-tick,
		/// a scene unloading under the agent — clears on the very next run, and any run that
		/// completes resets the count, so thirty straight failures is a brain that is broken, not
		/// unlucky: an archetype with a null slot, a state that throws on entry. Carrying on would
		/// burn the tick on it for the rest of the NPC's life and leave it half-updated each time.
		/// </para>
		/// <para>
		/// Counted in failures rather than seconds so the threshold means the same thing at every
		/// LOD tier; a Far-tier brain that runs once a second takes proportionally longer to trip.
		/// </para>
		/// </remarks>
		public const int MaxConsecutiveTickFaults = 30;

		/// <summary>
		/// Brains currently ticking.
		/// </summary>
		private readonly List<AIController> brains = new List<AIController>();

		/// <summary>
		/// Where a ticking brain sits in the host's lists, for swap-removal.
		/// </summary>
		private struct Membership
		{
			/// <summary>Index in <see cref="brains"/>.</summary>
			public int Slot;

			/// <summary>The physics scene whose body grid holds the brain's NPC.</summary>
			public PhysicsScene Scene;

			/// <summary>Index in that scene's <see cref="SceneBodies.Members"/>.</summary>
			public int SceneSlot;
		}

		/// <summary>
		/// Each ticking brain's place in <see cref="brains"/> and in its scene's member list.
		/// </summary>
		private readonly Dictionary<AIController, Membership> slots = new Dictionary<AIController, Membership>();

		/// <summary>
		/// The NPC bodies of one physics scene and the grid separation reads them from.
		/// </summary>
		private sealed class SceneBodies
		{
			/// <summary>Every ticking brain whose NPC stands in this scene.</summary>
			public readonly List<AIController> Members = new List<AIController>();

			/// <summary>Their positions as of <see cref="BuiltAtTick"/>.</summary>
			public readonly AIBodyGrid Grid = new AIBodyGrid();

			/// <summary>The host tick the grid was last filled on; -1 for never.</summary>
			public int BuiltAtTick = -1;
		}

		/// <summary>
		/// Body grids by physics scene. See <see cref="GetBodyGrid"/>.
		/// </summary>
		private readonly Dictionary<PhysicsScene, SceneBodies> bodiesByScene = new Dictionary<PhysicsScene, SceneBodies>();

		/// <summary>
		/// Every collider on every ticking NPC, to the brain that drives it.
		/// </summary>
		/// <remarks>
		/// Lets the enemy sweep resolve an NPC it overlapped with one dictionary read instead of a
		/// rigidbody read and an interface component lookup. NPCs share the character layer with
		/// players, so in a camp most of what a sweep touches is other NPCs.
		/// </remarks>
		private readonly Dictionary<Collider, AIController> brainsByCollider = new Dictionary<Collider, AIController>();

		/// <summary>
		/// Fault logs for brains whose tick has thrown, created on first failure. Empty while every
		/// brain is healthy, so the per-tick path pays only a count read.
		/// </summary>
		private readonly Dictionary<AIController, RepeatingFaultLog> faults = new Dictionary<AIController, RepeatingFaultLog>();

		/// <summary>
		/// Network ticks this host has run. Marks when each scene's body grid was last filled.
		/// </summary>
		private int tickCount;

		/// <summary>
		/// Copy of <see cref="brains"/> walked by the tick, so a brain that despawns an NPC (its own,
		/// or another's) mid-tick cannot corrupt the walk.
		/// </summary>
		private readonly List<AIController> tickSnapshot = new List<AIController>();

		/// <summary>
		/// The time manager subscribed to, kept so the subscription is released from the same one.
		/// </summary>
		private TimeManager timeManager;

		/// <summary>
		/// Seconds per network tick.
		/// </summary>
		private float tickDelta = 1f / 30f;

		/// <summary>
		/// Number of brains currently ticking. Diagnostics and tests.
		/// </summary>
		public int Count => brains.Count;

		/// <summary>
		/// Packs this host ticks. A pack joins when its first member does and leaves when it is
		/// released.
		/// </summary>
		private readonly List<NPCGroup> groups = new List<NPCGroup>();

		/// <summary>
		/// Copy of <see cref="groups"/> walked by the tick, so a pack released mid-tick cannot
		/// corrupt the walk.
		/// </summary>
		private readonly List<NPCGroup> groupSnapshot = new List<NPCGroup>();

		/// <summary>
		/// Network ticks per pack AI tick: the brains' default rate, resolved against this host's
		/// network tick, so a pack thinks on the cadence its members do.
		/// </summary>
		private int groupTicksPerAiUpdate = AIController.ResolveTicksPerAiUpdate(AIController.DEFAULT_AI_TICK_RATE, 1f / 30f);

		/// <summary>
		/// Number of packs currently ticking. Diagnostics and tests.
		/// </summary>
		public int GroupCount => groups.Count;

		/// <summary>
		/// Creates a host. Nothing happens until <see cref="Start"/>.
		/// </summary>
		/// <param name="networkManager">The network manager whose NPCs this host drives.</param>
		/// <param name="catalogue">Which brain each NPC prefab runs.</param>
		public AIBrainHost(NetworkManager networkManager, AIBrainCatalogue catalogue)
		{
			NetworkManager = networkManager;
			Catalogue = catalogue;
		}

		/// <summary>
		/// The running host for <paramref name="networkManager"/>, if there is one.
		/// </summary>
		public static bool TryGet(NetworkManager networkManager, out AIBrainHost host)
		{
			host = null;
			return networkManager != null && hosts.TryGetValue(networkManager, out host);
		}

		/// <summary>
		/// Starts hosting: follows NPC spawns and the network tick.
		/// </summary>
		public void Start()
		{
			if (Running || NetworkManager == null)
			{
				return;
			}

			if (hosts.TryGetValue(NetworkManager, out AIBrainHost existing) && existing != this)
			{
				Log.Warning("AIBrainHost", $"A brain host is already running for {NetworkManager.name}; the new one replaces it.");
				existing.Stop();
			}
			hosts[NetworkManager] = this;

			timeManager = NetworkManager.TimeManager;
			if (timeManager != null)
			{
				tickDelta = (float)timeManager.TickDelta;
				timeManager.OnTick += OnTick;
			}
			groupTicksPerAiUpdate = AIController.ResolveTicksPerAiUpdate(AIController.DEFAULT_AI_TICK_RATE, tickDelta);

			NPC.OnServerSpawned += OnNPCSpawned;
			NPC.OnServerDespawned += OnNPCDespawned;

			/* Pets are linked to their owners through a shared-code event; the dispatcher must be
			 * listening before the first pet can be summoned. */
			AggressionDispatcher.EnsurePetLinksTracked();

			Running = true;
		}

		/// <summary>
		/// Stops hosting. Brains stay on their NPCs but no longer tick.
		/// </summary>
		public void Stop()
		{
			if (!Running)
			{
				return;
			}
			Running = false;

			NPC.OnServerSpawned -= OnNPCSpawned;
			NPC.OnServerDespawned -= OnNPCDespawned;

			if (timeManager != null)
			{
				timeManager.OnTick -= OnTick;
				timeManager = null;
			}

			brains.Clear();
			slots.Clear();
			tickSnapshot.Clear();
			bodiesByScene.Clear();
			brainsByCollider.Clear();
			faults.Clear();

			// The packs stay with their members but no longer tick; a later host re-adopts none.
			for (int i = 0; i < groups.Count; ++i)
			{
				groups[i].Host = null;
				groups[i].HostSlot = -1;
			}
			groups.Clear();
			groupSnapshot.Clear();

			if (NetworkManager != null &&
				hosts.TryGetValue(NetworkManager, out AIBrainHost current) &&
				current == this)
			{
				hosts.Remove(NetworkManager);
			}
		}

		/// <summary>
		/// Gives <paramref name="npc"/> its brain for this spawn and starts it ticking.
		/// </summary>
		/// <remarks>
		/// Call after the NPC is active and in its final scene, and before
		/// <c>ServerManager.Spawn</c>: initialising warps the agent onto the NavMesh at
		/// <paramref name="home"/> and enters the archetype's first state, both of which need a live
		/// agent. Safe to call again for the same spawn; the latest call wins.
		/// </remarks>
		/// <param name="npc">The NPC being spawned.</param>
		/// <param name="home">Where it leashes and wanders around.</param>
		/// <param name="archetypeOverride">A spawner's archetype for this placement, or null for the prefab's.</param>
		/// <param name="waypoints">Optional patrol route.</param>
		/// <returns>The brain, or null when <paramref name="npc"/> is null.</returns>
		public AIController Prepare(NPC npc, Vector3 home, AIArchetypeTemplate archetypeOverride = null, Vector3[] waypoints = null)
		{
			if (npc == null)
			{
				return null;
			}

			AIController brain = npc.GetComponent<AIController>();
			if (brain == null)
			{
				brain = npc.gameObject.AddComponent<AIController>();
			}

			brain.InitializeOnce(npc);
			brain.Host = this;

			/* A new life is a new chance. A brain quarantined in its last life (see
			 * MaxConsecutiveTickFaults) may be given a different archetype this time, and one that
			 * failed only transiently must not carry the count into it. */
			brain.Quarantined = false;
			faults.Remove(brain);

			brain.StartTicking(tickDelta);

			AIBrainCatalogue.Entry entry = null;
			if (Catalogue != null)
			{
				Catalogue.TryGetEntry(npc.NetworkObject, out entry);
			}

			AIArchetypeTemplate archetype = archetypeOverride != null ? archetypeOverride : entry?.Archetype;
			if (archetype == null)
			{
				Log.Warning("AIBrainHost", $"{npc.gameObject.name} has no brain: its prefab is not in the AI brain catalogue and its spawner names no archetype.");
			}
			brain.Archetype = archetype;
			brain.BossScript = entry?.BossScript;

			brain.Prepared = true;
			brain.enabled = true;
			brain.Initialize(home, waypoints);

			// After Initialize, which enters the initial state the leash period belongs to.
			brain.SeedTimerPhases();

			Track(brain);
			return brain;
		}

		/// <summary>
		/// Prepares any NPC that its spawn path did not, where it stands.
		/// </summary>
		private void OnNPCSpawned(NPC npc)
		{
			if (!Owns(npc))
			{
				return;
			}

			AIController brain = npc.GetComponent<AIController>();
			if (brain != null && brain.Prepared)
			{
				Track(brain);
				return;
			}

			Prepare(npc, npc.Transform != null ? npc.Transform.position : npc.transform.position);
		}

		/// <summary>
		/// Stops and resets an NPC's brain as it leaves the world.
		/// </summary>
		private void OnNPCDespawned(NPC npc)
		{
			if (!Owns(npc))
			{
				return;
			}

			RetireBrain(npc.GetComponent<AIController>());
		}

		/// <summary>
		/// Takes a brain off the tick and resets it for the pool: what a despawn does, for an NPC
		/// that is going back into the pool without one.
		/// </summary>
		/// <remarks>
		/// A spawn rolled back before the network spawn stores its instance straight back in the
		/// pool, and FishNet raises no despawn for an object that was never spawned — so the brain
		/// prepared for it stayed on the tick, stepping an inactive pooled body, until it faulted
		/// its way into quarantine. The rollbacks call this instead. Safe for a brain not on the
		/// tick.
		/// </remarks>
		/// <param name="brain">The brain, or null.</param>
		internal void RetireBrain(AIController brain)
		{
			if (brain == null)
			{
				return;
			}

			Untrack(brain);
			brain.ResetForPool();
		}

		/// <summary>
		/// Retires the brain of an instance going back into the pool unspawned, through the host
		/// running for <paramref name="networkManager"/>, or directly when none is.
		/// </summary>
		/// <param name="networkManager">The network manager whose host prepared the brain.</param>
		/// <param name="instance">The pooled instance; one with no brain is left alone.</param>
		internal static void RetireBrainOf(NetworkManager networkManager, FishNet.Object.NetworkObject instance)
		{
			AIController brain = instance != null ? instance.GetComponent<AIController>() : null;
			if (brain == null)
			{
				return;
			}

			if (TryGet(networkManager, out AIBrainHost host))
			{
				host.RetireBrain(brain);
			}
			else
			{
				brain.ResetForPool();
			}
		}

		/// <summary>
		/// Starts ticking a pack. Called by <see cref="NPCGroup.AddMember"/> for its first member.
		/// </summary>
		/// <param name="group">The pack.</param>
		internal void AddGroup(NPCGroup group)
		{
			if (group == null || group.Released || group.Host == this)
			{
				return;
			}

			// A pack answers to one host; one handed over (a harness restarting its host) moves.
			group.Host?.RemoveGroup(group);

			group.Host = this;
			group.HostSlot = groups.Count;
			groups.Add(group);
		}

		/// <summary>
		/// Stops ticking a pack, swapping the last one into its slot. Called when it is released.
		/// </summary>
		/// <param name="group">The pack.</param>
		internal void RemoveGroup(NPCGroup group)
		{
			if (group == null || group.Host != this)
			{
				return;
			}

			int slot = group.HostSlot;
			if (slot < 0 || slot >= groups.Count || !ReferenceEquals(groups[slot], group))
			{
				// Out of step with the record; fall back to a search rather than remove the wrong one.
				slot = groups.IndexOf(group);
			}
			if (slot >= 0)
			{
				int last = groups.Count - 1;
				NPCGroup moved = groups[last];
				groups[slot] = moved;
				moved.HostSlot = slot;
				groups.RemoveAt(last);
			}

			group.Host = null;
			group.HostSlot = -1;
		}

		/// <summary>
		/// True when <paramref name="npc"/> is spawned under this host's network manager.
		/// </summary>
		private bool Owns(NPC npc)
		{
			return npc != null && npc.NetworkManager == NetworkManager;
		}

		/// <summary>
		/// Adds a brain to the tick, once, and its NPC to its scene's bodies.
		/// </summary>
		/// <remarks>
		/// A brain already ticking is re-filed if its physics scene has changed since it was
		/// tracked — preparing again for the same spawn is allowed, and the latest call wins.
		/// </remarks>
		private void Track(AIController brain)
		{
			if (slots.TryGetValue(brain, out Membership existing))
			{
				if (existing.Scene != brain.PhysicsScene)
				{
					RemoveFromScene(brain, existing);
					existing.Scene = brain.PhysicsScene;
					existing.SceneSlot = AddToScene(brain, existing.Scene);
					slots[brain] = existing;
				}
				return;
			}

			Membership membership = new Membership
			{
				Slot = brains.Count,
				Scene = brain.PhysicsScene,
			};
			brains.Add(brain);
			membership.SceneSlot = AddToScene(brain, membership.Scene);
			slots[brain] = membership;

			Collider[] colliders = brain.BodyColliders;
			if (colliders != null)
			{
				for (int i = 0; i < colliders.Length; ++i)
				{
					if (colliders[i] != null)
					{
						brainsByCollider[colliders[i]] = brain;
					}
				}
			}
		}

		/// <summary>
		/// Removes a brain from the tick by swapping the last one into its slot, and its NPC from
		/// its scene's bodies and the collider map.
		/// </summary>
		private void Untrack(AIController brain)
		{
			if (!slots.TryGetValue(brain, out Membership membership))
			{
				return;
			}

			int last = brains.Count - 1;
			AIController moved = brains[last];
			brains[membership.Slot] = moved;
			if (!ReferenceEquals(moved, brain))
			{
				Membership movedMembership = slots[moved];
				movedMembership.Slot = membership.Slot;
				slots[moved] = movedMembership;
			}
			brains.RemoveAt(last);

			RemoveFromScene(brain, membership);
			slots.Remove(brain);
			faults.Remove(brain);

			/* By reference: a brain destroyed with its scene still holds its collider array, and a
			 * destroyed collider still finds its own entry, because a Unity object's hash and
			 * equality are its instance ID. */
			Collider[] colliders = brain.BodyColliders;
			if (colliders != null)
			{
				for (int i = 0; i < colliders.Length; ++i)
				{
					if (!ReferenceEquals(colliders[i], null) &&
						brainsByCollider.TryGetValue(colliders[i], out AIController owner) &&
						ReferenceEquals(owner, brain))
					{
						brainsByCollider.Remove(colliders[i]);
					}
				}
			}
		}

		/// <summary>
		/// Files a brain's NPC under a physics scene's bodies.
		/// </summary>
		/// <returns>Its index in that scene's member list.</returns>
		private int AddToScene(AIController brain, PhysicsScene scene)
		{
			if (!bodiesByScene.TryGetValue(scene, out SceneBodies bodies))
			{
				bodies = new SceneBodies();
				bodiesByScene[scene] = bodies;
			}
			bodies.Members.Add(brain);
			bodies.BuiltAtTick = -1;
			return bodies.Members.Count - 1;
		}

		/// <summary>
		/// Takes a brain's NPC out of its scene's bodies by swapping the last member into its
		/// place, and drops the scene's entry once it holds nobody — a stacked instance that has
		/// unloaded never comes back under the same physics scene.
		/// </summary>
		private void RemoveFromScene(AIController brain, Membership membership)
		{
			if (!bodiesByScene.TryGetValue(membership.Scene, out SceneBodies bodies))
			{
				return;
			}

			List<AIController> members = bodies.Members;
			int slot = membership.SceneSlot;
			if (slot < 0 || slot >= members.Count || !ReferenceEquals(members[slot], brain))
			{
				// Out of step with the record; fall back to a search rather than remove the wrong one.
				slot = members.IndexOf(brain);
				if (slot < 0)
				{
					return;
				}
			}

			int last = members.Count - 1;
			AIController moved = members[last];
			members[slot] = moved;
			members.RemoveAt(last);
			if (!ReferenceEquals(moved, brain) && slots.TryGetValue(moved, out Membership movedMembership))
			{
				movedMembership.SceneSlot = slot;
				slots[moved] = movedMembership;
			}
			bodies.BuiltAtTick = -1;

			if (members.Count == 0)
			{
				bodiesByScene.Remove(membership.Scene);
			}
		}

		/// <summary>
		/// The NPC bodies standing in <paramref name="scene"/> this network tick, or null when the
		/// host drives no NPC there.
		/// </summary>
		/// <remarks>
		/// Filled on the first request in each tick and shared by every request after it, so a
		/// scene nobody is separating in costs nothing and a crowded one costs one pass over its
		/// NPCs however many of them ask. Positions are read as the pass finds them: brains earlier
		/// in the tick have already stepped, which is fresher than the physics broadphase the
		/// overlap it replaced was reading.
		/// </remarks>
		/// <param name="scene">The physics scene.</param>
		/// <returns>The scene's grid, or null.</returns>
		internal AIBodyGrid GetBodyGrid(PhysicsScene scene)
		{
			if (!bodiesByScene.TryGetValue(scene, out SceneBodies bodies))
			{
				return null;
			}

			if (bodies.BuiltAtTick != tickCount)
			{
				bodies.BuiltAtTick = tickCount;
				bodies.Grid.Clear();

				List<AIController> members = bodies.Members;
				for (int i = 0; i < members.Count; ++i)
				{
					AIController member = members[i];
					/* Every NPC body in the scene, corpses included: the overlap this replaced
					 * returned any NPC collider on the layer, and a corpse keeps its collider. A
					 * quarantined brain's NPC still stands where it stopped. */
					if (member == null || !member.gameObject.activeInHierarchy)
					{
						continue;
					}
					ICharacter character = member.Character;
					if (character == null || character.Transform == null)
					{
						continue;
					}
					bodies.Grid.Add(character.Transform.position, member.IdentityKey);
				}
			}

			return bodies.Grid;
		}

		/// <summary>
		/// The ticking brain that drives the NPC <paramref name="collider"/> belongs to.
		/// </summary>
		/// <param name="collider">A collider a physics query returned.</param>
		/// <param name="brain">The brain, when the collider is on an NPC this host drives.</param>
		/// <returns>True when one was found.</returns>
		internal bool TryGetBrain(Collider collider, out AIController brain)
		{
			brain = null;
			return !ReferenceEquals(collider, null) && brainsByCollider.TryGetValue(collider, out brain) && brain != null;
		}

		/// <summary>
		/// Records a brain's failure and, once it has failed
		/// <see cref="MaxConsecutiveTickFaults"/> times in a row, takes it off the tick.
		/// </summary>
		/// <remarks>
		/// Logged through a <see cref="RepeatingFaultLog"/> per brain: the first failure in full,
		/// identical repeats as periodic counts. Isolating each brain was right — one broken brain
		/// must not stop every other NPC — but isolation alone turned a deterministic throw into a
		/// full stack trace every tick: fifty NPCs sharing a broken archetype wrote fifteen hundred
		/// a second. The quarantine line is written once and names the NPC, its archetype and its
		/// scene, so the log says why an NPC stopped thinking.
		/// </remarks>
		/// <param name="brain">The brain that threw.</param>
		/// <param name="ex">What it threw.</param>
		private void ReportFault(AIController brain, System.Exception ex)
		{
			if (!faults.TryGetValue(brain, out RepeatingFaultLog log))
			{
				log = new RepeatingFaultLog("AIBrainHost", $"Brain on {DescribeBrain(brain)}");
				faults[brain] = log;
			}
			log.Report(ex, Time.realtimeSinceStartupAsDouble);

			if (!ShouldQuarantine(log.ConsecutiveFailures))
			{
				return;
			}

			string description = DescribeBrain(brain);
			Untrack(brain);
			brain.Quarantine();

			Log.Error("AIBrainHost",
				$"Stopped the brain on {description} after {MaxConsecutiveTickFaults} consecutive failures. " +
				$"This NPC will not move or fight again until it respawns. Last failure: {ex}");
		}

		/// <summary>
		/// Whether a brain with this many failures in a row is taken off the tick. Pure, so the
		/// threshold can be pinned.
		/// </summary>
		/// <param name="consecutiveFailures">Failures since the brain last completed a run.</param>
		/// <returns>True to quarantine.</returns>
		public static bool ShouldQuarantine(int consecutiveFailures)
		{
			return consecutiveFailures >= MaxConsecutiveTickFaults;
		}

		/// <summary>
		/// Names a brain's NPC, archetype and scene for a log line.
		/// </summary>
		private static string DescribeBrain(AIController brain)
		{
			string name = brain != null ? brain.gameObject.name : "<destroyed>";
			string archetype = brain != null && brain.Archetype != null ? brain.Archetype.name : "none";
			string scene = brain != null ? brain.gameObject.scene.name : "?";
			long id = brain != null && brain.Character != null ? brain.Character.ID : 0;
			return $"'{name}' (ID {id}, archetype '{archetype}', scene '{scene}')";
		}

		/// <summary>
		/// Runs one network tick of every brain.
		/// </summary>
		private void OnTick()
		{
			++tickCount;

			tickSnapshot.Clear();
			tickSnapshot.AddRange(brains);

			for (int i = 0; i < tickSnapshot.Count; ++i)
			{
				AIController brain = tickSnapshot[i];

				/* Reference check first, then Unity's liveness check: an NPC destroyed outright
				 * (scene unload) never raised a despawn, and its brain is dropped here. */
				if (ReferenceEquals(brain, null) || brain == null)
				{
					if (!ReferenceEquals(brain, null))
					{
						Untrack(brain);
					}
					continue;
				}

				// Despawned earlier in this same tick by another brain's actions.
				if (!slots.ContainsKey(brain))
				{
					continue;
				}

				try
				{
					/* Only a completed run proves the brain healthy. A throw inside the pipeline
					 * recurs once per run, and the body-only ticks between runs must not reset the
					 * count, or a deterministic fault would never add up to a quarantine. */
					if (brain.Tick() && faults.Count > 0 && faults.TryGetValue(brain, out RepeatingFaultLog healed))
					{
						healed.ReportSuccess();
					}
				}
				catch (System.Exception ex)
				{
					// One broken brain must not stop every other NPC in the process.
					ReportFault(brain, ex);
				}
			}

			tickSnapshot.Clear();

			TickGroups();
		}

		/// <summary>
		/// Runs one network tick of every pack, after the brains have run theirs.
		/// </summary>
		/// <remarks>
		/// After, so a pack evaluates what its members did this tick. Each pack gates itself onto
		/// its own AI tick and its members' LOD (<see cref="NPCGroup.Tick"/>), so most calls return
		/// on a counter. A pack that throws is reported through its own
		/// <see cref="RepeatingFaultLog"/> and the rest carry on; unlike a brain it is not
		/// quarantined, because nothing it computes is needed for its members to keep fighting.
		/// </remarks>
		private void TickGroups()
		{
			if (groups.Count == 0)
			{
				return;
			}

			groupSnapshot.Clear();
			groupSnapshot.AddRange(groups);

			for (int i = 0; i < groupSnapshot.Count; ++i)
			{
				NPCGroup group = groupSnapshot[i];

				// Released earlier in this same walk, or by a brain's actions this tick.
				if (group.Released || group.Host != this)
				{
					continue;
				}

				try
				{
					if (group.Tick(tickDelta, groupTicksPerAiUpdate))
					{
						group.Faults?.ReportSuccess();
					}
				}
				catch (System.Exception ex)
				{
					group.Faults ??= new RepeatingFaultLog("AIBrainHost", $"Pack of {group.MemberCount} ({group.Tactic})");
					group.Faults.Report(ex, Time.realtimeSinceStartupAsDouble);
				}
			}

			groupSnapshot.Clear();
		}
	}
}
