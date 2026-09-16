using System;
using System.Collections.Generic;
using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer.Spawner
{
	/// <summary>
	/// Allows a respawn only while nothing the listed spawners produced is still alive.
	/// </summary>
	/// <remarks>
	/// The boss-encounter rule: a camp does not come back while its guard stands. It replaces a
	/// condition that listed NPC scene objects directly, which could never have worked with pooled
	/// spawns — the NPCs a spawner produces do not exist in the scene file — and cannot survive the
	/// spawner bake either. Naming the spawners instead names every creature they will ever
	/// produce.
	/// </remarks>
	[Serializable]
	public class SpawnersClearedCondition : RespawnCondition
	{
		/// <summary>
		/// The spawners whose creatures must all be dead. Authoring only; baked into
		/// <see cref="SpawnerIndices"/>.
		/// </summary>
		[Tooltip("Spawners in this scene whose creatures must all be dead before this spawner may respawn.")]
		public List<ObjectSpawner> Spawners = new List<ObjectSpawner>();

		/// <summary>
		/// The same spawners as indices into the scene's spawn table.
		/// </summary>
		[HideInInspector]
		public List<int> SpawnerIndices = new List<int>();

		/// <inheritdoc />
		public override void Bake(RespawnCondition authored, Func<ObjectSpawner, int> resolveSpawnerIndex)
		{
			SpawnerIndices.Clear();

			List<ObjectSpawner> source = (authored as SpawnersClearedCondition)?.Spawners;
			if (source != null)
			{
				for (int i = 0; i < source.Count; ++i)
				{
					int index = source[i] != null ? resolveSpawnerIndex(source[i]) : -1;
					if (index >= 0 && !SpawnerIndices.Contains(index))
					{
						SpawnerIndices.Add(index);
					}
				}
			}

			// The table is loaded without the scene; a scene reference there would be a dangling id.
			Spawners.Clear();
		}

		/// <inheritdoc />
		public override bool OnCheckCondition(SpawnerRuntime spawner)
		{
			if (spawner == null || SpawnerIndices == null || SpawnerIndices.Count < 1)
			{
				return true;
			}

			for (int i = 0; i < SpawnerIndices.Count; ++i)
			{
				SpawnerRuntime other = spawner.GetSibling(SpawnerIndices[i]);
				if (other == null)
				{
					continue;
				}

				foreach (ISpawnable spawned in other.Spawned)
				{
					if (spawned is ICharacter character &&
						character.TryGet(out ICharacterDamageController damageController) &&
						damageController.IsAlive)
					{
						return false;
					}
				}
			}
			return true;
		}
	}
}
