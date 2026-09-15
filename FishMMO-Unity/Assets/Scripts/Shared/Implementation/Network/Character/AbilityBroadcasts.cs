using FishNet.Broadcast;
using FishNet.Serializing;
using FishNet.CodeGenerating;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Broadcast for adding a known ability to a character.
	/// Contains the template ID of the ability to add.
	/// </summary>
	[UseGlobalCustomSerializer]
	public struct KnownAbilityAddBroadcast : IBroadcast
	{
		/// <summary>Template ID of the ability to add.</summary>
		public int TemplateID;
	}

	/// <summary>Wire format for <see cref="KnownAbilityAddBroadcast"/>.</summary>
	/// <remarks>
	/// Hand written for one field. <c>TemplateID</c> is a deterministic 32-bit hash
	/// (<c>CachedScriptableObject.AddToCache</c>), so it spans the whole signed range and FishNet's
	/// signed-packed form zigzags all but a sixteenth of it past 2^28 and spends FIVE bytes where
	/// unpacked spends exactly four. The login sync ships the ENTIRE known-ability set in one
	/// <see cref="KnownAbilityAddMultipleBroadcast"/> — on the order of a hundred ids — so that
	/// byte is paid per known ability on every login. See <c>InventorySetItemBroadcast</c>.
	/// </remarks>
	public static class KnownAbilityAddBroadcastSerializer
	{
		/// <summary>Upper bound accepted for a known-ability array length, against a corrupt stream.</summary>
		/// <remarks>
		/// <para>
		/// Registry-bounded rather than capacity-bounded. One entry is one learned
		/// <c>BaseAbilityTemplate</c>, so the largest legitimate array — the whole known set shipped
		/// at login in one <see cref="KnownAbilityAddMultipleBroadcast"/> — cannot exceed the number
		/// of base ability templates authored in the project. That is currently under a hundred
		/// assets under <c>Assets/Templates/Entity/Abilities</c>, and there is no constant anywhere
		/// that caps it: nothing stops a designer adding the next one, so no exact bound exists to
		/// reference.
		/// </para>
		/// <para>
		/// This ceiling is therefore an explicitly generous, explicitly arbitrary one rather than a
		/// derived limit — two orders of magnitude above today's content, chosen so that ability
		/// authoring can grow for the life of the project without anyone rediscovering this file.
		/// The deliberate asymmetry: a bound set too low truncates a legitimate array and a player
		/// logs in missing abilities they own, which is a much worse bug than the large allocation
		/// the bound exists to prevent. A bound set too high still turns an unbounded
		/// <c>new T[2_000_000_000]</c> into a few tens of kilobytes, which is the entire point.
		/// </para>
		/// <para>
		/// Server → client, so the honest threat is a mangled or truncated stream — a reader that
		/// has lost alignment and is interpreting some other field as a length prefix — not a
		/// hostile client, which has no way to send this message to itself.
		/// </para>
		/// </remarks>
		public const int MaxKnownAbilities = 8192;

		/// <summary>Writes a <see cref="KnownAbilityAddBroadcast"/>.</summary>
		public static void WriteKnownAbilityAddBroadcast(this Writer writer, KnownAbilityAddBroadcast value)
		{
			writer.WriteInt32Unpacked(value.TemplateID);
		}

		/// <summary>Reads a <see cref="KnownAbilityAddBroadcast"/>.</summary>
		public static KnownAbilityAddBroadcast ReadKnownAbilityAddBroadcast(this Reader reader)
		{
			return new KnownAbilityAddBroadcast()
			{
				TemplateID = reader.ReadInt32Unpacked(),
			};
		}

		/// <summary>Writes an array of <see cref="KnownAbilityAddBroadcast"/>.</summary>
		/// <remarks>
		/// Explicit because the element carries <c>[UseGlobalCustomSerializer]</c>. That attribute
		/// tells FishNet the element has a hand-written serializer, and codegen then declines to
		/// synthesise one for the ARRAY as well — it emits nothing and says nothing at build time.
		/// The gap only appears when a broadcast carrying the array is actually sent, as
		/// "Write method not found for KnownAbilityAddBroadcast[]", by which point the send has already failed.
		/// A null array is written as length -1 so it round-trips as null rather than as empty.
		/// </remarks>
		public static void WriteKnownAbilityAddBroadcastArray(this Writer writer, KnownAbilityAddBroadcast[] value)
		{
			if (value == null)
			{
				writer.WriteInt32(-1);
				return;
			}

			writer.WriteInt32(value.Length);
			for (int i = 0; i < value.Length; i++)
			{
				writer.WriteKnownAbilityAddBroadcast(value[i]);
			}
		}

		/// <summary>Reads an array of <see cref="KnownAbilityAddBroadcast"/>.</summary>
		/// <remarks>
		/// A length past <see cref="MaxKnownAbilities"/> cannot be allocated and cannot be
		/// resynchronised past either — the entries behind it are only locatable by trusting the
		/// count just rejected — so the array comes back empty and nothing is learned. Only the
		/// sender's own -1, which is not a length at all, means null.
		/// </remarks>
		public static KnownAbilityAddBroadcast[] ReadKnownAbilityAddBroadcastArray(this Reader reader)
		{
			int length = reader.ReadInt32();
			if (length < 0)
			{
				return null;
			}

			if (length > MaxKnownAbilities)
			{
				return System.Array.Empty<KnownAbilityAddBroadcast>();
			}

			KnownAbilityAddBroadcast[] value = new KnownAbilityAddBroadcast[length];
			for (int i = 0; i < length; i++)
			{
				value[i] = reader.ReadKnownAbilityAddBroadcast();
			}

			return value;
		}
	}

	/// <summary>
	/// Broadcast for adding multiple known abilities to a character at once.
	/// Used for bulk updates or synchronization.
	/// </summary>
	public struct KnownAbilityAddMultipleBroadcast : IBroadcast
	{
		/// <summary>List of known abilities to add.</summary>
		public KnownAbilityAddBroadcast[] Abilities;
	}

	/// <summary>
	/// Broadcast for adding a known ability event to a character.
	/// Contains the template ID of the ability event to add.
	/// </summary>
	[UseGlobalCustomSerializer]
	public struct KnownAbilityEventAddBroadcast : IBroadcast
	{
		/// <summary>Template ID of the ability event to add.</summary>
		public int TemplateID;
	}

	/// <summary>Wire format for <see cref="KnownAbilityEventAddBroadcast"/>.</summary>
	/// <remarks>
	/// The same single-field case as <c>KnownAbilityAddBroadcast</c>: an ability event's
	/// <c>TemplateID</c> is a full-range deterministic hash, costing five packed bytes against four
	/// unpacked, and the login sync ships every known event in one
	/// <see cref="KnownAbilityEventAddMultipleBroadcast"/>.
	/// </remarks>
	public static class KnownAbilityEventAddBroadcastSerializer
	{
		/// <summary>Upper bound accepted for a known-ability-event array length, against a corrupt stream.</summary>
		/// <remarks>
		/// <para>
		/// Registry-bounded, exactly as <see cref="KnownAbilityAddBroadcastSerializer.MaxKnownAbilities"/>
		/// is: one entry is one learned ability event template, so the largest legitimate array —
		/// the whole known event set at login, in one
		/// <see cref="KnownAbilityEventAddMultipleBroadcast"/> — cannot exceed the number of event
		/// templates authored under <c>Assets/Templates/Entity/Abilities/Events</c>. No constant
		/// caps that count, so there is no exact bound to reference and this is a deliberately
		/// generous ceiling rather than a derived one.
		/// </para>
		/// <para>
		/// Held at the same value as the base-ability bound on purpose. The two arrive together in
		/// the same login sync and grow together as abilities are authored, so one number moving
		/// without the other would be the kind of asymmetry that makes a future reader guess. Event
		/// templates typically outnumber base abilities — several per ability — which is the other
		/// reason not to set this one tighter.
		/// </para>
		/// <para>
		/// Note this is a distinct concern from <see cref="AbilityAddBroadcastSerializer.MAX_EVENTS"/>,
		/// which bounds the events baked into ONE crafted ability. That is genuinely a handful and
		/// bounded by <c>AdditionalEventSlots</c>; this is the player's whole learned catalogue.
		/// </para>
		/// <para>
		/// Server → client, so the honest threat is a mangled or truncated stream rather than a
		/// hostile client.
		/// </para>
		/// </remarks>
		public const int MaxKnownAbilityEvents = 8192;

		/// <summary>Writes a <see cref="KnownAbilityEventAddBroadcast"/>.</summary>
		public static void WriteKnownAbilityEventAddBroadcast(this Writer writer, KnownAbilityEventAddBroadcast value)
		{
			writer.WriteInt32Unpacked(value.TemplateID);
		}

		/// <summary>Reads a <see cref="KnownAbilityEventAddBroadcast"/>.</summary>
		public static KnownAbilityEventAddBroadcast ReadKnownAbilityEventAddBroadcast(this Reader reader)
		{
			return new KnownAbilityEventAddBroadcast()
			{
				TemplateID = reader.ReadInt32Unpacked(),
			};
		}

		/// <summary>Writes an array of <see cref="KnownAbilityEventAddBroadcast"/>.</summary>
		/// <remarks>
		/// Explicit because the element carries <c>[UseGlobalCustomSerializer]</c>. That attribute
		/// tells FishNet the element has a hand-written serializer, and codegen then declines to
		/// synthesise one for the ARRAY as well — it emits nothing and says nothing at build time.
		/// The gap only appears when a broadcast carrying the array is actually sent, as
		/// "Write method not found for KnownAbilityEventAddBroadcast[]", by which point the send has already failed.
		/// A null array is written as length -1 so it round-trips as null rather than as empty.
		/// </remarks>
		public static void WriteKnownAbilityEventAddBroadcastArray(this Writer writer, KnownAbilityEventAddBroadcast[] value)
		{
			if (value == null)
			{
				writer.WriteInt32(-1);
				return;
			}

			writer.WriteInt32(value.Length);
			for (int i = 0; i < value.Length; i++)
			{
				writer.WriteKnownAbilityEventAddBroadcast(value[i]);
			}
		}

		/// <summary>Reads an array of <see cref="KnownAbilityEventAddBroadcast"/>.</summary>
		/// <remarks>
		/// A length past <see cref="MaxKnownAbilityEvents"/> cannot be allocated and cannot be
		/// resynchronised past either — the entries behind it are only locatable by trusting the
		/// count just rejected — so the array comes back empty and nothing is learned. Only the
		/// sender's own -1, which is not a length at all, means null.
		/// </remarks>
		public static KnownAbilityEventAddBroadcast[] ReadKnownAbilityEventAddBroadcastArray(this Reader reader)
		{
			int length = reader.ReadInt32();
			if (length < 0)
			{
				return null;
			}

			if (length > MaxKnownAbilityEvents)
			{
				return System.Array.Empty<KnownAbilityEventAddBroadcast>();
			}

			KnownAbilityEventAddBroadcast[] value = new KnownAbilityEventAddBroadcast[length];
			for (int i = 0; i < length; i++)
			{
				value[i] = reader.ReadKnownAbilityEventAddBroadcast();
			}

			return value;
		}
	}

	/// <summary>
	/// Broadcast for adding multiple known ability events to a character at once.
	/// Used for bulk updates or synchronization.
	/// </summary>
	public struct KnownAbilityEventAddMultipleBroadcast : IBroadcast
	{
		/// <summary>List of known ability events to add.</summary>
		public KnownAbilityEventAddBroadcast[] AbilityEvents;
	}

	/// <summary>
	/// Broadcast for adding an ability to a character, including its events.
	/// Contains the ability's instance ID, template ID, and associated event IDs.
	/// </summary>
	[UseGlobalCustomSerializer]
	public struct AbilityAddBroadcast : IBroadcast
	{
		/// <summary>Unique instance ID of the ability.</summary>
		public long ID;
		/// <summary>Template ID of the ability.</summary>
		public int TemplateID;
		/// <summary>List of event IDs associated with the ability.</summary>
		public int[] Events;
	}

	/// <summary>Wire format for <see cref="AbilityAddBroadcast"/>.</summary>
	/// <remarks>
	/// Hand written because every id in it but one is a template id. <c>TemplateID</c> and EVERY
	/// element of <see cref="AbilityAddBroadcast.Events"/> is a deterministic 32-bit hash
	/// (<c>CachedScriptableObject.AddToCache</c>), full-range by construction, and FishNet's
	/// signed-packed form spends FIVE bytes on each where unpacked spends exactly four — a crafted
	/// ability carries several events, and the login sync ships the whole ability set in one
	/// <see cref="AbilityAddMultipleBroadcast"/>. <c>ID</c> stays packed: it is a database sequence
	/// value and small, and unpacking a <c>long</c> would COST six or seven bytes. The event array's
	/// length prefix stays packed too — a count of a handful is one byte.
	/// </remarks>
	public static class AbilityAddBroadcastSerializer
	{
		/// <summary>
		/// Hard cap on the crafted event ids carried by one <see cref="AbilityAddBroadcast"/>.
		/// </summary>
		/// <remarks>
		/// The same bound, for the same reason, as <c>MAX_LEARNED_EVENTS</c> on the observer form of
		/// this message: an ability's event count is its template's baked events plus its
		/// <c>AdditionalEventSlots</c>, which is a handful. The cap exists so a malformed or hostile
		/// message cannot make the reader allocate an arbitrarily large array before the stream runs
		/// out — the count is read straight off the wire and is otherwise unbounded.
		/// </remarks>
		public const int MAX_EVENTS = 64;

		/// <summary>Upper bound accepted for a crafted-ability array length, against a corrupt stream.</summary>
		/// <remarks>
		/// <para>
		/// The weakest anchor of the seven bounds added here, and worth being honest about. The
		/// other arrays are one entry per authored template and so are bounded, however loosely, by
		/// the content in the project. These are crafted ability INSTANCES — each a database row
		/// with its own <c>ID</c> — and a player assembles them from combinations of templates, so
		/// the count is bounded by nothing in the codebase: no cap on crafting, no cap in the
		/// controller, and <c>Constants.Configuration.MaximumPlayerHotkeys</c> (12) bounds only what
		/// can be BOUND to a key, not what can be owned.
		/// </para>
		/// <para>
		/// So this is a clearly-labelled generous ceiling, not a derived limit, and it is set high
		/// deliberately: the failure mode of a bound that is too low is that a dedicated player's
		/// login sync silently drops the abilities past it, and abilities are the one thing on this
		/// list that cost real time to acquire. Eight thousand instances is a long way past any
		/// plausible collection and still a trivial allocation, which is the trade this bound is
		/// making — it exists only to keep a corrupt length from turning into a multi-gigabyte
		/// <c>new AbilityAddBroadcast[...]</c>.
		/// </para>
		/// <para>
		/// If crafted abilities ever gain a real per-character cap, this should be re-anchored to it
		/// rather than left as a guess.
		/// </para>
		/// <para>
		/// Server → client, so the realistic trigger is a mangled or truncated stream rather than a
		/// hostile client.
		/// </para>
		/// </remarks>
		public const int MaxAbilities = 8192;

		/// <summary>Writes an <see cref="AbilityAddBroadcast"/>. A null event array travels as length 0.</summary>
		public static void WriteAbilityAddBroadcast(this Writer writer, AbilityAddBroadcast value)
		{
			writer.WriteInt64(value.ID);
			writer.WriteInt32Unpacked(value.TemplateID);

			int count = value.Events != null ? value.Events.Length : 0;
			if (count > MAX_EVENTS)
			{
				Log.Warning("AbilityAddBroadcast",
					$"Write event count {count} exceeds limit {MAX_EVENTS}. Truncating to preserve stream integrity.");
				count = MAX_EVENTS;
			}

			writer.WriteInt32(count);
			for (int i = 0; i < count; ++i)
			{
				writer.WriteInt32Unpacked(value.Events[i]);
			}
		}

		/// <summary>Reads an <see cref="AbilityAddBroadcast"/>. An empty event array reads back as an empty array, never null.</summary>
		public static AbilityAddBroadcast ReadAbilityAddBroadcast(this Reader reader)
		{
			long id = reader.ReadInt64();
			int templateID = reader.ReadInt32Unpacked();

			int count = reader.ReadInt32();
			if (count < 0 || count > MAX_EVENTS)
			{
				/* Cannot resynchronise past a count that is not trusted — the bytes to skip are
				 * derived from the count just rejected — so the ability is dropped rather than
				 * half-read. The broadcast reader discards the remainder of the packet. */
				Log.Error("AbilityAddBroadcast",
					$"Read event count {count} is outside [0, {MAX_EVENTS}]. Dropping the ability.");
				return new AbilityAddBroadcast() { ID = 0, TemplateID = 0, Events = new int[0] };
			}

			int[] events = count > 0 ? new int[count] : new int[0];
			for (int i = 0; i < count; ++i)
			{
				events[i] = reader.ReadInt32Unpacked();
			}

			return new AbilityAddBroadcast()
			{
				ID = id,
				TemplateID = templateID,
				Events = events,
			};
		}

		/// <summary>Writes an array of <see cref="AbilityAddBroadcast"/>.</summary>
		/// <remarks>
		/// Explicit because the element carries <c>[UseGlobalCustomSerializer]</c>. That attribute
		/// tells FishNet the element has a hand-written serializer, and codegen then declines to
		/// synthesise one for the ARRAY as well — it emits nothing and says nothing at build time.
		/// The gap only appears when a broadcast carrying the array is actually sent, as
		/// "Write method not found for AbilityAddBroadcast[]", by which point the send has already failed.
		/// A null array is written as length -1 so it round-trips as null rather than as empty.
		/// </remarks>
		public static void WriteAbilityAddBroadcastArray(this Writer writer, AbilityAddBroadcast[] value)
		{
			if (value == null)
			{
				writer.WriteInt32(-1);
				return;
			}

			writer.WriteInt32(value.Length);
			for (int i = 0; i < value.Length; i++)
			{
				writer.WriteAbilityAddBroadcast(value[i]);
			}
		}

		/// <summary>Reads an array of <see cref="AbilityAddBroadcast"/>.</summary>
		/// <remarks>
		/// A length past <see cref="MaxAbilities"/> cannot be allocated and cannot be resynchronised
		/// past either — and less so here than anywhere else in this file, because each element is
		/// variable length (its own event count), so the bytes to skip are not even computable from
		/// the rejected count. The array comes back empty. Only the sender's own -1, which is not a
		/// length at all, means null.
		/// </remarks>
		public static AbilityAddBroadcast[] ReadAbilityAddBroadcastArray(this Reader reader)
		{
			int length = reader.ReadInt32();
			if (length < 0)
			{
				return null;
			}

			if (length > MaxAbilities)
			{
				return System.Array.Empty<AbilityAddBroadcast>();
			}

			AbilityAddBroadcast[] value = new AbilityAddBroadcast[length];
			for (int i = 0; i < length; i++)
			{
				value[i] = reader.ReadAbilityAddBroadcast();
			}

			return value;
		}
	}

	/// <summary>
	/// Broadcast for adding multiple abilities to a character at once.
	/// Used for bulk updates or synchronization.
	/// </summary>
	public struct AbilityAddMultipleBroadcast : IBroadcast
	{
		/// <summary>List of abilities to add.</summary>
		public AbilityAddBroadcast[] Abilities;
	}

	/// <summary>
	/// Client → server. The player asked to forget one of their crafted abilities.
	/// </summary>
	/// <remarks>
	/// Names the ability's row identity, never its template. The delete matches
	/// <c>WHERE id = ?</c>, and a template id would either match nothing while reporting success or,
	/// on a table holding several rows for one template, remove the wrong one.
	/// </remarks>
	public struct AbilityForgetBroadcast : IBroadcast
	{
		/// <summary>Unique instance ID of the ability to forget.</summary>
		public long AbilityID;
	}

	/// <summary>
	/// Why a forget request was refused.
	/// </summary>
	public enum AbilityForgetFailure : byte
	{
		/// <summary>The ability was forgotten.</summary>
		None = 0,
		/// <summary>The character does not know an ability with that instance ID.</summary>
		Unknown = 1,
		/// <summary>The database write failed; the ability is still known.</summary>
		PersistFailed = 2,
		/// <summary>A forget for this connection is already in flight.</summary>
		Busy = 3,
	}

	/// <summary>
	/// Server → owner. The answer to <see cref="AbilityForgetBroadcast"/>.
	/// </summary>
	/// <remarks>
	/// The confirmation, not just the refusal. The panel removes the row on this message rather than
	/// on the click, so a forget that was refused leaves the ability where it was and the player is
	/// told why — the same contract the craft panel keeps with
	/// <see cref="AbilityCraftResultBroadcast"/>.
	/// </remarks>
	public struct AbilityForgetResultBroadcast : IBroadcast
	{
		/// <summary>Unique instance ID the request named.</summary>
		public long AbilityID;
		/// <summary>True when the ability was forgotten.</summary>
		public bool Success;
		/// <summary>Why it was refused, or <see cref="AbilityForgetFailure.None"/>.</summary>
		public AbilityForgetFailure Failure;
	}

	/// <summary>
	/// Server → observers. One character started or stopped an activation.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What observers could see before this.</b> Only the ability OBJECT: a projectile appeared
	/// and flew, reproduced from <c>AbilityActivatedBroadcast</c>. An ability that spawns no world
	/// object — a self-buff, a pet summon, anything without an object prefab — told observers
	/// nothing at all, and a consumable told them nothing ever. There was no way to look at another
	/// character and see that it was doing something.
	/// </para>
	/// <para>
	/// This is sent from the activation state machine rather than from the spawn path, which is
	/// what makes it cover every activation there is, object or no object, ability or consumable.
	/// </para>
	/// <para>
	/// <b>Only what the receiver cannot already know.</b> Duration and ability type are NOT sent:
	/// both live on the template, templates are immutable and loaded identically on every peer,
	/// and the receiver resolves the ability from the caster's own <c>KnownAbilities</c>. Sending
	/// them would be paying, per cast per observer, for two numbers already in memory. An
	/// observer-side animation hook reads the type from the same template, so adding one later
	/// still does not change this message.
	/// </para>
	/// <para>
	/// <b>And two shapes, named by the first byte.</b> A stop carries neither the tick nor the
	/// consumable flag, because its handler reads neither — see
	/// <see cref="CharacterCastBroadcastSerializer"/>, which lives beside the other two observer
	/// formats rather than here.
	/// </para>
	/// </remarks>
	[UseGlobalCustomSerializer]
	public struct CharacterCastBroadcast : IBroadcast
	{
		/// <summary>The casting character's network object id.</summary>
		public int CasterObjectID;

		/// <summary>
		/// The ability INSTANCE id, or the item template id when <see cref="IsConsumable"/>.
		/// </summary>
		/// <remarks>
		/// An instance id rather than a template id, for the same reason
		/// <c>AbilityActivatedBroadcast</c> carries one: an observer holds the caster's
		/// <c>KnownAbilities</c> — filled by the spawn payload and kept current by
		/// <c>AbilityLearnedObserverBroadcast</c> — so it can resolve the instance and name the
		/// crafted ability the player actually built, rather than the base template it was built
		/// from. <c>TryGetAbilityForVisuals</c> is the same lookup the activation handler uses.
		/// </remarks>
		public long ReferenceID;

		/// <summary>True when this is an item being used rather than an ability.</summary>
		public bool IsConsumable;

		/// <summary>
		/// The server tick the activation started on.
		/// </summary>
		/// <remarks>
		/// The message spends a network delay in flight, during which the cast has been running.
		/// The receiver measures that delay from this and starts the bar partway, rather than
		/// restarting it — which would leave every observed cast finishing late by one trip.
		/// </remarks>
		public uint ServerTick;

		/// <summary>True when the activation began; false when it ended, however it ended.</summary>
		public bool Started;
	}
}
