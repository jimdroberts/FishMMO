using FishNet.Broadcast;
using FishNet.CodeGenerating;
using FishNet.Serializing;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Carries changed character attributes to everyone observing a character.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What this replaces.</b> While state forwarding was enabled, an observer received the whole
	/// of <c>CharacterReconcileData</c> every tick, and its <c>Attributes</c> array is what kept a
	/// peer's attribute sheet current. Forwarding is off on every prefab now — relaying each owner's
	/// input and state to each observer is what made a busy scene unaffordable — so that channel is
	/// gone, and this message is what keeps observers correct instead. The spawn payload seeds the
	/// full sheet; this carries every change after that.
	/// </para>
	/// <para>
	/// <b>Why observers need attributes at all.</b> A resource's maximum is a formula over them. An
	/// observer that holds strength but not the ring that raised it draws the wrong maximum health
	/// under every bar, and any client-side display derived from a peer's attributes is wrong in the
	/// same way.
	/// </para>
	/// <para>
	/// <b>Delta.</b> Only attributes whose value or external modifier actually changed are written —
	/// one equip or one buff expiring costs one entry, not the character's whole sheet. Each entry
	/// carries its own <c>TemplateID</c> and its absolute values rather than an index into the
	/// previous array and a difference from it. That costs a few bytes per entry and buys two things
	/// worth far more: a receiver applies an entry correctly no matter what state it was in
	/// beforehand, and a message can never be decoded against a baseline the sender has since moved
	/// past. FishNet's difference-encoded deltas have exactly that failure mode, which is why they
	/// need a sequence number and a periodic absolute resend to be safe.
	/// </para>
	/// <para>
	/// <b>Reliable.</b> Attribute changes are rare and event driven — gear, buffs, levels — so the
	/// cost of reliability is negligible, and it removes the whole question of what a dropped update
	/// would leave behind. An unreliable attribute update that went missing would leave an observer
	/// holding a stale sheet until that same attribute happened to change again, which for a
	/// maximum-health ring could be never.
	/// </para>
	/// </remarks>
	[UseGlobalCustomSerializer]
	public struct CharacterAttributesBroadcast : IBroadcast
	{
		/// <summary>NetworkObject id of the character these attributes belong to.</summary>
		/// <remarks>
		/// A broadcast is not addressed to a NetworkBehaviour the way an RPC is; the handler is
		/// registered once per client and has to be told which character it is about.
		/// </remarks>
		public int CharacterObjectID;

		/// <summary>
		/// True when <see cref="Attributes"/> is the character's entire sheet rather than the
		/// entries that changed.
		/// </summary>
		/// <remarks>
		/// Sent when the sender has no usable baseline for this character — its first push, or after
		/// the attribute set itself was rebuilt. A receiver treats a full set as authoritative for
		/// every attribute it names; it does not clear attributes the message omits, because the
		/// sheet is fixed at spawn and an omission means "unchanged", never "gone".
		/// </remarks>
		public bool IsFullSet;

		/// <summary>The attributes that changed, or the whole sheet when <see cref="IsFullSet"/>.</summary>
		public AttributeReconcileEntry[] Attributes;
	}

	/// <summary>Wire format for <see cref="CharacterAttributesBroadcast"/>.</summary>
	/// <remarks>
	/// Hand written for the count: FishNet's generated array serializer writes a four-byte length
	/// and a null sentinel, where this message's count is bounded by the character's attribute sheet
	/// and a null array is simply an empty one. The cap also stops a malformed or hostile message
	/// making the reader allocate an arbitrarily large array before the stream runs out.
	/// </remarks>
	public static class CharacterAttributesBroadcastSerializer
	{
		/// <summary>Hard cap on entries in one message.</summary>
		/// <remarks>
		/// Comfortably above any real character's sheet (the project's databases hold a few dozen
		/// attributes) and small enough that the count fits two bytes.
		/// </remarks>
		public const int MAX_ATTRIBUTES = 4096;

		/// <summary>Writes a <see cref="CharacterAttributesBroadcast"/>.</summary>
		public static void WriteCharacterAttributesBroadcast(this Writer writer, CharacterAttributesBroadcast value)
		{
			writer.WriteInt32(value.CharacterObjectID);
			writer.WriteBoolean(value.IsFullSet);

			int count = value.Attributes?.Length ?? 0;
			if (count > MAX_ATTRIBUTES)
			{
				Log.Warning("CharacterAttributesBroadcast",
					$"Write count {count} exceeds limit {MAX_ATTRIBUTES}. Truncating to preserve stream integrity.");
				count = MAX_ATTRIBUTES;
			}

			writer.WriteUInt16((ushort)count);
			for (int i = 0; i < count; ++i)
			{
				value.Attributes[i].WriteTo(writer);
			}
		}

		/// <summary>Reads a <see cref="CharacterAttributesBroadcast"/>.</summary>
		public static CharacterAttributesBroadcast ReadCharacterAttributesBroadcast(this Reader reader)
		{
			CharacterAttributesBroadcast value = new CharacterAttributesBroadcast()
			{
				CharacterObjectID = reader.ReadInt32(),
				IsFullSet = reader.ReadBoolean(),
			};

			int count = reader.ReadUInt16();
			if (count > MAX_ATTRIBUTES)
			{
				/* Unlike a spawn payload there is no frame to seek past here: a broadcast is its own
				 * message, so returning an empty set costs this one update and nothing after it. */
				Log.Warning("CharacterAttributesBroadcast",
					$"Read count {count} exceeds limit {MAX_ATTRIBUTES}. Discarding this update.");
				value.Attributes = System.Array.Empty<AttributeReconcileEntry>();
				return value;
			}

			AttributeReconcileEntry[] entries = count > 0
				? new AttributeReconcileEntry[count]
				: System.Array.Empty<AttributeReconcileEntry>();

			for (int i = 0; i < count; ++i)
			{
				entries[i] = AttributeReconcileEntry.ReadFrom(reader);
			}

			value.Attributes = entries;
			return value;
		}
	}
}
