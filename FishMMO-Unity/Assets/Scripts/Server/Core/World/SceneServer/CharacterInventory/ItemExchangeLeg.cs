using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// One character's half of a two-character exchange, as handed to
	/// <see cref="ICharacterInventorySystem.TryPersistExchange"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The in-memory containers have already been mutated by the time this exists; it names
	/// which rows that mutation touched so the write can be captured on the main thread and
	/// applied on a worker as one unit with the other character's leg. Live <see cref="Item"/>
	/// references are read once, at capture, and never on the worker.
	/// </para>
	/// <para>
	/// An item that moved between the two characters whole appears ONLY in the receiver's
	/// <see cref="ChangedInventoryItems"/>: its row keeps its identity and the upsert moves it
	/// by rewriting <c>character_id</c>. The giver must not also list it as removed, or the
	/// delete would race the move. An item that merged entirely into a stack the receiver
	/// already held ceased to exist as a row and IS listed by the giver as removed; the stack
	/// it merged into is listed by the receiver as changed.
	/// </para>
	/// </remarks>
	public sealed class ItemExchangeLeg
	{
		/// <summary>The character this leg writes for.</summary>
		public IPlayerCharacter Character;

		/// <summary>
		/// Inventory items whose rows must be written: reduced stacks, stacks that grew by a
		/// merge, items placed in a slot — including items that arrived from the other
		/// character, which carry their existing identity and are re-owned by the write.
		/// </summary>
		public List<Item> ChangedInventoryItems = new List<Item>();

		/// <summary>Inventory rows that ceased to exist, with the versions that authorise the deletes.</summary>
		public List<RemovedItemRecord> RemovedInventoryItems = new List<RemovedItemRecord>();

		/// <summary>
		/// True when the character's currency moved, so the attribute sheet rides in the same
		/// transaction as the item rows. Half a trade landing — the items without the coin —
		/// is exactly the failure this exists to rule out.
		/// </summary>
		public bool PersistAttributes;

		/// <summary>
		/// Currency this character paid to the other, for the economy ledger. Zero writes no
		/// ledger row.
		/// </summary>
		public long CurrencyPaid;

		/// <summary>The ledger reason to record <see cref="CurrencyPaid"/> under.</summary>
		public CurrencyMovementReason LedgerReason = CurrencyMovementReason.PlayerTrade;
	}
}
