using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// The single <c>Update</c> that drives <see cref="ObjectSpawnerScheduler"/>.
	/// </summary>
	/// <remarks>
	/// One behaviour for the whole process, created on demand and kept across scene loads, rather
	/// than one per spawner. That is the point of the scheduler: the per-frame cost of respawning
	/// stops scaling with how many spawners exist.
	/// <para>
	/// Hidden and not saved, so it never appears in a scene file or in the hierarchy.
	/// </para>
	/// </remarks>
	[DisallowMultipleComponent]
	public sealed class ObjectSpawnerSchedulerDriver : MonoBehaviour
	{
		/// <summary>
		/// Wakes any spawner whose scheduled time has arrived.
		/// </summary>
		private void Update()
		{
			ObjectSpawnerScheduler.Tick(DateTime.UtcNow, Time.time);
		}
	}
}
