using FishNet.Managing.Timing;
using UnityEngine;

namespace FishMMO.Server
{
	/// <summary>
	/// Handles ticking the physics simulation for a specific PhysicsScene using FishNet's TimeManager events.
	/// </summary>
	public class PhysicsTicker : MonoBehaviour
	{
		/// <summary>
		/// The physics scene to simulate.
		/// </summary>
		private PhysicsScene _physicsScene;
		/// <summary>
		/// Reference to the TimeManager that provides simulation events.
		/// </summary>
		private TimeManager timeManager;

		/// <summary>
		/// Initializes the PhysicsTicker with the given physics scene and time manager.
		/// Subscribes to the OnPrePhysicsSimulation event.
		/// </summary>
		/// <param name="physicsScene">The physics scene to simulate.</param>
		/// <param name="timeManager">The time manager providing simulation events.</param>
		internal void InitializeOnce(PhysicsScene physicsScene, TimeManager timeManager)
		{
			if (timeManager != null)
			{
				this.timeManager = timeManager;
				// Subscribe to pre-physics simulation event to manually tick the physics scene.
				this.timeManager.OnPrePhysicsSimulation += TimeManager_OnPrePhysicsSimulation;
				_physicsScene = physicsScene;
			}
		}

		/// <summary>
		/// Unity OnDestroy callback. Unsubscribes from the OnPrePhysicsSimulation event and clears references.
		/// </summary>
		void OnDestroy()
		{
			if (this.timeManager != null)
			{
				this.timeManager.OnPrePhysicsSimulation -= TimeManager_OnPrePhysicsSimulation;
				this.timeManager = null;
			}
		}

		/// <summary>
		/// Event handler called before physics simulation. Advances the physics scene simulation by deltaTime.
		/// </summary>
		/// <param name="deltaTime">The time step for the simulation.</param>
		void TimeManager_OnPrePhysicsSimulation(float deltaTime)
		{
			_physicsScene.Simulate(deltaTime);
		}
	}
}