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
