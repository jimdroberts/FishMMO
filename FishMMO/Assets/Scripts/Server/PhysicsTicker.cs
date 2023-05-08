using FishNet;
using UnityEngine;

public class PhysicsTicker : MonoBehaviour
{
	private PhysicsScene _physicsScene;

	void Awake()
	{
		if (InstanceFinder.TimeManager != null)
		{
			InstanceFinder.TimeManager.OnTick += TimeManager_OnTick;
			_physicsScene = gameObject.scene.GetPhysicsScene();
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