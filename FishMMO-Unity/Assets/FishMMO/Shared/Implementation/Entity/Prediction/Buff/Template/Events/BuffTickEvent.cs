using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA trigger fired on each tick of a buff, driving damage-over-time, heal-over-time, and any
	/// other periodic effect.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The event is fired with the buff's carrier as the target and the character that applied the
	/// buff as the initiator, so an <see cref="ApplyDamageAction"/> attached here behaves exactly
	/// like the same action on an ability's on-hit event: resistance is applied, threat is
	/// generated against the caster, the target can die of it, and the caster is credited.
	/// </para>
	/// <para>
	/// This is why a DoT does not need a bespoke buff-template subclass. Damage, healing, resource
	/// drain, chained debuffs, dispels and summons are all already ECA actions; a periodic version
	/// of any of them is this event plus the action, with no new C# to write. The condition
	/// branches work too — a poison that only ticks while its victim is moving is a condition, not
	/// a new template type.
	/// </para>
	/// <para>
	/// <b>Fires on the server only, once per tick.</b> An action here is a side effect — damage
	/// credit, threat, a chained debuff, achievement progress — and must happen exactly once, so
	/// <see cref="BaseBuffTemplate.InvokeTickEvents"/> suppresses it on every client pass and on
	/// the server's reconcile replays alike. The owning client still runs the buff's resource
	/// mutation for prediction; its health is corrected by the attribute reconcile.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "New Buff Tick Event", menuName = "FishMMO/Character/Buff/Events/Buff Tick Event", order = 0)]
	public class BuffTickEvent : Trigger
	{
	}
}
