using System;
using FishMMO.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that plays a visual effect (FX) at a determined position, typically at the point of collision or interaction.
	/// </summary>
	[Serializable]
	public class PlayFXAction : BaseAction
	{
		/// <summary>
		/// The FX prefab to play when this action is executed.
		/// </summary>
		[Tooltip("The FX prefab to play.")]
		public GameObject FXPrefab;

		/// <summary>
		/// Purely presentational: particles and nothing else.
		/// </summary>
		/// <remarks>
		/// This is the one action in the project whose whole effect is a rendered object, so it is
		/// the one action that may still run on a peer whose selector declined to resolve — see
		/// <see cref="BaseAction.IsPresentation"/>.
		/// </remarks>
		public override bool IsPresentation => true;

		/// <summary>
		/// Which of the event's several position sources an FX instance should be placed at.
		/// </summary>
		/// <remarks>
		/// Named rather than resolved inline so the precedence can be asserted as a truth table
		/// without a scene, a NetworkManager or a spawned ability object. Every branch below was a
		/// real authored wiring that placed an effect somewhere wrong (or nowhere at all).
		/// </remarks>
		internal enum FXOrigin
		{
			/// <summary>Nothing the event carries knows where to put it. Play nothing.</summary>
			None = 0,
			/// <summary>The resolved impact point of a swept or traced hit.</summary>
			CollisionPoint = 1,
			/// <summary>Whatever the event is scoped to — the victim of an area hit, the dying object, a fan-out candidate.</summary>
			EventTarget = 2,
			/// <summary>The ability object itself: a spawn, a lingering tick, a detonation with no target.</summary>
			AbilityObject = 3,
			/// <summary>The caster. Last resort for an event with no spatial content at all.</summary>
			Initiator = 4,
		}

		/// <summary>
		/// The placement rule, as a pure function of what the event was able to offer.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>The impact point wins.</b> It is the only source that knows where on a body the hit
		/// landed; everything else is an origin. A fan-out of this action over a selector therefore
		/// places every instance at the one impact point the parent hit resolved, which is the
		/// behaviour it has always had.
		/// </para>
		/// <para>
		/// <b>The event target beats the ability object</b>, so a designer who fans this out over an
		/// area selector on an OnDestroy trigger gets one effect per victim rather than four copies
		/// stacked on the corpse of the projectile. The two agree for an unforked destroy event,
		/// whose payload sets <see cref="EventData.Target"/> to the dying object on purpose.
		/// </para>
		/// <para>
		/// <b>The ability object is what closes the original defect.</b> This action used to require
		/// a <see cref="CollisionEventData"/> and give up otherwise, and <c>AbilityCollisionEventData</c>
		/// is the only ability payload that is one — so a <c>PlayFXAction</c> on an OnDestroy, OnSpawn
		/// or OnTick trigger resolved nothing, instantiated nothing and logged a warning, on every
		/// peer, every time. Three shipped abilities (Lesser Fireball, Orc Firebolt, Scroll of Flame
		/// Impact) had a dead impact effect because of it, and the warning was the hottest log line
		/// in combat. Resolving through <see cref="AbilityObject.TryResolveFrom"/> is what every
		/// other object-scoped action already does.
		/// </para>
		/// <para>
		/// <b>There is no world-origin fallback.</b> It used to end at <see cref="Vector3.zero"/>,
		/// which is not a place an authored effect ever belongs; an event that can name no position
		/// plays nothing and says so at Debug.
		/// </para>
		/// </remarks>
		/// <param name="hasCollisionPoint">True when a <see cref="CollisionEventData"/> carries a resolved impact point.</param>
		/// <param name="hasEventTarget">True when the event is scoped to a GameObject.</param>
		/// <param name="hasAbilityObject">True when an ability object is reachable from the event.</param>
		/// <param name="hasInitiator">True when the initiator has a transform.</param>
		/// <returns>The source to read the spawn position from.</returns>
		internal static FXOrigin ChooseOrigin(bool hasCollisionPoint, bool hasEventTarget, bool hasAbilityObject, bool hasInitiator)
		{
			if (hasCollisionPoint)
			{
				return FXOrigin.CollisionPoint;
			}
			if (hasEventTarget)
			{
				return FXOrigin.EventTarget;
			}
			if (hasAbilityObject)
			{
				return FXOrigin.AbilityObject;
			}
			if (hasInitiator)
			{
				return FXOrigin.Initiator;
			}
			return FXOrigin.None;
		}

		/// <summary>
		/// Resolves where this event wants an effect played, applying <see cref="ChooseOrigin"/>.
		/// </summary>
		/// <param name="initiator">The character the action is running for, or null.</param>
		/// <param name="eventData">The event being executed, or null.</param>
		/// <param name="position">The resolved world position. Only meaningful when this returns true.</param>
		/// <returns>True when a position was resolved.</returns>
		internal static bool TryResolveSpawnPosition(ICharacter initiator, EventData eventData, out Vector3 position)
		{
			position = Vector3.zero;

			CollisionEventData collision = null;
			if (eventData != null && eventData.TryGet(out CollisionEventData found))
			{
				collision = found;
			}
			GameObject targetObject = eventData?.Target;
			bool hasAbilityObject = AbilityObject.TryResolveFrom(eventData, out AbilityObject abilityObject) &&
									abilityObject.Transform != null;
			bool hasInitiator = initiator != null && initiator.Transform != null;

			/* HasHitPoint, not merely "a collision payload is present". An area effect resolves a
			 * whole overlap at once and carries no single contact, which is why the flag exists. */
			switch (ChooseOrigin(collision != null && collision.HasHitPoint, targetObject != null, hasAbilityObject, hasInitiator))
			{
				case FXOrigin.CollisionPoint:
					position = collision.HitPoint;
					return true;
				case FXOrigin.EventTarget:
					position = targetObject.transform.position;
					return true;
				case FXOrigin.AbilityObject:
					position = abilityObject.Transform.position;
					return true;
				case FXOrigin.Initiator:
					position = initiator.Transform.position;
					return true;
				default:
					return false;
			}
		}

		/// <summary>
		/// Plays the FX prefab at the collision or interaction location.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">The event data containing collision or interaction information.</param>
		/// <remarks>
		/// <para>
		/// The FX is spawned wherever <see cref="TryResolveSpawnPosition"/> lands — the impact point
		/// a <see cref="CollisionEventData"/> carries, then the event's own target, then the ability
		/// object the event belongs to, then the initiator.
		/// </para>
		/// <para>
		/// It used to read <c>Collision.contacts[0]</c>. That threw as soon as anything dispatched a
		/// hit without a Unity collision — <see cref="AbilityApplyAreaAction"/> always did, and now
		/// every ability hit does, because <see cref="AbilityObject"/> resolves them with a swept
		/// query so they can be lag compensated. The event carries the impact point directly instead.
		/// </para>
		/// <para>
		/// VFX instantiation is suppressed during prediction replay ticks to prevent visual spam.
		/// </para>
		/// <para>
		/// <b>The instance this creates is the action's responsibility.</b> It is placed in the scene
		/// the effect happened in rather than the active one, and is handed to
		/// <see cref="FXInstanceLifetime"/> so that it ends whether or not its prefab knows how to end
		/// itself. Both are what the action previously left to chance; see issue #258 for what that
		/// cost, and <see cref="ResolveSpawnScene"/> and <see cref="FXInstanceLifetime"/> for the two
		/// halves of it.
		/// </para>
		/// </remarks>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			/* Clients only. OnHit now dispatches on every peer, so without this the dedicated
			 * server instantiates a particle prefab nobody can see — and before that widening, this
			 * action ran ONLY there, which is why authored impact effects were invisible to every
			 * player. Purely presentational, so it takes the client gate and never an authority one. */
			if (!IsClientPeer(initiator, eventData))
			{
				return;
			}

			// Suppress VFX during prediction replay to prevent visual spam
			if (IsReplayTick(eventData)) return;

			if (FXPrefab == null)
			{
				return;
			}

			if (!TryResolveSpawnPosition(initiator, eventData, out Vector3 spawnPosition))
			{
				/* Debug, not Warning. The only way to get here is an event with no collision point,
				 * no target, no ability object and no initiator transform — a malformed authoring
				 * case, not the normal path. The Warning this replaced fired on the NORMAL path for
				 * every destroy, spawn and tick trigger, once per object per peer. */
				Log.Debug("PlayFXAction", "No position could be resolved from the event; nothing played.");
				return;
			}

			SpawnFX(FXPrefab, spawnPosition, ResolveSpawnScene(initiator, eventData));
		}

		/// <summary>
		/// Creates the FX instance, places it where it belongs, and makes the action answerable for it.
		/// </summary>
		/// <param name="prefab">The FX prefab to play.</param>
		/// <param name="position">Where to play it.</param>
		/// <param name="scene">The scene it belongs in, from <see cref="ResolveSpawnScene"/>.</param>
		/// <returns>The instance, already bounded by its own lifetime, or null if there was no prefab.</returns>
		/// <remarks>
		/// Separate from <see cref="Execute"/> so the two promises the action makes about the instance —
		/// the scene it lands in, and that it ends — are assertable without a peer, an event or a
		/// NetworkManager. Everything the spawn needs is passed in; nothing here reads the event.
		/// </remarks>
		internal static GameObject SpawnFX(GameObject prefab, Vector3 position, Scene scene)
		{
			if (prefab == null)
			{
				return null;
			}

			GameObject instance = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);

			/* Not the active scene, which a bare Instantiate would have used: see ResolveSpawnScene. */
			if (scene.IsValid() && scene.isLoaded && instance.scene != scene)
			{
				SceneManager.MoveGameObjectToScene(instance, scene);
			}

			/* This action spawned it, so this action is answerable for it ending. Leaving that to the
			 * prefab left every hit of the three fire abilities with a looping, stop-action None
			 * particle system in the world, forever, on every peer — issue #258. */
			FXInstanceLifetime.Attach(instance);

			return instance;
		}

		/// <summary>
		/// The scene an FX instance belongs in: wherever the effect actually happened, not wherever the
		/// client happens to have decided its active scene is.
		/// </summary>
		/// <param name="initiator">The character the action is running for, or null.</param>
		/// <param name="eventData">The event being executed, or null.</param>
		/// <returns>The scene to place the instance in.</returns>
		/// <remarks>
		/// A bare <c>Instantiate</c> places its object in the active scene. This project loads world
		/// scenes additively and never calls <c>SetActiveScene</c> itself, so the active scene is
		/// whatever the client was started in — which means an effect played in a world scene could
		/// outlive that scene being unloaded and still be on screen after a scene change (issue #269).
		/// <see cref="AbilityObject.Spawn"/> moves its instances to the caster's scene for exactly this
		/// reason, and the ability object has already made that decision here, so it is asked first.
		/// </remarks>
		internal static Scene ResolveSpawnScene(ICharacter initiator, EventData eventData)
		{
			if (AbilityObject.TryResolveFrom(eventData, out AbilityObject abilityObject) &&
				IsUsableScene(abilityObject.GameObject))
			{
				return abilityObject.GameObject.scene;
			}

			if (initiator != null && IsUsableScene(initiator.GameObject))
			{
				return initiator.GameObject.scene;
			}

			return SceneManager.GetActiveScene();
		}

		/// <summary>
		/// Whether a GameObject's scene is one an instance can be moved into.
		/// </summary>
		private static bool IsUsableScene(GameObject gameObject)
		{
			return gameObject != null && gameObject.scene.IsValid() && gameObject.scene.isLoaded;
		}
	}
}
