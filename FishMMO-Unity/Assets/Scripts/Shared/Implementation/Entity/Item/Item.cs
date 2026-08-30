using System;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents an item instance in the game, including stackable, equippable, and generated properties.
	/// Handles initialization, attribute management, and tooltip generation.
	/// Implements <see cref="ITooltip"/> for consistent UI tooltip display.
	/// </summary>
	public class Item : ITooltip
	{
		/// <summary>
		/// The item generator responsible for random attributes and generation logic.
		/// </summary>
		public ItemGenerator Generator;

		/// <summary>
		/// The equippable component for this item, if applicable.
		/// </summary>
		public ItemEquippable Equippable;

		/// <summary>
		/// The stackable component for this item, if applicable.
		/// </summary>
		public ItemStackable Stackable;

		/// <summary>
		/// Event triggered when the item is destroyed.
		/// </summary>
		public event Action OnDestroy;

		/// <summary>
		/// The item template defining base properties and attributes.
		/// </summary>
		public BaseItemTemplate Template { get; private set; }

		/// <summary>
		/// This item's identity: the primary key of its row in <c>character_item</c>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>There is exactly one item identity, and this is it.</b> There used to be two — this,
		/// and a process-local <c>InstanceID</c> counter that the attribute ledger keyed on — because
		/// the database could not supply an identity that was usable. Items lived in three tables
		/// (<c>character_inventory</c>, <c>character_equipment</c>, <c>character_bank</c>), each row
		/// was keyed <c>(character_id, slot)</c>, and each table had its own identity sequence. So a
		/// row id named a SLOT rather than an item: it changed when the item moved, it was reused by
		/// the next item through that slot, and the same number named three different items across
		/// the three tables. Keying an attribute contribution by it would have merged the bonuses of
		/// every item that ever passed through a socket.
		/// </para>
		/// <para>
		/// The single <c>character_item</c> table removed all three problems at once, so the second
		/// identity had nothing left to do. <c>container</c> and <c>slot</c> are now ordinary columns
		/// on a row keyed by this value, which means it survives a move between slots, a move between
		/// containers, and a relog.
		/// </para>
		/// <para>
		/// <b>Zero means "not yet written".</b> An item created at runtime — loot, a quest reward, a
		/// merchant purchase, a stack split — has no identity until its first persist returns one,
		/// which is what <see cref="AssignPersistentID"/> is for. Nothing may key durable state by a
		/// zero id.
		/// </para>
		/// </remarks>
		public long ID { get; private set; }

		/// <summary>
		/// Records the identity the database assigned to this item on its first write.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Publishes the item's attribute contribution, which is why this is a method.</b> An item
		/// equipped before its first persist returned has written NO ledger entries at all:
		/// <c>ItemGenerator.TryResolveLedgerSource</c> declines for a zero id, because zero is the
		/// absence of an identity and two such items would collide on <c>ModifierSource.Item(0)</c>.
		/// The release/re-apply pair below is what states the contribution for the first time, under
		/// the identity the database has just issued. The release half is a no-op today (there is
		/// nothing under the old key to release) and is kept because it is the correct shape if the
		/// zero-id rule ever changes; <c>SetSource</c> states a whole contribution rather than adding
		/// to one, so running both is idempotent either way.
		/// </para>
		/// <para>
		/// <b>And derives the generation seed, for the same reason.</b> <see cref="Initialize"/>
		/// derives a generated item's seed from its id, and an item built with no id could not do
		/// that — so a looted weapon rolled its attributes from seed 0, and the reload after logout
		/// re-derived a real seed from the id the database had meanwhile assigned and rolled a
		/// DIFFERENT set. Deriving here closes that: the item's stats stop changing behind the
		/// player's back at the next relog. The re-key above then publishes the corrected values,
		/// because it re-reads the generator after this runs.
		/// </para>
		/// <para>
		/// Ignores a second call with the same id, and refuses a non-positive one. Reassigning a
		/// live identity is not a thing that can legitimately happen and would silently move the
		/// item's ledger key, so it is refused rather than accommodated.
		/// </para>
		/// </remarks>
		/// <param name="id">The identity the database assigned. Must be positive.</param>
		/// <returns>True when the identity was applied.</returns>
		public bool AssignPersistentID(long id)
		{
			if (id <= 0 || ID == id)
			{
				return false;
			}

			if (ID != 0)
			{
				Log.Error("Item", $"Refusing to reassign item identity {ID} to {id} ('{Name}'). " +
					"An item's id is fixed once the database has issued one; changing it would move " +
					"its attribute-ledger key out from under any contribution it has applied.");
				return false;
			}

			// Non-null only while this item is equipped AND generated, which is the only combination
			// that has written ledger entries under the old key.
			ICharacter equippedTo = IsGenerated && IsEquippable ? Equippable.Character : null;
			if (equippedTo != null)
			{
				Generator.RemoveAttributes(equippedTo);
			}

			ID = id;

			if (IsGenerated && Generator.Seed == 0)
			{
				Generator.Seed = DeriveSeed(id);
			}

			if (equippedTo != null)
			{
				Generator.ApplyAttributes(equippedTo);
			}

			return true;
		}

		/// <summary>
		/// The generation seed an item with this identity rolls its attributes from.
		/// </summary>
		/// <remarks>
		/// Shared by <see cref="Initialize"/> and <see cref="AssignPersistentID"/> so the seed an
		/// item is created with and the seed it is reloaded with cannot drift apart — which is
		/// exactly what happened while only the load path derived one.
		/// </remarks>
		/// <param name="id">The item's identity. Zero yields zero: there is nothing to derive from.</param>
		public static int DeriveSeed(long id)
		{
			if (id == 0)
			{
				return 0;
			}

			byte[] longBytes = BitConverter.GetBytes(id);

			// Integers from the first and last 4 bytes of the long. The high word is preferred so
			// that consecutive ids do not produce consecutive seeds; it is zero for small ids, in
			// which case the low word is the only thing there is.
			int high = BitConverter.ToInt32(longBytes, 4);
			int low = BitConverter.ToInt32(longBytes, 0);
			return high > 0 ? high : low;
		}

		/// <summary>
		/// Version number for this item instance, used for client synchronization and updates.
		/// Incremented whenever the item's state changes in a way that requires client updates.
		/// </summary>
		public long Version;

		/// <summary>
		/// The slot index this item is currently assigned to.
		/// </summary>
		public int Slot;

		/// <summary>
		/// Gets the icon sprite from the item template.
		/// </summary>
		public Sprite Icon { get { return Template?.Icon; } }

		/// <summary>
		/// Gets the display name from the item template.
		/// </summary>
		public string Name { get { return Template?.Name; } }

		/// <summary>
		/// Returns true if the item has a generator (is randomly generated).
		/// </summary>
		public bool IsGenerated { get { return Generator != null; } }

		/// <summary>
		/// Returns true if the item is equippable.
		/// </summary>
		public bool IsEquippable { get { return Equippable != null; } }

		/// <summary>
		/// Returns true if the item is stackable.
		/// </summary>
		public bool IsStackable { get { return Stackable != null; } }

		/// <summary>
		/// Constructs an item from a template and amount, initializing all components.
		/// <para>
		/// IMPORTANT: This constructor creates items with ID=0. The ID is assigned by the database
		/// on first persist, so an item built here is not yet a database row. It is still a fully
		/// formed gameplay item: every server-side grant path (merchant purchase, quest and
		/// achievement rewards, gathering nodes, world pickups, <c>GiveItemAction</c>) builds items
		/// through here.
		/// </para>
		/// </summary>
		/// <remarks>
		/// This used to construct only the stackable component, leaving <see cref="Equippable"/> and
		/// <see cref="Generator"/> null. <see cref="EquipmentController"/> refuses any item whose
		/// <see cref="IsEquippable"/> is false, so a sword you had just bought or looted could not be
		/// equipped at all until you relogged and the item came back through
		/// <see cref="Item(long, int, int, uint)"/>, which does call <see cref="Initialize"/>. Both
		/// constructors now go through the same initialization; there is no second, weaker kind of Item.
		/// <para>
		/// The seed is passed as 0 deliberately: <see cref="Initialize"/> derives one from the item's
		/// id, and an item built here has none yet. <see cref="AssignPersistentID"/> derives it when
		/// the first persist returns the id, so the attributes this item rolls are the same ones it
		/// will roll when it is reloaded. (They were not, before that: a looted weapon generated from
		/// seed 0 and came back after a relog with a different roll.)
		/// </para>
		/// </remarks>
		/// <param name="template">The item template.</param>
		/// <param name="amount">The stack amount.</param>
		public Item(BaseItemTemplate template, uint amount)
		{
			Slot = -1;
			Template = template;

			// Initialize dereferences Template unconditionally; a null template is a caller error
			// elsewhere, but it must not become a NullReferenceException inside a constructor.
			if (template != null)
			{
				Initialize(0, amount, 0);
			}
		}

		/// <summary>
		/// Constructs an item from an ID, seed, template, and amount, initializing all components.
		/// </summary>
		/// <param name="id">The item ID.</param>
		/// <param name="seed">The random seed for generation.</param>
		/// <param name="template">The item template.</param>
		/// <param name="amount">The stack amount.</param>
		public Item(long id, int seed, BaseItemTemplate template, uint amount)
		{
			ID = id;
			Slot = -1;
			Template = template;

			Initialize(id, amount, seed);
		}

		/// <summary>
		/// Constructs an item from an ID, seed, template ID, and amount, initializing all components.
		/// </summary>
		/// <param name="id">The item ID.</param>
		/// <param name="seed">The random seed for generation.</param>
		/// <param name="templateID">The template ID.</param>
		/// <param name="amount">The stack amount.</param>
		public Item(long id, int seed, int templateID, uint amount)
		{
			ID = id;
			Slot = -1;
			Template = BaseItemTemplate.Get<BaseItemTemplate>(templateID);

			Initialize(id, amount, seed);
		}

		/// <summary>
		/// Initializes the item, setting up stackable, equippable, and generator components as needed.
		/// Handles seed logic for random generation and event wiring for attribute changes.
		/// </summary>
		/// <remarks>
		/// Internal because it writes <see cref="ID"/> directly. Reassigning an item's identity is
		/// <see cref="AssignPersistentID"/>'s job — it re-keys any contribution the item has already
		/// applied, which this does not — so the only callers are this type's own constructors.
		/// </remarks>
		/// <param name="id">The item ID.</param>
		/// <param name="amount">The stack amount.</param>
		/// <param name="seed">The random seed for generation.</param>
		internal void Initialize(long id, uint amount, int seed)
		{
			ID = id;

			bool initializeEquippable = false;
			bool initializeGenerator = false;

			// Check if the item is stackable and initialize if needed.
			if (amount > 0)
			{
				if (Stackable == null)
				{
					if (Template.MaxStackSize > 1)
					{
						Stackable = new ItemStackable(this, amount.Clamp(1, Template.MaxStackSize));
					}
				}
				else
				{
					Stackable.Amount = amount;
				}
			}

			// Ensure ItemEquippable is created if it's an equippable item type.
			if (Equippable == null &&
				Template as EquippableItemTemplate != null)
			{
				initializeEquippable = true;
				Equippable = new ItemEquippable();
			}

			// Ensure ItemGenerator is created if the item can be generated.
			if (Generator == null &&
				Template.Generate)
			{
				initializeGenerator = true;
				Generator = new ItemGenerator();

				// Get the item's seed if none is provided. Shared with AssignPersistentID so an
				// item that gets its identity later rolls the same attributes it will roll when it
				// is reloaded from that identity.
				if (seed == 0)
				{
					seed = DeriveSeed(ID);
				}
			}

			// Finalize initialization of components and wire events.
			if (initializeEquippable)
			{
				Equippable?.Initialize(this);
			}
			if (initializeGenerator)
			{
				Generator?.Initialize(this, seed);

				if (initializeEquippable)
				{
					Equippable.OnEquip += ItemEquippable_OnEquip;
					Equippable.OnUnequip += ItemEquippable_OnUnequip;
				}
			}
		}

		/// <summary>
		/// Destroys the item, cleaning up generator, equippable, and stackable components and detaching events.
		/// </summary>
		public void Destroy()
		{
			/* Unequip BEFORE detaching the handlers.
			 *
			 * Equippable.Destroy() unequips, which raises OnUnequip, and this item's own handler is
			 * what calls ItemGenerator.RemoveAttributes. Detaching first meant that event fired into
			 * an empty invocation list, so every generated modifier an equipped item had applied
			 * stayed on the character's ExternalModifier after the item was destroyed. Clients
			 * happen to recover from the next spawn payload; the server has nothing that re-asserts
			 * it, so a pooled character carried the previous occupant's gear bonuses. */
			if (Equippable != null)
			{
				Equippable.Destroy();
			}
			if (Generator != null)
			{
				if (IsEquippable)
				{
					Equippable.OnEquip -= ItemEquippable_OnEquip;
					Equippable.OnUnequip -= ItemEquippable_OnUnequip;
				}
				Generator.Destroy();
			}
			// Zeroing the stack is part of destruction, not an optimisation. Destroy() used to
			// detach the components and raise OnDestroy while leaving Stackable.Amount intact, so
			// anything that re-read the item afterwards still saw a live stack. The visible symptom
			// was infinite consumables: ConsumableTemplate.Invoke destroyed the item on its last
			// charge without decrementing, and CanConsume then kept reporting enough charges for
			// the rest of the session.
			if (Stackable != null)
			{
				Stackable.Amount = 0;
			}
			OnDestroy?.Invoke();
		}

		/// <summary>
		/// Determines if this item matches another item, comparing template ID and generation seed.
		/// Used for stacking and item comparison logic.
		/// </summary>
		/// <param name="other">The other item to compare.</param>
		/// <returns>True if the items match, false otherwise.</returns>
		public bool IsMatch(Item other)
		{
			// Parenthesized for clarity: && binds before ||.
			return Template.ID == other.Template.ID &&
					(IsGenerated && other.IsGenerated && Generator.Seed == other.Generator.Seed ||
					!IsGenerated && !other.IsGenerated);
		}

		/// <summary>
		/// Returns the formatted tooltip string for this item, including ID, slot, template tooltip, and generator info.
		/// </summary>
		/// <returns>The formatted tooltip string.</returns>
		public string Tooltip()
		{
			using (var builder = new TooltipBuilder())
			{
				BuildTooltip(builder);
				return builder.Build();
			}
		}

		/// <summary>
		/// Populates the tooltip builder with this item's tooltip lines.
		/// </summary>
		/// <param name="builder">The tooltip builder to populate.</param>
		public void BuildTooltip(TooltipBuilder builder)
		{
			builder.AddLine($"ID: {ID}", 5, TooltipColors.Label);
			builder.AddLine($"Slot: {Slot}", 6, TooltipColors.Label);
			Template?.BuildTooltip(builder);
			Generator?.BuildTooltip(builder);
		}

		/// <summary>
		/// Event handler called when the item is equipped by a character. Applies generated attributes.
		/// </summary>
		/// <param name="character">The character equipping the item.</param>
		public void ItemEquippable_OnEquip(ICharacter character)
		{
			if (IsGenerated)
			{
				Generator.ApplyAttributes(character);
			}
		}

		/// <summary>
		/// Event handler called when the item is unequipped by a character. Removes generated attributes.
		/// </summary>
		/// <param name="character">The character unequipping the item.</param>
		public void ItemEquippable_OnUnequip(ICharacter character)
		{
			if (IsGenerated)
			{
				Generator.RemoveAttributes(character);
			}
		}
	}
}