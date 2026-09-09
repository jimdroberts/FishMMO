using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// One character's half of a two-character exchange, as handed to
	/// <see cref="ICharacterInventorySystem.TryRunExchange"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Built on the main thread, inside the apply hop, while both characters' row locks are
	/// held and immediately after memory was mutated; it names which rows that mutation
	/// touched so the write can be captured there and applied on the worker as one unit with
	/// the other character's leg. Live <see cref="Item"/> references are read once, at capture,
	/// and never on the worker.
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

		/// <summary>
		/// Currency this character RECEIVES, which is NOT yet in memory when the leg is captured.
		/// </summary>
		/// <remarks>
		/// Deductions are applied to memory before the write (an escrow: a concurrent spend can
		/// only spend what is left), but credits are applied only after the commit. A credit
		/// applied before it could be spent during the write, and a refused write could then not
		/// take it back exactly. So the attribute row for <see cref="CurrencyTemplateID"/> is
		/// written as memory plus this amount, and the caller credits memory on success.
		/// </remarks>
		public long CurrencyCredit;

		/// <summary>The attribute template <see cref="CurrencyCredit"/> is added to. Zero when no credit.</summary>
		public int CurrencyTemplateID;

		/// <summary>
		/// Every inventory slot the exchange read or wrote on this character. Locked from the
		/// moment memory is applied until the exchange has committed or been undone, so that an
		/// undo is exact: nothing else can move into, out of, or merge with these slots while
		/// the outcome is still open.
		/// </summary>
		public List<int> TouchedSlots = new List<int>();
	}
}
