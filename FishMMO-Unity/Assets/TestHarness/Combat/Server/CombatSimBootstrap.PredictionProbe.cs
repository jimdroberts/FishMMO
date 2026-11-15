using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FishNet.Managing.Timing;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.TestHarness
{
	/// <summary>
	/// What <see cref="CombatSimBootstrap.RunPredictionProbe"/> measured. Plain fields, so a test assembly that cannot
	/// see FishMMO's internals can assert on them.
	/// </summary>
	public sealed class PredictionProbeResult
	{
		/// <summary>Why the probe could not run, or null when it ran to the end.</summary>
		public string Error;

		// ── A cast across ticks the server ran without input ──
		/// <summary>Inputs, after the one that started it, a cast took to finish with an input on every tick.</summary>
		public int InputsToFinishSteady;
		/// <summary>The same count with a blank tick before every input.</summary>
		public int InputsToFinishWithBlanks;
		public int BlankTicksRun;
		public bool CastSurvivedEveryBlankTick;
		public bool SpawnedOnABlankTick;
		public bool SpawnedOnFinish;
		public bool CancelledAfterStall;
		public bool SpawnedWhenCancelled;

		// ── Hit claims ──
		public bool ClaimGrantedWithinTolerance;
		public bool ClaimRefusedOutsideTolerance;
		public bool ClaimNotGrantedTwice;

		// ── An observed copy on the playback clock ──
		public bool ObservedBegan;
		public long ObservedTicksBeforeClock;
		public long ObservedTicksAtThreeAndAHalf;
		public bool ObservedEndWaitedForClock;
		public bool ObservedEndedOnClock;
	}

	public sealed partial class CombatSimBootstrap
	{
		/// <summary>True while a probe has the fighters to itself: the director starts no casts.</summary>
		public bool DirectorPaused;

		/// <summary>
		/// Drives one fighter's ability controller by hand, on the sim's real server, through the three behaviours no
		/// EditMode test can reach: a cast across ticks that carry no input, a hit the owner claims, and an observed copy
		/// run on the playback clock.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A single process cannot be two FishNet peers, so this is the server's half, exercised with the inputs an
		/// owner would send. The fighter's own prediction controller is taken off the tick first: nothing but this probe
		/// runs its abilities, and each call here is one replicate of the ability controller with exactly the input and
		/// state named.
		/// </para>
		/// </remarks>
		public IEnumerator RunPredictionProbe(PredictionProbeResult result)
		{
			DirectorPaused = true;
			try
			{
				Fighter shooter = null;
				Fighter victim = null;
				foreach (Fighter fighter in fighters)
				{
					if (!fighter.Alive || fighter.Npc == null)
					{
						continue;
					}
					if (shooter == null)
					{
						shooter = fighter;
					}
					else if (fighter.Team != shooter.Team)
					{
						victim = fighter;
						break;
					}
				}
				if (shooter == null || victim == null)
				{
					result.Error = "the probe needs a living fighter on each team";
					yield break;
				}

				TimeManager time = shooter.Npc.GetComponent<CharacterPredictionController>().TimeManager;
				Quiet(shooter, time);
				Quiet(victim, time);
				// Whatever the brain had already started has to be off the controller before the probe starts its own.
				shooter.Abilities.Cancel();
				yield return null;

				Ability projectile = FindProjectile(shooter.Abilities);
				if (projectile == null)
				{
					result.Error = "the shooter knows no ability that launches a moving projectile with a cast time";
					yield break;
				}

				// Up and ahead: nothing is in the sky to meet it, so only the probe decides what it hits.
				Vector3 aim = (shooter.Npc.Transform.forward + Vector3.up * 1.5f).normalized;
				// Tick numbers are the owner's own counter; far ahead of anything the sim has used, and of every cooldown.
				uint tick = time.LocalTick + 1000000u;

				// ── 1. Steady: an input every tick ──
				int objectsBefore = CountObjects(projectile);
				if (!StartCast(shooter.Abilities, projectile, ref tick, aim))
				{
					result.Error = $"'{projectile.Name}' would not start (steady run)";
					yield break;
				}
				result.InputsToFinishSteady = RunToFinish(shooter.Abilities, ref tick, aim, blankBeforeEach: false, projectile,
					out _, out _, out _);
				result.SpawnedOnFinish = CountObjects(projectile) > objectsBefore;
				DestroyObjects(projectile);

				// ── 2. A blank tick before every input: the cast must wait each one out and finish on the same input ──
				tick += 1000000u;
				if (!StartCast(shooter.Abilities, projectile, ref tick, aim))
				{
					result.Error = $"'{projectile.Name}' would not start (blank-tick run)";
					yield break;
				}
				result.InputsToFinishWithBlanks = RunToFinish(shooter.Abilities, ref tick, aim, blankBeforeEach: true, projectile,
					out result.BlankTicksRun, out result.CastSurvivedEveryBlankTick, out result.SpawnedOnABlankTick);
				DestroyObjects(projectile);

				// ── 3. Input that never comes: the cast is cancelled, and fires nothing ──
				tick += 1000000u;
				objectsBefore = CountObjects(projectile);
				if (!StartCast(shooter.Abilities, projectile, ref tick, aim))
				{
					result.Error = $"'{projectile.Name}' would not start (stall run)";
					yield break;
				}
				for (int i = 0; i <= shooter.Abilities.BlankTicksAllowedInCast; ++i)
				{
					Blank(shooter.Abilities, tick);
				}
				result.CancelledAfterStall = !shooter.Abilities.IsActivating;
				result.SpawnedWhenCancelled = CountObjects(projectile) > objectsBefore;
				shooter.Abilities.Cancel();

				// ── 4. A hit the owner claims, a hair outside the server's own sweep ──
				for (int attempt = 0; attempt < 2; ++attempt)
				{
					bool near = attempt == 0;
					tick += 1000000u;
					AbilityObject flying = null;
					yield return LaunchAndFly(shooter.Abilities, projectile, tick, aim, time, launched => flying = launched);
					tick += 1000u;
					if (flying == null || flying.IsDestroyed)
					{
						result.Error = "no projectile was flying to claim a hit for";
						yield break;
					}

					/* Beside where the projectile WILL be a few ticks on, then wait for it to get there. The check reads the
					 * victim out of the server's position history, for the view the caster claimed (the sim gives its
					 * fighters a claim), so the victim has to have stood there for those ticks already: placed on the
					 * spot and claimed in the same frame, the history still has it where it stood before. */
					const uint TicksAhead = 6u;
					uint placedAt = flying.ElapsedTicks;
					Vector3 ahead = flying.Transform.position +
						flying.Transform.forward * (flying.Speed * (float)time.TickDelta * TicksAhead);
					PlaceBeside(victim, flying, ahead, clearance: near ? 0.05f : 2f);
					float arriveBy = Time.realtimeSinceStartup + 5f;
					while (flying != null && !flying.IsDestroyed && flying.ElapsedTicks < placedAt + TicksAhead &&
						Time.realtimeSinceStartup < arriveBy)
					{
						yield return null;
					}
					if (flying == null || flying.IsDestroyed)
					{
						result.Error = "the projectile ended before it reached the victim's side (the server's own sweep must not have hit it)";
						yield break;
					}
					uint claimTick = unchecked(flying.StepInputTick + 1u);
					bool granted = flying.TryApplyClaimedHit(victim.Character, claimTick);
					if (near)
					{
						result.ClaimGrantedWithinTolerance = granted;
						// The projectile had one hit to give, and the victim joins its hit set either way.
						result.ClaimNotGrantedTwice = flying == null || flying.IsDestroyed || !flying.TryApplyClaimedHit(victim.Character, claimTick);
					}
					else
					{
						result.ClaimRefusedOutsideTolerance = !granted;
					}
					if (flying != null && !flying.IsDestroyed)
					{
						flying.DestroyAbilityObjectInternal(dispatchDestroyEvents: false);
					}
					yield return null;
				}

				// ── 5. An observed copy: it runs on the playback clock, and a reported end waits for that clock ──
				tick += 1000000u;
				AbilityObject observed = null;
				yield return LaunchAndFly(shooter.Abilities, projectile, tick, aim, time, launched => observed = launched, ticksToFly: 0);
				if (observed == null || observed.IsDestroyed)
				{
					result.Error = "no projectile was launched to observe";
					yield break;
				}

				uint spawnedOn = time.LocalTick + 5000u;
				double clock = spawnedOn - 2.5d;
				AbilityObject.PlaybackTickOverride = () => clock;
				try
				{
					// This peer is the server; an observer's copy differs from it in this one flag.
					typeof(AbilityObject).GetField("isServer", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(observed, false);
					uint already = observed.ElapsedTicks;
					result.ObservedBegan = observed.BeginObservedPlayback(spawnedOn);

					yield return WaitTicks(time, 3);
					result.ObservedTicksBeforeClock = (long)observed.ElapsedTicks - already;

					clock = spawnedOn + already + 3.5d;
					yield return WaitTicks(time, 2);
					result.ObservedTicksAtThreeAndAHalf = observed == null ? -1 : (long)observed.ElapsedTicks - already;

					if (observed != null && !observed.IsDestroyed)
					{
						observed.EndObserved(spawnedOn + already + 6u);
						yield return WaitTicks(time, 2);
						result.ObservedEndWaitedForClock = observed != null && !observed.IsDestroyed;

						clock = spawnedOn + already + 6.25d;
						yield return WaitTicks(time, 2);
						result.ObservedEndedOnClock = observed == null || observed.IsDestroyed;
					}
				}
				finally
				{
					AbilityObject.PlaybackTickOverride = null;
				}
			}
			finally
			{
				DirectorPaused = false;
			}
		}

		/// <summary>Takes a fighter off its own clocks: its prediction controller stops ticking, its brain and its legs stop.</summary>
		private static void Quiet(Fighter fighter, TimeManager time)
		{
			CharacterPredictionController prediction = fighter.Npc.GetComponent<CharacterPredictionController>();
			const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
			MethodInfo onPreTick = typeof(CharacterPredictionController).GetMethod("TimeManager_OnPreTick", Private);
			MethodInfo onTick = typeof(CharacterPredictionController).GetMethod("TimeManager_OnTick", Private);
			if (onPreTick != null)
			{
				time.OnPreTick -= (Action)Delegate.CreateDelegate(typeof(Action), prediction, onPreTick);
			}
			if (onTick != null)
			{
				time.OnTick -= (Action)Delegate.CreateDelegate(typeof(Action), prediction, onTick);
			}
			if (fighter.Ai != null)
			{
				fighter.Ai.enabled = false;
			}
			if (fighter.Agent != null)
			{
				fighter.Agent.enabled = false;
			}
		}

		/// <summary>An ability of the shooter's that launches a moving projectile toward the aim and takes time to cast: the shortest such.</summary>
		private static Ability FindProjectile(AbilityController abilities)
		{
			Ability best = null;
			foreach (Ability ability in abilities.KnownAbilities.Values)
			{
				if (ability == null || ability.Template == null || ability.Template.AbilityObjectPrefab == null ||
					ability.Template.AbilitySpawnTarget != AbilitySpawnTarget.SpawnerWithCameraRotation ||
					ability.Template.RequiresTarget || ability.Template.HitCount != 1 ||
					ability.Speed <= 0f || ability.LifeTime < 1f || ability.ActivationTime < 0.2f)
				{
					continue;
				}
				if (abilities.RequiresHeld(ability.ID))
				{
					continue;
				}
				if (best == null || ability.ActivationTime < best.ActivationTime)
				{
					best = ability;
				}
			}
			return best;
		}

		private static CharacterReplicateData Input(uint tick, long queuedAbilityID, Vector3 aim)
		{
			CharacterReplicateData input = default;
			input.ActivationFlags = 1 << (int)AbilityActivationFlags.IsActualData;
			input.QueuedAbilityID = queuedAbilityID;
			input.AimDirection = AimDirectionCompression.Quantize(aim);
			input.SetTick(tick);
			return input;
		}

		/// <summary>One replicate with an input the owner sent.</summary>
		private static void Created(AbilityController abilities, uint tick, long queuedAbilityID, Vector3 aim)
		{
			CharacterReplicateData input = Input(tick, queuedAbilityID, aim);
			abilities.OnReplicate(ref input, ReplicateState.Ticked | ReplicateState.Created, Channel.Unreliable);
		}

		/// <summary>One replicate as the server runs it with its queue empty: default data, not created.</summary>
		private static void Blank(AbilityController abilities, uint tick)
		{
			CharacterReplicateData input = default;
			input.SetTick(tick);
			abilities.OnReplicate(ref input, ReplicateState.Ticked, Channel.Unreliable);
		}

		private static bool StartCast(AbilityController abilities, Ability ability, ref uint tick, Vector3 aim)
		{
			Created(abilities, tick++, ability.ID, aim);
			return abilities.IsActivating;
		}

		/// <summary>Feeds inputs until the cast is over; returns how many it took.</summary>
		private static int RunToFinish(AbilityController abilities, ref uint tick, Vector3 aim, bool blankBeforeEach, Ability ability,
			out int blanks, out bool survivedEveryBlank, out bool spawnedOnABlank)
		{
			blanks = 0;
			survivedEveryBlank = true;
			spawnedOnABlank = false;
			int inputs = 0;
			while (abilities.IsActivating && inputs < 600)
			{
				if (blankBeforeEach)
				{
					int before = CountObjects(ability);
					// The blank carries the number of the input that is late; the input then arrives with it.
					Blank(abilities, tick);
					blanks++;
					survivedEveryBlank &= abilities.IsActivating;
					spawnedOnABlank |= CountObjects(ability) > before;
					if (!abilities.IsActivating)
					{
						break;
					}
				}
				Created(abilities, tick++, AbilityController.NO_ABILITY, aim);
				inputs++;
			}
			return inputs;
		}

		private static int CountObjects(Ability ability)
		{
			int count = 0;
			if (ability.Objects != null)
			{
				foreach (Dictionary<int, AbilityObject> container in ability.Objects.Values)
				{
					count += container != null ? container.Count : 0;
				}
			}
			return count;
		}

		private static void DestroyObjects(Ability ability)
		{
			if (ability.Objects == null)
			{
				return;
			}
			List<AbilityObject> all = new List<AbilityObject>();
			foreach (Dictionary<int, AbilityObject> container in ability.Objects.Values)
			{
				if (container != null)
				{
					all.AddRange(container.Values);
				}
			}
			foreach (AbilityObject abilityObject in all)
			{
				if (abilityObject != null && !abilityObject.IsDestroyed)
				{
					abilityObject.DestroyAbilityObjectInternal(dispatchDestroyEvents: false);
				}
			}
		}

		/// <summary>Casts the projectile by hand and lets the server tick it; hands back the object it launched.</summary>
		private static IEnumerator LaunchAndFly(AbilityController abilities, Ability ability, uint tick, Vector3 aim, TimeManager time,
			Action<AbilityObject> launched, int ticksToFly = 3)
		{
			DestroyObjects(ability);
			if (!StartCast(abilities, ability, ref tick, aim))
			{
				yield break;
			}
			RunToFinish(abilities, ref tick, aim, blankBeforeEach: false, ability, out _, out _, out _);

			AbilityObject newest = null;
			if (ability.Objects != null)
			{
				foreach (Dictionary<int, AbilityObject> container in ability.Objects.Values)
				{
					if (container != null && container.TryGetValue(0, out AbilityObject root) && root != null && !root.IsDestroyed &&
						(newest == null || root.SpawnTick.Value > newest.SpawnTick.Value))
					{
						newest = root;
					}
				}
			}
			if (newest != null && ticksToFly > 0)
			{
				uint until = newest.ElapsedTicks + (uint)ticksToFly;
				float deadline = Time.realtimeSinceStartup + 5f;
				while (newest != null && !newest.IsDestroyed && newest.ElapsedTicks < until && Time.realtimeSinceStartup < deadline)
				{
					yield return null;
				}
			}
			launched(newest);
		}

		/// <summary>
		/// Stands the victim beside the projectile's latest position, its body <paramref name="clearance"/> clear of the
		/// projectile's shape: close enough to have been drawn in its path, or plainly not.
		/// </summary>
		private static void PlaceBeside(Fighter victim, AbilityObject flying, Vector3 at, float clearance)
		{
			Vector3 side = Vector3.Cross(flying.Transform.forward, Vector3.up);
			side = side.sqrMagnitude > 1e-6f ? side.normalized : Vector3.right;

			// The victim's body as the server's check measures it (AbilityObject.ResolveVictimCapsule).
			float radius = 0.4f;
			float height = 1.8f;
			Collider body = victim.Character.Collider;
			if (body is CapsuleCollider capsule)
			{
				Vector3 scale = capsule.transform.lossyScale;
				radius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
				height = Mathf.Max(capsule.height * Mathf.Abs(scale.y), radius * 2f);
			}
			else if (body != null)
			{
				Vector3 extents = body.bounds.extents;
				radius = Mathf.Max(extents.x, extents.z);
				height = Mathf.Max(extents.y * 2f, radius * 2f);
			}
			Vector3 halfExtents = AbilityPrefabColliderCache.ResolveShapeHalfExtents(flying.GetComponentInChildren<Collider>(true));
			float reach = Mathf.Max(halfExtents.x, Mathf.Max(halfExtents.y, halfExtents.z));

			victim.Character.Transform.position = at + side * (radius + reach + clearance) - Vector3.up * (height * 0.5f);
		}

		private static IEnumerator WaitTicks(TimeManager time, uint ticks)
		{
			uint until = time.LocalTick + ticks;
			float deadline = Time.realtimeSinceStartup + 5f;
			while (time.LocalTick < until && Time.realtimeSinceStartup < deadline)
			{
				yield return null;
			}
		}
	}
}
