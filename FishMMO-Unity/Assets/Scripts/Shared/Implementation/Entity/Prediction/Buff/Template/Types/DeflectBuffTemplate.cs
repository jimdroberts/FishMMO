using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Buff template that turns incoming ability objects away instead of absorbing them — the
	/// Deflect half of block and deflect.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A deflect is a REJECTED hit, not a mitigated one.</b> Damage negation runs inside
	/// <c>CharacterDamageController.Damage</c>, after the projectile has already connected; this
	/// runs earlier, in <c>AbilityObject.ApplyHit</c>, before the hit is accepted at all. The
	/// projectile never counts as having struck the defender, its OnHit events never fire, and it
	/// carries on along a new heading — so nothing downstream has to know a deflect happened.
	/// </para>
	/// <para>
	/// <b>Which way it goes is not a choice.</b> The new heading is the incoming one reflected about
	/// the impact normal, and both the server and every observer compute it from the SAME normal —
	/// the one the server measured inside its rewind scope and put in
	/// <c>AbilityObjectHitBroadcast</c>. No new field on that message beyond the one bit that says a
	/// deflection happened, and no way for two peers to disagree about where the projectile went.
	/// </para>
	/// <para>
	/// <b>Two authored limits, and they compose.</b> <see cref="DeflectAngleDegrees"/> decides what
	/// can be deflected — a narrow guard covers the sword in front and nothing else — and
	/// <see cref="MaxDeflections"/> decides how often, spending
	/// <see cref="Buff.RemainingCharges"/> so a parry that turns one arrow ends the moment it does
	/// its job. Leave <see cref="MaxDeflections"/> at zero for a window that deflects everything
	/// that arrives while it is up, which is the short reactive parry the deflect ability wants.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "New Deflect Buff Template", menuName = "FishMMO/Character/Buff/Deflect Buff", order = 4)]
	public class DeflectBuffTemplate : BaseBuffTemplate
	{
		/// <summary>
		/// Total spread of the deflecting arc in degrees — 120 deflects things arriving from within
		/// 60 degrees either side of forward.
		/// </summary>
		/// <remarks>
		/// Measured through <see cref="TargetOrdering.IsWithinCone"/>, the same test every cone in
		/// the project uses, against the direction the incoming object came FROM. 360 deflects from
		/// any direction; a projectile sitting exactly on the defender is deflected by nothing,
		/// because a direction test cannot be satisfied by a point with no direction.
		/// </remarks>
		[Tooltip("Total spread of the deflecting arc in degrees. 120 covers 60 degrees either side of forward.")]
		[Range(0f, 360f)]
		public float DeflectAngleDegrees = 120f;

		/// <summary>
		/// How many objects this buff may turn away before it ends. Zero means no limit — the
		/// window's duration is the only bound.
		/// </summary>
		/// <remarks>
		/// Zero and one are the two abilities: a timed parry window that turns away everything
		/// arriving during it, and a single-use guard that is consumed by the first thing it stops.
		/// </remarks>
		[Tooltip("Objects this buff may deflect before ending. 0 means unlimited for the buff's duration.")]
		[Min(0)]
		public int MaxDeflections = 0;

		/// <inheritdoc/>
		/// <remarks>
		/// Deflections rather than damage points — see <see cref="Buff.RemainingCharges"/> for why
		/// one counter serves both. Zero when <see cref="MaxDeflections"/> is zero, which is what
		/// keeps <see cref="Buff.IsSpent"/> from ending an unlimited window the first time it is
		/// asked.
		/// </remarks>
		public override int InitialCharges => MaxDeflections;

		/// <summary>
		/// The heading an object should leave on after being turned away by this buff.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A mirror about the impact surface, which is both the physically obvious answer and the
		/// only one every peer can reproduce: <paramref name="impactNormal"/> is measured once, on
		/// the server, inside the rewind scope, and travels in the hit broadcast.
		/// </para>
		/// <para>
		/// A degenerate normal — a query that could not produce one — falls back to reversing the
		/// incoming heading. Straight back at the attacker is a worse deflection than a mirror but a
		/// far better one than <see cref="Quaternion.LookRotation"/> being handed a zero vector, and
		/// it is still identical on every peer.
		/// </para>
		/// </remarks>
		/// <param name="incomingHeading">The direction the object was travelling.</param>
		/// <param name="impactNormal">Surface normal the server measured at the impact.</param>
		/// <returns>The heading to redirect the object along.</returns>
		public static Vector3 ResolveDeflectedHeading(Vector3 incomingHeading, Vector3 impactNormal)
		{
			if (incomingHeading.sqrMagnitude < 1e-8f)
			{
				return Vector3.forward;
			}

			Vector3 heading = incomingHeading.normalized;
			if (impactNormal.sqrMagnitude < 1e-8f)
			{
				return -heading;
			}

			Vector3 reflected = Vector3.Reflect(heading, impactNormal.normalized);
			/* A mirror against a surface the object was travelling ALONG returns the incoming
			 * heading unchanged, which is not a deflection — the object would carry straight on
			 * through the defender it was supposed to have been stopped by. Reversing is the honest
			 * fallback and keeps the guarantee the caller relies on: after this, the object is not
			 * still heading at the defender. */
			return reflected.sqrMagnitude < 1e-8f || Vector3.Dot(reflected, heading) > 0.9999f
				? -heading
				: reflected.normalized;
		}

		/// <summary>
		/// Appends a secondary tooltip describing the deflect window.
		/// </summary>
		/// <param name="builder">The tooltip builder to populate.</param>
		public override void SecondaryTooltip(TooltipBuilder builder)
		{
			builder.AddLine($"Deflects projectiles within {DeflectAngleDegrees:0}°", 20, TooltipColors.Stat);
			if (MaxDeflections > 0)
			{
				builder.AddLine(MaxDeflections == 1 ? "Deflects one attack" : $"Deflects {MaxDeflections} attacks", 21, TooltipColors.Label);
			}
		}

		/// <summary>
		/// Nothing to install — the window is read at hit time. See
		/// <see cref="DamageNegationBuffTemplate.OnApply"/> for why this is empty rather than absent.
		/// </summary>
		public override void OnApply(Buff buff, ICharacter target) { }

		/// <summary>Nothing to reverse — see <see cref="OnApply"/>.</summary>
		public override void OnRemove(Buff buff, ICharacter target) { }

		/// <summary>
		/// Refills the deflection count when a stack lands.
		/// </summary>
		/// <remarks>
		/// <c>Buff.AddStack</c> increments AFTER this returns, so the post-operation count is
		/// <c>Stacks + 1</c> — the convention <see cref="AttributeBuffTemplate.OnApplyStack"/>
		/// documents.
		/// </remarks>
		public override void OnApplyStack(Buff buff, ICharacter target)
		{
			if (buff == null || MaxDeflections <= 0)
			{
				return;
			}
			buff.RemainingCharges = MaxDeflections * (2 + (buff.Stacks < 0 ? 0 : buff.Stacks));
		}

		/// <summary>
		/// Trims the deflection count back to what the remaining stacks are worth, never raising it.
		/// </summary>
		public override void OnRemoveStack(Buff buff, ICharacter target)
		{
			if (buff == null || MaxDeflections <= 0)
			{
				return;
			}
			int ceiling = MaxDeflections * (buff.Stacks < 0 ? 0 : buff.Stacks);
			if (buff.RemainingCharges > ceiling)
			{
				buff.RemainingCharges = ceiling;
			}
		}
	}
}
