using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// Paints one occupied or empty slot from an item, and keeps that slot's tooltip in step.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Static and element-addressed, rather than methods on the panel base, because a slot is
	/// painted by panels that deliberately do NOT derive from <see cref="UITKSlotPanelBase"/>.
	/// The inspect window draws a character's sockets and must not inherit the base's operation
	/// tracker, its slot locks or its container lookup — all three of those bottom out in the
	/// LOCAL player, and an inspect window's subject is somebody else. What the two genuinely
	/// share is how a socket LOOKS, and that is all this class is.
	/// </para>
	/// <para>
	/// The resolution of the elements is still each panel's own business, which is the rule
	/// <see cref="UITKSlotPanelBase"/> already states: the item grids CREATE one element per
	/// container slot and the sheet QUERIES ten authored sockets. This takes those elements as
	/// arguments and never looks an element up by name.
	/// </para>
	/// <para>
	/// The bodies here were lifted from <c>UITKSlotPanelBase.SetSlotItem</c>, <c>ClearSlot</c> and
	/// <c>RefreshSlotTooltip</c>, which now call through. They stay in one place for the reason
	/// issue #280 had to be fixed twice: the tooltip was opened from whatever item the pointer
	/// found on the way in and nothing re-read it, so a swap under a stationary cursor left it
	/// describing an item that was no longer there.
	/// </para>
	/// </remarks>
	public static class UITKSlotPainter
	{
		/// <summary>
		/// Name of the shared tooltip overlay, resolved by GameObject name through <see cref="UIManager"/>.
		/// </summary>
		/// <remarks>
		/// The same string <see cref="UITKSlotPanelBase.TOOLTIP_NAME"/> holds; the base now reads it
		/// from here so there is one literal rather than two that agree by hand.
		/// </remarks>
		public const string TOOLTIP_NAME = "UITooltip";

		/// <summary>
		/// Paints an occupied slot: its icon, its stack badge, and its tooltip.
		/// </summary>
		/// <param name="icon">The slot's icon element.</param>
		/// <param name="amount">The slot's stack-count label, or null.</param>
		/// <param name="item">The item the slot now holds.</param>
		/// <param name="hiddenClass">The USS class that hides the stack badge.</param>
		public static void Paint(VisualElement icon, Label amount, Item item, string hiddenClass)
		{
			if (item == null)
			{
				return;
			}

			// Placeholder when the template has no icon — or no template at all. A slot holding an
			// item must look occupied even when the item cannot describe itself, which is the rule
			// UITKItemIcon.Apply already implements for the null-sprite case.
			UITKItemIcon.Apply(icon, item.Template != null ? item.Template.Icon : null);

			if (amount != null)
			{
				if (item.IsStackable && item.Stackable != null)
				{
					amount.text = item.Stackable.Amount.ToString();
					amount.RemoveFromClassList(hiddenClass);
				}
				else
				{
					amount.text = "";
					amount.AddToClassList(hiddenClass);
				}
			}
		}

		/// <summary>
		/// Paints an empty slot: icon cleared, stack badge hidden.
		/// </summary>
		/// <param name="icon">The slot's icon element.</param>
		/// <param name="amount">The slot's stack-count label, or null.</param>
		/// <param name="hiddenClass">The USS class that hides the stack badge.</param>
		public static void Clear(VisualElement icon, Label amount, string hiddenClass)
		{
			UITKItemIcon.Clear(icon);

			if (amount != null)
			{
				amount.text = "";
				amount.AddToClassList(hiddenClass);
			}
		}

		/// <summary>
		/// Opens the item tooltip for a slot the pointer has entered.
		/// </summary>
		/// <param name="owner">The slot element, so the tooltip can close itself if it is rebuilt.</param>
		/// <param name="item">What the slot holds.</param>
		public static void OpenTooltip(VisualElement owner, Item item)
		{
			if (owner == null || item == null)
			{
				return;
			}

			if (UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				tooltip.Open(item, owner);
			}
		}

		/// <summary>
		/// Hides the item tooltip when the pointer leaves a slot.
		/// </summary>
		/// <param name="owner">The slot element the pointer left.</param>
		public static void HideTooltip(VisualElement owner)
		{
			if (owner == null)
			{
				return;
			}

			if (UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				// HideFor, so a stale leave cannot close a tooltip another slot has since opened.
				tooltip.HideFor(owner);
			}
		}

		/// <summary>
		/// Re-points a slot's tooltip at what the slot now shows.
		/// </summary>
		/// <param name="owner">The slot element.</param>
		/// <param name="item">What it now holds, or null when it now holds nothing.</param>
		/// <remarks>
		/// Called from both painters, so every path that changes a slot — a replicate arriving, a
		/// resync, a rebuild, a panel opening — keeps the tooltip honest without having to remember
		/// to. <see cref="UITKTooltip.RefreshFor"/> does nothing unless the pointer is over THIS
		/// slot, which is what makes a per-slot call safe from a refresh loop.
		/// </remarks>
		public static void RefreshTooltip(VisualElement owner, Item item)
		{
			if (owner == null)
			{
				return;
			}

			if (UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				tooltip.RefreshFor(owner, item);
			}
		}
	}
}
