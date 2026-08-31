using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Sweeps a shield volume for ability objects in flight and destroys what it catches — the
	/// outward-looking half of a block.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why this exists alongside the gate.</b> <c>DamageMitigation.TryBlockAtVolume</c> already
	/// guarantees the OUTCOME: a projectile whose impact lands inside a raised shield is stopped and
	/// deals nothing, decided by the incoming object's own swept query, which is tunnel-proof and
	/// runs inside the caster's rewind. What it cannot do is stop something that was never going to
	/// hit you. A tower shield held out to the side should sweep an arrow out of the air whether or
	/// not that arrow was aimed at your body, and a fireball should die on the shield face rather
	/// than reaching your chest and expiring there. That is what this adds.
	/// </para>
	/// <para>
	/// <b>It cannot change whether damage lands, and that is deliberate.</b> Both halves read the
	/// same authored <see cref="ShieldVolume"/>, so anything this catches would have been gated
	/// anyway if it had gone on to strike. Its ordering is therefore free to be imperfect — see
	/// below — because the worst it can do is stop a projectile a tick later than it might have, or
	/// stop one that was going to miss.
	/// </para>
	/// <para>
	/// <b>Tick order, stated rather than relied upon.</b> Every <c>AbilityObject</c> subscribes to
	/// <c>TimeManager.OnTick</c> when it spawns, so an object already in flight ticks BEFORE a shield
	/// object spawned this tick — it moves and resolves its own sweep first. A fast projectile can
	/// therefore reach the body on the same tick this would have caught it. The gate is what makes
	/// that harmless; this is an accelerator, not the mechanism.
	/// </para>
	/// <para>
	/// <b>Wire it to the channel's OnTick.</b> A channelled block re-spawns its object every tick,
	/// so an OnSpawn event fires this once per tick for the life of the channel — which is the same
	/// cadence and the simpler authoring. Put it on OnTick instead for a shield that is one
	/// long-lived object rather than a stream of short ones.
	/// </para>
	/// </remarks>
	[Serializable]
	public class ShieldInterceptAction : BaseAction
	{
		/// <summary>
		/// The shield's dimensions, in the blocking character's own space.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Authored here as well as on the buff, on purpose.</b> This action is what a designer
		/// reaches for when the shield's REACH is a property of the ability rather than of the buff
		/// it applies — a shield bash that sweeps forward, a parry that only covers the weapon arm.
		/// Leave <see cref="ShieldVolume.Shape"/> at <see cref="ShieldShape.None"/> and this action
		/// instead sweeps every volume the character's active block buffs have raised, so the common
		/// case authors the dimensions exactly once, on the buff.
		/// </para>
		/// </remarks>
		[Tooltip("Shield dimensions in the blocker's own space. Leave Shape as None to sweep whatever volumes the character's active block buffs already define.")]
		public ShieldVolume Volume = new ShieldVolume();

		/// <summary>Layers the sweep looks on. Should be the layer ability objects live on.</summary>
		[Tooltip("Layers to sweep for incoming ability objects.")]
		public LayerMask InterceptLayers = ~0;

		/// <summary>
		/// Maximum objects to stop in one tick. Zero or less means no cap.
		/// </summary>
		/// <remarks>
		/// Matches <see cref="TargetOrdering.CappedCount"/> and every other capped path in the
		/// project. A cap here is a balance knob — how many arrows one shield can eat in a tick —
		/// rather than a performance one; the query is bounded by the shield's own size.
		/// </remarks>
		[Tooltip("Objects the shield may stop in one tick. 0 or less means no cap.")]
		[Min(0)]
		public int MaxIntercepts = 0;

		/// <summary>Reused sweep buffer, grown on demand. See <see cref="inUse"/>.</summary>
		[NonSerialized]
		private Collider[] hits;

		/// <summary>Reused list of the volumes this pass is sweeping.</summary>
		[NonSerialized]
		private List<ShieldVolume> volumes;

		/// <summary>Bodies already stopped this pass, so a multi-collider object costs one intercept.</summary>
		[NonSerialized]
		private List<GameObject> keptKeys;

		/// <summary>True while the buffers above are lent to an <see cref="Execute"/> still running.</summary>
		/// <remarks>
		/// An intercept destroys an ability object, which dispatches its OnDestroy events — arbitrary
		/// authored content that is free to reach this same serialized action instance again, since
		/// one asset serves every character that casts the ability. The same borrow-or-allocate shape
		/// <c>AbilityApplyAreaAction</c> uses, and for the same reason.
		/// </remarks>
		[NonSerialized]
		private bool inUse;

		/// <summary>
		/// Sweeps the shield and destroys the ability objects inside it.
		/// </summary>
		/// <param name="initiator">The blocking character.</param>
		/// <param name="eventData">The event the block ability dispatched this from.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			/* The server, or the client that OWNS the blocker — see EcaAuthority.MayPredict.
			 *
			 * Predicting is safe here in a way it is not for most hit resolution, because neither
			 * side of this test is interpolated: an ability object's position is a closed form of
			 * its spawn pose and elapsed ticks, identical on every peer, and the blocker's own
			 * position is the one thing its client predicts and reconciles. So the owner and the
			 * server reach the same verdict from the same numbers, and the blocker sees the arrow
			 * die on the shield immediately rather than a round trip later.
			 *
			 * A disagreement costs a visual, never damage: the server's destroy broadcast still
			 * arrives for anything it stopped, and anything it did NOT stop still resolves its own
			 * hit — where the shield gate refuses it a second time. */
			if (!EcaAuthority.MayPredict(initiator, eventData))
			{
				return;
			}

			/* Not on a replayed tick. A reconcile replays every tick since the correction, and an
			 * intercept destroys objects and fires their OnDestroy events — replaying that would
			 * play one impact per replayed tick for a single block. */
			if (IsReplayTick(eventData))
			{
				return;
			}

			Transform blocker = initiator?.Transform;
			if (blocker == null)
			{
				return;
			}

			bool ownsShared = !inUse;
			List<ShieldVolume> shieldBuffer;
			List<GameObject> keyBuffer;
			if (ownsShared)
			{
				inUse = true;
				shieldBuffer = volumes ??= new List<ShieldVolume>(2);
				keyBuffer = keptKeys ??= new List<GameObject>(8);
			}
			else
			{
				shieldBuffer = new List<ShieldVolume>(2);
				keyBuffer = new List<GameObject>(8);
			}

			try
			{
				shieldBuffer.Clear();
				keyBuffer.Clear();

				if (Volume != null && Volume.IsActive)
				{
					shieldBuffer.Add(Volume);
				}
				else
				{
					// The common case: the dimensions live on the buff, authored once.
					DamageMitigation.CollectShieldVolumes(initiator, shieldBuffer);
				}

				if (shieldBuffer.Count == 0)
				{
					return;
				}

				PhysicsScene physicsScene = initiator.GameObject.scene.GetPhysicsScene();
				int cap = MaxIntercepts > 0 ? MaxIntercepts : int.MaxValue;

				for (int v = 0; v < shieldBuffer.Count && keyBuffer.Count < cap; ++v)
				{
					SweepOne(physicsScene, blocker, initiator, shieldBuffer[v], cap, keyBuffer);
				}
			}
			finally
			{
				shieldBuffer.Clear();
				keyBuffer.Clear();
				if (ownsShared)
				{
					inUse = false;
				}
			}
		}

		/// <summary>
		/// Sweeps one volume and destroys what it catches.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>A bounding SPHERE query, narrowed by the authored shape.</b> The broadphase is asked for
		/// a sphere that fully contains the volume and every candidate is then tested through
		/// <see cref="ShieldVolume.Contains"/> — the identical local-space test the incoming
		/// projectile's own gate uses. Letting the physics overlap approximate a box or a capsule
		/// instead would give the shield two slightly different sizes depending on which side asked,
		/// and a player would find hits landing inside a shield that had just stopped one.
		/// </para>
		/// </remarks>
		private void SweepOne(PhysicsScene physicsScene, Transform blocker, ICharacter initiator,
			ShieldVolume volume, int cap, List<GameObject> keyBuffer)
		{
			Vector3 center = volume.GetWorldCenter(blocker);
			float radius = volume.GetWorldBoundingRadius(blocker);
			if (radius <= 0f)
			{
				return;
			}

			/* Re-queried until the buffer stops coming back full, through the shared helper. A full
			 * non-allocating query says nothing about what it discarded, and the ones it discarded
			 * were chosen by the broadphase — so a cap applied afterwards would be stopping an
			 * arbitrary subset of what reached the shield. */
			int count;
			while (true)
			{
				hits ??= new Collider[TargetOrdering.QueryBufferSize(MaxIntercepts)];
				count = physicsScene.OverlapSphere(center, radius, hits, InterceptLayers, QueryTriggerInteraction.Collide);
				if (!TargetOrdering.TryGrowQueryBuffer(ref hits, count))
				{
					break;
				}
			}

			for (int i = 0; i < count && keyBuffer.Count < cap; ++i)
			{
				Collider collider = hits[i];
				if (collider == null)
				{
					continue;
				}

				AbilityObject abilityObject = collider.GetComponentInParent<AbilityObject>();
				if (abilityObject == null || abilityObject.IsDestroyed)
				{
					continue;
				}

				/* Never your own, and never an ally's cast that happens to be passing. Whose
				 * projectile may be stopped is a faction question the authored Conditions on this
				 * action's trigger answer; what this refuses outright is the case that is always
				 * wrong — a shield eating the shots of the character holding it. */
				if (abilityObject.Caster == initiator)
				{
					continue;
				}

				GameObject key = abilityObject.GameObject != null ? abilityObject.GameObject : abilityObject.gameObject;
				if (TargetOrdering.ContainsBody(keyBuffer, key))
				{
					continue;
				}

				/* The authored shape, tested in the blocker's own space — the same call the incoming
				 * projectile's gate makes, so one shield is one size. */
				Vector3 localPoint = blocker.InverseTransformPoint(abilityObject.Transform != null
					? abilityObject.Transform.position
					: collider.transform.position);
				if (!volume.Contains(localPoint))
				{
					continue;
				}

				keyBuffer.Add(key);

				/* Destroyed, and its OnDestroy events run: an impact on a shield is exactly when an
				 * authored effect wants to play. Observers are told only by the server, through the
				 * reliable destroy broadcast that ends any collision — a predicting blocker has
				 * already removed its own copy and needs no message. */
				abilityObject.DestroyAbilityObjectInternal(
					dispatchDestroyEvents: true,
					notifyObservers: abilityObject.IsServer);
			}
		}
	}
}
