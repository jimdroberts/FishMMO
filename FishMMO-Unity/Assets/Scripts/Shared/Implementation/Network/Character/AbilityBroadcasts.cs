using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Broadcast for adding a known ability to a character.
	/// Contains the template ID of the ability to add.
	/// </summary>
	public struct KnownAbilityAddBroadcast : IBroadcast
	{
		/// <summary>Template ID of the ability to add.</summary>
		public int TemplateID;
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
	public struct KnownAbilityEventAddBroadcast : IBroadcast
	{
		/// <summary>Template ID of the ability event to add.</summary>
		public int TemplateID;
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
	public struct AbilityAddBroadcast : IBroadcast
	{
		/// <summary>Unique instance ID of the ability.</summary>
		public long ID;
		/// <summary>Template ID of the ability.</summary>
		public int TemplateID;
		/// <summary>List of event IDs associated with the ability.</summary>
		public int[] Events;
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
	/// </remarks>
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
