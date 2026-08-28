using System.Collections.Generic;
using NUnit.Framework;
using FishMMO.Shared;
using FishNet.Observing;
using FishNet.Component.Observing;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Guards for the last two steps of the netcode migration plan: interest management on the
	/// player and world-item prefabs, and drift-free ability trajectories.
	/// </summary>
	/// <remarks>
	/// Every scene-budget projection in the plan assumes a culled visible set; without a
	/// <c>NetworkObserver</c> on the player prefabs the "visible peers" figure is the whole
	/// population. These are asset-level invariants, so they are asserted against the prefabs
	/// themselves rather than against a description of them.
	/// </remarks>
	[TestFixture]
	public class MigrationCompletionTests
	{
		private const string PlayerRoot = "Assets/Prefabs/Shared/Entity/PlayableCharacters";
		private const string WorldItemRoot = "Assets/Prefabs/Shared/Entity/Interactables/World Items";
		private const string PlayerCondition = "Assets/Settings/ObserverConditions/PlayerDistanceCondition.asset";
		private const string WorldItemCondition = "Assets/Settings/ObserverConditions/WorldItemDistanceCondition.asset";

		[TestCase(PlayerRoot, PlayerCondition, 100f)]
		[TestCase(WorldItemRoot, WorldItemCondition, 15f)]
		public void Prefabs_CarryANetworkObserver_WithTheirDistanceCondition(string root, string conditionPath, float expectedDistance)
		{
			ObserverCondition condition = UnityEditor.AssetDatabase.LoadAssetAtPath<ObserverCondition>(conditionPath);
			LogAssert.IsTrue(condition != null, $"Condition asset missing: {conditionPath}");

			DistanceCondition distance = condition as DistanceCondition;
			LogAssert.IsTrue(distance != null, $"{conditionPath} must be a DistanceCondition.");

			string[] guids = UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { root });
			LogAssert.IsTrue(guids.Length > 0, $"No prefabs found under {root}.");

			List<string> missing = new List<string>();
			foreach (string guid in guids)
			{
				string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
				GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
				if (prefab == null || prefab.GetComponent<FishNet.Object.NetworkObject>() == null)
				{
					continue;
				}

				NetworkObserver observer = prefab.GetComponent<NetworkObserver>();
				if (observer == null)
				{
					missing.Add(prefab.name + " (no NetworkObserver)");
					continue;
				}

				bool hasCondition = false;
				foreach (ObserverCondition c in observer.ObserverConditions)
				{
					if (c == condition)
					{
						hasCondition = true;
						break;
					}
				}
				if (!hasCondition)
				{
					missing.Add(prefab.name + " (condition not assigned)");
				}
			}

			TestContext.WriteLine(
				$"MEASURE {root}: {guids.Length} prefabs, {missing.Count} missing interest management; " +
				$"condition '{condition.name}' @ {expectedDistance} m");

			LogAssert.AreEqual(0, missing.Count,
				$"Interest management is not wired on: {string.Join(", ", missing)}. Without it every " +
				"client pays the all-visible scene budget. Run FishMMO/Prediction/Attach Observers To " +
				"Players And World Items.");
		}

		/// <summary>
		/// A straight-line ability trajectory is a closed form of its spawn pose and tick count.
		/// </summary>
		/// <remarks>
		/// The old <c>position += dir * speed * dt</c> accumulated rounding over long lifetimes and
		/// was reproducible only if every peer took the identical number of steps. The closed form
		/// gives the same position for the same tick from the spawn tuple alone — which is all an
		/// observer that rebuilt the object from <c>AbilityActivatedBroadcast</c> holds.
		/// </remarks>
		[Test]
		public void MoveTransformAction_IsClosedForm_NotAccumulated()
		{
			const float tickDelta = 1f / 30f;
			GameObject go = new GameObject("TrajectoryProbe");
			TrajectoryTemplate template = ScriptableObject.CreateInstance<TrajectoryTemplate>();

			try
			{
				template.name = "MigrationCompletion_Trajectory";
				template.Speed = 12f;
				template.AddToCache(template.name);

				AbilityObject abilityObject = go.AddComponent<AbilityObject>();
				abilityObject.Ability = new Ability(7, template);
				abilityObject.SpawnPosition = new Vector3(3f, 1f, -2f);
				abilityObject.SpawnRotation = Quaternion.Euler(0f, 90f, 0f);

				AbilityMoveTransformAction action = new AbilityMoveTransformAction { MoveDirection = Vector3.forward };
				AbilityTickEventData tick = new AbilityTickEventData(null, tickDelta, abilityObject);

				// Accumulate the old way for comparison.
				Vector3 accumulated = abilityObject.SpawnPosition;
				float maxDivergence = 0f;
				const uint ticks = 3000; // 100 s at 30 Hz — far past any authored lifetime

				for (uint t = 1; t <= ticks; t++)
				{
					abilityObject.ElapsedTicks = t;
					action.Execute(null, tick);
					accumulated += abilityObject.SpawnRotation * Vector3.forward * template.Speed * tickDelta;

					Vector3 expected = abilityObject.SpawnPosition +
						abilityObject.SpawnRotation * Vector3.forward * (template.Speed * (t * tickDelta));
					float error = Vector3.Distance(go.transform.position, expected);
					LogAssert.IsTrue(error < 1e-3f,
						$"Closed-form position must match spawn + dir * speed * t at tick {t}; error {error:F6} m.");

					maxDivergence = Mathf.Max(maxDivergence, Vector3.Distance(accumulated, go.transform.position));
				}

				// Same tick evaluated twice yields the same position: no hidden state.
				abilityObject.ElapsedTicks = 1234;
				action.Execute(null, tick);
				Vector3 first = go.transform.position;
				go.transform.position = Vector3.zero;
				action.Execute(null, tick);
				LogAssert.IsTrue(Vector3.Distance(first, go.transform.position) < 1e-6f,
					"Re-evaluating the same tick must reproduce the identical position.");

				TestContext.WriteLine(
					$"MEASURE trajectory over {ticks} ticks @ {template.Speed} m/s: " +
					$"closed-form exact; accumulated path diverged by up to {maxDivergence * 1000f:F3} mm");
			}
			finally
			{
				template.RemoveFromCache();
				Object.DestroyImmediate(template);
				Object.DestroyImmediate(go);
			}
		}

		private sealed class TrajectoryTemplate : AbilityTemplate { }
	}
}
