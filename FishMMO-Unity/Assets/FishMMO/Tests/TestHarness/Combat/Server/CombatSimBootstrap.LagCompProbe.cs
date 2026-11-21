using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet.Managing.Timing;
using UnityEngine;

namespace FishMMO.TestHarness
{
	/// <summary>One scripted shot of <see cref="CombatSimBootstrap.RunLagCompProbe"/>.</summary>
	public sealed class LagCompShot
	{
		public string Ability;
		public string Mode;
		public int ClaimMs;
		/// <summary>How far back the real resolver rewinds for this claim (claim + server queue), in ticks.</summary>
		public double RewindTicks;
		public bool ExpectHit;
		public bool Hit;
		/// <summary>Server tick of the decisive sweep (the hit's, or the one that crossed the victim's line).</summary>
		public uint SweepTick;
		/// <summary>Victim's sideways offset from the shot line at that sweep: live, and at the pose the sweep was rewound to.</summary>
		public float LiveOffset;
		public float RewoundOffset;
		public int SweepsRecorded;
		public string Note;
		public bool Passed => Hit == ExpectHit;
	}

	public sealed class LagCompProbeResult
	{
		public string Error;
		public float VictimSpeed;
		public readonly List<LagCompShot> Shots = new List<LagCompShot>();
	}

	/// <summary>
	/// Proves, on the real server, that lag compensation decides melee and projectile hits.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A victim strafes across the shot line at a known speed, written each server tick so the history ring records
	/// it exactly. Each ability is then fired three ways in the SAME scripted world:
	/// </para>
	/// <list type="number">
	/// <item>aimed where a lagged client saw the victim, with that client's claim: the rewind must put the victim in
	/// the attack and the hit must land, though the live body is metres clear;</item>
	/// <item>the identical shot and world with no claim: the attack meets only the live body's side, and must miss;</item>
	/// <item>aimed at the live body with no claim: must hit (so a miss above is the rewind's absence, not a broken
	/// attack).</item>
	/// </list>
	/// <para>
	/// Positions at the decisive sweep are read from the real resolver and the real ring on that very tick, so the
	/// offsets reported are the ones the sweep used.
	/// </para>
	/// </remarks>
	public sealed partial class CombatSimBootstrap
	{
		/// <summary>One line on what the probe is doing (capture caption).</summary>
		public string LagCompStatus { get; private set; } = "";

		private const float ProbeVictimSpeed = 7f;
		private const float PunchLineDistance = 1.4f;
		private const float ProjectileLineDistance = 9f;

		public IEnumerator RunLagCompProbe(LagCompProbeResult result, AbilityTemplate punchTemplate, AbilityTemplate projectileTemplate,
			int claimMs)
		{
			DirectorPaused = true;
			result.VictimSpeed = ProbeVictimSpeed;
			Fighter shooter = null, victim = null;
			foreach (Fighter fighter in fighters)
			{
				if (!fighter.Alive || fighter.Npc == null) continue;
				if (shooter == null) shooter = fighter;
				else if (fighter.Team != shooter.Team) { victim = fighter; break; }
			}
			if (shooter == null || victim == null)
			{
				result.Error = "the probe needs a living fighter on each team";
				yield break;
			}
			if (punchTemplate == null || projectileTemplate == null)
			{
				result.Error = "Punch / projectile template missing";
				yield break;
			}

			TimeManager time = shooter.Npc.GetComponent<CharacterPredictionController>().TimeManager;
			Quiet(shooter, time);
			Quiet(victim, time);
			shooter.Abilities.Cancel();

			Ability punch = Teach(shooter.Abilities, punchTemplate);
			Ability projectile = Teach(shooter.Abilities, projectileTemplate);

			// The ability's object ring (private) and the recorded-sweep struct, read by reflection: test tooling only.
			FieldInfo ringField = typeof(AbilityObject).GetField("recordedSweeps", BindingFlags.Instance | BindingFlags.NonPublic);

			// Both stand on the arena; the shooter at the centre facing +x, the victim on a line across its fire.
			Vector3 origin = new Vector3(0f, shooter.Npc.Transform.position.y, 0f);
			shooter.Npc.Transform.SetPositionAndRotation(origin, Quaternion.LookRotation(Vector3.right));
			Vector3 aim = Vector3.right;

			// ── The victim's scripted motion and the per-tick trace, both driven from the server tick ──
			float dt = (float)time.TickDelta;
			float lineX = 0f;
			double zeroTick = 0d;             // the tick at which the victim crosses z = 0
			bool moving = false;
			Dictionary<uint, (float live, float rewound)> trace = new Dictionary<uint, (float, float)>();
			Dictionary<uint, (Vector3 from, Vector3 to)> sweeps = new Dictionary<uint, (Vector3, Vector3)>();
			AbilityObject tracked = null;
			Action<uint> fireAtTick = null;
			uint fireTick = 0;
			int hits = 0;
			uint hitTick = 0;

			float ZAt(uint tick) => (float)(ProbeVictimSpeed * dt * (tick - zeroTick));

			void OnTick()
			{
				uint now = LagCompensationTick.ServerTickDomain(time);
				if (moving)
				{
					Vector3 p = victim.Npc.Transform.position;
					victim.Npc.Transform.position = new Vector3(origin.x + lineX, p.y, ZAt(now));
				}
				if (fireAtTick != null && now >= fireTick)
				{
					Action<uint> fire = fireAtTick;
					fireAtTick = null;
					fire(now);
				}
				// What a sweep on this tick is rewound to: the real resolver, the real ring.
				float rewound = float.NaN;
				if (LagCompensationTick.TryResolve(shooter.Character, time, out RewindTarget target)
					&& victim.History.TryResolve(target, out CharacterPositionHistory.Snapshot snap))
				{
					rewound = snap.Position.z;
				}
				trace[now] = (victim.Npc.Transform.position.z, rewound);
				if (tracked != null && ringField?.GetValue(tracked) is Array ring)
				{
					foreach (object boxed in ring)
					{
						Type t = boxed.GetType();
						if (!(bool)t.GetField("Valid").GetValue(boxed)) continue;
						uint st = (uint)t.GetField("ServerTick").GetValue(boxed);
						sweeps[st] = ((Vector3)t.GetField("From").GetValue(boxed), (Vector3)t.GetField("To").GetValue(boxed));
					}
				}
			}

			void OnHit(ICharacter attacker, ICharacter defender, int amount, DamageAttributeTemplate template)
			{
				if (attacker == shooter.Character && defender == victim.Character)
				{
					if (hits++ == 0) hitTick = LagCompensationTick.ServerTickDomain(time);
				}
			}

			/* A rewind needs history behind it: right after boot the server tick is smaller than the claim, the resolver
			 * declines, and every schedule below would be built on a depth of zero. Wait for the ring to hold the
			 * whole window first. */
			float ringDeadline = Time.realtimeSinceStartup + 10f;
			while ((LagCompensationTick.ServerTickDomain(time) < 60u || victim.History.RecordedTicks < 10) &&
				Time.realtimeSinceStartup < ringDeadline)
			{
				yield return null;
			}

			time.OnTick += OnTick;
			ICharacterDamageController.OnDamaged += OnHit;
			uint inputTick = time.LocalTick + 3000000u;
			try
			{
				AbilityTemplate[] order = { punchTemplate, projectileTemplate };
				foreach (AbilityTemplate template in order)
				{
					bool isPunch = template == punchTemplate;
					Ability ability = isPunch ? punch : projectile;
					lineX = isPunch ? PunchLineDistance : ProjectileLineDistance;

					// The rewind depth for the lagged claim, measured once from the real resolver.
					ClaimMs = claimMs;
					RefreshClaims();
					yield return WaitTicks(time, 2);
					double lagTicks = MeasureRewindTicks(shooter, time);
					ClaimMs = 0;
					RefreshClaims();
					yield return WaitTicks(time, 2);
					double zeroClaimTicks = MeasureRewindTicks(shooter, time);
					if (lagTicks < 1d)
					{
						result.Error = $"the resolver gave no rewind for a {claimMs} ms claim (depth {lagTicks:F2} ticks)";
						yield break;
					}

					(string mode, int claim, bool aimedAtLive, bool expect)[] cases =
					{
						("aimed at the lagged view, claim " + claimMs + " ms", claimMs, false, true),
						("same shot and world, no claim", 0, false, false),
						("aimed at the live body, no claim", 0, true, true),
					};
					foreach ((string mode, int claim, bool aimedAtLive, bool expect) in cases)
					{
						ClaimMs = claim;
						RefreshClaims();
						shooter.Abilities.Cancel();
						DestroyObjects(ability);
						hits = 0;
						hitTick = 0;
						tracked = null;
						sweeps.Clear();
						trace.Clear();
						LagCompShot shot = new LagCompShot
						{
							Ability = template.Name,
							Mode = mode,
							ClaimMs = claim,
							RewindTicks = claim > 0 ? lagTicks : zeroClaimTicks,
							ExpectHit = expect,
						};
						LagCompStatus = $"{template.Name}: {mode}";

						/* The decisive moment: the punch sweeps on (or one tick after) the tick it is cast; the projectile
						 * reaches the victim's line a flight time later. The schedule puts the victim at z = 0 on the
						 * lagged claim's rewound tick for the first two cases (same world for both), on the live tick for
						 * the third. */
						uint now = LagCompensationTick.ServerTickDomain(time);
						fireTick = now + (uint)Math.Ceiling(lagTicks) + 10u;
						double contactTick = fireTick; // refined at launch for the projectile
						double schedule = aimedAtLive ? 0d : lagTicks;
						zeroTick = contactTick - schedule;
						moving = true;
						Physics.SyncTransforms();

						bool launched = false;
						string launchError = null;
						HashSet<AbilityObject> before = new HashSet<AbilityObject>(
							FindObjectsByType<AbilityObject>(FindObjectsInactive.Include, FindObjectsSortMode.None));
						fireAtTick = tick =>
						{
							/* An instant (Punch: no cast time) completes and spawns inside its first input, so "not still
							 * activating" is not a refusal: what was spawned is the test. */
							if (StartCast(shooter.Abilities, ability, ref inputTick, aim))
							{
								RunToFinish(shooter.Abilities, ref inputTick, aim, blankBeforeEach: false, ability, out _, out _, out _);
							}
							inputTick += 1000000u; // clear of the next cast's cooldown bookkeeping
							tracked = NewestRoot(ability);
							if (tracked == null)
							{
								// Ended inside its own input (an instant that hit at once): the server keeps an ended object a few
								// ticks for late claims, so it is still in the scene.
								foreach (AbilityObject candidate in FindObjectsByType<AbilityObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
								{
									if (candidate != null && !before.Contains(candidate)) { tracked = candidate; break; }
								}
							}
							launched = tracked != null || hits > 0;
							if (!launched)
							{
								launchError = $"'{ability.Name}' spawned nothing";
								return;
							}
							if (!isPunch && tracked != null)
							{
								// Contact when the projectile's leading face meets the body's side of the line.
								float bodyRadius = BodyRadius(victim);
								Vector3 half = AbilityPrefabColliderCache.ResolveShapeHalfExtents(tracked.GetComponentInChildren<Collider>(true));
								float distance = lineX + origin.x - bodyRadius - half.x - tracked.Transform.position.x;
								double flightTicks = distance / (tracked.Speed * dt);
								contactTick = tick + flightTicks;
								zeroTick = contactTick - schedule;
							}
						};

						float deadline = Time.realtimeSinceStartup + 10f;
						while (fireAtTick != null && Time.realtimeSinceStartup < deadline) yield return null;
						if (launchError != null || !launched)
						{
							shot.Note = launchError ?? "nothing was launched";
							result.Shots.Add(shot);
							continue;
						}
						// Let it play out: the punch lives a tick or two, the projectile flies past the line.
						yield return WaitTicks(time, isPunch ? 12u : 40u);
						moving = false;

						shot.Hit = hits > 0;
						shot.SweepsRecorded = sweeps.Count;
						// The decisive sweep: the hit's, else the one that crossed the line (projectile) or the only one (punch).
						uint decisive = 0;
						if (shot.Hit && sweeps.ContainsKey(hitTick)) decisive = hitTick;
						else if (shot.Hit) decisive = hitTick;
						else
						{
							float best = float.MaxValue;
							foreach (KeyValuePair<uint, (Vector3 from, Vector3 to)> s in sweeps)
							{
								float cross = isPunch ? 0f : Mathf.Min(Mathf.Abs(s.Value.from.x - (origin.x + lineX)), Mathf.Abs(s.Value.to.x - (origin.x + lineX)));
								bool spans = isPunch || (s.Value.from.x <= origin.x + lineX + 0.6f && s.Value.to.x >= origin.x + lineX - 1.6f);
								if (spans && cross < best) { best = cross; decisive = s.Key; }
							}
						}
						shot.SweepTick = decisive;
						if (decisive != 0 && trace.TryGetValue(decisive, out (float live, float rewound) at))
						{
							shot.LiveOffset = at.live;
							shot.RewoundOffset = at.rewound;
						}
						result.Shots.Add(shot);
						LagCompStatus = $"{template.Name}: {mode} → {(shot.Hit ? "HIT" : "miss")} (expected {(expect ? "hit" : "miss")})";
						Debug.Log($"[LagCompProbe] {shot.Ability} | {shot.Mode} | rewind {shot.RewindTicks:F2} ticks | sweep tick {shot.SweepTick} " +
							$"| live z {shot.LiveOffset:F2} rewound z {shot.RewoundOffset:F2} | {(shot.Hit ? "HIT" : "miss")} expected {(expect ? "hit" : "miss")} " +
							$"| sweeps {shot.SweepsRecorded}");

						yield return WaitSeconds(isPunch ? 1.3f : 2.8f); // cooldowns
					}
				}
			}
			finally
			{
				time.OnTick -= OnTick;
				ICharacterDamageController.OnDamaged -= OnHit;
				moving = false;
				DirectorPaused = false;
			}
		}

		private static Ability Teach(AbilityController abilities, AbilityTemplate template)
		{
			long id = template.ID;
			if (abilities.KnownAbilities.TryGetValue(id, out Ability known))
			{
				return known;
			}
			Ability ability = new Ability(id, template);
			abilities.LearnAbility(ability);
			return ability;
		}

		private double MeasureRewindTicks(Fighter shooter, TimeManager time)
		{
			if (!LagCompensationTick.TryResolve(shooter.Character, time, out RewindTarget target))
			{
				return 0d;
			}
			return LagCompensationTick.ServerTickDomain(time) - target.AsFractionalTick;
		}

		private static AbilityObject NewestRoot(Ability ability)
		{
			AbilityObject newest = null;
			if (ability.Objects == null) return null;
			foreach (Dictionary<int, AbilityObject> container in ability.Objects.Values)
			{
				if (container != null && container.TryGetValue(0, out AbilityObject root) && root != null && !root.IsDestroyed &&
					(newest == null || root.SpawnTick.Value > newest.SpawnTick.Value))
				{
					newest = root;
				}
			}
			return newest;
		}

		private static float BodyRadius(Fighter fighter)
		{
			Collider body = fighter.Character.Collider;
			if (body is CapsuleCollider capsule)
			{
				Vector3 scale = capsule.transform.lossyScale;
				return capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
			}
			return body != null ? Mathf.Max(body.bounds.extents.x, body.bounds.extents.z) : 0.4f;
		}

		private static IEnumerator WaitSeconds(float seconds)
		{
			float until = Time.realtimeSinceStartup + seconds;
			while (Time.realtimeSinceStartup < until) yield return null;
		}
	}
}
