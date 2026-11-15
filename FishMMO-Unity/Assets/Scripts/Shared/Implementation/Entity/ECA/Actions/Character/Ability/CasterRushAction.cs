using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Rushes the caster forward: a charge. How far and how fast follow the charge stage the cast was released at
	/// (<see cref="AbilityTemplate.ChargeStages"/>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// Wire it to the ability's OnSpawn. The rush itself is the caster's movement (<see cref="KCCController.BeginMotion"/>):
	/// predicted on the owner, authoritative on the server, reconciled as motor state. The way it goes is the caster's
	/// movement heading for this tick (the replicated aim turned onto the body), which both peers hold.
	/// </para>
	/// <para>
	/// <b>Re-applied on a replayed tick</b> (<see cref="ReappliesOnReplay"/>). A correction for a tick before the cast takes the
	/// rush back with the rest of the motor state, and the replay of the cast's tick must start it again or the owner stands
	/// still for a round trip and then jumps. This is the case BaseAction's warning about teleports does not cover: the
	/// effect lives in state the correction restores.
	/// </para>
	/// <para>
	/// Players only: an NPC is moved by its NavMesh agent, not a motor, so an NPC casting this strikes from where it stands.
	/// Refused while rooted or incapacitated.
	/// </para>
	/// </remarks>
	[Serializable]
	public class CasterRushAction : BaseAction
	{
		/// <summary>Metres rushed at each charge stage, from stage 1.</summary>
		[Tooltip("Metres rushed at each charge stage, from stage 1.")]
		public float[] StageDistances = { 3f, 6f, 9f };

		/// <summary>Seconds each stage's rush takes, from stage 1 (distance ÷ seconds is its speed).</summary>
		[Tooltip("Seconds each stage's rush takes, from stage 1. Distance / seconds is its speed.")]
		public float[] StageSeconds = { 0.2f, 0.33f, 0.45f };

		/// <inheritdoc/>
		public override bool ReappliesOnReplay => true;

		/// <inheritdoc/>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			ICharacter caster = initiator ?? eventData?.Initiator;
			if (caster == null || !EcaAuthority.MayPredict(caster, eventData))
			{
				return;
			}
			if (!(caster is IPlayerCharacter player) || player.CharacterController == null)
			{
				return;
			}
			if (CharacterIncapacitation.IsRooted(caster) || CharacterIncapacitation.IsIncapacitated(caster))
			{
				return;
			}
			if (StageDistances == null || StageDistances.Length == 0 || StageSeconds == null || StageSeconds.Length == 0)
			{
				return;
			}

			byte stage = ChargeStageValue.StageOf(eventData);
			float distance = StageDistances[ChargeStageValue.IndexOf(stage, StageDistances.Length)];
			float seconds = StageSeconds[ChargeStageValue.IndexOf(stage, StageSeconds.Length)];

			KCCController controller = player.CharacterController;
			Vector3 direction = controller.VirtualCameraRotation * Vector3.forward;
			direction.y = 0f;
			if (direction.sqrMagnitude < 1e-6f)
			{
				direction = caster.Transform.forward;
			}
			controller.BeginMotion(direction, distance, seconds, TickDeltaOf(caster));
		}

		/// <summary>The caster's network tick length, seconds (30 Hz when it cannot be read, as in an edit-mode test).</summary>
		internal static float TickDeltaOf(ICharacter caster)
		{
			double delta = caster?.NetworkObject?.TimeManager != null ? caster.NetworkObject.TimeManager.TickDelta : 0d;
			return delta > 0d ? (float)delta : 1f / 30f;
		}
	}
}
