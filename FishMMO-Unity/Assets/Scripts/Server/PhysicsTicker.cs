using FishNet.Managing.Timing;
using UnityEngine;

namespace FishMMO.Server
{
	public class PhysicsTicker : MonoBehaviour
	{
		private PhysicsScene _physicsScene;
		private TimeManager timeManager;

		internal void InitializeOnce(PhysicsScene physicsScene, TimeManager timeManager)
		{
			if (timeManager != null)
			{
				this.timeManager = timeManager;
				this.timeManager.OnPrePhysicsSimulation += TimeManager_OnPrePhysicsSimulation;
				_physicsScene = physicsScene;
			}
		}

		void OnDestroy()
		{
			if (this.timeManager != null)
			{
				this.timeManager.OnPrePhysicsSimulation -= TimeManager_OnPrePhysicsSimulation;
				this.timeManager = null;
			}
		}

		void TimeManager_OnPrePhysicsSimulation(float deltaTime)
		{
			_physicsScene.Simulate(deltaTime);
		}
	}
}