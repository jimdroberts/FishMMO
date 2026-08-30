using System;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// One item owned by a character, in whichever container currently holds it.
	/// </summary>
	/// <remarks>
	/// Replaces <c>CharacterInventoryEntity</c>, <c>CharacterEquipmentEntity</c> and
	/// <c>CharacterBankEntity</c>. See <see cref="CharacterItemData"/> for why the three were
	/// merged: three tables meant three identity sequences, so a row id could not identify an item.
	/// </remarks>
	public class CharacterItemEntity : IVersionedEntity
	{
		/// <summary>
		/// Primary key, and the item's durable identity.
		/// </summary>
		/// <remarks>
		/// Database-generated on first insert and returned to the caller, which writes it back onto
		/// the runtime item. It survives slot moves and container moves, because
		/// <see cref="Container"/> and <see cref="Slot"/> are ordinary columns rather than part of
		/// the key.
		/// </remarks>
		public long ID { get; set; }

		/// <summary>
		/// Application-level concurrency token. Incremented on every write to detect stale updates.
		/// </summary>
		public long Version { get; set; }

		/// <summary>
		/// Foreign key to the owning character.
		/// </summary>
		public long CharacterID { get; set; }

		public CharacterEntity Character { get; set; }

		/// <summary>
		/// Which of the character's containers holds this item.
		/// </summary>
		public ItemContainerType Container { get; set; }

		/// <summary>
		/// Slot index within <see cref="Container"/>.
		/// </summary>
		public int Slot { get; set; }

		/// <summary>
		/// Template identifier for this item.
		/// </summary>
		public int TemplateID { get; set; }

		/// <summary>
		/// Randomization seed for item properties.
		/// </summary>
		public int Seed { get; set; }

		/// <summary>
		/// Quantity of the item in this slot.
		/// </summary>
		public uint Amount { get; set; }

		public DateTime TimeCreated { get; set; }

		public bool Deleted { get; set; }

		public DateTime? TimeDeleted { get; set; }
	}
}
