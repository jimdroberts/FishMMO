using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Managing.Timing;
using FishMMO.Logging;
using FishMMO.Shared;
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
		/// Brains currently ticking.
		/// </summary>
		private readonly List<AIController> brains = new List<AIController>();

		/// <summary>
		/// Each ticking brain's slot in <see cref="brains"/>, for swap-removal.
		/// </summary>
		private readonly Dictionary<AIController, int> slots = new Dictionary<AIController, int>();

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

			AIController brain = npc.GetComponent<AIController>();
			if (brain == null)
			{
				return;
			}

			Untrack(brain);
			brain.ResetForPool();
		}

		/// <summary>
		/// True when <paramref name="npc"/> is spawned under this host's network manager.
		/// </summary>
		private bool Owns(NPC npc)
		{
			return npc != null && npc.NetworkManager == NetworkManager;
		}

		/// <summary>
		/// Adds a brain to the tick, once.
		/// </summary>
		private void Track(AIController brain)
		{
			if (slots.ContainsKey(brain))
			{
				return;
			}
			slots[brain] = brains.Count;
			brains.Add(brain);
		}

		/// <summary>
		/// Removes a brain from the tick by swapping the last one into its slot.
		/// </summary>
		private void Untrack(AIController brain)
		{
			if (!slots.TryGetValue(brain, out int slot))
			{
				return;
			}

			int last = brains.Count - 1;
			AIController moved = brains[last];
			brains[slot] = moved;
			slots[moved] = slot;
			brains.RemoveAt(last);
			slots.Remove(brain);
		}

		/// <summary>
		/// Runs one network tick of every brain.
		/// </summary>
		private void OnTick()
		{
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
					brain.Tick();
				}
				catch (System.Exception ex)
				{
					// One broken brain must not stop every other NPC in the process.
					Log.Error("AIBrainHost", $"Brain on {brain.gameObject.name} threw: {ex}");
				}
			}

			tickSnapshot.Clear();
		}
	}
}
