using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Client;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The hotkey bar is a shortcut table, not a container: what a drop binds, and what a lift
	/// carries.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A right-click used to seed the cursor with the item drag the shortcut pointed at — a bag slot
	/// typed <c>Inventory</c>, a socket typed <c>Equipment</c> — so the next press on a bag slot
	/// moved the real item and the next press on a socket unequipped it. A lift now carries
	/// <see cref="ReferenceButtonType.Hotkey"/> with the source slot, which only the bar itself can
	/// resolve, through the binding it remembered lifting.
	/// </para>
	/// <para>
	/// The resolver is exercised directly, on a bar and a drag object that were never shown: the
	/// rule is pure data, and mounting two documents to test a switch statement would test the
	/// documents.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class HotkeyBarLiftTests
	{
		private const string HotkeyBarPath = "Assets/Scripts/Client/GUI/World/HotkeyBar/UITKHotkeyBar.cs";

		private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

		private GameObject barHost;
		private GameObject dragHost;
		private UITKHotkeyBar bar;
		private UITKDragObject drag;
		private MethodInfo resolve;

		[SetUp]
		public void SetUp()
		{
			barHost = new GameObject("UIHotkeyBar");
			bar = barHost.AddComponent<UITKHotkeyBar>();

			dragHost = new GameObject("UIDragObject");
			drag = dragHost.AddComponent<UITKDragObject>();

			resolve = typeof(UITKHotkeyBar).GetMethod("TryResolveDroppedBinding", Private);
			LogAssert.IsNotNull(resolve, "the bar must still resolve a drop through TryResolveDroppedBinding");
		}

		[TearDown]
		public void TearDown()
		{
			if (dragHost != null) UnityEngine.Object.DestroyImmediate(dragHost);
			if (barHost != null) UnityEngine.Object.DestroyImmediate(barHost);
		}

		private void Carry(ReferenceButtonType type, long referenceID, uint splitAmount = 0)
		{
			drag.Type = type;
			drag.ReferenceID = referenceID;

			PropertyInfo split = typeof(UITKDragObject).GetProperty("SplitAmount");
			LogAssert.IsNotNull(split, "the drag object must still expose SplitAmount");
			split.GetSetMethod(true).Invoke(drag, new object[] { splitAmount });
		}

		private void Lift(int slotIndex, ReferenceButtonType type, long referenceID)
		{
			Type bindingType = typeof(UITKHotkeyBar).GetNestedType("HotkeyBinding", BindingFlags.NonPublic);
			LogAssert.IsNotNull(bindingType, "the bar must still keep bindings as HotkeyBinding");

			object binding = Activator.CreateInstance(bindingType);
			bindingType.GetField("Type").SetValue(binding, type);
			bindingType.GetField("ReferenceID").SetValue(binding, referenceID);

			typeof(UITKHotkeyBar).GetField("liftedBinding", Private).SetValue(bar, binding);
			typeof(UITKHotkeyBar).GetField("liftedSlotIndex", Private).SetValue(bar, slotIndex);
		}

		private bool Resolve(out ReferenceButtonType type, out long referenceID)
		{
			object[] args = { drag, null, null };
			bool accepted = (bool)resolve.Invoke(bar, args);
			type = (ReferenceButtonType)args[1];
			referenceID = (long)args[2];
			return accepted;
		}

		[Test]
		public void AnInventoryEquipmentOrAbilityDrag_BindsAsItself()
		{
			foreach (ReferenceButtonType type in new[] { ReferenceButtonType.Inventory, ReferenceButtonType.Equipment, ReferenceButtonType.Ability })
			{
				Carry(type, 7L);

				LogAssert.IsTrue(Resolve(out ReferenceButtonType bound, out long id), $"a {type} drag is what the bar is for");
				LogAssert.AreEqual(type, bound, "the binding's type is the drag's");
				LogAssert.AreEqual(7L, id, "the binding's reference is the drag's");
			}
		}

		[Test]
		public void ABankDrag_IsRefused()
		{
			/* A shortcut to a slot the player is not carrying is not a shortcut to anything, and
			 * the server never accepts one; refusing here is what keeps the two sides agreeing. */
			Carry(ReferenceButtonType.Bank, 3L);

			LogAssert.IsFalse(Resolve(out _, out _), "bank items are never assignable to the hotkey bar");
		}

		[Test]
		public void AnEmptyDrag_IsRefused()
		{
			Carry(ReferenceButtonType.None, 3L);

			LogAssert.IsFalse(Resolve(out _, out _), "nothing carried, nothing bound");
		}

		[Test]
		public void ASplitDrag_IsRefused()
		{
			/* The drag object's own contract: anything that cannot take a quantity must refuse a
			 * drag with one, rather than act on the whole stack it was taken from. */
			Carry(ReferenceButtonType.Inventory, 7L, splitAmount: 5);

			LogAssert.IsFalse(Resolve(out _, out _), "a hotkey binds a slot, not a quantity");
		}

		[Test]
		public void ALiftedShortcut_ResolvesToWhatWasLifted()
		{
			Lift(slotIndex: 3, ReferenceButtonType.Ability, 4242L);
			Carry(ReferenceButtonType.Hotkey, 3L);

			LogAssert.IsTrue(Resolve(out ReferenceButtonType bound, out long id), "a shortcut lifted off the bar may land on another slot");
			LogAssert.AreEqual(ReferenceButtonType.Ability, bound, "...as the binding it was, not as a Hotkey");
			LogAssert.AreEqual(4242L, id, "...pointing where it pointed");
		}

		[Test]
		public void ALiftedShortcut_IsRefused_WhenTheDragIsNotTheOneThatLiftedIt()
		{
			/* A stale memory of an earlier lift must not be bound by an unrelated later drag. */
			Lift(slotIndex: 3, ReferenceButtonType.Ability, 4242L);
			Carry(ReferenceButtonType.Hotkey, 5L);

			LogAssert.IsFalse(Resolve(out _, out _), "the drag names slot 5; slot 3 is what was lifted");
		}

		[Test]
		public void ALiftedShortcut_IsRefused_WhenNothingWasLifted()
		{
			Carry(ReferenceButtonType.Hotkey, 3L);

			LogAssert.IsFalse(Resolve(out _, out _), "a Hotkey drag with no lift behind it resolves to nothing");
		}

		[Test]
		public void ALift_CarriesTheSlot_NotTheItem()
		{
			/* The gesture itself: a right-click must put a Hotkey reference to the SLOT on the
			 * cursor. Seeding the binding's own type is the two-outcome drag this fixture exists to
			 * keep out. */
			string source = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), HotkeyBarPath)).Replace("\r\n", "\n");
			int start = source.IndexOf("private void HandleSlotRightClick(HotkeySlot slot)", StringComparison.Ordinal);
			int end = source.IndexOf("private void ClearBinding(int index, bool broadcast)", start, StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0 && end > start, "the right-click handler must still be locatable");
			string body = source.Substring(start, end - start);

			LogAssert.IsTrue(body.Contains("dragObject.SetReference(sprite, slot.Index, ReferenceButtonType.Hotkey)"),
				"a lift puts the bar slot on the cursor, typed Hotkey");
			LogAssert.IsFalse(body.Contains("bindings[slot.Index].Type)"),
				"...never the binding's own type, which item panels would act on");
			LogAssert.IsTrue(body.Contains("ClearBinding(slot.Index, broadcast: true)"),
				"the right-click is still the gesture that removes the shortcut");

			int drop = source.IndexOf("private void HandleSlotLeftClick(HotkeySlot slot)", StringComparison.Ordinal);
			int dropEnd = source.IndexOf("private bool TryResolveDroppedBinding(", drop, StringComparison.Ordinal);
			string dropBody = source.Substring(drop, dropEnd - drop);
			LogAssert.IsTrue(dropBody.Contains("Type = (byte)type,"),
				"what goes on the wire is the RESOLVED type; Hotkey is never sent");
		}
	}
}
