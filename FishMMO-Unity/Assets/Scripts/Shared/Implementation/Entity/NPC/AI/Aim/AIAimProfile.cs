using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// How well an NPC aims: which point on a target it shoots at, how long it needs to settle, and
	/// how far it misses by while it has not.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Assign on the archetype, never on the prefab.</b> The archetype is the single source of an
	/// NPC's brain, and aim quality is part of that brain — a prefab-level aim slot would let an NPC
	/// quietly disagree with the archetype it names, which is the failure the archetype design
	/// exists to prevent.
	/// </para>
	/// <para>
	/// <b>A null profile means perfect aim.</b> Every archetype that does not name one keeps exactly
	/// the behaviour it has today — an exact ray from the aim origin to the target's centre — so
	/// adding this system changes nothing until a profile is authored. That is deliberate: the
	/// profile is a tuning instrument, and the untuned default should be the behaviour everyone
	/// already has rather than a guess at what a stranger's aim ought to feel like.
	/// </para>
	/// <para>
	/// <b>The three dials are independent.</b> <see cref="BaseAccuracy"/> and
	/// <see cref="SpreadDegrees"/> say how far off a settled shot is; <see cref="LockSeconds"/> says
	/// how long settling takes; <see cref="LeadAccuracy"/> says whether it compensates for a target
	/// that is running. An NPC that is deadly against a stationary target and hopeless against a
	/// moving one, or the reverse, is a combination of these and not a new mechanism.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "New AI Aim Profile", menuName = "FishMMO/Character/NPC/AI/Aim Profile", order = 20)]
	public class AIAimProfile : ScriptableObject
	{
		[Header("Where to Aim")]
		[Tooltip("Which point on the target's body an ability is aimed at.")]
		public AIAimPoint AimPoint = AIAimPoint.Center;

		[Header("Accuracy")]
		[Range(0f, 1f)]
		[Tooltip("Accuracy reached once the target has been tracked for LockSeconds. 1 is a perfect shot; 0 is the full spread on every cast.")]
		public float BaseAccuracy = 1.0f;

		[Tooltip("Seconds of continuously tracking a target before accuracy reaches BaseAccuracy. 0 disables the ramp.")]
		public float LockSeconds = 0f;

		[Tooltip("Worst-case angular error in degrees, reached at zero accuracy. 0 is a perfect shot.")]
		public float SpreadDegrees = 0f;

		[Tooltip("Seconds of tracking required before the NPC will cast at all. 0 casts immediately. Requires LockSeconds to be greater than 0 to ever be satisfied.")]
		public float MinimumLockToFire = 0f;

		[Header("Target Movement")]
		[Range(0f, 1f)]
		[Tooltip("How much of the shot's flight time is compensated for. 0 ignores a moving target, 1 leads it perfectly.")]
		public float LeadAccuracy = 0f;

		/// <summary>
		/// Checks this profile for combinations that cannot behave as configured.
		/// </summary>
		/// <remarks>
		/// Same contract as <see cref="AIArchetypeTemplate.Validate"/>: every problem returned is one
		/// that would compile, run, and then quietly do nothing, so the shipped profiles can be
		/// asserted in an EditMode test rather than discovered in play.
		/// </remarks>
		/// <param name="problems">Receives one line per problem found. Cleared first.</param>
		/// <returns>True when the profile is internally consistent.</returns>
		public bool Validate(List<string> problems)
		{
			if (problems == null)
			{
				problems = new List<string>();
			}
			problems.Clear();

			/* An unsatisfiable or vacuous lock requirement, in either direction: a threshold on a
			 * value that starts above it, or one that cannot be reached before the target is lost. */
			if (MinimumLockToFire > 0f && LockSeconds <= 0f)
			{
				problems.Add($"'{name}': MinimumLockToFire is {MinimumLockToFire} but LockSeconds is 0 — accuracy is at its ceiling from the first tick, so the NPC simply always fires.");
			}
			else if (MinimumLockToFire > LockSeconds)
			{
				problems.Add($"'{name}': MinimumLockToFire ({MinimumLockToFire}) exceeds LockSeconds ({LockSeconds}) — the NPC can never hold a target long enough to fire.");
			}

			if (SpreadDegrees < 0f)
			{
				problems.Add($"'{name}': SpreadDegrees is negative — it is treated as zero, so the NPC shoots perfectly.");
			}

			if (LockSeconds < 0f)
			{
				problems.Add($"'{name}': LockSeconds is negative — it is treated as zero, so accuracy never ramps.");
			}

			if (SpreadDegrees <= 0f && BaseAccuracy >= 1f && LeadAccuracy >= 1f)
			{
				problems.Add($"'{name}': BaseAccuracy is 1 with no spread and full lead — the profile has no effect and should be left off the archetype so the intent is not mistaken for tuning.");
			}

			return problems.Count == 0;
		}
	}
}
