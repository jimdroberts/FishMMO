using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New AI Idle State", menuName = "Character/NPC/AI/Idle State", order = 0)]
	public class IdleState : BaseAIState
	{
		[Tooltip("If max update rate is greater than the update rate it will return a random range between the two.")]
		public float MaxUpdateRate;

		public override float GetUpdateRate()
        {
			float updateRate = base.GetUpdateRate();
			if (MaxUpdateRate > updateRate)
			{
				updateRate = Random.Range(updateRate, MaxUpdateRate);
			}
            return updateRate;
        }

		public override void Enter(AIController controller)
		{
			controller.Stop();
		}

		public override void Exit(AIController controller)
		{
			controller.LookTarget = null;
			controller.Resume();
		}

		public override void UpdateState(AIController controller, float deltaTime)
		{
			if (controller.LookTarget == null ||
				Vector3.Distance(controller.transform.position, controller.LookTarget.position) > DetectionRadius * 0.5f)
			{
				controller.TransitionToRandomMovementState();
			}
		}
	}
}