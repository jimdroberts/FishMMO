using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using FishNet.Object;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// Builds a character the UI panels can read from, without a network session.
	/// </summary>
	/// <remarks>
	/// <para>The real <c>PlayerCharacter</c> component is used rather than a hand-written
	/// <c>IPlayerCharacter</c>: that interface and <c>ICharacter</c> carry 76 members between them,
	/// nearly all irrelevant to rendering. <c>AddComponent</c> works in edit mode (no networking is
	/// touched), identity is settable, and <c>BaseCharacter.Behaviours</c> — the dictionary
	/// <c>TryGet&lt;T&gt;</c> reads — can be seeded by reflection. Panels therefore resolve their
	/// controllers through exactly the path they use at runtime.</para>
	/// <para>Only the controllers the panels actually ask for are faked, and only the members they
	/// call carry real behaviour; the rest satisfy the compiler.</para>
	/// </remarks>
	public static class Rig
	{
		public static PlayerCharacter Character { get; private set; }

		/// <summary>Creates the viewer's character and registers every faked controller on it.</summary>
		public static PlayerCharacter Build(GameObject host)
		{
			Character = Create(host, "Thalorin", 1001L);
			return Character;
		}

		/// <summary>
		/// Creates a second, fully furnished character that is not the viewer.
		/// </summary>
		/// <remarks>
		/// For the panels that display somebody else's data — inspecting a player reads their
		/// equipment controller, not the viewer's. Deliberately does not touch
		/// <see cref="Character"/>: every other panel in the run believes that property is the
		/// player, and a capture that quietly reassigned it would render the wrong character
		/// everywhere it ran before this one.
		/// </remarks>
		/// <param name="host">GameObject to attach the character to.</param>
		/// <param name="name">Character name to display.</param>
		/// <param name="id">Character ID.</param>
		public static PlayerCharacter BuildOther(GameObject host, string name, long id)
		{
			return Create(host, name, id);
		}

		private static PlayerCharacter Create(GameObject host, string name, long id)
		{
			PlayerCharacter character = host.AddComponent<PlayerCharacter>();
			Set(character, "CharacterName", name);
			Set(character, "ID", id);

			/* Awake never runs on a component added in edit mode, so the two references
			 * BaseCharacter assigns there are still null. Panels that take a character from
			 * somewhere other than SetCharacter — the inspect window — refuse a target with no
			 * Transform, and would silently render nothing. */
			Set(character, "Transform", host.transform);
			Set(character, "GameObject", host);

			/* And the same again for the network identity, which is what a panel reads to tell that
			 * the character it is showing still exists. RequireComponent on BaseCharacter puts a
			 * NetworkObject on this GameObject, but NetworkBehaviour caches its reference in Awake
			 * and reports IsSpawned from it, so without this every rigged character reads as
			 * despawned and the inspect window closes itself on its first tick. */
			NetworkObject networkObject = host.GetComponent<NetworkObject>();
			if (networkObject != null)
			{
				Set(networkObject, "ObjectId", (int)id);
				Set(networkObject, "IsDeinitializing", false);
				Set(character, "_networkObjectCache", networkObject);
			}

			FieldInfo field = typeof(BaseCharacter).GetField("Behaviours",
				BindingFlags.NonPublic | BindingFlags.Instance);
			Dictionary<Type, ICharacterBehaviour> behaviours =
				field.GetValue(character) as Dictionary<Type, ICharacterBehaviour>;

			Register<IInventoryController>(behaviours, new FakeInventory(character));
			Register<IEquipmentController>(behaviours, new FakeEquipment(character));
			Register<IBankController>(behaviours, new FakeBank(character));
			Register<ICharacterAttributeController>(behaviours, new FakeAttributes(character));
			Register<IAchievementController>(behaviours, new FakeAchievements(character));
			Register<IFactionController>(behaviours, new FakeFactions(character));
			Register<IFriendController>(behaviours, new FakeFriends(character));
			Register<IPetController>(behaviours, new FakePet(character));

			Fixtures.Apply(character);
			return character;
		}

		private static void Register<T>(Dictionary<Type, ICharacterBehaviour> behaviours,
			ICharacterBehaviour behaviour) where T : class, ICharacterBehaviour
		{
			behaviours[typeof(T)] = behaviour;
		}

		// ── Preview rigging ──────────────────────────────────────────────────

		/// <summary>Path of the race model the character preview photographs.</summary>
		public const string PREVIEW_MODEL_PATH =
			"Assets/Prefabs/Client/Models/EthanCharacter/Prefabs/Ethan.prefab";

		/// <summary>Height of the stand-in used when that model is missing, in metres.</summary>
		private const float PREVIEW_STANDIN_HEIGHT = 1.8f;

		/// <summary>
		/// Gives a character the two things its preview needs: a mesh root holding a body, and a
		/// camera authored the way the playable prefabs author theirs.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The preview is a render, so a character from <see cref="Create"/> without this has nothing
		/// to photograph — no <c>MeshRoot</c> for the framing to measure, no <c>EquipmentViewCamera</c>
		/// for the renderer to adopt — and the capture comes back with an empty viewport regardless of
		/// how correct the panel is. Every capture whose panel shows the viewport calls this.
		/// </para>
		/// <para>
		/// The camera is deliberately NOT written to the character's serialized field: its name is the
		/// only thing that finds it, which is the fallback <c>PlayerCharacter.EquipmentViewCamera</c>
		/// carries for prefabs that do not author the reference. So this is also the thing that would
		/// fail loudly if that fallback were removed.
		/// </para>
		/// </remarks>
		/// <param name="character">The character to rig.</param>
		/// <returns>The mesh root the body was parented under, or null when there was no character.</returns>
		public static Transform AttachPreview(PlayerCharacter character)
		{
			if (character == null || character.Transform == null)
			{
				return null;
			}

			GameObject smoothing = new GameObject("Smoothing");
			smoothing.transform.SetParent(character.Transform, false);

			GameObject visualRoot = new GameObject("MeshRoot");
			visualRoot.transform.SetParent(smoothing.transform, false);

			/* The real race model, not a stand-in: a skinned mesh reports the bounds of its bind pose,
			 * which is what the framing has to fit, and a capsule would not exercise that. */
			GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(PREVIEW_MODEL_PATH);
			if (model != null)
			{
				GameObject body = UnityEngine.Object.Instantiate(model, visualRoot.transform);
				body.name = "Body";
				body.transform.localPosition = Vector3.zero;
				body.transform.localRotation = Quaternion.identity;
			}
			else
			{
				/* A capsule is 2 units tall at unit scale, centred on its own origin, so half of the
				 * stand-in height puts its feet on y = 0 — the ground the real models stand on. */
				Debug.LogWarning($"[Rig] no model at {PREVIEW_MODEL_PATH}; falling back to a capsule, " +
					"which does NOT exercise skinned bounds.");
				GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
				body.name = "Body";
				body.transform.SetParent(visualRoot.transform, false);
				body.transform.localPosition = new Vector3(0.0f, PREVIEW_STANDIN_HEIGHT * 0.5f, 0.0f);
				body.transform.localScale = new Vector3(0.45f, PREVIEW_STANDIN_HEIGHT * 0.5f, 0.45f);
			}

			GameObject cameraObject = new GameObject("EquipmentViewCamera");
			cameraObject.transform.SetParent(smoothing.transform, false);
			cameraObject.transform.localPosition =
				new Vector3(0.0f, 0.0f, EquipmentPreviewRenderer.CameraDistance);
			cameraObject.transform.localRotation = Quaternion.Euler(0.0f, 180.0f, 0.0f);
			cameraObject.AddComponent<Camera>().orthographic = true;

			// Authored disabled, exactly as the prefabs author it.
			cameraObject.SetActive(false);

			// The subject and its equipment go on the visual layer; the camera does not need to.
			BaseCharacter.ApplyVisualLayer(visualRoot);

			Set(character, "meshRoot", visualRoot.transform);
			return visualRoot.transform;
		}

		/// <summary>Writes a property, its auto-property backing field, or a plain field.</summary>
		public static bool Set(object target, string member, object value)
		{
			Type t = target.GetType();
			PropertyInfo prop = t.GetProperty(member,
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			if (prop != null && prop.CanWrite)
			{
				try { prop.SetValue(target, value); return true; } catch { }
			}

			for (Type c = t; c != null; c = c.BaseType)
			{
				FieldInfo f = c.GetField(member,
					BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
					?? c.GetField($"<{member}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
				if (f != null)
				{
					try { f.SetValue(target, value); return true; } catch { }
				}
			}
			return false;
		}
	}

	/// <summary>
	/// Shared slot storage for the three container controllers.
	/// </summary>
	/// <remarks>
	/// Inventory, Equipment and Bank all satisfy <see cref="IItemContainer"/>, and the panels read
	/// them the same way: <c>Items</c> for the grid and <c>TryGetItem</c> per slot. Everything that
	/// mutates the container is implemented honestly rather than stubbed, because the panels call
	/// the query half of it while laying out.
	/// </remarks>
	public abstract class FakeContainer : IItemContainer, ICharacterBehaviour
	{
		public event Action<IItemContainer, Item, int> OnSlotUpdated;
		public event Action<IItemContainer, int, bool> OnSlotLockChanged;

		private readonly List<Item> items = new List<Item>();
		private readonly HashSet<int> locked = new HashSet<int>();

		protected FakeContainer(ICharacter character, int slots)
		{
			Character = character;
			for (int i = 0; i < slots; ++i) { items.Add(null); }
		}

		public ICharacter Character { get; private set; }
		public bool Initialized => true;
		public void InitializeOnce(ICharacter character) { Character = character; }
		public void OnStartCharacter() { }
		public void OnStopCharacter() { }

		public List<Item> Items => items;

		public bool CanManipulate() => true;
		public bool IsValidSlot(int slot) => slot >= 0 && slot < items.Count;
		public bool IsSlotEmpty(int slot) => IsValidSlot(slot) && items[slot] == null;

		public bool TryGetItem(int slot, out Item item)
		{
			item = IsValidSlot(slot) ? items[slot] : null;
			return item != null;
		}

		public bool ContainsItem(BaseItemTemplate template) => GetItemCount(template) > 0;

		public int GetItemCount(BaseItemTemplate template)
		{
			int count = 0;
			for (int i = 0; i < items.Count; ++i)
			{
				if (items[i] == null || items[i].Template != template) { continue; }
					// Stackable is null for a non-stacking item; that is one of it, not none.
					count += items[i].Stackable != null ? (int)items[i].Stackable.Amount : 1;
			}
			return count;
		}

		public void AddSlots(List<Item> add, int amount)
		{
			for (int i = 0; i < amount; ++i) { items.Add(add != null && i < add.Count ? add[i] : null); }
		}

		public void Clear() { items.Clear(); }
		public bool HasFreeSlot() => FreeSlots() > 0;

		public int FreeSlots()
		{
			int free = 0;
			for (int i = 0; i < items.Count; ++i) { if (items[i] == null) { ++free; } }
			return free;
		}

		public int FilledSlots() => items.Count - FreeSlots();
		public bool CanAddItem(Item item) => HasFreeSlot();

		public bool TryAddItem(Item item, out List<Item> modified)
		{
			modified = new List<Item>();
			for (int i = 0; i < items.Count; ++i)
			{
				if (items[i] == null)
				{
					items[i] = item;
					modified.Add(item);
					OnSlotUpdated?.Invoke(this, item, i);
					return true;
				}
			}
			return false;
		}

		public bool SetItemSlot(Item item, int slot)
		{
			if (!IsValidSlot(slot)) { return false; }
			items[slot] = item;
			OnSlotUpdated?.Invoke(this, item, slot);
			return true;
		}

		public bool SwapItemSlots(int from, int to) => SwapItemSlots(from, to, out _, out _);

		public bool SwapItemSlots(int from, int to, out Item fromItem, out Item toItem)
		{
			fromItem = null;
			toItem = null;
			if (!IsValidSlot(from) || !IsValidSlot(to)) { return false; }
			fromItem = items[from];
			toItem = items[to];
			items[from] = toItem;
			items[to] = fromItem;
			return true;
		}

		public Item RemoveItem(int slot)
		{
			if (!IsValidSlot(slot)) { return null; }
			Item removed = items[slot];
			items[slot] = null;
			OnSlotUpdated?.Invoke(this, null, slot);
			return removed;
		}

		public bool IsSlotLocked(int slot) => locked.Contains(slot);

		public void LockSlot(int slot)
		{
			locked.Add(slot);
			OnSlotLockChanged?.Invoke(this, slot, true);
		}

		public void UnlockSlot(int slot)
		{
			locked.Remove(slot);
			OnSlotLockChanged?.Invoke(this, slot, false);
		}
	}

	public sealed class FakeInventory : FakeContainer, IInventoryController
	{
		public FakeInventory(ICharacter character) : base(character, 32) { }
		public void Activate(int index) { }
		public bool CanSwapItemSlots(int from, int to, InventoryType fromInventory) => true;
	}

	public sealed class FakeBank : FakeContainer, IBankController
	{
		public FakeBank(ICharacter character) : base(character, 48) { }
		public long LastInteractableID { get; set; }
		public long Currency { get; set; } = 128450;
		public bool CanSwapItemSlots(int from, int to, InventoryType fromInventory) => true;
	}

	public sealed class FakeEquipment : FakeContainer, IEquipmentController
	{
		public FakeEquipment(ICharacter character)
			: base(character, Enum.GetValues(typeof(ItemSlot)).Length) { }

		public event Action<Item, ItemSlot> OnItemEquipped;
		public event Action<Item, ItemSlot> OnItemUnequipped;

		public List<Trigger> OnEquipTriggers { get; } = new List<Trigger>();
		public List<Trigger> OnUnequipTriggers { get; } = new List<Trigger>();

		public void Activate(int index) { }

		/* The render rig has no server and no reconcile, so there is no request to remember. */
		public event Action<EquipmentRequestKind, ItemSlot, InventoryType, int, bool> OnRequestResolved;
		public event Action<IEquipmentController, EquipmentChange> OnServerEquipmentChanged;

		// The render rig equips directly; there is no replicate tick to queue a request into.
		public bool RequestEquip(Item item, int sourceIndex, InventoryType fromContainer, ItemSlot socket) => false;
		public bool RequestUnequip(ItemSlot socket, InventoryType toContainer) => false;
		public void ApplyUnequipDestination(long itemID, InventoryType container, int slot) { }

		public bool Equip(Item item, int inventoryIndex, IItemContainer container, ItemSlot toSlot)
		{
			bool ok = SetItemSlot(item, (int)toSlot);
			if (ok) { OnItemEquipped?.Invoke(item, toSlot); }
			return ok;
		}

		public bool Unequip(IItemContainer container, byte slot, out List<Item> modified)
		{
			modified = new List<Item>();
			Item removed = RemoveItem(slot);
			if (removed == null) { return false; }
			modified.Add(removed);
			OnItemUnequipped?.Invoke(removed, (ItemSlot)slot);
			return true;
		}
	}
}
