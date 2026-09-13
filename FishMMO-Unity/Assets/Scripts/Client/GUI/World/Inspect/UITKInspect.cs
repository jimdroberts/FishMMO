using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// A read-only view of another player character, drawn on the shared character sheet.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This window and the player's own equipment panel mount the SAME markup —
	/// <c>UICharacterSheet.uxml</c> — and show the same three regions. They were separate files once
	/// and drifted: this one was a 300px window holding a wrapping row of 44px sockets that read
	/// nothing like the sheet beside it. The sheet is now the one description of what a character is
	/// wearing, and what differs between the two windows is applied at runtime and nothing else: the
	/// <c>sheet--inspect</c> placement class, and a <see cref="CharacterSheetOptions"/> that withholds
	/// the attribute list.
	/// </para>
	/// <para>
	/// <b>Why this does not derive from <see cref="UITKSlotPanelBase"/>.</b> That class is an
	/// operation tracker, not a drawing kit: its <c>OwnContainer</c>, <c>IsSlotBlocked</c> and
	/// <c>ApplySlotLockVisual</c> all resolve the LOCAL player, which <see cref="UIManager"/> hands to
	/// every <see cref="UITKCharacterControl"/> it enrols with no way to opt out. Inheriting it would
	/// silently give this window the local player's containers, locks and in-flight equip state while
	/// it is showing somebody else. So the sharing is composition — <see cref="CharacterSheetView"/>
	/// for the display and <see cref="UITKSlotPainter"/> for the sockets — and the sockets here carry
	/// no press, drag or drop handler at all. The read-only-ness is structural, not a flag that some
	/// path could forget to check.
	/// </para>
	/// <para>
	/// <b>Movable, and it needs to be.</b> The screen is fully tiled at 1200 units — sheet, bank and
	/// bag — so a 520px inspect window cannot help but cover part of them, and a covered UI Toolkit
	/// window is deaf rather than hidden. The sheet is centred by default and the player drags it
	/// clear; that costs nothing here because <see cref="UITKControl.CanDrag"/> already defaults true
	/// and the shared header is already <c>name="panel-header"</c>, which is the resolved drag handle.
	/// Position is per-open: nothing persists, and a tree rebuild resets it.
	/// </para>
	/// <para>
	/// <b>The preview shows an unarmed body, and cannot help it.</b> An observed character's race
	/// model is real and correctly instantiated for observers, but no equipment geometry is ever built
	/// for a character you do not own — <c>EquipmentVisualController.OnStartCharacter</c> is the only
	/// thing that creates it and it runs from an <c>IsOwner</c>-gated fan-out. Their gear reads
	/// correctly in the sockets beside the viewport, which is why the viewport label is left as
	/// authored rather than promising a dressed model. Making remote gear render is a separate change.
	/// </para>
	/// </remarks>
	public class UITKInspect : UITKControl
	{
		/// <summary>Name of the close button in the shared sheet.</summary>
		private const string CLOSE_BUTTON_NAME = "close-button";

		/// <summary>
		/// USS class selecting the inspect placement in <c>UICharacterSheet.uss</c>.
		/// </summary>
		/// <remarks>
		/// Applied at runtime rather than authored in the markup, because the markup is shared and the
		/// equipment placement has to stay the one a bare-mounted tree lays out with — the overlap
		/// fixture mounts this UXML with no component and no <c>OnStarting</c>, so anything this window
		/// applies at runtime is invisible to it and cannot disturb that layout. The stylesheet's rule is
		/// <c>.eq-panel.sheet--inspect</c>, so this belongs on <see cref="CharacterSheetView.PanelRoot"/>
		/// — the authored window — and not on the <c>UIDocument</c>'s container, which is the element the
		/// tree is cloned into and which no rule in the stylesheet matches.
		/// </remarks>
		private const string INSPECT_CLASS = "sheet--inspect";

		/// <summary>The shared sheet binding.</summary>
		private CharacterSheetView sheet;

		/// <summary>Close button.</summary>
		private Button closeButton;

		/// <summary>
		/// The character last passed to <see cref="Inspect"/>.
		/// </summary>
		/// <remarks>
		/// Held so <see cref="OnAfterStarting"/> can rebuild the panel if the visual tree is replaced
		/// while it is open — otherwise the panel comes back correctly wired and empty — and so
		/// <see cref="OnTick"/> can tell that the character has since despawned.
		/// </remarks>
		private IPlayerCharacter inspected;

		/// <summary>
		/// Resolves the sheet, applies the inspect placement, and wires the close button.
		/// </summary>
		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			/* A rebuilt tree means the old view's element references point into a tree nobody can see,
			 * so it is replaced. Disposed rather than dropped: it owns a render texture and a camera
			 * reference, and a hide/show cycle would otherwise leak one of each every time. */
			sheet?.Dispose();

			/* ShowAttributes is the whole difference between this viewer and the player's own sheet:
			 * the sheet renders what it is told to, and this one is told to hold the attributes back.
			 * Currency would be one of those rows, so it cannot appear here at all. */
			sheet = new CharacterSheetView(root, CharacterSheetOptions.Inspect);
			sheet.EnableSocketHover();
			MarkSockets();

			/* After the sheet, because the class goes on the window the sheet resolved. This is the same
			 * element the drag target resolves to — UITKControl drags the nearest fish-panel ancestor of
			 * the header — so the placement and the drag agree about which box they are moving. */
			sheet.PanelRoot?.AddToClassList(INSPECT_CLASS);

			closeButton = root.Q<Button>(CLOSE_BUTTON_NAME);
			if (closeButton != null)
			{
				closeButton.clicked -= Hide;
				closeButton.clicked += Hide;
			}
		}

		/// <summary>
		/// Marks the authored sockets as sockets.
		/// </summary>
		/// <remarks>
		/// A press on an inspected item's socket is a press on a slot, so it does not cancel the item
		/// the player is carrying. The authored markup carries the look; this carries the meaning. See
		/// <see cref="UITKControl.OnRootPointerDownCancelDrag"/>.
		/// </remarks>
		private void MarkSockets()
		{
			if (sheet == null)
			{
				return;
			}

			for (int i = 0; i < sheet.SlotRoots.Count; ++i)
			{
				sheet.SlotRoots[i]?.AddToClassList(SLOT_MARKER_CLASS);
			}
		}

		/// <summary>
		/// Rebuilds the panel from the last inspected character after a tree rebuild.
		/// </summary>
		protected override void OnAfterStarting()
		{
			if (inspected != null)
			{
				Populate(inspected);
			}
		}

		/// <summary>
		/// Populates the sheet with the target player's data and shows it.
		/// </summary>
		/// <param name="target">The player character to inspect.</param>
		public void Inspect(IPlayerCharacter target)
		{
			if (target == null || target.Transform == null)
			{
				return;
			}

			inspected = target;

			// Shown first so the visual tree exists before Populate writes into it.
			Show();
			Populate(target);
		}

		/// <summary>
		/// Writes a character's name and equipment into the sheet.
		/// </summary>
		/// <param name="target">The player character to display.</param>
		private void Populate(IPlayerCharacter target)
		{
			if (sheet == null)
			{
				return;
			}

			if (sheet.TitleLabel != null)
			{
				sheet.TitleLabel.text = target.CharacterName;
			}

			// Binds the subject, paints their sockets from their own replicated container, and leaves
			// the attribute list empty — this viewer may not see it.
			sheet.SetSubject(target);
			sheet.Refresh();
		}

		/// <summary>
		/// Keeps the panel honest about the character it is showing.
		/// </summary>
		/// <remarks>
		/// A remote character leaves range, despawns and is pooled, and the reference this panel holds
		/// then means nothing: the preview camera and <c>MeshRoot</c> would be handed to a different
		/// player underneath it, and the sockets would go on showing gear nobody is wearing. The panel
		/// has no server notification for that — the character simply stops being replicated — so it
		/// revalidates the subject itself and closes when the subject is gone.
		/// </remarks>
		protected override void OnTick()
		{
			if (!Visible || sheet == null)
			{
				return;
			}

			if (inspected != null && !IsSubjectAlive(inspected))
			{
				// The subject is gone; the window has nothing left to describe.
				Hide();
				return;
			}

			/* The preview is driven per frame rather than on the equipment events because it shows an
			 * animating character: the idle pose moves every frame, and a texture rendered once when
			 * the panel opened would freeze it mid-stride. A frame is also when the viewport's layout
			 * first becomes measurable, so this is the hook that gets the preview its size. */
			sheet.RefreshPreview();
		}

		/// <summary>
		/// Whether a character still exists, is spawned, and can be photographed.
		/// </summary>
		/// <param name="subject">The character to test.</param>
		/// <returns>True while the character is still there.</returns>
		private static bool IsSubjectAlive(IPlayerCharacter subject)
		{
			return subject != null &&
				subject.Transform != null &&
				subject.NetworkObject != null &&
				subject.NetworkObject.IsSpawned;
		}

		/// <summary>
		/// Hides the panel and forgets the inspected character.
		/// </summary>
		/// <remarks>
		/// Overrides <c>Hide(bool)</c>, not <c>Hide()</c>. <c>Hide()</c> is non-virtual and only
		/// forwards here, so this is the one place teardown can live where every caller reaches it —
		/// quit-to-login calls the bool form directly.
		/// </remarks>
		public override void Hide(bool overrideIsAlwaysOpen)
		{
			/* Cleared so a later tree rebuild does not silently repopulate a panel the player closed,
			 * and so the character reference is not held past its usefulness. Guarded so a refused
			 * hide leaves the still-open panel's subject intact. */
			if (!overrideIsAlwaysOpen && Document != null)
			{
				inspected = null;

				/* The preview camera and texture belong to the character and are handed back, and the
				 * sockets are emptied, so nothing of this character is left on a sheet that is about to
				 * show a different one. */
				sheet?.SetSubject(null);
			}

			base.Hide(overrideIsAlwaysOpen);
		}

		/// <summary>
		/// Releases the preview camera and texture with the panel.
		/// </summary>
		public override void OnDestroying()
		{
			sheet?.Dispose();
			sheet = null;

			base.OnDestroying();
		}
	}
}
