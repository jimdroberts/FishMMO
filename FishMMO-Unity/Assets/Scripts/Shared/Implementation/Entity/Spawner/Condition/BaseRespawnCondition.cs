using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract base class for respawn conditions. Used to determine if an object spawner is allowed to respawn entities based on custom logic.
	/// </summary>
	public abstract class BaseRespawnCondition : MonoBehaviour
	{
		/// <summary>
		/// Checks whether the respawn condition is met for the given object spawner.
		/// </summary>
		/// <param name="spawner">The object spawner requesting the condition check.</param>
		/// <returns>True if respawn is allowed, false otherwise.</returns>
		public abstract bool OnCheckCondition(ObjectSpawner spawner);
	}
}