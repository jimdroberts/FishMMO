using System.Reflection;
using FishMMO.Shared;
using NUnit.Framework;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The loading screen's content stage reads <see cref="SceneContentLoads"/>: a wave's progress must only move forward as
	/// loads join and end (the old mean of the running loads went back to 0 each time one finished), work probes keep it busy
	/// after the loads are done, and builders get the covered budget only while the overlay covers the world.
	/// </summary>
	public class SceneContentLoadsTests
	{
		[SetUp]
		public void SetUp() => Reset();

		[TearDown]
		public void TearDown()
		{
			SceneContentLoads.RemoveWork("test work");
			SceneContentLoads.RemoveWork("other work");
			Reset();
		}

		private static void Reset()
		{
			typeof(SceneContentLoads).GetMethod("ResetStatics", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
		}

		[Test]
		public void Wave_ProgressCountsEndedLoads()
		{
			SceneContentLoads.Load terrain = SceneContentLoads.Begin("terrain");
			SceneContentLoads.Load biomes = SceneContentLoads.Begin("biomes");
			Assert.AreEqual(0.0f, SceneContentLoads.Progress, 1e-6f);

			terrain.End();
			Assert.AreEqual(0.5f, SceneContentLoads.Progress, 1e-6f, "one of two ended");

			// A load joining a running wave widens it; the bar never reads less than the wave's ended share of it.
			SceneContentLoads.Load rivers = SceneContentLoads.Begin("rivers");
			Assert.AreEqual(3, SceneContentLoads.WaveBegun);
			Assert.AreEqual(1.0f / 3.0f, SceneContentLoads.Progress, 1e-6f);

			biomes.End();
			rivers.End();
			Assert.AreEqual(0, SceneContentLoads.Pending);
			Assert.AreEqual(1.0f, SceneContentLoads.Progress, 1e-6f);
		}

		[Test]
		public void Wave_StartsAgainOnceEmpty()
		{
			SceneContentLoads.Begin("terrain").End();
			SceneContentLoads.Load next = SceneContentLoads.Begin("biomes");
			Assert.AreEqual(1, SceneContentLoads.WaveBegun, "a load begun with nothing in flight is a new wave");
			Assert.AreEqual(0, SceneContentLoads.WaveEnded);
			next.End();
		}

		[Test]
		public void End_Twice_CountsOnce()
		{
			SceneContentLoads.Load a = SceneContentLoads.Begin("a");
			SceneContentLoads.Load b = SceneContentLoads.Begin("b");
			a.End();
			a.End();
			Assert.AreEqual(1, SceneContentLoads.WaveEnded);
			Assert.AreEqual(1, SceneContentLoads.Pending);
			b.End();
		}

		[Test]
		public void Work_KeepsBusyAfterLoads()
		{
			bool building = true;
			float done = 0.25f;
			SceneContentLoads.AddWork("test work", () => building, () => done);
			Assert.IsTrue(SceneContentLoads.Busy);
			Assert.IsTrue(SceneContentLoads.TryDescribe(out string hint, out float progress));
			Assert.AreEqual("Preparing test work", hint);
			Assert.AreEqual(0.25f, progress, 1e-6f);

			// Loads come first on the bar.
			SceneContentLoads.Load load = SceneContentLoads.Begin("biomes");
			Assert.IsTrue(SceneContentLoads.TryDescribe(out hint, out _));
			Assert.AreEqual("Loading biomes", hint);
			load.End();

			building = false;
			Assert.IsFalse(SceneContentLoads.Busy);
			Assert.IsFalse(SceneContentLoads.TryDescribe(out _, out _));
		}

		[Test]
		public void Work_AddedTwice_ReplacesByName()
		{
			SceneContentLoads.AddWork("test work", () => true);
			SceneContentLoads.AddWork("test work", () => false);
			Assert.IsNull(SceneContentLoads.BusyWork(), "the second add replaced the first");
		}

		[Test]
		public void Work_WithoutProgress_LeavesBar()
		{
			SceneContentLoads.AddWork("other work", () => true);
			Assert.IsTrue(SceneContentLoads.TryDescribe(out _, out float progress));
			Assert.Less(progress, 0.0f);
		}

		[Test]
		public void Budget_GrowsOnlyWhileCovered()
		{
			Assert.AreEqual(1.0, SceneContentLoads.Budget(1.0), 1e-9);
			SceneContentLoads.Covered = true;
			Assert.AreEqual(SceneContentLoads.CoveredBudgetMilliseconds, SceneContentLoads.Budget(1.0), 1e-9);
			Assert.AreEqual(150.0, SceneContentLoads.Budget(150.0), 1e-9, "a larger budget is kept");
			SceneContentLoads.Covered = false;
			Assert.AreEqual(4.0, SceneContentLoads.Budget(4.0), 1e-9);
		}
	}
}
