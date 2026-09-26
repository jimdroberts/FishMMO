using System;
using System.Collections.Generic;
using FishMMO.Server.Implementation.World.SceneServer.Spawner;
using FishMMO.Shared.Core;
using FishNet.Object;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Builds <see cref="SpawnerRuntime"/>s that can run a full respawn pass without a network stack.
	/// </summary>
	/// <remarks>
	/// The network half of a spawn is replaced through
	/// <see cref="SpawnerRuntime.NetworkSpawnOverride"/> with one that makes a
	/// <see cref="FakeSpawnable"/>, so a spawn succeeds and is tracked. Everything around it — the
	/// guards, the condition evaluation, the selection, the timer bookkeeping — is the code under
	/// test and runs for real. It used to lean on a runtime with no network manager returning
	/// from <see cref="SpawnerRuntime.SpawnObject"/> early, which spent the timer; a spawn that
	/// does not happen now keeps its timer, so that stand-in no longer looks like a success.
	/// </remarks>
	internal static class SpawnerTestKit
	{
		/// <summary>IDs for spawned fakes, clear of the small ones tests hand-pick.</summary>
		private static long nextSpawnedId = 1_000_000;

		/// <summary>
		/// A runtime with one (prefab-less) spawnable and a zero check interval, so a test tick is
		/// never swallowed by the spawner's own gate. Its spawns succeed unless
		/// <paramref name="spawn"/> says otherwise.
		/// </summary>
		/// <param name="scheduler">The scheduler the runtime reports to.</param>
		/// <param name="maxSpawnCount">The spawner's maximum.</param>
		/// <param name="tune">Adjusts the definition before the runtime is built.</param>
		/// <param name="spawn">Stands in for the network spawn; null makes a fresh <see cref="FakeSpawnable"/>.</param>
		public static SpawnerRuntime NewRuntime(SpawnerScheduler scheduler, int maxSpawnCount = 10, Action<SpawnerDefinition> tune = null, Func<SpawnableSettings, ISpawnable> spawn = null)
		{
			SpawnerDefinition definition = new SpawnerDefinition
			{
				Name = "TestSpawner",
				MaxSpawnCount = maxSpawnCount,
				Spawnables = new List<SpawnableSettings> { new ItemSpawnableSettings() },
				RespawnCheckIntervalMinimum = 0.0f,
				RespawnCheckIntervalMaximum = 0.0f,
			};
			tune?.Invoke(definition);
			SpawnerRuntime runtime = new SpawnerRuntime(definition, default, null, scheduler);
			runtime.NetworkSpawnOverride = spawn ?? (_ => new FakeSpawnable(++nextSpawnedId));
			return runtime;
		}

		/// <summary>
		/// Gives <paramref name="runtime"/> <paramref name="count"/> live objects.
		/// </summary>
		public static void FillWith(SpawnerRuntime runtime, int count)
		{
			for (int i = 0; i < count; ++i)
			{
				runtime.Track(new FakeSpawnable(i + 1), null);
			}
		}
	}

	/// <summary>
	/// A spawnable with an ID and nothing else.
	/// </summary>
	internal sealed class FakeSpawnable : ISpawnable
	{
		public FakeSpawnable(long id)
		{
			ID = id;
		}

		public ISpawnOwner Spawner { get; set; }

		public NetworkObject NetworkObject => null;

		public long ID { get; }

		public int DespawnCalls { get; private set; }

		public void Despawn()
		{
			++DespawnCalls;
			Spawner?.Despawn(this);
		}
	}

	/// <summary>
	/// A respawn condition that records how often it was asked.
	/// </summary>
	[Serializable]
	internal sealed class CountingRespawnCondition : RespawnCondition
	{
		/// <summary>What this condition answers.</summary>
		public bool Allow = true;

		/// <summary>How many times it has been asked.</summary>
		public int Calls;

		public override bool OnCheckCondition(SpawnerRuntime spawner)
		{
			++Calls;
			return Allow;
		}
	}

	/// <summary>
	/// A respawn condition that throws, standing in for anything in a pass that can.
	/// </summary>
	[Serializable]
	internal sealed class ThrowingRespawnCondition : RespawnCondition
	{
		/// <summary>How many times it has been asked.</summary>
		public int Calls;

		public override bool OnCheckCondition(SpawnerRuntime spawner)
		{
			++Calls;
			throw new InvalidOperationException("condition failed");
		}
	}

	/// <summary>
	/// A respawn condition that drops its own spawner from the schedule while it is being asked,
	/// standing in for anything a spawn or despawn callback could do mid-pass.
	/// </summary>
	[Serializable]
	internal sealed class SelfUnregisteringRespawnCondition : RespawnCondition
	{
		public override bool OnCheckCondition(SpawnerRuntime spawner)
		{
			spawner.Scheduler.Unregister(spawner);
			return true;
		}
	}
}
