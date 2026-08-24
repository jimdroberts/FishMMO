using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Asserts the movement-state configuration invariants that decide whether an NPC can actually
	/// get where it is going.
	/// </summary>
	/// <remarks>
	/// The movement bugs found in this audit were all cases where a state reported success it had
	/// not achieved — an arrival test that also matches "no path at all", a partial path treated as
	/// a completed one. The behaviour itself needs a NavMesh to exercise, but the asset settings
	/// that bound each recovery path do not, and a zero in one of them silently disables the
	/// recovery entirely.
	/// </remarks>
	[TestFixture]
	public class AIMovementStateTests
	{
		/// <summary>Root of the AI asset tree.</summary>
		private const string AI_ROOT = "Assets/Templates/Entity/NPCs/AI";

		/// <summary>
		/// Loads every asset of a type under the AI root.
		/// </summary>
		/// <typeparam name="T">Asset type to load.</typeparam>
		/// <returns>All matching assets.</returns>
		private static List<T> LoadAll<T>() where T : ScriptableObject
		{
			List<T> results = new List<T>();

			foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { AI_ROOT }))
			{
				T asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
				if (asset != null)
				{
					results.Add(asset);
				}
			}

			return results;
		}

		// --- Pet following -------------------------------------------------------------------

		[Test]
		public void PetFollowStates_HaveAWorkingHysteresisBand()
		{
			/* Without a band the pet flickers between "close enough, stop" and "too far, move" on
			 * alternating ticks, which reads as a stutter and repaths every tick. The closing
			 * standoff must be strictly inside the follow distance for the band to exist. */
			foreach (PetIdleState follow in LoadAll<PetIdleState>())
			{
				Assert.Greater(follow.FollowDistance, 0f,
					$"'{follow.name}' has no follow distance.");
				Assert.Greater(follow.FollowHysteresis, 0f,
					$"'{follow.name}' has no hysteresis, so it will stutter at the follow boundary.");
				Assert.Less(follow.FollowHysteresis, follow.FollowDistance,
					$"'{follow.name}' closes past its own follow distance, which inverts the band.");
			}
		}

		[Test]
		public void PetFollowStates_CanEscapeBeingWedged()
		{
			/* The two escape hatches are independent and cover different failures: the distance
			 * leash catches an owner who went somewhere unreachable, and the stuck timer catches a
			 * pet jammed behind a prop five metres away that will never trip a distance check. */
			foreach (PetIdleState follow in LoadAll<PetIdleState>())
			{
				Assert.Greater(follow.TeleportDistance, follow.FollowDistance,
					$"'{follow.name}' teleports at or inside its follow distance — the pet would " +
					"warp constantly instead of walking.");

				Assert.Greater(follow.StuckTeleportSeconds, 0f,
					$"'{follow.name}' has no stuck timeout, so a pet wedged near its owner is " +
					"stuck permanently: it never exceeds the distance leash.");
			}
		}

		[Test]
		public void PetFollowStates_DoNotSweepEveryTick()
		{
			foreach (PetIdleState follow in LoadAll<PetIdleState>())
			{
				Assert.Greater(follow.AggressiveSweepRate, 0f,
					$"'{follow.name}' would run a physics overlap on every AI tick for an " +
					"aggressive pet.");
			}
		}

		// --- Bounded manoeuvres ---------------------------------------------------------------

		[Test]
		public void CombatManoeuvres_AreTimeBounded()
		{
			/* Every combat sub-state has to be able to give up. A manoeuvre whose destination
			 * becomes unreachable mid-move otherwise parks the NPC in a state that is out of
			 * combat in every way that matters while still holding threat. */
			foreach (OrbitState orbit in LoadAll<OrbitState>())
			{
				Assert.Greater(orbit.OrbitDuration, 0f,
					$"'{orbit.name}' orbits indefinitely and never rejoins the attack.");
			}

			foreach (GetBehindState flank in LoadAll<GetBehindState>())
			{
				Assert.Greater(flank.MaxManoeuvreSeconds, 0f,
					$"'{flank.name}' has no manoeuvre timeout.");
			}

			foreach (RetreatState retreat in LoadAll<RetreatState>())
			{
				Assert.Greater(retreat.MaxRetreatSeconds, 0f,
					$"'{retreat.name}' retreats indefinitely — a cornered NPC would cower forever " +
					"instead of turning and fighting.");
			}
		}

		[Test]
		public void RetreatStates_StopFurtherOutThanTheyStep()
		{
			foreach (RetreatState retreat in LoadAll<RetreatState>())
			{
				Assert.Greater(retreat.SafeDistance, retreat.RetreatDistance,
					$"'{retreat.name}' considers itself safe before completing one retreat leg, " +
					"so it disengages while still on top of its attacker.");
			}
		}

		[Test]
		public void AttackingStates_GiveUpOnUnreachableTargets()
		{
			/* A target standing somewhere the NPC cannot path to produces a partial path: the NPC
			 * walks to the closest reachable point and stops, in combat, holding threat, forever.
			 * The timeout is what breaks that. */
			foreach (BaseAttackingState state in LoadAll<BaseAttackingState>())
			{
				Assert.Greater(state.UnreachableTargetTimeout, 0f,
					$"'{state.name}' will chase an unreachable target indefinitely.");
			}
		}

		// --- Patrol and wander ----------------------------------------------------------------

		[Test]
		public void PatrolStates_BoundTheirSkipping()
		{
			/* A waypoint ring whose entries have drifted off the NavMesh would otherwise advance
			 * through every waypoint once per tick, forever, without moving. */
			foreach (PatrolState patrol in LoadAll<PatrolState>())
			{
				Assert.Greater(patrol.MaxSkippedWaypoints, 0,
					$"'{patrol.name}' would cycle unreachable waypoints indefinitely.");
				Assert.Greater(patrol.WaypointTolerance, 0f,
					$"'{patrol.name}' has a zero arrival tolerance and can never reach a waypoint.");
			}
		}

		[Test]
		public void WanderStates_HaveSomewhereToWander()
		{
			foreach (WanderState wander in LoadAll<WanderState>())
			{
				Assert.Greater(wander.WanderRadius, 0f,
					$"'{wander.name}' has a zero wander radius, so every destination is home itself.");
			}
		}

		[Test]
		public void ReturnHomeStates_HaveAnArrivalRadius()
		{
			foreach (ReturnHomeState home in LoadAll<ReturnHomeState>())
			{
				Assert.Greater(home.HomeArrivalRadius, 0f,
					$"'{home.name}' has a zero arrival radius; an NPC can never be exactly on its " +
					"home point, so it would never stop returning.");
			}
		}

		// --- Enum contracts -------------------------------------------------------------------

		[Test]
		public void MovementResult_TreatsFailedAsTheZeroValue()
		{
			/* Call sites test `!= AIMovementResult.Failed` to mean "moving". Failed being the zero
			 * value means a default-initialised result reads as failure, which is the safe
			 * direction: a caller that forgets to assign it retries instead of assuming success. */
			Assert.AreEqual(0, (int)AIMovementResult.Failed);
			Assert.AreEqual(0, (int)AIMovementProgress.Idle);
		}
	}
}
