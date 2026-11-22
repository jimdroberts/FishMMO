using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Draws a beam (or a tracer) from the ability object along its heading to the first body or wall within
	/// <see cref="Range"/>, with <see cref="BeamPrefab"/> (a <see cref="LineRenderer"/> + <see cref="LightningArc"/>). Put it
	/// on a spawn event beside the hitscan that does the damage: a held beam spawns every pulse, so the line is redrawn
	/// every pulse and reads as one steady beam.
	/// </summary>
	/// <remarks>
	/// Presentation only and on every client (the spawn chain runs on observers too); it decides nothing. Its own ray is
	/// the same line the hitscan takes, from what this client sees, so it ends where the hit lands or near it.
	/// </remarks>
	[Serializable]
	public class PlayBeamFXAction : BaseAction
	{
		[Tooltip("The line drawn: a prefab with a LineRenderer and a LightningArc.")]
		public GameObject BeamPrefab;

		[Tooltip("How far the beam reaches, metres (match the hitscan's range).")]
		public float Range = 25f;

		[Tooltip("How long each drawn line lasts, seconds. A held beam wants a little more than its pulse, so the lines overlap.")]
		public float Seconds = 0.25f;

		/// <inheritdoc />
		public override bool IsPresentation => true;

		private static readonly RaycastHit[] rayHits = new RaycastHit[16];

		/// <inheritdoc />
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (!IsClientPeer(initiator, eventData) || IsReplayTick(eventData) || BeamPrefab == null ||
				!AbilityObject.TryResolveFrom(eventData, out AbilityObject abilityObject) || abilityObject.Transform == null)
			{
				return;
			}
			Vector3 from = abilityObject.Transform.position;
			Vector3 heading = abilityObject.Transform.forward;
			Vector3 to = from + heading * Range;
			UnityEngine.SceneManagement.Scene scene = abilityObject.GameObject.scene;
			PhysicsScene physics = scene.IsValid() ? scene.GetPhysicsScene() : Physics.defaultPhysicsScene;
			int count = physics.Raycast(from, heading, rayHits, Range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
			float nearest = float.MaxValue;
			for (int i = 0; i < count; ++i)
			{
				Collider hit = rayHits[i].collider;
				if (hit == null || rayHits[i].distance >= nearest)
				{
					continue;
				}
				// Not the caster's own body, and not another ability object.
				ICharacter owner = hit.GetComponentInParent<ICharacter>();
				if ((owner != null && ReferenceEquals(owner, initiator)) || hit.GetComponentInParent<AbilityObject>() != null)
				{
					continue;
				}
				nearest = rayHits[i].distance;
				to = rayHits[i].point;
			}
			Array.Clear(rayHits, 0, rayHits.Length);
			LightningArc.Spawn(BeamPrefab, from, to, scene, Seconds);
		}
	}

	/// <summary>
	/// Draws a stream from the struck target back to the initiator with <see cref="LinkPrefab"/>: what a drain looks like,
	/// health running home. Put it on the hit event that drains. Presentation only, on every client that runs the hit.
	/// </summary>
	[Serializable]
	public class PlayLinkFXAction : BaseAction
	{
		[Tooltip("The line drawn: a prefab with a LineRenderer and a LightningArc.")]
		public GameObject LinkPrefab;

		[Tooltip("Height above each character's feet the stream meets it at, metres.")]
		public float BodyHeight = 1.1f;

		[Tooltip("How long the stream lasts, seconds.")]
		public float Seconds = 0.45f;

		/// <inheritdoc />
		public override bool IsPresentation => true;

		/// <inheritdoc />
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			ICharacter target = eventData?.TargetCharacter;
			if (!IsClientPeer(initiator, eventData) || IsReplayTick(eventData) || LinkPrefab == null ||
				target == null || target.Transform == null || initiator == null || initiator.Transform == null ||
				ReferenceEquals(target, initiator))
			{
				return;
			}
			Vector3 up = Vector3.up * BodyHeight;
			LightningArc.Spawn(LinkPrefab, target.Transform.position + up, initiator.Transform.position + up, target.GameObject.scene, Seconds);
		}
	}
}
