using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// How a <see cref="DamageNegationBuffTemplate"/> takes damage off an incoming hit.
	/// </summary>
	public enum DamageNegationMode : byte
	{
		/// <summary>
		/// A POOL of damage points. Each hit spends from it, and the buff ends the moment the pool
		/// reaches zero — the classic absorb shield.
		/// </summary>
		/// <remarks>
		/// The only mode that spends <see cref="Buff.RemainingCharges"/>, and therefore the only one
		/// whose strength depends on state the reconcile has to carry.
		/// </remarks>
		Absorb = 0,

		/// <summary>
		/// A PERCENTAGE off every qualifying hit, for the buff's whole duration. Spends nothing and
		/// ends only on its timer.
		/// </summary>
		Reduce = 1,

		/// <summary>
		/// Every qualifying hit is negated outright for the buff's whole duration.
		/// </summary>
		/// <remarks>
		/// Equivalent to <see cref="Reduce"/> at 100%, kept separate so an author can express
		/// "immune" without it silently becoming "99%" through a percentage they can mistype, and so
		/// a tooltip can say the right thing.
		/// </remarks>
		Immune = 2,
	}

	/// <summary>
	/// Buff template that takes damage off incoming hits — the Block half of block and deflect.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Passive, and queried rather than applied.</b> Unlike
	/// <see cref="AttributeBuffTemplate"/> this writes nothing to the attribute ledger: negation is
	/// not a stat, it is a decision taken while one specific hit is being resolved, and it needs the
	/// attacker's position to answer the facing question. <c>DamageMitigation.Negate</c> is what
	/// reads it, from inside <c>CharacterDamageController.Damage</c>.
	/// </para>
	/// <para>
	/// <b>The three modes are the three block abilities.</b> A channelled shield holds
	/// <see cref="DamageNegationMode.Reduce"/> or <see cref="DamageNegationMode.Immune"/> for as
	/// long as the button is down, because a channel re-applies its buff every tick and a
	/// duration-only buff refreshes cleanly. A consumable barrier — "absorbs 500 damage, then
	/// vanishes" — is <see cref="DamageNegationMode.Absorb"/>. See the block-and-deflect section of
	/// the buff README for how to wire either through ECA.
	/// </para>
	/// <para>
	/// <b>Facing is what makes a shield a shield.</b> With <see cref="RequiresFacing"/> set, only
	/// damage arriving from within <see cref="FacingAngleDegrees"/> of where the defender is looking
	/// is negated — so a player who blocks the sword in front still takes the arrow in the back.
	/// The test runs server-side against real positions; see <c>DamageMitigation</c> for why it
	/// cannot be lag compensated and why that is the right answer here.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "New Damage Negation Buff Template", menuName = "FishMMO/Character/Buff/Damage Negation Buff", order = 3)]
	public class DamageNegationBuffTemplate : BaseBuffTemplate
	{
		/// <summary>How this buff takes damage off a hit.</summary>
		[Tooltip("Absorb spends a pool of damage points and ends at zero. Reduce takes a percentage off every hit. Immune negates every hit.")]
		public DamageNegationMode Mode = DamageNegationMode.Absorb;

		/// <summary>
		/// The pool size for <see cref="DamageNegationMode.Absorb"/>, or the percentage for
		/// <see cref="DamageNegationMode.Reduce"/>. Unused by <see cref="DamageNegationMode.Immune"/>.
		/// </summary>
		/// <remarks>
		/// One field for both because a template is only ever in one mode, and two fields would let a
		/// designer fill in the one the mode does not read and wonder why nothing happened.
		/// </remarks>
		[Tooltip("Absorb: damage points the pool holds. Reduce: percent taken off each hit (0-100). Immune: unused.")]
		[Min(0)]
		public int Amount = 100;

		/// <summary>
		/// True to negate only damage arriving from in front of the defender.
		/// </summary>
		[Tooltip("Negate only damage arriving from within FacingAngleDegrees of where the defender is looking.")]
		public bool RequiresFacing = true;

		/// <summary>
		/// Total spread of the protected arc, in degrees — 120 means 60 either side of forward.
		/// </summary>
		/// <remarks>
		/// Measured the same way <see cref="TargetOrdering.IsWithinCone"/> measures every other cone
		/// in the project, so "in front" means one thing across targeting and mitigation. An attacker
		/// standing exactly on the defender is outside every cone, which is the same rule and the
		/// same reason: a direction test cannot be satisfied by a point with no direction.
		/// </remarks>
		[Tooltip("Total spread of the protected arc in degrees. 120 protects 60 degrees either side of forward.")]
		[Range(0f, 360f)]
		public float FacingAngleDegrees = 120f;

		/// <summary>
		/// The physical shield this buff raises: a real volume with real dimensions, standing where
		/// the character holds it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>A different question from the arc above, answered for a different kind of damage.</b>
		/// <see cref="RequiresFacing"/> gates the MITIGATION — how much is taken off damage that
		/// reaches the character, whether that is a sword, a burning ground or an explosion, none of
		/// which has a meaningful impact point on a shield. This volume gates PHYSICAL INTERCEPTION:
		/// an ability object whose impact lands inside it never touched the character at all, so it
		/// is stopped outright rather than reduced, and the mode above never comes into it.
		/// </para>
		/// <para>
		/// Leaving <see cref="ShieldVolume.Shape"/> at <see cref="ShieldShape.None"/> gives a buff
		/// that only mitigates — a ward or a damage shield rather than a held shield. Setting it
		/// gives an object in the world that arrows stop against, and it is the same shape
		/// <c>ShieldInterceptAction</c> sweeps for things in flight.
		/// </para>
		/// </remarks>
		[Tooltip("Physical shield volume. Ability objects that strike inside it are stopped outright, whatever the mode above.")]
		public ShieldVolume Shield = new ShieldVolume();

		/// <summary>
		/// Charges spent each time <see cref="Shield"/> stops something. Zero — the default — means
		/// blocking does not wear the buff down.
		/// </summary>
		/// <remarks>
		/// The knob that separates a shield bounded by TIME from one bounded by PUNISHMENT. Zero
		/// suits a channelled block, which ends when the player releases it. A positive value against
		/// <see cref="DamageNegationMode.Absorb"/> makes each stopped projectile eat that much of the
		/// pool, so a barrier that has already turned three arrows has visibly less left in it.
		/// </remarks>
		[Tooltip("Charges spent per object the shield volume stops. 0 means blocking never wears it down.")]
		[Min(0)]
		public int VolumeBlockCost = 0;

		/// <summary>
		/// The pool a fresh application carries, which is <see cref="Amount"/> in
		/// <see cref="DamageNegationMode.Absorb"/> and nothing in the other two.
		/// </summary>
		/// <remarks>
		/// This is what makes the buff "disappear when the remaining amount hits 0": a non-zero
		/// value is what <see cref="Buff.IsSpent"/> tests, and the reduce and immune modes must NOT
		/// report one or they would be removed the first time anything asked.
		/// </remarks>
		public override int InitialCharges => Mode == DamageNegationMode.Absorb ? Amount : 0;

		/// <summary>
		/// Damage this buff takes off <paramref name="incoming"/>, without spending anything.
		/// </summary>
		/// <remarks>
		/// Pure, so the whole mitigation rule can be exercised without a character, a buff container
		/// or a physics scene — the same reason <c>LagCompensationTick.ResolveAnchor</c> and
		/// <c>CharacterPredictionController.IsTransformRedundant</c> are shaped this way. The caller
		/// is responsible for the facing test and for spending the pool; this only answers "how
		/// much".
		/// </remarks>
		/// <param name="incoming">Damage remaining after resistances and any earlier negation.</param>
		/// <param name="remainingCharges">The buff instance's pool, for <see cref="DamageNegationMode.Absorb"/>.</param>
		/// <returns>How much of <paramref name="incoming"/> this buff would take off.</returns>
		public int ResolveNegation(int incoming, int remainingCharges)
		{
			if (incoming <= 0)
			{
				return 0;
			}

			switch (Mode)
			{
				case DamageNegationMode.Immune:
					return incoming;

				case DamageNegationMode.Reduce:
					{
						/* Clamped rather than trusted, and rounded DOWN. A percentage above 100 would
						 * otherwise negate more than arrived and hand the caller a negative
						 * remainder; rounding down means a 1-damage hit against a 50% reduction still
						 * lands for 1, which is the direction that keeps chip damage meaningful. */
						int percent = Amount < 0 ? 0 : (Amount > 100 ? 100 : Amount);
						return (int)((long)incoming * percent / 100L);
					}

				default:
					{
						int pool = remainingCharges < 0 ? 0 : remainingCharges;
						return incoming < pool ? incoming : pool;
					}
			}
		}

		/// <summary>
		/// Appends a secondary tooltip describing what this buff blocks.
		/// </summary>
		/// <param name="builder">The tooltip builder to populate.</param>
		public override void SecondaryTooltip(TooltipBuilder builder)
		{
			string effect;
			switch (Mode)
			{
				case DamageNegationMode.Immune:
					effect = "Negates all damage";
					break;
				case DamageNegationMode.Reduce:
					effect = $"Reduces damage by {Amount}%";
					break;
				default:
					effect = $"Absorbs {Amount} damage";
					break;
			}
			builder.AddLine(effect, 20, TooltipColors.Stat);
			if (RequiresFacing)
			{
				builder.AddLine($"Front {FacingAngleDegrees:0}° only", 21, TooltipColors.Label);
			}
			string shield = Shield != null && Shield.IsActive ? Shield.Describe() : null;
			if (shield != null)
			{
				builder.AddLine($"Blocks projectiles: {shield}", 22, TooltipColors.Stat);
			}
		}

		/// <summary>
		/// Nothing to install. The pool is seeded by <see cref="Buff.RefreshCharges"/> from
		/// <see cref="InitialCharges"/>, and the negation itself is read at damage time.
		/// </summary>
		/// <remarks>
		/// Deliberately empty rather than absent: <see cref="BaseBuffTemplate.OnApply"/> is abstract,
		/// and a passive buff having nothing to do on apply is a real answer rather than an omission.
		/// </remarks>
		public override void OnApply(Buff buff, ICharacter target) { }

		/// <summary>Nothing to reverse — see <see cref="OnApply"/>.</summary>
		public override void OnRemove(Buff buff, ICharacter target) { }

		/// <summary>
		/// Refills the pool when a stack lands, so holding or re-casting a shield restores it.
		/// </summary>
		/// <remarks>
		/// <c>Buff.AddStack</c> calls this BEFORE incrementing <see cref="Buff.Stacks"/>, so the
		/// count after the operation is <c>Stacks + 1</c> — the same off-by-one
		/// <see cref="AttributeBuffTemplate.OnApplyStack"/> documents. Refreshing to the
		/// post-operation count is what makes two stacks hold twice the damage rather than the same
		/// pool twice.
		/// </remarks>
		public override void OnApplyStack(Buff buff, ICharacter target)
		{
			if (buff == null)
			{
				return;
			}
			int initial = InitialCharges;
			buff.RemainingCharges = initial <= 0 ? 0 : initial * (2 + (buff.Stacks < 0 ? 0 : buff.Stacks));
		}

		/// <summary>
		/// Trims the pool back to what the remaining stacks are worth.
		/// </summary>
		/// <remarks>
		/// <c>Buff.RemoveStack</c> decrements after this returns, so the post-operation count is
		/// <c>Stacks - 1</c>. Never RAISES the pool: a shield that has already spent most of its
		/// charge must not be topped up by losing a stack.
		/// </remarks>
		public override void OnRemoveStack(Buff buff, ICharacter target)
		{
			if (buff == null)
			{
				return;
			}
			int initial = InitialCharges;
			int ceiling = initial <= 0 ? 0 : initial * (buff.Stacks < 0 ? 0 : buff.Stacks);
			if (buff.RemainingCharges > ceiling)
			{
				buff.RemainingCharges = ceiling;
			}
		}
	}
}
