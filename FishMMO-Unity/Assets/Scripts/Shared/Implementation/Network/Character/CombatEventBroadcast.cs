using FishNet.Broadcast;
using FishNet.CodeGenerating;
using FishNet.Serializing;

namespace FishMMO.Shared
{
	/// <summary>
	/// What a <see cref="CombatEventBroadcast"/> describes.
	/// </summary>
	public enum CombatEventKind : byte
	{
		/// <summary>Health was removed. <see cref="CombatEventBroadcast.DamageTemplateID"/> names the type.</summary>
		Damage = 0,
		/// <summary>Health was restored.</summary>
		Heal = 1,
	}

	/// <summary>
	/// Tells everyone who can see a character that it just took damage or was healed.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Ability hits are resolved on the server only, and <c>CharacterDamageController</c> reports
	/// them through in-process static events — so before this message existed no client ever
	/// learned about a hit it did not simulate itself, and the floating combat numbers were
	/// invisible for every ability on every client. The owner's own predicted damage-over-time
	/// ticks did produce a label, and produced it again on every replay of the same tick.
	/// </para>
	/// <para>
	/// This is the single source of combat numbers on a client. The amount is the server's, after
	/// resistance, so what is shown is what actually happened. Sent unreliably to the target's
	/// observers — one lost label is not worth a resend — and coalesced per target per tick, so a
	/// burst of area hits on one creature costs one message rather than one per hit.
	/// </para>
	/// </remarks>
	[UseGlobalCustomSerializer]
	public struct CombatEventBroadcast : IBroadcast
	{
		/// <summary>NetworkObject id of the character that took the damage or the heal.</summary>
		public int TargetObjectID;

		/// <summary>NetworkObject id of the character responsible, or 0 when there was none.</summary>
		/// <remarks>
		/// Zero is FishNet's "unset" object id, so it can never collide with a real one. A damage
		/// tick from a lingering poison whose caster has left, or an environmental hazard, sends 0.
		/// </remarks>
		public int SourceObjectID;

		/// <summary>The amount, after every modifier the server applies. Never negative.</summary>
		public int Amount;

		/// <summary>What happened; a <see cref="CombatEventKind"/>.</summary>
		public byte Kind;

		/// <summary>
		/// Cached template id of the <c>DamageAttributeTemplate</c> for a damage event, or 0.
		/// </summary>
		/// <remarks>
		/// The client colours the label by damage type. The template id is the smallest handle that
		/// resolves through the existing scriptable-object cache on the client without a second
		/// table to keep in step; it is packed, so a small id costs one byte.
		/// </remarks>
		public int DamageTemplateID;

		/// <summary>
		/// How many separate hits <see cref="Amount"/> was summed from. Always at least one.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>The caster predicted each of them separately.</b> The coalescer merges every hit
		/// sharing a (source, kind, damage type) within one tick into a single entry, which is right
		/// for display — one readable number instead of a stack of labels. But the caster's client
		/// drew one predicted label PER HIT, and <c>PredictedCombatEvents.TryConfirm</c> consumes one
		/// pending entry per report. A three-projectile volley landing together therefore confirmed
		/// one prediction and left the other two to time out and grey themselves out, marking hits
		/// that landed as hits that were denied — the one place the prediction display lied.
		/// </para>
		/// <para>
		/// One packed byte-ish int, and it is 1 for every hit outside a multi-hit burst, so it costs
		/// a single byte in the overwhelming case.
		/// </para>
		/// </remarks>
		public int Occurrences;
	}

	/// <summary>
	/// Custom serializer for <see cref="CombatEventBroadcast"/>.
	/// </summary>
	/// <remarks>
	/// Written by hand rather than generated for two reasons: every field is packed, which the
	/// generated writer does for ints anyway but this makes explicit and testable; and the
	/// EditMode tests can round-trip the message without FishNet's IL post-processor having run.
	/// </remarks>
	public static class CombatEventBroadcastSerializer
	{
		/// <summary>Writes a <see cref="CombatEventBroadcast"/>. Discovered by FishNet's codegen by name.</summary>
		public static void WriteCombatEventBroadcast(this Writer writer, CombatEventBroadcast value)
		{
			writer.WriteInt32(value.TargetObjectID);
			writer.WriteInt32(value.SourceObjectID);
			writer.WriteInt32(value.Amount);
			writer.WriteUInt8Unpacked(value.Kind);
			writer.WriteInt32(value.DamageTemplateID);
			/* Written last so the field's arrival does not move any existing one. Clamped to at least
			 * one: a producer that leaves it default must still confirm the single prediction its hit
			 * produced, or every un-updated call site silently stops confirming anything. */
			writer.WriteInt32(value.Occurrences < 1 ? 1 : value.Occurrences);
		}

		/// <summary>Reads a <see cref="CombatEventBroadcast"/> in the order <see cref="WriteCombatEventBroadcast"/> wrote it.</summary>
		public static CombatEventBroadcast ReadCombatEventBroadcast(this Reader reader)
		{
			return new CombatEventBroadcast()
			{
				TargetObjectID = reader.ReadInt32(),
				SourceObjectID = reader.ReadInt32(),
				Amount = reader.ReadInt32(),
				Kind = reader.ReadUInt8Unpacked(),
				DamageTemplateID = reader.ReadInt32(),
				Occurrences = reader.ReadInt32(),
			};
		}
	}
}
