using UnityEngine;
using UnityEngine.UIElements;

namespace FishMMO.Client
{
	/// <summary>
	/// The keys that cancel, close and back out: Escape, and ~ (the backquote key) everywhere except a text field.
	/// </summary>
	/// <remarks>
	/// <para>
	/// ~ exists because of the browser. A web build cannot have Escape: it always releases the pointer lock and
	/// leaves fullscreen, no page can cancel that, and the browser often does not deliver the key at all. Escape is
	/// kept beside it for the desktop client and for the times a browser does pass it through.
	/// </para>
	/// <para>
	/// While the player is typing, only Escape counts. ~ and ` are characters, and a field that ate them could not
	/// hold a password containing one, or a chat line. Typing also means the cursor is already free, which is when
	/// a browser lets Escape through.
	/// </para>
	/// <para>
	/// The same pair is bound in the input asset to Player/Cancel and Player/CloseLastUI, whose handlers apply the
	/// same rule through <see cref="UIManager.InputControlHasFocus(UITKControl)"/>; see PlayerInputController.
	/// </para>
	/// </remarks>
	public static class CancelKey
	{
		/// <summary>Input System control path of the ~ key, as bound in PlayerControls.inputactions.</summary>
		public const string BackquoteControlPath = "<Keyboard>/backquote";

		/// <summary>Input System control path of Escape.</summary>
		public const string EscapeControlPath = "<Keyboard>/escape";

		/// <summary>
		/// USS class shared by every UI Toolkit text input (<c>TextInputBaseField&lt;T&gt;.ussClassName</c>); the same
		/// test <see cref="UITKControl.IsInputFieldFocused"/> uses.
		/// </summary>
		private const string TextInputFieldClass = "unity-base-text-field";

		/// <summary>
		/// True when a key press should cancel: Escape always, ~ unless it was pressed inside a text field.
		/// </summary>
		/// <param name="evt">The key-down event a panel received.</param>
		public static bool Matches(KeyDownEvent evt)
		{
			if (evt == null)
			{
				return false;
			}
			if (evt.keyCode == KeyCode.Escape)
			{
				return true;
			}
			return evt.keyCode == KeyCode.BackQuote && !IsInTextField(evt.target as VisualElement);
		}

		/// <summary>True when <paramref name="element"/> is a text input or sits inside one.</summary>
		/// <remarks>
		/// A TextField delegates focus to its inner text element, so a key event targets that child; walking up
		/// catches both. Matched by USS class rather than type so every field type (TextField, IntegerField, ...)
		/// is covered, and Buttons and Labels — which are TextElements too — are not.
		/// </remarks>
		public static bool IsInTextField(VisualElement element)
		{
			for (; element != null; element = element.parent)
			{
				if (element is TextField || element.ClassListContains(TextInputFieldClass))
				{
					return true;
				}
			}
			return false;
		}
	}
}
