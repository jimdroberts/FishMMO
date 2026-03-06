using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Coordinates a pack of NPCs that fight together. Provides shared state so
	/// individual NPC brains can make group-aware decisions via the behavior tree
	/// or state machine.
	/// <para>
	/// <b>Shared state includes:</b>
	/// <list type="bullet">
	///   <item>Group target — the enemy the group is focusing.</item>
	///   <item>Lowest-health member — so healers know who needs help.</item>
	///   <item>Alive member count — for pack-wipe / rally logic.</item>
	///   <item>Combat flag — whether any member is in combat.</item>
	/// </list>
	/// </para>
	/// <para>
	/// Place this component on an empty GameObject near the pack spawn point.
	/// Assign members in the inspector or call <see cref="AddMember"/> at runtime.
	/// Each member's <see cref="AIController.Group"/> is set automatically.
	/// </para>
	/// </summary>
	public class NPCGroup : MonoBehaviour
	{
		[Header("Members")]
		[Tooltip("NPCs in this group. Assign in inspector or add at runtime.")]
		[SerializeField]
		private List<NPCGroupMember> members = new List<NPCGroupMember>();

		[Header("Targeting")]
		[Tooltip("When true, all DPS members share the tank's target.")]
		public bool FocusTargeting = true;

		[Header("Pack Tactics")]
		[Tooltip("How the group coordinates spatial positioning around the target during combat.")]
		public PackTactic Tactic = PackTactic.None;

		[Tooltip("Base orbit radius for pack tactic positioning (meters from target).")]
		public float TacticOrbitRadius = 5f;

		[Tooltip("Degrees per second the Kite tactic rotates the group ring.")]
		public float KiteRotationSpeed = 30f;

		/// <summary>
		/// Running angle offset (radians) for the Kite tactic's slow rotation.
		/// </summary>
		private float kiteAngleOffset;

		/// <summary>
		/// The group's shared combat target. Typically set by the tank or by whichever
		/// member first enters combat.
		/// </summary>
		public Transform GroupTarget { get; set; }

		/// <summary>
		/// The member with the lowest health percentage. Updated every evaluation.
		/// Healers can read this to decide who to heal.
		/// </summary>
		public AIController LowestHealthMember { get; private set; }

		/// <summary>
		/// Percentage (0-1) of the lowest-health member's HP.
		/// </summary>
		public float LowestHealthPercent { get; private set; }

		/// <summary>
		/// Number of members currently alive.
		/// </summary>
		public int AliveMemberCount { get; private set; }

		/// <summary>
		/// True if any member is in an attacking state.
		/// </summary>
		public bool IsInCombat { get; private set; }

		/// <summary>
		/// Total number of members (alive or dead).
		/// </summary>
		public int MemberCount => members.Count;

		/// <summary>
		/// Read-only access to the member list.
		/// </summary>
		public IReadOnlyList<NPCGroupMember> Members => members;

		private float nextEvaluateTime;
		private const float EVALUATE_INTERVAL = 0.5f;

		void Awake()
		{
			// Link members back to this group.
			for (int i = 0; i < members.Count; i++)
			{
				if (members[i]?.Controller != null)
				{
					members[i].Controller.Group = this;
					members[i].Controller.GroupRole = members[i].Role;
				}
			}
		}

		void Update()
		{
			if (Time.time < nextEvaluateTime) return;
			nextEvaluateTime = Time.time + EVALUATE_INTERVAL;

			EvaluateGroupState();
		}

		/// <summary>
		/// Adds a member at runtime and links the group reference.
		/// </summary>
		public void AddMember(AIController controller, NPCGroupRole role = NPCGroupRole.DPS)
		{
			if (controller == null) return;

			NPCGroupMember member = new NPCGroupMember
			{
				Controller = controller,
				Role = role
			};
			members.Add(member);
			controller.Group = this;
			controller.GroupRole = role;
		}

		/// <summary>
		/// Removes a member from the group.
		/// </summary>
		public void RemoveMember(AIController controller)
		{
			if (controller == null) return;

			for (int i = members.Count - 1; i >= 0; i--)
			{
				if (members[i].Controller == controller)
				{
					members[i].Controller.Group = null;
					members[i].Controller.GroupRole = NPCGroupRole.None;
					members.RemoveAt(i);
					break;
				}
			}
		}

		/// <summary>
		/// Signals the entire group that combat has started with the given target.
		/// Each member that isn't already fighting will transition to its attacking state.
		/// </summary>
		public void AlertGroup(Transform enemy)
		{
			if (enemy == null) return;

			GroupTarget = enemy;

			for (int i = 0; i < members.Count; i++)
			{
				NPCGroupMember member = members[i];
				if (member?.Controller == null) continue;
				if (!IsMemberAlive(member.Controller)) continue;

				// Only alert members not already in combat.
				if (member.Controller.CurrentState != member.Controller.AttackingState)
				{
					member.Controller.Target = enemy;
				}
			}
		}

		/// <summary>
		/// Returns the first member with the given role, or null.
		/// </summary>
		public AIController GetMemberByRole(NPCGroupRole role)
		{
			for (int i = 0; i < members.Count; i++)
			{
				if (members[i]?.Controller != null && members[i].Role == role &&
					IsMemberAlive(members[i].Controller))
				{
					return members[i].Controller;
				}
			}
			return null;
		}

		/// <summary>
		/// Re-evaluates group state: alive count, lowest health, combat status, focus target.
		/// </summary>
		private void EvaluateGroupState()
		{
			AliveMemberCount = 0;
			LowestHealthMember = null;
			LowestHealthPercent = float.MaxValue;
			IsInCombat = false;

			Transform tankTarget = null;

			for (int i = 0; i < members.Count; i++)
			{
				NPCGroupMember member = members[i];
				if (member?.Controller == null) continue;
				if (member.Controller.Character == null) continue;

				if (!member.Controller.Character.TryGet(out ICharacterDamageController dmg))
					continue;

				if (!dmg.IsAlive) continue;

				AliveMemberCount++;

				// Track lowest health.
				float hp = dmg.ResourceInstance != null && dmg.ResourceInstance.FinalValue > 0
					? dmg.ResourceInstance.CurrentValue / dmg.ResourceInstance.FinalValue
					: 1f;
				if (hp < LowestHealthPercent)
				{
					LowestHealthPercent = hp;
					LowestHealthMember = member.Controller;
				}

				// Track combat status.
				if (member.Controller.CurrentState == member.Controller.AttackingState)
				{
					IsInCombat = true;
				}

				// Get tank's target for focus fire.
				if (member.Role == NPCGroupRole.Tank && member.Controller.Target != null)
				{
					tankTarget = member.Controller.Target;
				}
			}

			// Focus targeting: have DPS/Support share the tank's target.
			if (FocusTargeting && tankTarget != null)
			{
				GroupTarget = tankTarget;
			}

			// Assign tactical orbit angles when in combat.
			if (IsInCombat && Tactic != PackTactic.None)
			{
				AssignTacticalPositions();
			}
		}

		/// <summary>
		/// Assigns each alive member's <see cref="AIController.OrbitAngle"/> based on the
		/// current <see cref="Tactic"/>. Called every <see cref="EVALUATE_INTERVAL"/> while
		/// in combat.
		/// </summary>
		private void AssignTacticalPositions()
		{
			if (AliveMemberCount < 1)
				return;

			switch (Tactic)
			{
				case PackTactic.Surround:
					AssignSurround();
					break;
				case PackTactic.Flank:
					AssignFlank();
					break;
				case PackTactic.FocusFire:
					AssignFocusFire();
					break;
				case PackTactic.Kite:
					AssignKite();
					break;
			}
		}

		/// <summary>
		/// Surround: spread members evenly in a 360° ring around the target.
		/// </summary>
		private void AssignSurround()
		{
			float angleStep = (Mathf.PI * 2f) / AliveMemberCount;
			int aliveIndex = 0;

			for (int i = 0; i < members.Count; i++)
			{
				NPCGroupMember member = members[i];
				if (member?.Controller == null) continue;
				if (!IsMemberAlive(member.Controller)) continue;

				member.Controller.OrbitAngle = angleStep * aliveIndex;
				aliveIndex++;
			}
		}

		/// <summary>
		/// Flank: tank faces the front (angle 0), all other members get angles in the
		/// rear 180° arc (π ± spread). Creates a pincer formation.
		/// </summary>
		private void AssignFlank()
		{
			int nonTankAlive = 0;
			for (int i = 0; i < members.Count; i++)
			{
				NPCGroupMember m = members[i];
				if (m?.Controller == null || !IsMemberAlive(m.Controller)) continue;
				if (m.Role != NPCGroupRole.Tank)
					nonTankAlive++;
			}

			// Spread the non-tank members across the rear 180° arc.
			float rearSpread = nonTankAlive > 1
				? Mathf.PI / (nonTankAlive - 1)
				: 0f;
			float rearStart = Mathf.PI * 0.5f; // Start at 90° (right side of rear)

			int flankIndex = 0;

			for (int i = 0; i < members.Count; i++)
			{
				NPCGroupMember member = members[i];
				if (member?.Controller == null || !IsMemberAlive(member.Controller)) continue;

				if (member.Role == NPCGroupRole.Tank)
				{
					// Tank faces the target head-on.
					member.Controller.OrbitAngle = 0f;
				}
				else
				{
					// Position behind the target.
					member.Controller.OrbitAngle = rearStart + (rearSpread * flankIndex);
					flankIndex++;
				}
			}
		}

		/// <summary>
		/// FocusFire: all members cluster on the same side with tight angular spread.
		/// </summary>
		private void AssignFocusFire()
		{
			// Tight 30° spread centered on angle 0.
			const float CLUSTER_SPREAD = 0.52f; // ~30 degrees in radians
			float angleStep = AliveMemberCount > 1
				? CLUSTER_SPREAD / (AliveMemberCount - 1)
				: 0f;
			float startAngle = -CLUSTER_SPREAD * 0.5f;

			int aliveIndex = 0;

			for (int i = 0; i < members.Count; i++)
			{
				NPCGroupMember member = members[i];
				if (member?.Controller == null) continue;
				if (!IsMemberAlive(member.Controller)) continue;

				member.Controller.OrbitAngle = startAngle + (angleStep * aliveIndex);
				aliveIndex++;
			}
		}

		/// <summary>
		/// Kite: spread evenly like Surround, but the entire ring slowly rotates each tick.
		/// </summary>
		private void AssignKite()
		{
			kiteAngleOffset += KiteRotationSpeed * Mathf.Deg2Rad * EVALUATE_INTERVAL;

			// Keep the offset in [0, 2π) to prevent float drift over long sessions.
			if (kiteAngleOffset > Mathf.PI * 2f)
				kiteAngleOffset -= Mathf.PI * 2f;

			float angleStep = (Mathf.PI * 2f) / AliveMemberCount;
			int aliveIndex = 0;

			for (int i = 0; i < members.Count; i++)
			{
				NPCGroupMember member = members[i];
				if (member?.Controller == null) continue;
				if (!IsMemberAlive(member.Controller)) continue;

				member.Controller.OrbitAngle = kiteAngleOffset + (angleStep * aliveIndex);
				aliveIndex++;
			}
		}

		/// <summary>
		/// Returns true if the member is alive.
		/// </summary>
		private static bool IsMemberAlive(AIController controller)
		{
			if (controller.Character == null) return false;
			if (!controller.Character.TryGet(out ICharacterDamageController dmg)) return false;
			return dmg.IsAlive;
		}
	}
}