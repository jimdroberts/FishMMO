using System;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Keeps an ability object in front of its caster: the striking edge of a charge, carried along by the rush rather
	/// than flying a path of its own.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Wire it to the ability's OnTick, with Speed 0 and no move action: this IS the object's motion. The object's sweep runs
	/// after the tick events, from where it was to where this put it, so everything the caster rushes through is swept and
	/// lag compensated like any projectile's path.
	/// </para>
	/// <para>
	/// With <see cref="EndWithCasterRush"/> the object ends when the caster's rush does (it struck something, or ran its
	/// length), on the peers that resolve hits; a peer that only watches lets its copy run out its lifetime, which changes
	/// nothing because it applies nothing. A caster with no motor (an NPC) never rushes, so its object simply lives out its
	/// lifetime in front of it.
	/// </para>
	/// </remarks>
	[Serializable]
	[UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "FishMMO.Shared", sourceAssembly: "FishMMO.Shared")]
	public class FollowCasterAction : BaseAction
	{
		/// <summary>Metres ahead of the caster's feet, along its facing.</summary>
		[Tooltip("Metres ahead of the caster's feet, along its facing.")]
		public float ForwardOffset = 0.9f;

		/// <summary>Metres above the caster's feet.</summary>
		[Tooltip("Metres above the caster's feet.")]
		public float HeightOffset = 0.9f;

		/// <summary>End the object once the caster's rush has ended.</summary>
		[Tooltip("End the object once the caster's rush (CasterRushAction) has ended.")]
		public bool EndWithCasterRush = true;

		/// <inheritdoc/>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (!eventData.TryGet(out AbilityTickEventData tickData) || tickData.AbilityObject == null)
			{
				Log.Warning("FollowCasterAction", "Expected AbilityTickEventData.");
				return;
			}
			AbilityObject abilityObject = tickData.AbilityObject;
			ICharacter caster = abilityObject.Caster ?? initiator;
			if (caster?.Transform == null)
			{
				return;
			}

			Transform casterTransform = caster.Transform;
			Vector3 forward = casterTransform.forward;
			forward.y = 0f;
			forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
			Transform objectTransform = abilityObject.Transform != null ? abilityObject.Transform : abilityObject.transform;
			objectTransform.SetPositionAndRotation(
				casterTransform.position + forward * ForwardOffset + Vector3.up * HeightOffset,
				Quaternion.LookRotation(forward, Vector3.up));

			/* Not on the first tick: the rush starts in the ability step of the cast's tick, which the object's own first
			 * tick may run ahead of on this peer (FishNet orders TimeManager.OnTick subscribers freely). */
			if (EndWithCasterRush && abilityObject.ElapsedTicks > 1 && abilityObject.ResolvesHitsLocally &&
				caster is IPlayerCharacter player && player.CharacterController != null && !player.CharacterController.HasMotion)
			{
				// Read by StepOnce right after the tick events: the object ends this tick, before it sweeps again.
				abilityObject.RemainingLifeTime = 0f;
			}
		}
	}
}
