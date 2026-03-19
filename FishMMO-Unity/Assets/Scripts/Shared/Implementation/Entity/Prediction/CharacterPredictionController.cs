using System;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Unified prediction controller that replaces per-subsystem [Replicate]/[Reconcile].
	/// Discovers all <see cref="IPredictableController"/> components on the same GameObject,
	/// sorts them by <see cref="IPredictableController.Order"/>, and drives them through
	/// a single FishNet Prediction V2 pipeline. This avoids the issues caused by having
	/// multiple predicted NetworkBehaviours on the same NetworkObject.
	/// </summary>
	public class CharacterPredictionController : NetworkBehaviour
	{
		private IPredictableController[] controllers = Array.Empty<IPredictableController>();

		private void Awake()
		{
			List<IPredictableController> list = new List<IPredictableController>();
			GetComponents(list);
			list.Sort((a, b) => a.Order.CompareTo(b.Order));
			controllers = list.ToArray();
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
			if (base.TimeManager != null)
			{
				base.TimeManager.OnTick -= TimeManager_OnTick;
			}

			base.OnStopNetwork();
		}

		private void TimeManager_OnTick()
		{
			CharacterReplicateData input = default;
			if (base.IsOwner)
			{
				for (int i = 0; i < controllers.Length; i++)
				{
					controllers[i].PopulateInput(ref input);
				}
			}
			Replicate(input);
			CreateReconcile();
		}

		[Replicate]
		private void Replicate(CharacterReplicateData input, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
		{
			for (int i = 0; i < controllers.Length; i++)
			{
				controllers[i].OnReplicate(ref input, state, channel);
			}
		}

		public override void CreateReconcile()
		{
			if (base.IsServerStarted)
			{
				CharacterReconcileData data = default;
				for (int i = 0; i < controllers.Length; i++)
				{
					controllers[i].OnCreateReconcile(ref data);
				}
				Reconcile(data);
			}
		}

		[Reconcile]
		private void Reconcile(CharacterReconcileData rd, Channel channel = Channel.Unreliable)
		{
			for (int i = 0; i < controllers.Length; i++)
			{
				controllers[i].OnReconcile(rd, channel);
			}
		}
	}
}