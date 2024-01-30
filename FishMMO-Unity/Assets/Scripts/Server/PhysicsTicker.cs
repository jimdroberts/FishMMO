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
				this.timeManager.OnTick += TimeManager_OnTick;
				_physicsScene = physicsScene;
			}
		}

		void OnDestroy()
		{
			if (this.timeManager != null)
			{
				this.timeManager.OnTick -= TimeManager_OnTick;
			}
		}

		void TimeManager_OnTick()
		{
			_physicsScene.Simulate((float)this.timeManager.TickDelta);
		}
	}
}