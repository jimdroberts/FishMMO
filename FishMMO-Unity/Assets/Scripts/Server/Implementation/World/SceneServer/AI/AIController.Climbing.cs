using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using FishMMO.Shared;

namespace FishMMO.Server.Implementation.World.SceneServer.AI
{
	/// <summary>
	/// Climbing half of <see cref="AIController"/>: an NPC whose path takes a ladder, rope ladder or rope (one of the scene's
	/// <see cref="SceneClimbables"/>, linked into the NavMesh by <see cref="Navigation.SceneNavMeshes"/>) climbs it, a
	/// scripted walk along the same geometry a player climbs: to its foot, up the line, over the top to the landing, or
	/// the other way down.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>NPCs do not run the KCC.</b> They move by their NavMeshAgent, one step per network tick (<see cref="StepAgent"/>),
	/// and observers see them through their NetworkTransform; so the climb is a timed move along points, not the
	/// predicted climbing mode. The agent's own straight-line link traversal is turned off: it would cut through the
	/// ledge's edge from the foot to the landing.
	/// </para>
	/// <para>
	/// <b>Never stuck on a link.</b> A traversal runs to its length or to twice its expected time and two seconds more,
	/// whichever comes first, and then completes the link wherever the body is. A halt (death, quarantine) completes it at
	/// the nearer end, so a corpse or a stopped brain never hangs on a ladder with its agent stranded on the link.
	/// </para>
	/// <para>
	/// <b>Who climbs</b> is the archetype's <see cref="AIArchetypeTemplate.CanClimb"/>: the links are the NavMesh's "Climb"
	/// area, which is in the agent's mask only for archetypes that climb.
	/// </para>
	/// </remarks>
	public partial class AIController
	{
		/// <summary>The name of the NavMesh area the climbables' links are in (<see cref="Navigation.SceneNavMeshes"/>).</summary>
		public const string ClimbAreaName = "Climb";

		/// <summary>How far a link's end may be from a climbable's foot or landing and still be that climbable's link, metres.</summary>
		public const float ClimbLinkMatchMetres = 1.5f;

		private static int climbArea = -2;

		/// <summary>The climb NavMesh area's index; -1 when the project has none.</summary>
		public static int ClimbArea => climbArea != -2 ? climbArea : (climbArea = NavMesh.GetAreaFromName(ClimbAreaName));

		/// <summary>Whether this NPC climbs: its archetype says so (on by default, as is an NPC with no archetype).</summary>
		public bool CanClimb => archetype == null || archetype.CanClimb;

		// The link being walked: its points, how far along them, for how long, and which climbable it is (-1 for a plain link).
		private readonly List<Vector3> linkPath = new List<Vector3>(6);
		private bool linkActive;
		private float linkDistance;
		private float linkLength;
		private float linkTimer;
		private float linkTimeout;
		private int linkClimbable = -1;
		private SceneClimbable linkClimb;

		/// <summary>An NPC on a climbable's link now (on the way to it, on it, or over its top).</summary>
		public bool IsClimbingLink => linkActive && linkClimbable >= 0;

		/// <summary>The agent's area mask with the climb links in it or out of it, as the archetype climbs or not.</summary>
		private int WithClimbArea(int mask)
		{
			int area = ClimbArea;
			if (area < 0)
			{
				return mask;
			}
			return CanClimb ? mask | (1 << area) : mask & ~(1 << area);
		}

		/// <summary>
		/// One tick along the off-mesh link the agent stands on: its points walked at climbing speed up and down the
		/// climbable and at walking speed across, the body facing the climbable while on it. Completes the link at the end.
		/// </summary>
		private void StepOffMeshLink(float tickDelta)
		{
			Transform t = Character.Transform;
			Vector3 before = t.position;
			if (!linkActive)
			{
				BeginOffMeshLink(before);
			}

			linkTimer += tickDelta;
			ClimbingRules.PointAlong(linkPath, linkDistance, out int leg);
			linkDistance += LegSpeed(leg) * tickDelta;
			Vector3 at = ClimbingRules.PointAlong(linkPath, linkDistance, out leg);
			t.position = at;
			measuredTickSpeedSqr = MeasureTickSpeedSqr(before, at, tickDelta);

			Vector3 facing = LegFacing(leg);
			if (facing.sqrMagnitude > 0f && ResolveTickHeading(t.rotation, facing, Mathf.Max(Agent.angularSpeed, 360f), tickDelta, out Quaternion heading))
			{
				t.rotation = heading;
			}

			if (linkDistance >= linkLength || linkTimer >= linkTimeout)
			{
				FinishOffMeshLink();
			}
		}

		/// <summary>Lays out the link the agent has just reached: a climbable's way when it is one, else a straight line.</summary>
		private void BeginOffMeshLink(Vector3 from)
		{
			OffMeshLinkData data = Agent.currentOffMeshLinkData;
			linkPath.Clear();
			linkClimbable = -1;
			linkClimb = default;
			if (SceneClimbables.TryGetAll(Character.GameObject.scene, out IReadOnlyList<SceneClimbable> climbables) &&
				TryMatchClimbable(climbables, data.startPos, data.endPos, out int index, out bool upward))
			{
				linkClimbable = index;
				linkClimb = climbables[index];
				ClimbingRules.TraversalPoints(in linkClimb, upward, Agent.radius + Constants.Character.ClimbHangClearance, from, data.endPos, linkPath);
			}
			else
			{
				linkPath.Add(from);
				linkPath.Add(data.endPos);
			}
			linkLength = ClimbingRules.PathLength(linkPath);
			float expected = 0f;
			for (int i = 1; i < linkPath.Count; i++)
			{
				expected += Vector3.Distance(linkPath[i - 1], linkPath[i]) / Mathf.Max(0.1f, LegSpeed(i - 1));
			}
			linkTimeout = expected * 2f + 2f;
			linkDistance = 0f;
			linkTimer = 0f;
			linkActive = true;
		}

		/// <summary>Climbing speed on a leg that is mostly up or down (a climbable's), walking speed on one mostly across.</summary>
		private float LegSpeed(int leg)
		{
			if (leg < 0 || leg + 1 >= linkPath.Count)
			{
				return Constants.Character.WalkSpeed;
			}
			Vector3 d = linkPath[leg + 1] - linkPath[leg];
			float across = new Vector2(d.x, d.z).magnitude;
			if (linkClimbable >= 0 && Mathf.Abs(d.y) > across)
			{
				return Constants.Character.ClimbSpeed * ClimbingRules.KindShare(linkClimb.Kind);
			}
			return linkClimbable >= 0 ? Constants.Character.ClimbMantleSpeed : Mathf.Max(Constants.Character.WalkSpeed, Agent.speed);
		}

		/// <summary>Facing the climbable on a leg up or down it; along the way on any other.</summary>
		private Vector3 LegFacing(int leg)
		{
			if (leg < 0 || leg + 1 >= linkPath.Count)
			{
				return Vector3.zero;
			}
			Vector3 d = linkPath[leg + 1] - linkPath[leg];
			float across = new Vector2(d.x, d.z).magnitude;
			if (linkClimbable >= 0 && Mathf.Abs(d.y) > across)
			{
				return -ClimbingRules.HangDirection(in linkClimb, linkClimb.Bottom, ClimbPhase.SteppingOn);
			}
			return new Vector3(d.x, 0f, d.z);
		}

		/// <summary>Completes the link where the walk ended and seats the agent and the body on its far end.</summary>
		private void FinishOffMeshLink()
		{
			ResetOffMeshLink();
			if (AgentIsUsable() && Agent.isOnOffMeshLink)
			{
				Agent.CompleteOffMeshLink();
			}
			SyncTransformToAgent();
		}

		/// <summary>
		/// Gives up a link mid-way (a halt, a death): the agent is completed onto it and placed at whichever end the body is
		/// nearer, so nothing is left hanging on a climbable or stranded on the link.
		/// </summary>
		private void AbandonOffMeshLink()
		{
			bool onLink = Agent != null && Agent.isActiveAndEnabled && Agent.isOnNavMesh && Agent.isOnOffMeshLink;
			if (!linkActive && !onLink)
			{
				return;
			}
			Vector3 at = Character != null ? Character.Transform.position : Vector3.zero;
			Vector3 nearer = at;
			if (onLink)
			{
				OffMeshLinkData data = Agent.currentOffMeshLinkData;
				nearer = (data.startPos - at).sqrMagnitude <= (data.endPos - at).sqrMagnitude ? data.startPos : data.endPos;
				Agent.CompleteOffMeshLink();
			}
			ResetOffMeshLink();
			if (onLink)
			{
				WarpTo(nearer);
			}
		}

		private void ResetOffMeshLink()
		{
			linkActive = false;
			linkPath.Clear();
			linkDistance = 0f;
			linkLength = 0f;
			linkTimer = 0f;
			linkTimeout = 0f;
			linkClimbable = -1;
			linkClimb = default;
		}

		/// <summary>
		/// The climbable whose NavMesh link runs from <paramref name="start"/> to <paramref name="end"/>, and whether the
		/// link is taken upward (foot to landing) or down: the one whose ends lie nearest both, within
		/// <see cref="ClimbLinkMatchMetres"/> of each. Lowest index on a tie, so the answer never depends on anything else.
		/// </summary>
		public static bool TryMatchClimbable(IReadOnlyList<SceneClimbable> climbables, Vector3 start, Vector3 end, out int index, out bool upward)
		{
			index = -1;
			upward = true;
			if (climbables == null)
			{
				return false;
			}
			float best = float.MaxValue;
			float tolerance = ClimbLinkMatchMetres * ClimbLinkMatchMetres;
			for (int i = 0; i < climbables.Count; i++)
			{
				SceneClimbable c = climbables[i];
				if (!ClimbingRules.IsValid(in c))
				{
					continue;
				}
				ClimbingRules.LinkEnds(in c, out Vector3 lower, out Vector3 upper);
				float up0 = (start - lower).sqrMagnitude, up1 = (end - upper).sqrMagnitude;
				if (up0 <= tolerance && up1 <= tolerance && up0 + up1 < best)
				{
					best = up0 + up1;
					index = i;
					upward = true;
				}
				float down0 = (start - upper).sqrMagnitude, down1 = (end - lower).sqrMagnitude;
				if (down0 <= tolerance && down1 <= tolerance && down0 + down1 < best)
				{
					best = down0 + down1;
					index = i;
					upward = false;
				}
			}
			return index >= 0;
		}
	}
}
