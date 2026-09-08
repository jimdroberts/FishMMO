using FishNet.Component.Transforming;
using FishNet.Component.Transforming.Beta;
using FishNet.Object;
using GameKit.Dependencies.Utilities;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Between-tick smoothing for the LOCAL PLAYER's visual, on FishNet's own
	/// <see cref="TickSmootherController"/> with the same settings a <c>KCCPlatform</c> deck is
	/// drawn with — so a rider and the deck under it trail their simulation by the same amount.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The owner's transform is written once per tick, at the end of its replicate, and nothing
	/// smoothed it between ticks: the playable prefabs have no <c>GraphicalObject</c>, so FishNet
	/// creates no <c>PredictionSmoother</c>, and KCC's own interpolation is dead code (motors are
	/// never registered with <c>KinematicCharacterSystem</c>). The moving platform's deck, on the
	/// other hand, IS smoothed — a <c>NetworkTickSmoother</c> on its Graphics child. A rider stood
	/// on a per-frame-smooth deck while stepping at 30 Hz, with the camera locked to the stepping
	/// root: the deck oscillated under its feet by one tick of travel every tick ("the platform is
	/// jittery with a player on it"). Worse, the deck's smoother ran ADAPTIVE interpolation, which
	/// grows with ping (round trip + 5 ticks at Moderate), so the visible deck trailed its collider
	/// by most of a metre at 100 ms while the rider — carried by the collider — did not. That
	/// offset points along the direction of travel, so it flipped sign at every end of the run:
	/// "it shifts you over a bit as it switches direction". Neither was a simulation defect; the
	/// server consumes one rider input per tick and pairs it with an exact tick, so a reversal is
	/// predicted correctly. Both were the presentation of a stepped rider on a smoothed deck.
	/// </para>
	/// <para>
	/// The fix is parity: the rider's mesh (and the camera follow point under it) sits under a
	/// <see cref="GraphicalRoot"/> node that this component smooths with FishNet's
	/// <see cref="UniversalTickSmoother"/> — the same class, the same flat
	/// <see cref="InterpolationTicks"/> the deck's smoother is held to
	/// (<c>PlatformRiderSmoothingTests</c> pins the scene to this constant). Same algorithm, same
	/// lag, so the rider is glued to the visible deck through a reversal, and the world scrolls
	/// per frame instead of per tick everywhere else.
	/// </para>
	/// <para>
	/// <b>Owner only.</b> An observed character's root is moved per frame by
	/// <c>NetworkTransform</c>; a tick smoother on top of that would pull the mesh back toward
	/// the previous tick's world pose every frame, fighting the interpolation it sits on. The
	/// server renders nothing. So the smoother starts when this client owns the character and
	/// stops — restoring the graphical node's rest pose, for the next pooled use — when it does
	/// not. It deliberately drives FishNet's controller from a plain MonoBehaviour rather than
	/// adding a <c>NetworkTickSmoother</c> NetworkBehaviour: the prefabs serialize FishNet's
	/// behaviour indices, and a NetworkBehaviour added by hand is a binding hazard for nothing.
	/// </para>
	/// <para>
	/// <c>CharacterHitReaction</c> keeps writing <c>MeshRoot.localPosition</c>; MeshRoot is now a
	/// child of the smoothed node, so its rest pose is still local identity and the lean composes
	/// with the smoothing instead of being overwritten by it.
	/// </para>
	/// </remarks>
	public sealed class CharacterTickSmoother : MonoBehaviour
	{
		/// <summary>
		/// Ticks the visual trails the simulation by, for the rider AND for every platform deck
		/// (the scene is held to this value). FishNet's default; two ticks tolerate a tick landing
		/// a frame late without the visual stalling, at the cost of ~66 ms of visual latency on the
		/// player's own movement.
		/// </summary>
		public const byte InterpolationTicks = 2;

		/// <summary>
		/// Per-tick displacement beyond which the visual snaps instead of sliding. Sprint moves
		/// about a quarter unit per tick; anything past this is a teleport or a scene load, and
		/// a mesh streaking across the map to catch up is not smoothing.
		/// </summary>
		public const float TeleportThreshold = 8f;

		/// <summary>
		/// The node between the character root and its mesh that is smoothed. Everything visual
		/// — the mesh, the name labels, the camera follow point — must live under it.
		/// </summary>
		[Tooltip("The node between the character root and its mesh that is smoothed. Everything visual must live under it.")]
		[SerializeField]
		private Transform graphicalRoot;

		/// <summary>The smoothed node. See <see cref="graphicalRoot"/>.</summary>
		public Transform GraphicalRoot => graphicalRoot;

		/// <summary>
		/// The settings the rider is smoothed with. Flat interpolation, never adaptive: adaptive
		/// interpolation scales the visual lag with ping, which is exactly what put the deck a
		/// metre from its collider — and a player's own character must not lag more the worse
		/// their connection is.
		/// </summary>
		public static MovementSettings RiderSettings => new MovementSettings(true)
		{
			AdaptiveInterpolationValue = AdaptiveInterpolationType.Off,
			InterpolationValue = InterpolationTicks,
			SmoothedProperties = TransformPropertiesFlag.Everything,
			SnapNonSmoothedProperties = false,
			EnableTeleport = true,
			TeleportThreshold = TeleportThreshold,
		};

#if !UNITY_SERVER
		/// <summary>FishNet's smoother driver, alive only while this client owns the character.</summary>
		private TickSmootherController controller;

		/// <summary>The graphical node's local pose before smoothing began, restored on stop.</summary>
		private Vector3 restLocalPosition;
		private Quaternion restLocalRotation;
		private Vector3 restLocalScale;
#endif

		/// <summary>True while the smoother is running.</summary>
		public bool IsSmoothing
		{
			get
			{
#if !UNITY_SERVER
				return controller != null;
#else
				return false;
#endif
			}
		}

		/// <summary>
		/// Starts smoothing when <paramref name="owner"/> is true and stops it otherwise. Safe to
		/// call repeatedly; the owner's spawn and ownership callbacks both route here.
		/// </summary>
		/// <param name="owner">Whether the local client owns the character.</param>
		/// <param name="initializer">
		/// A NetworkBehaviour on the character root, from which FishNet's smoother takes its
		/// TimeManager, its ownership and its reconcile state.
		/// </param>
		public void SetOwnerSmoothing(bool owner, NetworkBehaviour initializer)
		{
#if !UNITY_SERVER
			if (!owner || initializer == null)
			{
				Stop();
				return;
			}
			if (controller != null)
			{
				return;
			}
			if (graphicalRoot == null)
			{
				FishMMO.Logging.Log.Warning("CharacterTickSmoother",
					$"'{name}' has no graphical root; the local player will step at tick rate.");
				return;
			}
			// A NetworkBehaviour that has not been spawned has no TimeManager; the smoother
			// dereferences it in Initialize.
			if (initializer.TimeManager == null)
			{
				return;
			}

			restLocalPosition = graphicalRoot.localPosition;
			restLocalRotation = graphicalRoot.localRotation;
			restLocalScale = graphicalRoot.localScale;

			InitializationSettings settings = new InitializationSettings
			{
				TargetTransform = transform,
				DetachOnStart = false,
				AttachOnStop = false,
			};
			settings.SetNetworkedRuntimeValues(initializer, graphicalRoot);

			controller = ResettableObjectCaches<TickSmootherController>.Retrieve();
			// Spectator settings are never selected (owner only), but hand FishNet the same
			// values so nothing depends on which branch it takes.
			controller.Initialize(settings, RiderSettings, RiderSettings);
			controller.StartSmoother();
#endif
		}

		/// <summary>
		/// Stops smoothing, releases FishNet's smoother (which also removes the tracker object it
		/// created beside the graphical node) and restores the graphical node's rest pose.
		/// </summary>
		public void Stop()
		{
#if !UNITY_SERVER
			if (controller == null)
			{
				return;
			}
			controller.StopSmoother();
			controller.OnDestroy();
			ResettableObjectCaches<TickSmootherController>.Store(controller);
			controller = null;

			if (graphicalRoot != null)
			{
				graphicalRoot.SetLocalPositionAndRotation(restLocalPosition, restLocalRotation);
				graphicalRoot.localScale = restLocalScale;
			}
#endif
		}

		private void OnDestroy()
		{
			Stop();
		}
	}
}
