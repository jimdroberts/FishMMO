using System;
using System.Collections;
using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet.Managing;
using FishNet.Managing.Timing;
using UnityEngine;
using UnityEngine.AI;

namespace FishMMO.TestHarness
{
	/// <summary>
	/// End-to-end combat simulation: a real FishNet SERVER runs in-process (no clients, no
	/// database) and two teams of real NPC fighters cast the entire mock ability roster at each
	/// other through the production pipeline — replicate-driven input, ECA dispatch, buffs,
	/// damage, death — while every caster carries a synthetic latency claim (0–500 ms) that
	/// engages the REAL lag-compensation rewind through <see cref="LagCompensationTick"/>'s
	/// test-only <c>ClaimOverride</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// What this scene proves: the server-authoritative half of combat runs deterministically
	/// end-to-end — every mock shape spawns, resolves hits, applies damage/heals/buffs, and
	/// rewinds targets by exactly the claimed view offset. The client-prediction half
	/// (rollback identity, replay convergence) is the PlatformSim's job; a single process
	/// cannot be both peers of a FishNet session, so the two scenes split the model between
	/// them. See the CombatSim research notes in the harness folder history.
	/// </para>
	/// <para>
	/// Everything here lives in Assets/TestHarness (plus the generated scene) for one-delete
	/// cleanup. The only production accommodation is <c>LagCompensationTick.ClaimOverride</c>,
	/// null in production, documented at its declaration.
	/// </para>
	/// </remarks>
	public sealed class CombatSimBootstrap : MonoBehaviour
	{
		[Tooltip("Direct references to the mock content (unregistered with addressables by design).")]
		public CombatSimManifest Manifest;

		[Tooltip("Fighters per team.")]
		[Range(1, 4)]
		public int TeamSize = 2;

		[Tooltip("Synthetic round-trip claim, in ms, applied to every caster (the live cap is 500).")]
		[Range(0, 500)]
		public int ClaimMs = 250;

		[Tooltip("Step the claim 0→500 in 100ms increments every 15 seconds.")]
		public bool AutoSweep = true;

		[Tooltip("Tugboat listen port — any free port; no client ever connects.")]
		public ushort Port = 7797;

		private readonly SimServer server = new SimServer();
		private NetworkManager networkManager;
		private TimeManager NetTime => networkManager != null ? networkManager.TimeManager : null;

		private sealed class Fighter
		{
			public ICharacter Character;
			public NPC Npc;
			public AbilityController Abilities;
			public AIController Ai;
			public TargetController Targeting;
			public CharacterPositionHistory History;
			public ICharacterDamageController Damage;
			public UnityEngine.AI.NavMeshAgent Agent;
			public TextMesh HealthLabel;
			public int Team;
			public int NextAbility;
			/// <summary>Per-fighter shuffled indices into the manifest roster, so no two fighters
			/// walk the same scripted-cast sequence.</summary>
			public int[] RosterOrder;
			/// <summary>Next wall time this fighter's sparse scripted coverage cast fires.</summary>
			public float NextScriptedCastAt;
			public bool Alive = true;
			public GameObject RewindGhost;
			public LineRenderer Trail;
			public string LastCast = "";
		}

		private readonly List<Fighter> fighters = new List<Fighter>();
		private readonly Dictionary<ICharacter, (byte ticks, byte fraction)> claims =
			new Dictionary<ICharacter, (byte, byte)>();
		private readonly List<(int team, float dueTime)> respawnQueue = new List<(int, float)>();

		// ── Stats ───────────────────────────────────────────────────────────────────
		private long damageEvents;
		private long totalDamage;
		private long healEvents;
		private long buffAdds;
		private long buffRemoves;
		private long kills;
		private long castsStarted;
		private long claimsConsulted;
		private long rewindsResolved;
		private int appliedClaimMs = -1;
		private float sweepClock;
		private float startClock;
		private bool ready;
		private NavMeshDataInstance navMeshInstance;

		private static readonly Vector3[] TeamAnchors =
		{
			new Vector3(-6f, 0f, 0f),
			new Vector3(6f, 0f, 0f),
		};

		private IEnumerator Start()
		{
#if UNITY_EDITOR
			// The PlayMode suite adds this component bare; find the generated manifest the way
			// the scene generator wired it. Editor-only — the scene carries the reference in builds.
			if (Manifest == null)
			{
				Manifest = UnityEditor.AssetDatabase.LoadAssetAtPath<CombatSimManifest>(
					"Assets/TestHarness/Combat/CombatSimManifest.asset");
			}
#endif
			if (Manifest == null || Manifest.NpcPrefab == null)
			{
				Debug.LogError("[CombatSim] No manifest (or no NPC prefab in it) — run FishMMO → Test Scenes → Generate Combat Sim first.");
				yield break;
			}

			BuildArena();

			// 1-3. Templates, KCC, and a started server — the shared bootstrap.
			yield return server.Boot(Manifest, transform, Port);
			networkManager = server.NetworkManager;
			if (networkManager == null)
			{
				yield break;
			}

			// 4. The synthetic latency claim — the door into the real rewind path.
			LagCompensationTick.ClaimOverride = ResolveClaim;

			/* 5. Death wiring + HUD counters. In production the DB-backed CharacterSystem owns
			 * the OnKilled → strip buffs → corpse chain; the harness replays those two calls. */
			ICharacterDamageController.OnDamaged += OnDamaged;
			ICharacterDamageController.OnHealed += OnHealed;
			ICharacterDamageController.OnKilled += OnKilled;
			IBuffController.OnAddBuff += OnAddBuff;
			IBuffController.OnRemoveBuff += OnRemoveBuff;
			IBuffController.OnAddDebuff += OnAddBuff;
			IBuffController.OnRemoveDebuff += OnRemoveBuff;

			// 6. Two teams, facing each other across the arena.
			for (int team = 0; team < 2; ++team)
			{
				for (int i = 0; i < TeamSize; ++i)
				{
					SpawnFighter(team, i);
				}
			}

			startClock = Time.time;
			ready = true;
			StartCoroutine(DirectorLoop());
		}

		private void OnDestroy()
		{
			LagCompensationTick.ClaimOverride = null;
			ICharacterDamageController.OnDamaged -= OnDamaged;
			ICharacterDamageController.OnHealed -= OnHealed;
			ICharacterDamageController.OnKilled -= OnKilled;
			IBuffController.OnAddBuff -= OnAddBuff;
			IBuffController.OnRemoveBuff -= OnRemoveBuff;
			IBuffController.OnAddDebuff -= OnAddBuff;
			IBuffController.OnRemoveDebuff -= OnRemoveBuff;
			if (navMeshInstance.valid)
			{
				NavMesh.RemoveNavMeshData(navMeshInstance);
			}
			server.Shutdown();
		}

		// ── World ───────────────────────────────────────────────────────────────────

		/// <summary>Flat arena floor plus a runtime-built NavMesh (AIController warps its agent
		/// on Initialize and stalls without one).</summary>
		private void BuildArena()
		{
			GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
			ground.name = "Arena";
			ground.transform.SetParent(transform, false);
			ground.transform.position = new Vector3(0f, -0.5f, 0f);
			ground.transform.localScale = new Vector3(40f, 1f, 40f);
			Renderer groundRenderer = ground.GetComponent<Renderer>();
			Material groundMaterial = new Material(Shader.Find("Unlit/Color"));
			groundMaterial.color = new Color(0.32f, 0.34f, 0.3f);
			groundRenderer.sharedMaterial = groundMaterial;

			Physics.SyncTransforms();

			Bounds bounds = new Bounds(Vector3.zero, new Vector3(60f, 10f, 60f));
			List<NavMeshBuildSource> sources = new List<NavMeshBuildSource>();
			NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0,
				new List<NavMeshBuildMarkup>(), sources);
			NavMeshData data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources,
				bounds, Vector3.zero, Quaternion.identity);
			navMeshInstance = NavMesh.AddNavMeshData(data);
		}

		// ── Fighters ────────────────────────────────────────────────────────────────

		private void SpawnFighter(int team, int slot)
		{
			Vector3 position = TeamAnchors[team] + new Vector3(0f, 0f, (slot - (TeamSize - 1) * 0.5f) * 3f);
			Quaternion facing = Quaternion.LookRotation(team == 0 ? Vector3.right : Vector3.left);

			/* Configured while inactive: the NPC prefabs ship with ChanneledTemplate/ChargedTemplate
			 * null (the held shapes would degrade to instants), and the sim wants an unrestricted
			 * targeting mask. BaseCharacter.Awake registers whatever behaviours exist at that
			 * moment, so the additions must precede activation. The TargetController is now a
			 * RequireComponent on NPC (issue #232 — shipped prefabs had none, so no NPC cast ever
			 * spawned an object outside this harness); adding a second would throw. */
			GameObject clone = server.Spawn(Manifest.NpcPrefab, position, facing, gameObject.scene,
				configure: go =>
				{
					go.name = $"{(team == 0 ? "Red" : "Blue")} Fighter {slot}";
					TargetController targeting = go.GetComponent<TargetController>();
					if (targeting == null)
					{
						targeting = go.AddComponent<TargetController>();
					}
					targeting.LayerMask = ~0;

					NPC configuring = go.GetComponent<NPC>();
					configuring.Abilities.Clear();
					configuring.Abilities.AddRange(Manifest.Roster);

					AbilityController casting = go.GetComponent<AbilityController>();
					casting.ChanneledTemplate = Manifest.ChannelMarker;
					casting.ChargedTemplate = Manifest.ChargeMarker;

					/* Every AI slot reads through the archetype, and the archetype is a shared
					 * asset — mutating it would edit the shipped brain. Each fighter gets a
					 * per-instance CLONE to tune instead. */
					AIController brain = go.GetComponent<AIController>();
					if (brain.Archetype != null)
					{
						AIArchetypeTemplate simBrain = Instantiate(brain.Archetype);
						simBrain.name = brain.Archetype.name + " (sim)";

						// Null LOD settings = always Active: with zero observers the tier
						// evaluator would otherwise park every brain Dormant and nothing would
						// ever fight.
						simBrain.LodSettings = null;

						/* The shipped attacking state has PreferredDistance 0 — close to melee.
						 * Two such teams collapse into a touching scrum, and aiming at a target
						 * you are standing inside produces a near-vertical fire direction
						 * (projectiles straight up). A cloned state holds a ranged standoff
						 * instead, so the red and blue lines trade fire visibly. */
						if (simBrain.AttackingState is BaseAttackingState prefabAttack)
						{
							BaseAttackingState rangedAttack = Instantiate(prefabAttack);
							rangedAttack.name = prefabAttack.name + " (ranged sim)";
							rangedAttack.PreferredDistance = 9f;
							rangedAttack.MinComfortDistance = 5f;
							// Global cast pacing on top of per-ability cooldowns: without it a
							// spellbook this deep lets the AI legally chain instants back-to-back.
							// The jitter is also what desynchronizes the two teams' rhythms.
							rangedAttack.AttackCooldown = 1.5f;
							rangedAttack.AttackCooldownJitter = 1.0f;
							simBrain.AttackingState = rangedAttack;
						}

						brain.Archetype = simBrain;
					}
				},
				afterActivate: go => go.GetComponent<AIController>().Initialize(position));

			NPC npc = clone.GetComponent<NPC>();
			AbilityController abilities = clone.GetComponent<AbilityController>();
			AIController ai = clone.GetComponent<AIController>();

			Fighter fighter = new Fighter
			{
				Character = npc,
				Npc = npc,
				Abilities = abilities,
				Ai = ai,
				Targeting = clone.GetComponent<TargetController>(),
				History = clone.GetComponent<CharacterPositionHistory>(),
				Agent = clone.GetComponent<UnityEngine.AI.NavMeshAgent>(),
				Team = team,
			};
			npc.TryGet(out fighter.Damage);
			fighter.RosterOrder = ShuffledIndices(Manifest.Roster.Count);
			fighter.NextScriptedCastAt = Time.time + UnityEngine.Random.Range(0.5f, 4f);
			abilities.OnUpdate += (name, remaining, total) => fighter.LastCast = name;
			BuildFighterVisual(fighter, clone.transform, team);
			fighters.Add(fighter);
			RefreshClaims();
		}


		/// <summary>The server never loads character models (that is the client's ReadPayload
		/// path), so give each fighter a team-colored capsule, a history trail, and a rewind
		/// ghost so the sim is watchable.</summary>
		private void BuildFighterVisual(Fighter fighter, Transform root, int team)
		{
			GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
			Destroy(body.GetComponent<Collider>());
			body.transform.SetParent(root, false);
			body.transform.localPosition = new Vector3(0f, 1f, 0f);
			Material bodyMaterial = new Material(Shader.Find("Unlit/Color"));
			bodyMaterial.color = team == 0 ? new Color(0.85f, 0.3f, 0.25f) : new Color(0.25f, 0.45f, 0.9f);
			body.GetComponent<Renderer>().sharedMaterial = bodyMaterial;

			GameObject ghost = GameObject.CreatePrimitive(PrimitiveType.Capsule);
			Destroy(ghost.GetComponent<Collider>());
			ghost.name = root.name + " (rewound pose)";
			ghost.transform.localScale = new Vector3(1.02f, 1.02f, 1.02f);
			Material ghostMaterial = new Material(Shader.Find("Sprites/Default"));
			ghostMaterial.color = new Color(1f, 0.9f, 0.2f, 0.35f);
			ghost.GetComponent<Renderer>().sharedMaterial = ghostMaterial;
			ghost.SetActive(false);
			fighter.RewindGhost = ghost;

			GameObject trailGo = new GameObject(root.name + " (history)");
			LineRenderer trail = trailGo.AddComponent<LineRenderer>();
			trail.material = new Material(Shader.Find("Sprites/Default"));
			trail.startColor = trail.endColor = new Color(1f, 1f, 1f, 0.25f);
			trail.startWidth = trail.endWidth = 0.06f;
			trail.positionCount = 0;
			fighter.Trail = trail;

			// Name + live health, floating over the head and facing the camera.
			GameObject labelGo = new GameObject("HealthLabel");
			labelGo.transform.SetParent(root, false);
			labelGo.transform.localPosition = new Vector3(0f, 2.6f, 0f);
			TextMesh label = labelGo.AddComponent<TextMesh>();
			label.characterSize = 0.28f;
			label.fontSize = 32;
			label.anchor = TextAnchor.LowerCenter;
			label.alignment = TextAlignment.Center;
			label.color = team == 0 ? new Color(1f, 0.55f, 0.5f) : new Color(0.55f, 0.75f, 1f);
			fighter.HealthLabel = label;
		}

		// ── Latency claims ──────────────────────────────────────────────────────────

		private (byte ticks, byte fraction)? ResolveClaim(ICharacter caster)
		{
			if (!claims.TryGetValue(caster, out (byte ticks, byte fraction) claim))
			{
				return null;
			}
			claimsConsulted++;
			return claim;
		}

		/// <summary>Converts the ms slider into the exact (ticks, fraction) bytes a client
		/// would have measured, through the production arithmetic.</summary>
		private void RefreshClaims()
		{
			TimeManager tm = NetTime;
			if (tm == null)
			{
				return;
			}
			claims.Clear();
			LagCompensationTick.ResolveViewOffset(ClaimMs, tm.TickDelta, out byte ticks, out byte fraction);
			foreach (Fighter fighter in fighters)
			{
				claims[fighter.Character] = (ticks, fraction);
			}
			appliedClaimMs = ClaimMs;
		}

		// ── Death and events ────────────────────────────────────────────────────────

		private readonly Dictionary<ICharacter, float> lastDamagedAt = new Dictionary<ICharacter, float>();

		private void OnDamaged(ICharacter attacker, ICharacter defender, int amount, DamageAttributeTemplate template)
		{
			damageEvents++;
			totalDamage += amount;
			lastDamagedAt[defender] = Time.time;
			if (defender?.Transform != null)
			{
				string kind = template != null ? template.name : "Damage";
				FloatingLabel.Spawn(defender.Transform.position + Vector3.up * 2.1f,
					$"-{amount} {kind}", new Color(1f, 0.35f, 0.3f));
			}
		}

		private void OnHealed(ICharacter healer, ICharacter target, int amount)
		{
			healEvents++;
			if (target?.Transform != null)
			{
				FloatingLabel.Spawn(target.Transform.position + Vector3.up * 2.1f,
					$"+{amount} Heal", new Color(0.35f, 1f, 0.45f));
			}
		}

		private void OnAddBuff(IBuffController controller, Buff buff)
		{
			buffAdds++;
			ICharacter owner = controller?.Character;
			if (owner?.Transform != null && buff?.Template != null)
			{
				FloatingLabel.Spawn(owner.Transform.position + Vector3.up * 2.4f,
					$"+ {buff.Template.name}", new Color(0.4f, 0.85f, 1f), 0.28f);
			}
		}

		private void OnRemoveBuff(IBuffController controller, Buff buff)
		{
			buffRemoves++;
			ICharacter owner = controller?.Character;
			if (owner?.Transform != null && buff?.Template != null)
			{
				FloatingLabel.Spawn(owner.Transform.position + Vector3.up * 2.4f,
					$"- {buff.Template.name}", new Color(0.65f, 0.65f, 0.65f), 0.24f);
			}
		}

		private void OnKilled(ICharacter killer, ICharacter victim)
		{
			kills++;
			if (victim?.Transform != null)
			{
				FloatingLabel.Spawn(victim.Transform.position + Vector3.up * 2.1f,
					"KILLED", new Color(0.9f, 0.1f, 0.1f), 0.45f);
			}
			// The production chain, minus the DB: strip buffs, then corpse the NPC.
			if (victim.TryGet(out IBuffController buffController))
			{
				buffController.RemoveAll();
			}
			foreach (Fighter fighter in fighters)
			{
				if (!ReferenceEquals(fighter.Character, victim))
				{
					continue;
				}
				fighter.Npc.Despawn();
				fighter.Alive = false;
				Destroy(fighter.RewindGhost);
				Destroy(fighter.Trail.gameObject);
				respawnQueue.Add((fighter.Team, Time.time + 4f));
				break;
			}
		}

		// ── Director ────────────────────────────────────────────────────────────────

		/// <summary>
		/// Keeps the fight going forever: re-pairs survivors into the (ranged-standoff)
		/// AttackingState every cycle — the AI's own enemy sweep is observer-gated and this
		/// scene has no observers — layers scripted roster casts on top so every mock shape
		/// provably fires, heals damaged fighters, and respawns the fallen.
		/// </summary>
		private IEnumerator DirectorLoop()
		{
			WaitForSeconds cadence = new WaitForSeconds(2f);
			while (true)
			{
				yield return cadence;

				for (int i = respawnQueue.Count - 1; i >= 0; --i)
				{
					if (Time.time >= respawnQueue[i].dueTime)
					{
						int team = respawnQueue[i].team;
						respawnQueue.RemoveAt(i);
						SpawnFighter(team, UnityEngine.Random.Range(0, TeamSize));
					}
				}
				fighters.RemoveAll(f => !f.Alive && f.Npc == null);

				foreach (Fighter fighter in fighters)
				{
					if (!fighter.Alive)
					{
						continue;
					}
					Fighter enemy = NearestEnemy(fighter);
					if (enemy == null)
					{
						continue;
					}

					/* Threat pairing enters the (per-fighter, ranged-standoff) AttackingState;
					 * from there the AI fights on its own — approach to ability range, pick with
					 * jittered scoring, pace by AttackCooldown — which is where the bulk of the
					 * damage comes from and where the two teams' rhythms naturally diverge. */
					fighter.Ai.OnThreatReceived(enemy.Character);

					if (Manifest.Roster.Count > 0)
					{
						/* SPARSE coverage casting, not a metronome: the earlier version cast the
						 * same roster entry for every fighter on the same 2s beat, which made the
						 * teams mirror each other visibly. Each fighter now follows its own
						 * SHUFFLED roster order on its own jittered clock; the AI's own combat
						 * casting carries the fight in between. */
						if (Time.time < fighter.NextScriptedCastAt)
						{
							continue;
						}
						fighter.NextScriptedCastAt = Time.time + UnityEngine.Random.Range(3f, 6f);

						/* Heals at full health (and on the dead) are deliberately suppressed by
						 * CharacterDamageController.Heal, so a blind roster cycle can spend a whole
						 * run never producing one OnHealed. A recently-damaged fighter casts a heal
						 * instead of its next roster entry — deterministic coverage of the
						 * restorative path on a target the suppression rules accept. */
						AbilityTemplate next = null;
						bool selfCast = false;
						if (lastDamagedAt.TryGetValue(fighter.Character, out float damagedAt)
							&& Time.time - damagedAt < 5f)
						{
							next = FindHealTemplate();
							selfCast = next != null;
							lastDamagedAt.Remove(fighter.Character);
						}
						if (next == null)
						{
							int[] order = fighter.RosterOrder;
							next = Manifest.Roster[order[fighter.NextAbility % order.Length]];
							fighter.NextAbility++;
						}

						/* Aim the target trace before casting (the RequiresTarget pre-filter
						 * refuses casts with no current target; the server re-traces
						 * authoritatively per cast anyway). Offensive shapes aim at the enemy.
						 * The heal must aim at the CASTER: its event carries a
						 * TargetAllianceCondition that excludes enemies and neutrals, so a heal
						 * fired at the opposing team's fighter resolves zero targets. */
						Vector3 origin;
						Vector3 aim;
						if (selfCast)
						{
							origin = fighter.Npc.Transform.position + Vector3.up * 2.5f;
							aim = Vector3.down;
						}
						else
						{
							origin = fighter.Npc.Transform.position + Vector3.up * 1.2f;
							aim = (enemy.Npc.Transform.position + Vector3.up * 1.2f - origin).normalized;
						}
						fighter.Targeting.UpdateTarget(origin, aim, 30f);

						long id = next.ID;
						bool held = fighter.Abilities.RequiresHeld(id);
						fighter.Abilities.Activate(id, held);
						castsStarted++;
						if (held)
						{
							StartCoroutine(ReleaseAfter(fighter.Abilities, 1.2f));
						}
					}
				}
			}
		}

		private static int[] ShuffledIndices(int count)
		{
			int[] indices = new int[Mathf.Max(count, 1)];
			for (int i = 0; i < indices.Length; ++i)
			{
				indices[i] = i % Mathf.Max(count, 1);
			}
			for (int i = indices.Length - 1; i > 0; --i)
			{
				int j = UnityEngine.Random.Range(0, i + 1);
				(indices[i], indices[j]) = (indices[j], indices[i]);
			}
			return indices;
		}

		private AbilityTemplate healTemplate;

		private AbilityTemplate FindHealTemplate()
		{
			if (healTemplate != null)
			{
				return healTemplate;
			}
			foreach (AbilityTemplate template in Manifest.Roster)
			{
				if (template == null || !template.name.Contains("Heal"))
				{
					continue;
				}
				healTemplate = template;
				/* Prefer the caster-centred area heal: it spawns on SELF with no target
				 * requirement, so it heals the damaged caster (and nearby allies) regardless of
				 * where the AI's aim happens to point. The targeted single heal resolves through
				 * the server's per-cast aim re-trace, which the AI keeps pointed at the ENEMY —
				 * and its ally-only alliance condition then refuses the traced target. */
				if (template.name == "Mock Area Heal")
				{
					break;
				}
			}
			return healTemplate;
		}

		private IEnumerator ReleaseAfter(AbilityController abilities, float seconds)
		{
			yield return new WaitForSeconds(seconds);
			if (abilities != null)
			{
				abilities.Release();
			}
		}

		private Fighter NearestEnemy(Fighter of)
		{
			Fighter best = null;
			float bestSqr = float.MaxValue;
			foreach (Fighter candidate in fighters)
			{
				if (!candidate.Alive || candidate.Team == of.Team || candidate.Npc == null)
				{
					continue;
				}
				float sqr = (candidate.Npc.Transform.position - of.Npc.Transform.position).sqrMagnitude;
				if (sqr < bestSqr)
				{
					bestSqr = sqr;
					best = candidate;
				}
			}
			return best;
		}

		// ── Per-frame: sweep + rewind visualization ─────────────────────────────────

		private void Update()
		{
			if (!ready)
			{
				return;
			}

			if (AutoSweep)
			{
				sweepClock += Time.deltaTime;
				if (sweepClock >= 15f)
				{
					sweepClock = 0f;
					ClaimMs = ClaimMs >= 500 ? 0 : ClaimMs + 100;
				}
			}
			if (ClaimMs != appliedClaimMs)
			{
				RefreshClaims();
			}

			TimeManager tm = NetTime;
			if (tm == null)
			{
				return;
			}

			Camera viewCamera = Camera.main;
			foreach (Fighter fighter in fighters)
			{
				if (!fighter.Alive || fighter.History == null)
				{
					continue;
				}

				// Live health over the head, camera-facing.
				if (fighter.HealthLabel != null)
				{
					CharacterResourceAttribute health = fighter.Damage?.ResourceInstance;
					fighter.HealthLabel.text = health != null
						? $"{fighter.Npc.name}\n{Mathf.CeilToInt(health.CurrentValue)} / {health.FinalValue} HP"
						: fighter.Npc.name;
					if (viewCamera != null)
					{
						fighter.HealthLabel.transform.rotation = Quaternion.LookRotation(
							fighter.HealthLabel.transform.position - viewCamera.transform.position);
					}
				}

				/* The rewind ghost: ask the REAL resolver where this caster's targets would be
				 * rewound to right now (ClaimOverride feeds it the synthetic claim) and read the
				 * REAL ring. The gap between the solid body and its yellow ghost IS the claimed
				 * latency, on screen. */
				if (LagCompensationTick.TryResolve(fighter.Character, tm, out RewindTarget target)
					&& fighter.History.TryResolve(target, out CharacterPositionHistory.Snapshot rewound))
				{
					rewindsResolved++;
					fighter.RewindGhost.SetActive(true);
					fighter.RewindGhost.transform.SetPositionAndRotation(
						rewound.Position + Vector3.up * 1f, rewound.Rotation);
				}
				else
				{
					fighter.RewindGhost.SetActive(false);
				}

				// History trail: the recorded ring, sampled every 3 ticks.
				int recorded = fighter.History.RecordedTicks;
				uint now = LagCompensationTick.ServerTickDomain(tm);
				int points = 0;
				fighter.Trail.positionCount = (recorded + 2) / 3;
				for (int back = recorded - 1; back >= 0; back -= 3)
				{
					if (fighter.History.TryResolve(now - (uint)back, out CharacterPositionHistory.Snapshot snap)
						&& points < fighter.Trail.positionCount)
					{
						fighter.Trail.SetPosition(points++, snap.Position + Vector3.up * 0.1f);
					}
				}
				fighter.Trail.positionCount = points;
			}
		}

		// ── HUD ─────────────────────────────────────────────────────────────────────

		private void OnGUI()
		{
			GUILayout.BeginArea(new Rect(10, 10, 460, 400), GUI.skin.box);
			GUILayout.Label("<b>Combat Simulation — server-authoritative, zero clients</b>", Rich());
			if (!ready)
			{
				GUILayout.Label("loading templates / starting server…");
				GUILayout.EndArea();
				return;
			}

			GUILayout.BeginHorizontal();
			GUILayout.Label($"Latency claim: {ClaimMs} ms", GUILayout.Width(150));
			ClaimMs = (int)GUILayout.HorizontalSlider(ClaimMs, 0, 500, GUILayout.Width(200));
			AutoSweep = GUILayout.Toggle(AutoSweep, "sweep");
			GUILayout.EndHorizontal();

			int alive = 0;
			foreach (Fighter fighter in fighters)
			{
				if (fighter.Alive) alive++;
			}
			GUILayout.Label($"fighters alive: {alive}   kills: {kills}   respawns queued: {respawnQueue.Count}");
			GUILayout.Label($"casts: {castsStarted}   damage events: {damageEvents} ({totalDamage} total)   heals: {healEvents}");
			GUILayout.Label($"buffs +{buffAdds} / -{buffRemoves}");
			GUILayout.Label($"claims consulted: {claimsConsulted}   rewinds resolved: {rewindsResolved}");
			foreach (Fighter fighter in fighters)
			{
				if (!fighter.Alive)
				{
					continue;
				}
				CharacterResourceAttribute health = fighter.Damage?.ResourceInstance;
				string healthText = health != null
					? $"{Mathf.CeilToInt(health.CurrentValue)}/{health.FinalValue} HP"
					: "no health";
				string castText = string.IsNullOrEmpty(fighter.LastCast) ? "" : $"   casting {fighter.LastCast}";
				GUILayout.Label($"{fighter.Npc.name}: {healthText}{castText}");
			}

			bool warmedUp = Time.time - startClock > 20f;
			bool pass = !warmedUp
				|| (damageEvents > 0 && healEvents > 0 && buffAdds > 0 && castsStarted > 0
					&& claimsConsulted > 0 && rewindsResolved > 0);
			GUI.color = warmedUp ? (pass ? Color.green : Color.red) : Color.yellow;
			GUILayout.Label(warmedUp ? (pass ? "<b>PASS</b>" : "<b>FAIL</b>") : "<b>warming up…</b>", Rich());
			GUI.color = Color.white;
			GUILayout.Label("Yellow ghost = where lag compensation rewinds that fighter's targets\n" +
				"to under the claimed latency. White trail = its recorded position history.");
			GUILayout.EndArea();
		}

		private static GUIStyle Rich()
		{
			GUIStyle style = new GUIStyle(GUI.skin.label) { richText = true };
			return style;
		}

		// ── Accessors for the PlayMode suite ────────────────────────────────────────

		public bool Ready => ready;
		public long DamageEvents => damageEvents;
		public long HealEvents => healEvents;
		public long BuffAdds => buffAdds;
		public long Kills => kills;
		public long CastsStarted => castsStarted;
		public long ClaimsConsulted => claimsConsulted;
		public long RewindsResolved => rewindsResolved;
		public int AliveFighters
		{
			get
			{
				int alive = 0;
				foreach (Fighter fighter in fighters)
				{
					if (fighter.Alive) alive++;
				}
				return alive;
			}
		}
	}
}
