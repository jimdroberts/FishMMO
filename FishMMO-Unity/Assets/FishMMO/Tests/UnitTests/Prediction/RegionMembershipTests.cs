using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Covers the region entry/exit decision logic that <see cref="FishMMO.Shared.Region"/> delegates
	/// to <see cref="RegionMembership{T}"/>, <see cref="RegionGeometry"/> and
	/// <see cref="RegionActionGate"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why the logic lives outside Region at all.</b> Region is a <c>NetworkBehaviour</c> whose
	/// callbacks arrive from FishNet's <c>NetworkCollider</c>. That collider re-polls during
	/// prediction reconcile replay, and Region deliberately fires nothing while replaying or while
	/// a character teleports. The old code simply early-returned on those callbacks, so a crossing
	/// that happened to land inside a replay window was lost forever — the character was physically
	/// in the region and the region never knew. Splitting "what the collider says" (raw) from "what
	/// we have announced" (effective) turns that into a diff that can be replayed once, on the next
	/// non-reconciling tick, and makes the whole thing testable without a live NetworkManager.
	/// </para>
	/// <para>
	/// <see cref="Hierarchy"/> below mirrors Region's parent/child protocol call-for-call so these
	/// tests exercise the real ordering (who takes ownership, who gets the paired exit, who is
	/// allowed to re-enter) rather than a simplified sketch of it.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class RegionMembershipTests
	{
		#region Test doubles

		/// <summary>A character key. Reference identity is all the membership set needs.</summary>
		private sealed class Actor
		{
			public readonly string Name;
			public bool IsTeleporting;
			public bool Destroyed;

			public Actor(string name)
			{
				Name = name;
			}

			public override string ToString() => Name;
		}

		private enum RegionEvent
		{
			Enter,
			Stay,
			Exit,
		}

		/// <summary>
		/// A stand-in for <see cref="FishMMO.Shared.Region"/> that drives the same
		/// <see cref="RegionMembership{T}"/> calls in the same order, minus the networking.
		/// </summary>
		private sealed class Node
		{
			public readonly string Name;
			public readonly Hierarchy Owner;
			public Node Parent;
			public readonly List<Node> Children = new List<Node>();

			/// <summary>
			/// Geometric containment for this node. Null models a region whose Collider was never
			/// assigned (Awake bails before assigning it) — the case that used to NRE the parent's
			/// containment loop.
			/// </summary>
			public Func<Actor, bool> Contains;

			public readonly RegionMembership<Actor> Membership = new RegionMembership<Actor>();

			private readonly List<Actor> flushEnters = new List<Actor>();
			private readonly List<Actor> flushExits = new List<Actor>();

			public Node(Hierarchy owner, string name, Node parent, Func<Actor, bool> contains)
			{
				Owner = owner;
				Name = name;
				Contains = contains;
				Parent = parent;
				parent?.Children.Add(this);
			}

			private bool IsSuppressed(Actor a) => Owner.IsReconciling || a.IsTeleporting;

			/// <summary>Mirrors <c>Region.NetworkCollider_OnEnter</c>.</summary>
			public void ColliderEnter(Actor a)
			{
				if (Membership.RecordEnter(a, IsSuppressed(a), !DescendantContains(a)))
				{
					Parent?.OnDescendantTookOwnership(a);
					Owner.Raise(this, RegionEvent.Enter, a);
				}
			}

			/// <summary>Mirrors <c>Region.NetworkCollider_OnStay</c>.</summary>
			public void ColliderStay(Actor a)
			{
				if (Membership.ShouldStay(a, IsSuppressed(a)))
				{
					Owner.Raise(this, RegionEvent.Stay, a);
				}
			}

			/// <summary>Mirrors <c>Region.NetworkCollider_OnExit</c>.</summary>
			public void ColliderExit(Actor a)
			{
				if (Membership.RecordExit(a, IsSuppressed(a)))
				{
					Owner.Raise(this, RegionEvent.Exit, a);
					Parent?.OnDescendantReleased(a);
				}
			}

			/// <summary>Mirrors <c>Region.OnDescendantTookOwnership</c>.</summary>
			public void OnDescendantTookOwnership(Actor a)
			{
				if (Membership.ForceExit(a))
				{
					Owner.Raise(this, RegionEvent.Exit, a);
				}
				Parent?.OnDescendantTookOwnership(a);
			}

			/// <summary>Mirrors <c>Region.OnDescendantReleased</c>.</summary>
			public void OnDescendantReleased(Actor a)
			{
				if (Membership.TryEnter(a, IsSuppressed(a), !DescendantContains(a)))
				{
					Owner.Raise(this, RegionEvent.Enter, a);
					return;
				}
				if (!Membership.IsRawInside(a))
				{
					Parent?.OnDescendantReleased(a);
				}
			}

			/// <summary>Mirrors <c>Region.DescendantContains</c>, null-collider skip included.</summary>
			public bool DescendantContains(Actor a)
			{
				if (a == null)
				{
					return false;
				}
				for (int i = 0; i < Children.Count; ++i)
				{
					Node child = Children[i];
					if (child == null)
					{
						continue;
					}
					if (child.Membership.IsRawInside(a))
					{
						return true;
					}
					// Region skips children whose Collider is still null instead of dereferencing it.
					if (child.Contains != null && child.Contains(a))
					{
						return true;
					}
					if (child.DescendantContains(a))
					{
						return true;
					}
				}
				return false;
			}

			/// <summary>Mirrors <c>Region.TimeManager_OnPostTick</c>.</summary>
			public void PostTick()
			{
				if (Owner.IsReconciling)
				{
					return;
				}
				flushEnters.Clear();
				flushExits.Clear();
				Membership.Flush(a => a.IsTeleporting, a => a.Destroyed, a => !DescendantContains(a), flushEnters, flushExits);

				for (int i = 0; i < flushExits.Count; ++i)
				{
					Owner.Raise(this, RegionEvent.Exit, flushExits[i]);
					Parent?.OnDescendantReleased(flushExits[i]);
				}
				for (int i = 0; i < flushEnters.Count; ++i)
				{
					Parent?.OnDescendantTookOwnership(flushEnters[i]);
					Owner.Raise(this, RegionEvent.Enter, flushEnters[i]);
				}
				flushEnters.Clear();
				flushExits.Clear();
			}

			/// <summary>Mirrors <c>Region.ClearMembersWithExit</c> (OnDisable / OnDestroy).</summary>
			public void Teardown()
			{
				flushExits.Clear();
				Membership.Clear(a => a.Destroyed, flushExits);
				for (int i = 0; i < flushExits.Count; ++i)
				{
					Owner.Raise(this, RegionEvent.Exit, flushExits[i]);
					Parent?.OnDescendantReleased(flushExits[i]);
				}
				flushExits.Clear();
			}
		}

		/// <summary>Owns the shared reconcile flag and records every event raised, in order.</summary>
		private sealed class Hierarchy
		{
			public bool IsReconciling;
			public readonly List<string> Log = new List<string>();

			public void Raise(Node node, RegionEvent e, Actor a)
			{
				Log.Add($"{node.Name}:{e}:{a.Name}");
			}

			public Node Region(string name, Node parent = null, Func<Actor, bool> contains = null)
			{
				return new Node(this, name, parent, contains);
			}

			public string Dump() => string.Join(" | ", Log);
		}

		#endregion

		#region Replay and teleport

		/// <summary>
		/// The bug this whole design exists for: a character crosses into a region during reconcile
		/// replay. The collider callback lands while <c>IsReconciling</c> is true, so nothing may
		/// fire yet — but the crossing must not evaporate. The first non-reconciling post-tick
		/// raises it exactly once.
		/// </summary>
		[Test]
		public void EnterDuringReplay_IsRaisedOnceOnTheNextNonReconcilingTick()
		{
			Hierarchy h = new Hierarchy();
			Node region = h.Region("Region");
			Actor a = new Actor("Player");

			h.IsReconciling = true;
			region.ColliderEnter(a);
			region.PostTick();
			LogAssert.AreEqual(0, h.Log.Count, "Nothing may fire while the prediction system is replaying. Got: " + h.Dump());
			LogAssert.IsFalse(region.Membership.IsInside(a), "The region must not consider a replay-time crossing announced yet.");

			h.IsReconciling = false;
			region.PostTick();
			LogAssert.AreEqual("Region:Enter:Player", h.Dump(), "The deferred enter must be raised on the first clean tick.");

			region.PostTick();
			region.PostTick();
			LogAssert.AreEqual("Region:Enter:Player", h.Dump(), "The deferred enter must be raised exactly once, not every tick.");
		}

		/// <summary>
		/// The mirror case: the exit callback arrives during replay. Without the raw/effective diff
		/// the character stays "inside" a region it has physically left, keeping region buffs alive.
		/// </summary>
		[Test]
		public void ExitDuringReplay_IsRaisedOnTheNextNonReconcilingTick()
		{
			Hierarchy h = new Hierarchy();
			Node region = h.Region("Region");
			Actor a = new Actor("Player");

			region.ColliderEnter(a);
			LogAssert.AreEqual("Region:Enter:Player", h.Dump());

			h.IsReconciling = true;
			region.ColliderExit(a);
			region.PostTick();
			LogAssert.AreEqual("Region:Enter:Player", h.Dump(), "No exit may fire mid-replay. Got: " + h.Dump());

			h.IsReconciling = false;
			region.PostTick();
			LogAssert.AreEqual("Region:Enter:Player | Region:Exit:Player", h.Dump());
			LogAssert.IsFalse(region.Membership.IsInside(a));
		}

		/// <summary>
		/// A whole enter+exit pair swallowed by one replay window collapses to nothing: the diff
		/// compares end states, so a character that left as it arrived produces no spurious events.
		/// </summary>
		[Test]
		public void EnterAndExitWithinTheSameReplayWindow_ProducesNoEvents()
		{
			Hierarchy h = new Hierarchy();
			Node region = h.Region("Region");
			Actor a = new Actor("Player");

			h.IsReconciling = true;
			region.ColliderEnter(a);
			region.ColliderExit(a);
			h.IsReconciling = false;
			region.PostTick();

			LogAssert.AreEqual(0, h.Log.Count, "A crossing that undid itself during replay must not surface. Got: " + h.Dump());
		}

		/// <summary>
		/// Teleporting suppresses events the same way replay does, but a teleport can span many
		/// ticks. The deferred crossing must wait for the teleport to finish, then fire once —
		/// the old code dropped it outright and never revisited it.
		/// </summary>
		[Test]
		public void EnterWhileTeleporting_IsDeferredUntilTheTeleportEnds()
		{
			Hierarchy h = new Hierarchy();
			Node region = h.Region("Region");
			Actor a = new Actor("Player") { IsTeleporting = true };

			region.ColliderEnter(a);
			region.PostTick();
			region.PostTick();
			LogAssert.AreEqual(0, h.Log.Count, "A teleporting character must not raise region events. Got: " + h.Dump());

			a.IsTeleporting = false;
			region.PostTick();
			LogAssert.AreEqual("Region:Enter:Player", h.Dump(), "The deferred enter must land once the teleport completes.");
		}

		/// <summary>
		/// A character teleported out of a region it was inside gets its paired exit once the
		/// teleport finishes, so region buffs/fog do not persist across a long-range jump.
		/// </summary>
		[Test]
		public void ExitWhileTeleporting_IsDeferredUntilTheTeleportEnds()
		{
			Hierarchy h = new Hierarchy();
			Node region = h.Region("Region");
			Actor a = new Actor("Player");

			region.ColliderEnter(a);
			a.IsTeleporting = true;
			region.ColliderExit(a);
			region.PostTick();
			LogAssert.AreEqual("Region:Enter:Player", h.Dump(), "No exit while teleporting. Got: " + h.Dump());

			a.IsTeleporting = false;
			region.PostTick();
			LogAssert.AreEqual("Region:Enter:Player | Region:Exit:Player", h.Dump());
		}

		/// <summary>
		/// Stay is a per-tick heartbeat; replaying it would apply a region's periodic effect several
		/// times for one real tick. It must never fire while suppressed.
		/// </summary>
		[Test]
		public void Stay_NeverFiresWhileReplayingOrTeleporting()
		{
			Hierarchy h = new Hierarchy();
			Node region = h.Region("Region");
			Actor a = new Actor("Player");
			region.ColliderEnter(a);
			h.Log.Clear();

			h.IsReconciling = true;
			region.ColliderStay(a);
			h.IsReconciling = false;
			a.IsTeleporting = true;
			region.ColliderStay(a);
			LogAssert.AreEqual(0, h.Log.Count, "Stay must be suppressed during replay and teleport. Got: " + h.Dump());

			a.IsTeleporting = false;
			region.ColliderStay(a);
			LogAssert.AreEqual("Region:Stay:Player", h.Dump());
		}

		#endregion

		#region Hierarchy

		/// <summary>
		/// A child region takes ownership: the parent must stop raising Stay for a character that
		/// is standing inside the child, otherwise both regions apply their effects at once.
		/// </summary>
		[Test]
		public void ParentDoesNotStay_WhileAChildOwnsTheCharacter()
		{
			Hierarchy h = new Hierarchy();
			Node parent = h.Region("Parent");
			Actor a = new Actor("Player");
			Node child = h.Region("Child", parent, x => x == a);

			parent.ColliderEnter(a);
			LogAssert.AreEqual(0, h.Log.Count,
				"The parent must not announce a character that is already geometrically inside its child. Got: " + h.Dump());

			child.ColliderEnter(a);
			LogAssert.AreEqual("Child:Enter:Player", h.Dump());

			parent.ColliderStay(a);
			child.ColliderStay(a);
			LogAssert.AreEqual("Child:Enter:Player | Child:Stay:Player", h.Dump(),
				"Only the owning child may raise Stay. Got: " + h.Dump());
		}

		/// <summary>
		/// The ordinary nested case: enter parent, walk into the child, walk back out. The parent
		/// gets a paired Exit when the child takes over and a paired Enter when the child releases,
		/// because the character is still physically inside the parent.
		/// </summary>
		[Test]
		public void ParentExitsAndReEnters_AroundAChildHandover()
		{
			Hierarchy h = new Hierarchy();
			Node parent = h.Region("Parent");
			Actor a = new Actor("Player");
			bool inChild = false;
			Node child = h.Region("Child", parent, x => inChild && x == a);

			parent.ColliderEnter(a);
			LogAssert.AreEqual("Parent:Enter:Player", h.Dump());

			inChild = true;
			child.ColliderEnter(a);
			LogAssert.AreEqual("Parent:Enter:Player | Parent:Exit:Player | Child:Enter:Player", h.Dump(),
				"The parent's exit must precede the child's enter. Got: " + h.Dump());

			inChild = false;
			child.ColliderExit(a);
			LogAssert.AreEqual(
				"Parent:Enter:Player | Parent:Exit:Player | Child:Enter:Player | Child:Exit:Player | Parent:Enter:Player",
				h.Dump(),
				"The parent must re-enter the character the child released. Got: " + h.Dump());
		}

		/// <summary>
		/// The unpaired-exit bug. A child that pokes outside its parent can be entered without the
		/// parent ever having entered the character. The old code exited the parent unconditionally,
		/// which stripped effects the parent had never applied (and fired an exit for a region the
		/// player had never been told they were in).
		/// </summary>
		[Test]
		public void ChildNotContainedByItsParent_ProducesNoUnpairedParentExit()
		{
			Hierarchy h = new Hierarchy();
			Node parent = h.Region("Parent");
			Actor a = new Actor("Player");
			// The character walks straight into the overhanging part of the child; the parent's
			// collider never reports it at all.
			Node child = h.Region("Child", parent, x => x == a);

			child.ColliderEnter(a);
			LogAssert.AreEqual("Child:Enter:Player", h.Dump(),
				"The parent never entered this character, so it must not exit it. Got: " + h.Dump());

			child.ColliderExit(a);
			LogAssert.AreEqual("Child:Enter:Player | Child:Exit:Player", h.Dump(),
				"The parent must not enter a character its own collider never reported. Got: " + h.Dump());
			LogAssert.IsFalse(parent.Membership.IsInside(a));
		}

		/// <summary>
		/// Three levels deep: only the innermost region owns the character, and every ancestor that
		/// had entered gets exactly one exit on the way in and one enter on the way out.
		/// </summary>
		[Test]
		public void ThreeLevelNesting_OnlyTheInnermostRegionOwnsTheCharacter()
		{
			Hierarchy h = new Hierarchy();
			Actor a = new Actor("Player");
			bool inMiddle = false;
			bool inInner = false;

			Node outer = h.Region("Outer");
			Node middle = h.Region("Middle", outer, x => (inMiddle || inInner) && x == a);
			Node inner = h.Region("Inner", middle, x => inInner && x == a);

			outer.ColliderEnter(a);
			LogAssert.AreEqual("Outer:Enter:Player", h.Dump());

			inMiddle = true;
			middle.ColliderEnter(a);
			LogAssert.AreEqual("Outer:Enter:Player | Outer:Exit:Player | Middle:Enter:Player", h.Dump());

			inInner = true;
			inner.ColliderEnter(a);
			LogAssert.AreEqual(
				"Outer:Enter:Player | Outer:Exit:Player | Middle:Enter:Player | Middle:Exit:Player | Inner:Enter:Player",
				h.Dump(),
				"Ownership must transfer inward one level at a time. Got: " + h.Dump());
			LogAssert.IsTrue(inner.Membership.IsInside(a));
			LogAssert.IsFalse(middle.Membership.IsInside(a));
			LogAssert.IsFalse(outer.Membership.IsInside(a));

			h.Log.Clear();
			inInner = false;
			inner.ColliderExit(a);
			LogAssert.AreEqual("Inner:Exit:Player | Middle:Enter:Player", h.Dump(),
				"Releasing the innermost region must hand the character back to its immediate parent. Got: " + h.Dump());

			h.Log.Clear();
			inMiddle = false;
			middle.ColliderExit(a);
			LogAssert.AreEqual("Middle:Exit:Player | Outer:Enter:Player", h.Dump());
		}

		/// <summary>
		/// Two sibling children of the same parent overlap. Walking from one into the other must not
		/// bounce the parent back in between: the parent may only re-enter once no descendant owns
		/// the character.
		/// </summary>
		[Test]
		public void TwoOverlappingSiblings_DoNotBounceTheParentBetweenThem()
		{
			Hierarchy h = new Hierarchy();
			Node parent = h.Region("Parent");
			Actor a = new Actor("Player");
			bool inLeft = false;
			bool inRight = false;
			Node left = h.Region("Left", parent, x => inLeft && x == a);
			Node right = h.Region("Right", parent, x => inRight && x == a);

			parent.ColliderEnter(a);
			inLeft = true;
			left.ColliderEnter(a);
			LogAssert.AreEqual("Parent:Enter:Player | Parent:Exit:Player | Left:Enter:Player", h.Dump());

			// Step into the overlap: right enters while left still holds them.
			h.Log.Clear();
			inRight = true;
			right.ColliderEnter(a);
			LogAssert.AreEqual("Right:Enter:Player", h.Dump(),
				"The parent had already exited, so no second parent exit may fire. Got: " + h.Dump());

			// Leave the left sibling while still inside the right one.
			h.Log.Clear();
			inLeft = false;
			left.ColliderExit(a);
			LogAssert.AreEqual("Left:Exit:Player", h.Dump(),
				"The parent must stay out while the other sibling still owns the character. Got: " + h.Dump());
			LogAssert.IsFalse(parent.Membership.IsInside(a));

			// Finally leave the right sibling: now the parent may reclaim them.
			h.Log.Clear();
			inRight = false;
			right.ColliderExit(a);
			LogAssert.AreEqual("Right:Exit:Player | Parent:Enter:Player", h.Dump());
		}

		/// <summary>
		/// A child registered with its parent before its Collider was assigned (Region's Awake can
		/// return early and leave it null) used to null-deref inside the parent's containment loop.
		/// A collider-less child simply cannot own anything and must be skipped.
		/// </summary>
		[Test]
		public void ChildWithNoCollider_IsSkippedInsteadOfThrowing()
		{
			Hierarchy h = new Hierarchy();
			Node parent = h.Region("Parent");
			// contains == null models "Collider is still null".
			h.Region("BrokenChild", parent, null);
			Actor a = new Actor("Player");

			Assert.DoesNotThrow(() => parent.ColliderEnter(a),
				"A child whose collider was never assigned must not break its parent's containment test.");
			LogAssert.AreEqual("Parent:Enter:Player", h.Dump(),
				"A collider-less child cannot own the character, so the parent must still enter it. Got: " + h.Dump());
		}

		#endregion

		#region Lifetime

		/// <summary>
		/// A character destroyed while standing inside a region must be forgotten silently — there
		/// is nobody left to raise an exit for, and holding the reference leaks the object.
		/// </summary>
		[Test]
		public void CharacterDestroyedWhileInside_IsForgottenWithoutAnExit()
		{
			Hierarchy h = new Hierarchy();
			Node region = h.Region("Region");
			Actor a = new Actor("Player");
			region.ColliderEnter(a);
			h.Log.Clear();

			a.Destroyed = true;
			region.PostTick();

			LogAssert.AreEqual(0, h.Log.Count, "A destroyed character must not receive an exit. Got: " + h.Dump());
			LogAssert.AreEqual(0, region.Membership.EffectiveCount, "The destroyed character must be dropped from membership.");
			LogAssert.AreEqual(0, region.Membership.RawCount, "The destroyed character must be dropped from raw presence too.");
		}

		/// <summary>
		/// A region destroyed or disabled with members inside can no longer poll its collider. Every
		/// live member gets a final exit now, so region-applied effects are cleaned up instead of
		/// being stranded on the character forever.
		/// </summary>
		[Test]
		public void RegionDestroyedWithMembersInside_ExitsEveryLiveMember()
		{
			Hierarchy h = new Hierarchy();
			Node region = h.Region("Region");
			Actor alive = new Actor("Alive");
			Actor gone = new Actor("Gone");
			region.ColliderEnter(alive);
			region.ColliderEnter(gone);
			h.Log.Clear();

			gone.Destroyed = true;
			region.Teardown();

			LogAssert.AreEqual("Region:Exit:Alive", h.Dump(),
				"Only the surviving member may receive a teardown exit. Got: " + h.Dump());
			LogAssert.AreEqual(0, region.Membership.EffectiveCount);
			LogAssert.AreEqual(0, region.Membership.RawCount);
		}

		/// <summary>
		/// Tearing down a child hands its members back to the parent, which still physically
		/// contains them.
		/// </summary>
		[Test]
		public void ChildDestroyedWithMembersInside_HandsThemBackToTheParent()
		{
			Hierarchy h = new Hierarchy();
			Node parent = h.Region("Parent");
			Actor a = new Actor("Player");
			bool childAlive = true;
			Node child = h.Region("Child", parent, x => childAlive && x == a);

			parent.ColliderEnter(a);
			child.ColliderEnter(a);
			h.Log.Clear();

			childAlive = false;
			parent.Children.Remove(child);
			child.Teardown();

			LogAssert.AreEqual("Child:Exit:Player | Parent:Enter:Player", h.Dump(),
				"The parent must reclaim a character stranded by a destroyed child. Got: " + h.Dump());
		}

		#endregion

		#region Geometry

		/// <summary>
		/// The pure box test, in the box's own space. <c>Collider.bounds</c> is an axis-aligned box
		/// in world space, so for a rotated region it reports a corner volume the region does not
		/// actually cover — which is how characters were "inside" a rotated region while standing
		/// outside it.
		/// </summary>
		[Test]
		public void BoxContainsLocalPoint_TestsTheBoxNotItsBounds()
		{
			Vector3 center = Vector3.zero;
			Vector3 size = new Vector3(2.0f, 2.0f, 2.0f);

			LogAssert.IsTrue(RegionGeometry.BoxContainsLocalPoint(Vector3.zero, center, size), "The centre is inside.");
			LogAssert.IsTrue(RegionGeometry.BoxContainsLocalPoint(new Vector3(1.0f, 1.0f, 1.0f), center, size), "A corner counts as inside.");
			LogAssert.IsFalse(RegionGeometry.BoxContainsLocalPoint(new Vector3(1.01f, 0.0f, 0.0f), center, size), "Just past a face is outside.");

			// An offset centre shifts the whole volume.
			Vector3 offset = new Vector3(5.0f, 0.0f, 0.0f);
			LogAssert.IsTrue(RegionGeometry.BoxContainsLocalPoint(new Vector3(5.0f, 0.0f, 0.0f), offset, size));
			LogAssert.IsFalse(RegionGeometry.BoxContainsLocalPoint(Vector3.zero, offset, size));

			// A negative size is a designer typo Unity permits; treat it as its magnitude rather
			// than as an empty volume that silently swallows every containment query.
			LogAssert.IsTrue(RegionGeometry.BoxContainsLocalPoint(new Vector3(0.5f, 0.0f, 0.0f), center, new Vector3(-2.0f, -2.0f, -2.0f)),
				"A negative authored size must not collapse the box to nothing.");
		}

		/// <summary>
		/// The same test through a real rotated <see cref="BoxCollider"/>: a point inside the
		/// axis-aligned bounds but outside the rotated box must be rejected.
		/// </summary>
		[Test]
		public void ContainsPoint_RejectsAPointInsideTheAabbButOutsideARotatedBox()
		{
			GameObject go = new GameObject("RotatedRegion");
			try
			{
				go.transform.position = Vector3.zero;
				go.transform.rotation = Quaternion.Euler(0.0f, 45.0f, 0.0f);
				BoxCollider box = go.AddComponent<BoxCollider>();
				box.size = new Vector3(2.0f, 2.0f, 2.0f);
				box.isTrigger = true;

				// The 45-degree rotation grows the AABB to roughly +/-1.414 on X and Z, so this
				// corner sits well inside bounds and well outside the actual box.
				Vector3 corner = new Vector3(1.3f, 0.0f, 1.3f);
				LogAssert.IsTrue(box.bounds.Contains(corner),
					"Precondition: the axis-aligned bounds must contain the point, or this test proves nothing.");
				LogAssert.IsFalse(RegionGeometry.ContainsPoint(box, corner),
					"A rotated region must not claim a point that only its axis-aligned bounds cover.");

				LogAssert.IsTrue(RegionGeometry.ContainsPoint(box, Vector3.zero), "The centre is still inside.");
				// Straight down the rotated local +X axis, comfortably inside the half-extent.
				Vector3 alongLocalX = go.transform.TransformPoint(new Vector3(0.9f, 0.0f, 0.0f));
				LogAssert.IsTrue(RegionGeometry.ContainsPoint(box, alongLocalX), "A point inside the rotated box is inside.");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		/// <summary>
		/// A null collider is the "Collider was never assigned" case; it contains nothing rather
		/// than throwing.
		/// </summary>
		[Test]
		public void ContainsPoint_NullColliderContainsNothing()
		{
			Assert.DoesNotThrow(() => RegionGeometry.ContainsPoint(null, Vector3.zero));
			LogAssert.IsFalse(RegionGeometry.ContainsPoint(null, Vector3.zero));
		}

		/// <summary>
		/// Sphere colliders go through <c>ClosestPoint</c>, which is exact for convex shapes.
		/// </summary>
		[Test]
		public void ContainsPoint_HandlesASphereExactly()
		{
			GameObject go = new GameObject("SphereRegion");
			try
			{
				go.transform.position = Vector3.zero;
				SphereCollider sphere = go.AddComponent<SphereCollider>();
				sphere.radius = 1.0f;
				sphere.isTrigger = true;

				LogAssert.IsTrue(RegionGeometry.ContainsPoint(sphere, new Vector3(0.5f, 0.0f, 0.0f)));
				// Inside the AABB corner, outside the sphere.
				LogAssert.IsFalse(RegionGeometry.ContainsPoint(sphere, new Vector3(0.9f, 0.9f, 0.0f)));
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		#endregion

		#region Authority gate

		/// <summary>
		/// Buffs and attribute changes are gameplay state. Running them on a client would apply an
		/// unauthoritative mutation that the next reconcile has to undo; running them during replay
		/// would apply them once per replayed tick.
		/// </summary>
		[Test]
		public void GameplayActions_RunOnlyOnTheServerAndNeverDuringReplay()
		{
			LogAssert.IsTrue(RegionActionGate.Decide(hasInitiator: true, isServerStarted: true, isReconciling: false),
				"The server outside replay is the only case that may mutate gameplay state.");

			LogAssert.IsFalse(RegionActionGate.Decide(hasInitiator: true, isServerStarted: false, isReconciling: false),
				"A client peer must never apply a region buff or attribute change itself.");
			LogAssert.IsFalse(RegionActionGate.Decide(hasInitiator: true, isServerStarted: true, isReconciling: true),
				"Even the server must not mutate gameplay state while replaying.");
			LogAssert.IsFalse(RegionActionGate.Decide(hasInitiator: false, isServerStarted: true, isReconciling: false),
				"With no initiator there is nobody to apply the effect to.");
			LogAssert.IsFalse(RegionActionGate.Decide(hasInitiator: false, isServerStarted: false, isReconciling: true));
		}

		/// <summary>
		/// A null <see cref="ICharacter"/> must be rejected rather than dereferenced — region
		/// actions are designer-authored and can be fired from a trigger whose event carries no
		/// initiator.
		/// </summary>
		[Test]
		public void GameplayGate_RejectsANullInitiatorWithoutThrowing()
		{
			Assert.DoesNotThrow(() => RegionActionGate.ShouldExecuteGameplay(null, null));
			LogAssert.IsFalse(RegionActionGate.ShouldExecuteGameplay(null, null));
		}

		#endregion

		#region Membership primitives

		/// <summary>
		/// <c>ForceExit</c> is what a parent calls when a child takes over. It must report false
		/// when no enter was ever raised, which is the whole defence against unpaired exits.
		/// </summary>
		[Test]
		public void ForceExit_ReportsFalseWhenNoEnterWasEverRaised()
		{
			RegionMembership<Actor> m = new RegionMembership<Actor>();
			Actor a = new Actor("Player");

			LogAssert.IsFalse(m.ForceExit(a), "No enter was raised, so there is no exit to pair with.");

			m.RecordEnter(a, suppressed: false);
			LogAssert.IsTrue(m.ForceExit(a), "An announced member must produce its paired exit.");
			LogAssert.IsFalse(m.ForceExit(a), "The exit must not be produced a second time.");
		}

		/// <summary>
		/// <c>TryEnter</c> is the child-release path. It may only promote a character the collider
		/// still reports as physically present, so a parent never re-enters somebody who has left.
		/// </summary>
		[Test]
		public void TryEnter_OnlyPromotesACharacterTheColliderStillReports()
		{
			RegionMembership<Actor> m = new RegionMembership<Actor>();
			Actor a = new Actor("Player");

			LogAssert.IsFalse(m.TryEnter(a, suppressed: false), "Nothing to promote: the collider never reported this character.");

			m.RecordEnter(a, suppressed: false, canEnter: false);
			LogAssert.IsTrue(m.IsRawInside(a), "Raw presence is recorded even when a child blocks the enter.");
			LogAssert.IsFalse(m.IsInside(a), "A blocked enter must not be announced.");

			LogAssert.IsFalse(m.TryEnter(a, suppressed: true), "A suppressed promotion waits for the flush.");
			LogAssert.IsFalse(m.TryEnter(a, suppressed: false, canEnter: false), "A child still owns the character.");
			LogAssert.IsTrue(m.TryEnter(a, suppressed: false), "Once unblocked the character is announced.");
			LogAssert.IsFalse(m.TryEnter(a, suppressed: false), "And only once.");
		}

		/// <summary>
		/// Null keys are ignored throughout rather than poisoning the sets — the collider can hand
		/// back a character whose component lookup failed.
		/// </summary>
		[Test]
		public void NullCharacters_AreIgnoredEverywhere()
		{
			RegionMembership<Actor> m = new RegionMembership<Actor>();

			LogAssert.IsFalse(m.RecordEnter(null, suppressed: false));
			LogAssert.IsFalse(m.RecordExit(null, suppressed: false));
			LogAssert.IsFalse(m.ShouldStay(null, suppressed: false));
			LogAssert.IsFalse(m.ForceExit(null));
			LogAssert.IsFalse(m.TryEnter(null, suppressed: false));
			LogAssert.IsFalse(m.IsInside(null));
			LogAssert.IsFalse(m.IsRawInside(null));
			Assert.DoesNotThrow(() => m.Forget(null));
			LogAssert.AreEqual(0, m.RawCount);
			LogAssert.AreEqual(0, m.EffectiveCount);
		}

		/// <summary>
		/// The flush is a diff, so calling it repeatedly with no collider traffic must be a no-op.
		/// A flush that re-reported its own results would fire a region's enter effects every tick.
		/// </summary>
		[Test]
		public void Flush_IsIdempotentWithoutColliderTraffic()
		{
			RegionMembership<Actor> m = new RegionMembership<Actor>();
			Actor a = new Actor("Player");
			List<Actor> enters = new List<Actor>();
			List<Actor> exits = new List<Actor>();

			m.RecordEnter(a, suppressed: true);
			m.Flush(null, null, null, enters, exits);
			LogAssert.AreEqual(1, enters.Count, "The deferred enter surfaces on the first flush.");
			LogAssert.AreEqual(0, exits.Count);

			enters.Clear();
			m.Flush(null, null, null, enters, exits);
			m.Flush(null, null, null, enters, exits);
			LogAssert.AreEqual(0, enters.Count, "Subsequent flushes must report nothing.");
			LogAssert.AreEqual(0, exits.Count);
		}

		#endregion
	}
}
