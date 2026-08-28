using System.Collections.Generic;
using UnityEngine;
using FishNet.Object;
using FishNet.Component.Prediction;
using System;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a networked region in the game world. Handles region hierarchy, collider setup, and triggers region actions on player entry, stay, and exit.
	/// <para>
	/// Membership is tracked locally in a <see cref="RegionMembership{T}"/> rather than trusting the
	/// NetworkCollider callbacks one-for-one: FishNet re-polls colliders during prediction reconcile
	/// replay (and we deliberately fire nothing while a character teleports), so callbacks that arrive
	/// while suppressed are recorded only and the resulting Enter/Exit is raised exactly once on the
	/// next non-reconciling post-tick via a raw-vs-effective diff. Stay never fires during replay.
	/// </para>
	/// <para>
	/// Hierarchy: a child region takes ownership of a character standing inside it. Every ancestor
	/// receives a paired Exit only if it had raised an Enter, and a parent re-Enters when the last
	/// child releases the character while it is still physically inside the parent. Parent Stay is
	/// suppressed while any descendant owns the character.
	/// </para>
	/// </summary>
	[RequireComponent(typeof(NetworkTrigger))]
	public class Region : NetworkBehaviour
	{
		/// <summary>
		/// The parent region in the hierarchy. Used for nested region logic.
		/// </summary>
		public Region Parent;

		/// <summary>
		/// The child regions nested under this region. Managed at runtime.
		/// </summary>
		[NonSerialized]
		public List<Region> Children = new List<Region>();

		/// <summary>
		/// The name of the region, taken from the GameObject's name.
		/// </summary>
		public string Name { get { return gameObject.name; } }

		/// <summary>
		/// The collider that defines the region's bounds.
		/// </summary>
		public Collider Collider;

		/// <summary>
		/// Optional terrain reference. If set, overrides collider bounds to match terrain size (requires BoxCollider).
		/// </summary>
		[Tooltip("Add a terrain if you would like the region to span the entire map. (Requires BoxCollider)")]
		public Terrain Terrain;

		/// <summary>
		/// Triggers to invoke when a player enters the region.
		/// </summary>
		public List<Trigger> OnRegionEnter = new List<Trigger>();

		/// <summary>
		/// Triggers to invoke while a player stays in the region.
		/// </summary>
		public List<Trigger> OnRegionStay = new List<Trigger>();

		/// <summary>
		/// Triggers to invoke when a player exits the region.
		/// </summary>
		public List<Trigger> OnRegionExit = new List<Trigger>();

		/// <summary>
		/// The <see cref="NetworkTrigger"/> component used to detect player entry, stay, and exit events.
		/// </summary>
		private NetworkTrigger networkTrigger;

		/// <summary>
		/// Raw-vs-effective membership bookkeeping for this region. See the class remarks.
		/// </summary>
		private readonly RegionMembership<IPlayerCharacter> membership = new RegionMembership<IPlayerCharacter>();

		private readonly List<IPlayerCharacter> flushEnters = new List<IPlayerCharacter>();
		private readonly List<IPlayerCharacter> flushExits = new List<IPlayerCharacter>();

		/// <summary>
		/// Cached predicates for <see cref="RegionMembership{T}.Flush"/> so the per-tick flush allocates nothing.
		/// </summary>
		private Func<IPlayerCharacter, bool> isTeleportingPredicate;
		private Func<IPlayerCharacter, bool> isDestroyedPredicate;
		private Func<IPlayerCharacter, bool> canEnterPredicate;

		/// <summary>
		/// True when an Enter has been raised for <paramref name="character"/> and no Exit yet.
		/// </summary>
		public bool Contains(IPlayerCharacter character) => membership.IsInside(character);

		/// <summary>
		/// Initializes the region, sets up collider, terrain bounds, and event handlers for network triggers.
		/// </summary>
		void Awake()
		{
			isTeleportingPredicate = c => c.IsTeleporting;
			isDestroyedPredicate = IsDestroyed;
			canEnterPredicate = c => !DescendantContains(c);

			// Set the region's layer to ignore raycasts.
			// GameObject.layer takes a layer index, not the LayerMask bit mask that
			// Constants.Layers.IgnoreRaycast holds — assigning the mask sets an invalid layer.
			if (Constants.Layers.Index.IgnoreRaycast >= 0)
			{
				gameObject.layer = Constants.Layers.Index.IgnoreRaycast;
			}

			// Get and configure the collider for this region.
			Collider = gameObject.GetComponent<Collider>();
			if (Collider == null)
			{
				// Not registered as a child: a region with no collider can never own a character,
				// so it must not block its parent's entry logic either.
				Log.Debug("Region", Name + " collider is null and will not function properly.");
				return;
			}
			// Ensure the collider is set as a trigger.
			Collider.isTrigger = true;

			// If terrain is assigned, override collider bounds to match terrain size (BoxCollider only).
			if (Terrain != null)
			{
				BoxCollider box = Collider as BoxCollider;
				if (box != null)
				{
					box.size = Terrain.terrainData.size;
				}
			}

			// Register this region as a child of its parent only once its collider is valid, so a
			// parent iterating Children never sees a child whose Collider is unassigned.
			if (Parent != null)
			{
				Parent.Children.Add(this);
			}

			// Set up network trigger event handlers for region entry, stay, and exit.
			networkTrigger = gameObject.GetComponent<NetworkTrigger>();
			if (networkTrigger != null)
			{
				networkTrigger.OnEnter += NetworkCollider_OnEnter;
				networkTrigger.OnStay += NetworkCollider_OnStay;
				networkTrigger.OnExit += NetworkCollider_OnExit;
			}
		}

		/// <inheritdoc />
		public override void OnStartNetwork()
		{
			base.OnStartNetwork();
			if (base.TimeManager != null)
			{
				base.TimeManager.OnPostTick += TimeManager_OnPostTick;
			}
		}

		/// <inheritdoc />
		public override void OnStopNetwork()
		{
			if (base.TimeManager != null)
			{
				base.TimeManager.OnPostTick -= TimeManager_OnPostTick;
			}
			base.OnStopNetwork();
		}

		/// <summary>
		/// A disabled region can no longer poll its collider, so every effective member receives its
		/// paired Exit now rather than being stranded "inside" forever.
		/// </summary>
		void OnDisable()
		{
			ClearMembersWithExit();
		}

		/// <summary>
		/// Cleans up event handler subscriptions and removes this region from its parent's children list.
		/// </summary>
		void OnDestroy()
		{
			ClearMembersWithExit();

			if (networkTrigger != null)
			{
				networkTrigger.OnEnter -= NetworkCollider_OnEnter;
				networkTrigger.OnStay -= NetworkCollider_OnStay;
				networkTrigger.OnExit -= NetworkCollider_OnExit;
				networkTrigger = null;
			}

			if (Parent != null)
			{
				Parent.Children.Remove(this);
			}
		}

#if UNITY_EDITOR
		/// <summary>
		/// The color used to draw the region's gizmo in the editor.
		/// </summary>
		public Color GizmoColor = Color.cyan;

		/// <summary>
		/// Draws the region's collider gizmo in the editor for visualization.
		/// </summary>
		void OnDrawGizmos()
		{
			Collider collider = gameObject.GetComponent<Collider>();
			if (collider != null)
			{
				collider.DrawGizmo(GizmoColor);
			}
		}
#endif

		#region Collider callbacks

		/// <summary>
		/// Handles a collider Enter. Recorded while reconciling/teleporting; otherwise raises Enter
		/// unless a child region owns the character, and pairs an Exit on every ancestor that had entered.
		/// </summary>
		private void NetworkCollider_OnEnter(Collider other)
		{
			IPlayerCharacter character = Resolve(other);
			if (character == null)
			{
				return;
			}
			bool suppressed = IsSuppressed(character);
			if (membership.RecordEnter(character, suppressed, !DescendantContains(character)))
			{
				Parent?.OnDescendantTookOwnership(character);
				Fire(OnRegionEnter, character);
			}
		}

		/// <summary>
		/// Handles a collider Stay. Only forwarded for effective members and never during replay/teleport.
		/// </summary>
		private void NetworkCollider_OnStay(Collider other)
		{
			IPlayerCharacter character = Resolve(other);
			if (character == null)
			{
				return;
			}
			if (membership.ShouldStay(character, IsSuppressed(character)))
			{
				Fire(OnRegionStay, character);
			}
		}

		/// <summary>
		/// Handles a collider Exit. Recorded while reconciling/teleporting; otherwise raises Exit only
		/// when an Enter was raised earlier, then offers the character back to the parent.
		/// </summary>
		private void NetworkCollider_OnExit(Collider other)
		{
			IPlayerCharacter character = Resolve(other);
			if (character == null)
			{
				return;
			}
			bool suppressed = IsSuppressed(character);
			if (membership.RecordExit(character, suppressed))
			{
				Fire(OnRegionExit, character);
				Parent?.OnDescendantReleased(character);
			}
		}

		#endregion

		#region Hierarchy

		/// <summary>
		/// A descendant raised Enter for <paramref name="character"/>: this region (and every ancestor)
		/// exits it, but only if it had actually entered — no unpaired Exit is ever produced.
		/// </summary>
		private void OnDescendantTookOwnership(IPlayerCharacter character)
		{
			if (membership.ForceExit(character))
			{
				Fire(OnRegionExit, character);
			}
			Parent?.OnDescendantTookOwnership(character);
		}

		/// <summary>
		/// A descendant raised Exit for <paramref name="character"/>: re-enter if the character is still
		/// physically inside this region and no other descendant owns it; otherwise let the next ancestor try.
		/// </summary>
		private void OnDescendantReleased(IPlayerCharacter character)
		{
			if (membership.TryEnter(character, IsSuppressed(character), !DescendantContains(character)))
			{
				Fire(OnRegionEnter, character);
				return;
			}
			if (!membership.IsRawInside(character))
			{
				Parent?.OnDescendantReleased(character);
			}
		}

		/// <summary>
		/// True when any child (recursively) physically contains the character — by its own collider
		/// report, or geometrically when its collider has not polled yet this tick. Children without a
		/// collider are skipped.
		/// </summary>
		private bool DescendantContains(IPlayerCharacter character)
		{
			if (Children == null || character == null)
			{
				return false;
			}
			for (int i = 0; i < Children.Count; ++i)
			{
				Region child = Children[i];
				if (child == null)
				{
					continue;
				}
				if (child.membership.IsRawInside(character))
				{
					return true;
				}
				if (child.Collider != null && RegionGeometry.ContainsPoint(child.Collider, CharacterPoint(character)))
				{
					return true;
				}
				if (child.DescendantContains(character))
				{
					return true;
				}
			}
			return false;
		}

		#endregion

		#region Flush

		/// <summary>
		/// Runs after every tick, once reconcile replay has finished (OnPostTick is outside the
		/// reconcile window, so <c>IsReconciling</c> is false here). Diffs raw presence against
		/// effective membership and raises whatever Enter/Exit was deferred during replay or teleport.
		/// </summary>
		private void TimeManager_OnPostTick()
		{
			if (IsReconciling)
			{
				return;
			}
			flushEnters.Clear();
			flushExits.Clear();
			membership.Flush(isTeleportingPredicate, isDestroyedPredicate, canEnterPredicate, flushEnters, flushExits);

			for (int i = 0; i < flushExits.Count; ++i)
			{
				Fire(OnRegionExit, flushExits[i]);
				Parent?.OnDescendantReleased(flushExits[i]);
			}
			for (int i = 0; i < flushEnters.Count; ++i)
			{
				Parent?.OnDescendantTookOwnership(flushEnters[i]);
				Fire(OnRegionEnter, flushEnters[i]);
			}
			flushEnters.Clear();
			flushExits.Clear();
		}

		/// <summary>
		/// Raises a final Exit for every live effective member and forgets everyone.
		/// </summary>
		private void ClearMembersWithExit()
		{
			if (membership.EffectiveCount == 0 && membership.RawCount == 0)
			{
				return;
			}
			flushExits.Clear();
			membership.Clear(isDestroyedPredicate, flushExits);
			for (int i = 0; i < flushExits.Count; ++i)
			{
				Fire(OnRegionExit, flushExits[i]);
				Parent?.OnDescendantReleased(flushExits[i]);
			}
			flushExits.Clear();
		}

		#endregion

		#region Helpers

		private bool IsReconciling
		{
			get
			{
				FishNet.Managing.Predicting.PredictionManager pm = base.PredictionManager;
				return pm != null && pm.IsReconciling;
			}
		}

		/// <summary>
		/// Events for a character are deferred while the prediction system replays or the character teleports.
		/// </summary>
		private bool IsSuppressed(IPlayerCharacter character)
		{
			return IsReconciling || character.IsTeleporting;
		}

		private static bool IsDestroyed(IPlayerCharacter character)
		{
			if (character == null)
			{
				return true;
			}
			// Unity's overloaded == reports a destroyed native object as null.
			return character is UnityEngine.Object uo && uo == null;
		}

		private static IPlayerCharacter Resolve(Collider other)
		{
			if (other == null)
			{
				return null;
			}
			return other.GetComponent<IPlayerCharacter>();
		}

		private static Vector3 CharacterPoint(IPlayerCharacter character)
		{
			if (character.Collider != null)
			{
				return character.Collider.bounds.center;
			}
			return character.Transform != null ? character.Transform.position : Vector3.zero;
		}

		/// <summary>
		/// Executes a trigger list for a character. Region events are never executed while reconciling,
		/// so <see cref="RegionEventData.IsReconciling"/> is always false here; the action-side guards
		/// remain as belt-and-braces.
		/// </summary>
		private void Fire(List<Trigger> triggers, IPlayerCharacter character)
		{
			if (triggers == null || triggers.Count == 0 || IsDestroyed(character))
			{
				return;
			}
			RegionEventData eventData = new RegionEventData(character, this, false);
			// Attach the local tick for callers that need timing context. On the server this is the
			// authoritative tick; on a client it is the client's own local (predicted) tick. Either way
			// it is NOT a replicate-domain tick, hence the non-replicate TickEventData constructor.
			if (base.TimeManager != null)
			{
				eventData.Add(new TickEventData(character, base.TimeManager.LocalTick));
			}
			for (int i = 0; i < triggers.Count; ++i)
			{
				triggers[i]?.Execute(eventData);
			}
		}

		#endregion
	}
}
