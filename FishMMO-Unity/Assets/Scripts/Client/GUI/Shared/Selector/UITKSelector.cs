using System;
using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using UnityEngine.UIElements;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit selector. Presents a list of selectable cached objects, lets the user pick one,
	/// and confirms or cancels the selection. Each option row is a plain <see cref="Button"/>, so
	/// no separate tooltip-button class is required.
	/// </summary>
	/// <remarks>
	/// Follows the shared open/close callback rules in <see cref="UITKCallbackDialog"/>: it is a
	/// singleton panel, so a second <c>Open</c> is refused rather than allowed to replace a list
	/// the player is already choosing from, and every exit path — accept, cancel, Escape,
	/// quit-to-login, a refused open — produces exactly one callback.
	/// </remarks>
	public class UITKSelector : UITKCallbackDialog
	{
		/// <summary>Draw order tier for this panel. See <see cref="UITKPanelLayer"/>.</summary>
		protected override UITKPanelLayer Layer => UITKPanelLayer.Popup;

		/// <summary>
		/// Name of the selector list container element.
		/// </summary>
		private const string BUTTON_PARENT_NAME = "selector-list";
		/// <summary>
		/// Name of the accept button element.
		/// </summary>
		private const string ACCEPT_BUTTON_NAME = "selector-accept-btn";
		/// <summary>
		/// Name of the cancel button element.
		/// </summary>
		private const string CANCEL_BUTTON_NAME = "selector-cancel-btn";
		/// <summary>
		/// Name of the row-count badge in the header.
		/// </summary>
		private const string COUNT_LABEL_NAME = "selector-count";
		/// <summary>
		/// Name of the "nothing to choose from" placeholder.
		/// </summary>
		private const string EMPTY_LABEL_NAME = "selector-empty";

		/// <summary>
		/// USS class applied to the currently selected item.
		/// </summary>
		private const string SELECTED_CLASS = "fish-row--selected";

		/// <summary>
		/// The container element for the option buttons.
		/// </summary>
		private VisualElement buttonParent;
		/// <summary>
		/// The accept button.
		/// </summary>
		private Button acceptButton;
		/// <summary>
		/// The cancel button.
		/// </summary>
		private Button cancelButton;
		/// <summary>
		/// The header row-count badge.
		/// </summary>
		private Label countLabel;
		/// <summary>
		/// The empty-list placeholder.
		/// </summary>
		private Label emptyLabel;

		/// <summary>
		/// Callback invoked with the selected object's ID when accepted.
		/// </summary>
		private Action<int> onAccept;
		/// <summary>
		/// Callback invoked when the selector is dismissed without a selection.
		/// </summary>
		/// <remarks>
		/// There used not to be one at all, so a caller that opened the selector had no way of
		/// learning that the player had walked away from it — it simply waited forever.
		/// </remarks>
		private Action onCancel;

		/// <summary>
		/// Index of the currently selected item, or -1 if none.
		/// </summary>
		private int selectedIndex = -1;

		/// <summary>
		/// Objects the current request offered.
		/// </summary>
		private List<ICachedObject> cachedObjects;

		/// <summary>
		/// The subset of <see cref="cachedObjects"/> that actually produced a row, in row order.
		/// </summary>
		/// <remarks>
		/// The rows and the source list are not the same sequence: an entry that is not an
		/// <see cref="ITooltip"/> produces no row. Indexing the source list with a row index
		/// therefore returned a different object than the one the player clicked — and, past the
		/// end, silently accepted nothing at all. Keeping the row-order list is what makes the
		/// two agree.
		/// </remarks>
		private readonly List<ICachedObject> rowObjects = new List<ICachedObject>();

		/// <summary>
		/// Pool of button elements for each selectable option.
		/// </summary>
		private readonly List<Button> buttonSlots = new List<Button>();

		/// <summary>
		/// Resolves cached elements and wires the accept/cancel buttons.
		/// </summary>
		public override void OnStarting()
		{
			if (Root == null)
			{
				return;
			}

			buttonParent = Root.Q<VisualElement>(BUTTON_PARENT_NAME);
			acceptButton = Root.Q<Button>(ACCEPT_BUTTON_NAME);
			cancelButton = Root.Q<Button>(CANCEL_BUTTON_NAME);
			countLabel = Root.Q<Label>(COUNT_LABEL_NAME);
			emptyLabel = Root.Q<Label>(EMPTY_LABEL_NAME);

			if (acceptButton != null)
			{
				acceptButton.clicked += OnClick_Accept;
			}
			if (cancelButton != null)
			{
				cancelButton.clicked += OnClick_Cancel;
			}

			// The count badge and the empty placeholder are in the UXML but nothing drove them.
			BindListChrome(buttonParent, countLabel, null, emptyLabel, "option", "options");

			AttachDialogKeys(Root);
		}

		/// <summary>
		/// Answers any outstanding request and clears button slots when the control is destroyed.
		/// </summary>
		public override void OnDestroying()
		{
			base.OnDestroying();
			ClearSlots();
		}

		/// <summary>
		/// Opens the selector with the provided cached objects and callbacks.
		/// </summary>
		/// <param name="cachedObjects">List of objects to select from.</param>
		/// <param name="onAccept">Callback invoked with the selected object's ID when accepted.</param>
		/// <param name="onCancel">Callback invoked when the player dismisses without choosing.</param>
		/// <returns>
		/// False when the request could not be honoured — nothing to choose from, or a selection
		/// already on screen. Either way <paramref name="onCancel"/> is invoked immediately, so
		/// the caller is never left waiting on a selector that was never shown.
		/// </returns>
		public bool Open(List<ICachedObject> cachedObjects, Action<int> onAccept, Action onCancel = null)
		{
			if (cachedObjects == null || cachedObjects.Count < 1)
			{
				/* Nothing to show. This used to return silently, which looked identical to the
				 * panel being broken and left the caller armed. */
				onCancel?.Invoke();
				return false;
			}

			if (!TryClaim())
			{
				onCancel?.Invoke();
				return false;
			}

			this.cachedObjects = cachedObjects;
			this.onAccept = onAccept;
			this.onCancel = onCancel;
			this.selectedIndex = -1;

			Show();
			return true;
		}

		/// <summary>
		/// Rebuilds the option rows into the live tree.
		/// </summary>
		protected override void ApplyRequest()
		{
			UpdateEventSlots();
			UpdateAcceptEnabled();
		}

		/// <summary>
		/// Drops the rows, the objects and the callbacks this request was opened with.
		/// </summary>
		protected override void ClearRequest()
		{
			ClearSlots();
			cachedObjects = null;
			onAccept = null;
			onCancel = null;
			selectedIndex = -1;
		}

		/// <summary>
		/// Focuses the panel root so the arrow keys and Enter have somewhere to arrive.
		/// </summary>
		/// <remarks>
		/// Not the first row: the rows are <c>Button</c>s, and a focused button is activated by
		/// the space bar, so the first option would silently select itself under a player who
		/// was mid-jump. The first arrow key highlights a row explicitly.
		/// </remarks>
		protected override void FocusDefault()
		{
			Root?.Focus();
		}

		/// <summary>
		/// Enter accepts the highlighted option.
		/// </summary>
		protected override void OnSubmitKey()
		{
			OnClick_Accept();
		}

		/// <summary>
		/// Arrow keys move the highlight through the option rows.
		/// </summary>
		/// <param name="direction">-1 for up, 1 for down.</param>
		/// <returns>True when a row was highlighted.</returns>
		protected override bool OnNavigateKey(int direction)
		{
			if (buttonSlots.Count < 1)
			{
				return false;
			}

			int next = selectedIndex < 0
				? (direction > 0 ? 0 : buttonSlots.Count - 1)
				: selectedIndex + direction;

			// Stop at the ends rather than wrapping; a list that wraps loses the player's place.
			next = UnityEngine.Mathf.Clamp(next, 0, buttonSlots.Count - 1);

			Select(next);
			buttonSlots[next].Focus();
			return true;
		}

		/// <summary>
		/// Removes all option rows and any tooltip they were showing.
		/// </summary>
		private void ClearSlots()
		{
			/* The tooltip is a separate panel that outlives these rows, so a row removed while
			 * the pointer was over it would leave its tooltip on screen with nothing under it. */
			if (buttonParent != null && UIManager.TryGetTK("UITooltip", out UITKTooltip tooltip))
			{
				tooltip.HideFor(buttonParent);
			}

			if (buttonParent != null)
			{
				buttonParent.Clear();
			}
			buttonSlots.Clear();
			rowObjects.Clear();
		}

		/// <summary>
		/// Rebuilds the option rows from the current cached objects.
		/// </summary>
		private void UpdateEventSlots()
		{
			ClearSlots();

			if (cachedObjects == null || buttonParent == null)
			{
				return;
			}

			for (int i = 0; i < cachedObjects.Count; ++i)
			{
				if (!(cachedObjects[i] is ITooltip tooltipObject))
				{
					continue;
				}

				// Row index, not source index — see rowObjects.
				int index = rowObjects.Count;
				Button button = new Button(() => EventEntry_OnLeftClick(index))
				{
					text = tooltipObject.Name,
				};
				/* A row in a list, not a button in a toolbar: .fish-row gives it the shared
				 * hover and selection treatment every other list in the game uses, where
				 * .fish-button would have made a stack of separate-looking controls. */
				button.AddToClassList("fish-row");
				button.AddToClassList("fish-row__name");
				button.AddToClassList("selector-entry");

				string tooltipText = tooltipObject.Tooltip();
				button.RegisterCallback<PointerEnterEvent>((evt) =>
				{
					if (!string.IsNullOrEmpty(tooltipText) && UIManager.TryGetTK("UITooltip", out UITKTooltip tooltip))
					{
						tooltip.Open(tooltipText, button);
					}
				});
				button.RegisterCallback<PointerLeaveEvent>((evt) =>
				{
					if (UIManager.TryGetTK("UITooltip", out UITKTooltip tooltip))
					{
						tooltip.HideFor(button);
					}
				});

				buttonParent.Add(button);
				buttonSlots.Add(button);
				rowObjects.Add(cachedObjects[i]);
			}

			// The tree was rebuilt, so re-apply whichever row was highlighted before.
			if (selectedIndex > -1 && selectedIndex < buttonSlots.Count)
			{
				buttonSlots[selectedIndex].AddToClassList(SELECTED_CLASS);
			}
			else
			{
				selectedIndex = -1;
			}
		}

		/// <summary>
		/// Updates the selected index and highlights the chosen row.
		/// </summary>
		/// <param name="index">Index of the clicked button.</param>
		private void EventEntry_OnLeftClick(int index)
		{
			Select(index);
		}

		/// <summary>
		/// Highlights a row by index, clearing whichever was highlighted before.
		/// </summary>
		private void Select(int index)
		{
			if (index < 0 || index >= buttonSlots.Count)
			{
				return;
			}

			if (selectedIndex > -1 && selectedIndex < buttonSlots.Count)
			{
				buttonSlots[selectedIndex].RemoveFromClassList(SELECTED_CLASS);
			}

			selectedIndex = index;
			buttonSlots[selectedIndex].AddToClassList(SELECTED_CLASS);
			UpdateAcceptEnabled();
		}

		/// <summary>
		/// Greys out Accept until something is actually selected.
		/// </summary>
		/// <remarks>
		/// Accept with nothing chosen used to close the selector and invoke nothing, which is
		/// indistinguishable from the request being silently dropped.
		/// </remarks>
		private void UpdateAcceptEnabled()
		{
			acceptButton?.SetEnabled(selectedIndex > -1 && selectedIndex < rowObjects.Count);
		}

		/// <summary>
		/// Invokes the accept callback with the selected object's ID and closes the selector.
		/// </summary>
		/// <remarks>
		/// With nothing selected there is no answer to give, so the selector stays open rather
		/// than closing on a non-answer.
		/// </remarks>
		public void OnClick_Accept()
		{
			if (selectedIndex < 0 || selectedIndex >= rowObjects.Count)
			{
				return;
			}

			int id = rowObjects[selectedIndex].ID;
			Action<int> callback = onAccept;
			Resolve(() => callback?.Invoke(id));
		}

		/// <summary>
		/// Closes the selector without making a selection.
		/// </summary>
		public void OnClick_Cancel()
		{
			CancelRequest();
		}

		/// <summary>
		/// Answers the request down its cancel path. Escape, quit-to-login and a bare
		/// <see cref="UITKControl.Hide()"/> all arrive here.
		/// </summary>
		protected override void CancelRequest()
		{
			Action callback = onCancel;
			Resolve(() => callback?.Invoke());
		}
	}
}
