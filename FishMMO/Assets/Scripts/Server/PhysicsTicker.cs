using FishNet;
using UnityEngine;

public class PhysicsTicker : MonoBehaviour
{
	private PhysicsScene _physicsScene;

	internal void InitializeOnce(PhysicsScene physicsScene)
	{
		if (InstanceFinder.TimeManager != null)
		{
			InstanceFinder.TimeManager.OnTick += TimeManager_OnTick;
			_physicsScene = physicsScene;
		}
	}

	void OnDestroy()
	{
		if (InstanceFinder.TimeManager != null)
		{
			InstanceFinder.TimeManager.OnTick -= TimeManager_OnTick;
		}
	}

	void TimeManager_OnTick()
	{
		_physicsScene.Simulate((float)InstanceFinder.TimeManager.TickDelta);
	}
}