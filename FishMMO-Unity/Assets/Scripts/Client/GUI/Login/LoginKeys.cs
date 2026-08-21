using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace FishMMO.Client
{
	/// <summary>
	/// Shared keyboard wiring for the login-flow panels: Enter submits, Escape backs out, and
	/// Tab walks the fields in the order they are authored.
	/// </summary>
	/// <remarks>
	/// There was no keyboard handling anywhere in the login tree at all. A player typing a
	/// password had to reach for the mouse to sign in, Escape did nothing on any of the six
	/// screens, and Tab moved between whichever elements UI Toolkit happened to consider
	/// focusable — which includes the container elements, so it took several presses to get from
	/// the username field to the password field.
	/// <para>
	/// The handlers are registered in the <b>trickle-down</b> phase deliberately. A focused
	/// <see cref="TextField"/> consumes Return before it bubbles, and the field is exactly where
	/// the player is standing when they want to submit; a focused <see cref="Button"/> consumes
	/// Escape the same way. Reading the keys on the way down is the only placement that sees them
	/// from every field on the panel.
	/// </para>
	/// <para>
	/// Every method is unregister-then-register and takes named methods rather than fresh
	/// lambdas where it can, because <c>OnStarting</c> runs again on every visual tree rebuild
	/// (see <see cref="UITKControl"/>) and a second registration on a surviving element would
	/// submit the form twice per key press.
	/// </para>
	/// </remarks>
	public static class LoginKeys
	{
		/// <summary>
		/// Wires Enter and Escape onto a panel root.
		/// </summary>
		/// <param name="root">The panel root to capture on. Null is tolerated.</param>
		/// <param name="onSubmit">Invoked on Return/KeypadEnter. May be null.</param>
		/// <param name="onCancel">Invoked on Escape. May be null.</param>
		public static void Attach(VisualElement root, Action onSubmit, Action onCancel)
		{
			if (root == null)
			{
				return;
			}

			/* The root has to be focusable itself, or a panel whose fields are all empty has
			 * nothing holding focus and no element for the key press to be dispatched to. */
			root.focusable = true;

			// Replacing rather than accumulating: this is re-attached on every tree rebuild.
			root.UnregisterCallback<KeyDownEvent>(Dispatch, TrickleDown.TrickleDown);
			handlers.Remove(root);
			handlers.Add(root, new Binding(onSubmit, onCancel));
			root.RegisterCallback<KeyDownEvent>(Dispatch, TrickleDown.TrickleDown);
		}

		/// <summary>
		/// Gives a panel's interactive elements a deliberate Tab order.
		/// </summary>
		/// <remarks>
		/// UI Toolkit's default order is a depth-first walk of every focusable element, and the
		/// generated inner parts of a <see cref="TextField"/> and the containers wrapping each row
		/// are focusable, so Tab landed on things that look like nothing. Assigning explicit
		/// ascending indices and taking everything else out of the ring makes the order the one
		/// the panel visibly has.
		/// </remarks>
		/// <param name="root">The panel root. Null is tolerated.</param>
		/// <param name="order">The elements, in the order Tab should visit them. Nulls are skipped.</param>
		public static void SetTabOrder(VisualElement root, params VisualElement[] order)
		{
			if (order == null)
			{
				return;
			}

			int index = 1;
			foreach (VisualElement element in order)
			{
				if (element == null)
				{
					continue;
				}
				element.focusable = true;
				element.tabIndex = index++;
			}

			if (root != null)
			{
				/* The root holds focus when the panel opens (see Attach) but must not be a Tab
				 * stop of its own, or the first press goes nowhere visible. */
				root.tabIndex = 0;
			}
		}

		/// <summary>
		/// Moves keyboard focus to the first element of a panel that the player should type into.
		/// </summary>
		/// <param name="fallbackRoot">Focused when <paramref name="first"/> is null.</param>
		/// <param name="first">The preferred element.</param>
		public static void FocusFirst(VisualElement fallbackRoot, VisualElement first)
		{
			if (first != null)
			{
				first.Focus();
				return;
			}
			fallbackRoot?.Focus();
		}

		/// <summary>
		/// The submit/cancel pair bound to one panel root.
		/// </summary>
		private sealed class Binding
		{
			public Binding(Action submit, Action cancel)
			{
				Submit = submit;
				Cancel = cancel;
			}

			public readonly Action Submit;
			public readonly Action Cancel;
		}

		/// <summary>
		/// Bindings by panel root.
		/// </summary>
		/// <remarks>
		/// Keyed on the element rather than captured in a closure so <see cref="Dispatch"/> can be
		/// a single named method — which is what makes the <c>UnregisterCallback</c> in
		/// <see cref="Attach"/> actually match, and stops handlers stacking up across tree
		/// rebuilds.
		/// <para>
		/// A <see cref="ConditionalWeakTable{TKey,TValue}"/> rather than a plain dictionary,
		/// because the key is a root that <see cref="UnityEngine.UIElements.UIDocument"/>
		/// <b>replaces</b> on every enable: a panel that is hidden and shown a hundred times
		/// produces a hundred distinct roots, and a strong-keyed dictionary would hold every one
		/// of them — and the whole discarded visual tree hanging off it — alive forever. The weak
		/// table lets a dead root and its binding go together, with no bookkeeping here at all.
		/// </para>
		/// </remarks>
		private static readonly ConditionalWeakTable<VisualElement, Binding> handlers =
			new ConditionalWeakTable<VisualElement, Binding>();

		/// <summary>
		/// Routes a key press to the binding registered for the element it was captured on.
		/// </summary>
		private static void Dispatch(KeyDownEvent evt)
		{
			if (!(evt.currentTarget is VisualElement root) ||
				!handlers.TryGetValue(root, out Binding binding) ||
				binding == null)
			{
				return;
			}

			switch (evt.keyCode)
			{
				case KeyCode.Return:
				case KeyCode.KeypadEnter:
					if (binding.Submit != null)
					{
						evt.StopPropagation();
						binding.Submit.Invoke();
					}
					return;
				case KeyCode.Escape:
					if (binding.Cancel != null)
					{
						evt.StopPropagation();
						binding.Cancel.Invoke();
					}
					return;
			}
		}
	}
}
