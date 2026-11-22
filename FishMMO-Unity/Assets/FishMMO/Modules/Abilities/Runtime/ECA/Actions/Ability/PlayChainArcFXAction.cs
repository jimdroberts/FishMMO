using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Draws a chain's lightning: from where the ability object struck, an arc to the body it struck, and from there an arc
	/// to each next body the chain would leap to (<see cref="ChainTargetSelector"/>), each the nearest within
	/// <see cref="ChainRadius"/> of the last.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Presentation only, and on every client.</b> Who a chain strikes is decided by <see cref="ChainTargetSelector"/> on
	/// the server and the caster's client; an observer selects nothing and would see the bolt strike one body and nothing
	/// else. This walks a look-alike chain locally (the same nearest-next rule, over the bodies the caster may harm) so every
	/// client draws the leaps. It decides nothing: a leap drawn to a body the server's walk did not reach does no harm.
	/// </para>
	/// <para>
	/// Put it on an OnHit event with no target selector (once per hit), beside the chain's damage event.
	/// </para>
	/// </remarks>
	[Serializable]
	public class PlayChainArcFXAction : BaseAction
	{
		[Tooltip("The arc drawn for each leap: a prefab with a LineRenderer and a LightningArc.")]
		public GameObject ArcPrefab;

		[Tooltip("Bodies in the chain, the one struck first included (match the chain's ChainLength).")]
		public int ChainLength = 4;

		[Tooltip("Furthest leap, metres (match the chain's ChainRadius).")]
		public float ChainRadius = 6f;

		[Tooltip("Height above a body's feet the arcs meet it at, metres.")]
		public float BodyHeight = 1.1f;

		/// <inheritdoc />
		public override bool IsPresentation => true;

		private static readonly Collider[] overlap = new Collider[32];
		private static readonly List<ICharacter> chain = new List<ICharacter>(8);

		/// <inheritdoc />
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (!IsClientPeer(initiator, eventData) || IsReplayTick(eventData) || ArcPrefab == null)
			{
				return;
			}
			ICharacter first = eventData?.TargetCharacter;
			if (first == null || first.Transform == null)
			{
				return;
			}
			Vector3 start = first.Transform.position + Vector3.up * BodyHeight;
			if (eventData.TryGet(out CollisionEventData collision) && collision.HasHitPoint)
			{
				start = collision.HitPoint;
			}
			UnityEngine.SceneManagement.Scene scene = first.GameObject.scene;

			chain.Clear();
			chain.Add(first);
			WalkChain(initiator, scene, chain, Mathf.Max(1, ChainLength), ChainRadius);

			Vector3 previous = start;
			for (int i = 0; i < chain.Count; ++i)
			{
				Vector3 next = chain[i].Transform.position + Vector3.up * BodyHeight;
				Arc(previous, next, scene);
				previous = next;
			}
			chain.Clear();
		}

		/// <summary>
		/// Extends <paramref name="into"/> (its first entry the body struck) by the nearest living body within
		/// <paramref name="radius"/> of the last, not already in it, not the caster, that the caster may harm.
		/// </summary>
		private static void WalkChain(ICharacter caster, UnityEngine.SceneManagement.Scene scene, List<ICharacter> into, int length, float radius)
		{
			PhysicsScene physics = scene.IsValid() ? scene.GetPhysicsScene() : Physics.defaultPhysicsScene;
			while (into.Count < length)
			{
				Vector3 from = into[into.Count - 1].Transform.position;
				int found = physics.OverlapSphere(from, radius, overlap, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
				ICharacter best = null;
				float bestDistance = float.MaxValue;
				for (int i = 0; i < found; ++i)
				{
					ICharacter candidate = overlap[i] != null ? overlap[i].GetComponentInParent<ICharacter>() : null;
					if (candidate == null || candidate.Transform == null || ReferenceEquals(candidate, caster) || into.Contains(candidate))
					{
						continue;
					}
					if (!candidate.TryGet(out ICharacterDamageController damage) || !damage.IsAlive ||
						(caster != null && !HarmRule.Allows(caster, candidate)))
					{
						continue;
					}
					float distance = (candidate.Transform.position - from).sqrMagnitude;
					if (distance < bestDistance)
					{
						bestDistance = distance;
						best = candidate;
					}
				}
				Array.Clear(overlap, 0, overlap.Length);
				if (best == null)
				{
					return;
				}
				into.Add(best);
			}
		}

		private void Arc(Vector3 from, Vector3 to, UnityEngine.SceneManagement.Scene scene)
		{
			GameObject instance = UnityEngine.Object.Instantiate(ArcPrefab, from, Quaternion.identity);
			if (scene.IsValid() && scene.isLoaded && instance.scene != scene)
			{
				UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(instance, scene);
			}
			LightningArc arc = instance.GetComponent<LightningArc>();
			if (arc != null)
			{
				arc.SetEnds(from, to);
			}
			else
			{
				UnityEngine.Object.Destroy(instance, 0.5f);
			}
		}
	}
}
