using System.Collections.Generic;
using System.Reflection;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Regression tests for deterministic ability object container allocation helpers.
	/// </summary>
	[TestFixture]
	public class AbilityObjectContainerIdTests
	{
		private const BindingFlags PrivateStaticFlags = BindingFlags.Static | BindingFlags.NonPublic;

		/// <summary>
		/// Verifies empty containers are treated as occupied collision slots, not same-spawn retries.
		/// </summary>
		[Test]
		public void IsSameSpawnContainer_EmptyContainer_ReturnsFalse()
		{
			Assert.IsFalse(IsSameSpawnContainer(null, 7, new PredictionTick(100u)));
			Assert.IsFalse(IsSameSpawnContainer(new Dictionary<int, AbilityObject>(), 7, new PredictionTick(100u)));
		}

		/// <summary>
		/// Verifies non-empty containers are same-spawn only when every live object matches seed and tick.
		/// </summary>
		[Test]
		public void IsSameSpawnContainer_NonEmptyContainer_RequiresMatchingSeedAndTick()
		{
			GameObject gameObject = new GameObject("AbilityObjectContainerIdTest");
			try
			{
				AbilityObject abilityObject = gameObject.AddComponent<AbilityObject>();
				abilityObject.SpawnSeed = 7;
				abilityObject.SpawnTick = new PredictionTick(100u);
				var container = new Dictionary<int, AbilityObject>
				{
					{ 1, abilityObject },
				};

				Assert.IsTrue(IsSameSpawnContainer(container, 7, new PredictionTick(100u)));
				Assert.IsFalse(IsSameSpawnContainer(container, 8, new PredictionTick(100u)));
				Assert.IsFalse(IsSameSpawnContainer(container, 7, new PredictionTick(101u)));
			}
			finally
			{
				Object.DestroyImmediate(gameObject);
			}
		}

		/// <summary>
		/// Verifies predicted-object rollback remains correct when the uint network tick wraps.
		/// </summary>
		[Test]
		public void IsSpawnTickAfter_AcrossUintWrap_UsesSignedComparison()
		{
			uint reconcileTick = uint.MaxValue - 5u;

			Assert.IsTrue(IsSpawnTickAfter(new PredictionTick(5u), reconcileTick),
				"A spawn tick just after uint wrap is newer than a reconcile tick just before wrap.");
			Assert.IsFalse(IsSpawnTickAfter(new PredictionTick(reconcileTick - 1u), reconcileTick),
				"A spawn tick before the reconcile tick must not be treated as predicted-after-reconcile.");
			Assert.IsFalse(IsSpawnTickAfter(new PredictionTick(reconcileTick), reconcileTick),
				"An object spawned exactly on the reconcile tick is confirmed and must remain.");
		}

		private static bool IsSameSpawnContainer(Dictionary<int, AbilityObject> container, int seed, PredictionTick spawnTick)
		{
			return (bool)typeof(AbilityObject)
				.GetMethod("IsSameSpawnContainer", PrivateStaticFlags)
				.Invoke(null, new object[] { container, seed, spawnTick });
		}

		private static bool IsSpawnTickAfter(PredictionTick spawnTick, uint tick)
		{
			return (bool)typeof(Ability)
				.GetMethod("IsSpawnTickAfter", PrivateStaticFlags)
				.Invoke(null, new object[] { spawnTick, tick });
		}
	}
}