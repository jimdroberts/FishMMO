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
	/// A runtime with no <c>NetworkManager</c> reaches <see cref="SpawnerRuntime.SpawnObject"/>'s
	/// own guard and returns. Everything up to that point — the guards, the condition evaluation,
	/// the timer bookkeeping — is the code under test and runs for real.
	/// </remarks>
	internal static class SpawnerTestKit
	{
		/// <summary>
		/// A runtime with one (prefab-less) spawnable and a zero check interval, so a test tick is
		/// never swallowed by the spawner's own gate.
		/// </summary>
		public static SpawnerRuntime NewRuntime(SpawnerScheduler scheduler, int maxSpawnCount = 10, Action<SpawnerDefinition> tune = null)
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
			return new SpawnerRuntime(definition, default, null, scheduler);
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
