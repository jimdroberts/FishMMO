using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using UnityEngine;
using System;
using KinematicCharacterController;

namespace FishMMO.Shared
{
	public class KCCPlayer : NetworkBehaviour
	{
		public KCCController CharacterController;
		public KCCCamera CharacterCamera;
		public KinematicCharacterMotor Motor;

		public Func<KCCInputReplicateData> OnHandleCharacterInput;

		private void Awake()
		{
			Motor = gameObject.GetComponent<KinematicCharacterMotor>();

			CharacterController = gameObject.GetComponent<KCCController>();
			CharacterController.Motor = Motor;
			Motor.CharacterController = CharacterController;

			KCCPlayer player = gameObject.GetComponent<KCCPlayer>();
			player.CharacterController = CharacterController;
			player.Motor = Motor;

			Rigidbody rb = GetComponent<Rigidbody>();
			if (rb != null)
			{
				rb.isKinematic = true;
			}
		}

		public override void OnStartNetwork()
		{
			base.OnStartNetwork();

			if (base.TimeManager != null)
			{
				base.TimeManager.OnTick += TimeManager_OnTick;
			}
		}

		public override void OnStopNetwork()
		{
			base.OnStopNetwork();

			if (base.TimeManager != null)
			{
				base.TimeManager.OnTick -= TimeManager_OnTick;
			}
		}

#if !UNITY_SERVER
		public override void OnStartClient()
		{
			base.OnStartClient();

			if (base.IsOwner)
			{
				Camera mc = Camera.main;
				if (mc != null)
				{
					CharacterCamera = mc.gameObject.GetComponent<KCCCamera>();
					if (CharacterCamera != null)
					{
						CharacterCamera.SetFollowTransform(CharacterController.CameraFollowPoint);
						CharacterCamera.IgnoredColliders.Clear();
						CharacterCamera.IgnoredColliders.AddRange(CharacterController.GetComponentsInChildren<Collider>());
					}
				}
			}
		}
#endif

		private void TimeManager_OnTick()
		{
			KCCInputReplicateData kCCInputReplicateData = OnHandleCharacterInput == null ? default : OnHandleCharacterInput();
			Replicate(kCCInputReplicateData);
			if (base.IsServerStarted)
			{
				Reconcile(CharacterController.GetState());
			}
		}

		[Replicate]
		private void Replicate(KCCInputReplicateData input, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
		{
			if (state == ReplicateState.Future)
				return;

			CharacterController.SetInputs(ref input);

			float deltaTime = (float)base.TimeManager.TickDelta;

			Motor.UpdatePhase1(deltaTime);
			Motor.UpdatePhase2(deltaTime);

			Motor.Transform.SetPositionAndRotation(Motor.TransientPosition, Motor.TransientRotation);
		}

		[Reconcile]
		private void Reconcile(KinematicCharacterMotorState rd, Channel channel = Channel.Unreliable)
		{
			CharacterController.ApplyState(rd);
		}

		public void UpdateCamera(float scrollInput, Vector3 lookInputVector)
		{
			CharacterCamera.UpdateWithInput((float)base.TimeManager.TickDelta, scrollInput, lookInputVector);
		}

		[ServerRpc(RunLocally = true)]
		public void SetOrientationMethod(OrientationMethod method)
		{
			CharacterController.OrientationMethod = method;
		}
	}
}