using FishNet.Transporting;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>The chat window, as the core and other modules talk to it. The Chat module's window implements it.</summary>
	public interface IChatWindow
	{
		/// <summary>Adds one line to the window.</summary>
		void ShowLine(ChatChannel channel, string speaker, string text);

		/// <summary>Puts text in the input, focused, ready to finish and send (for example a tell's address).</summary>
		void SetInputText(string text);

		/// <summary>The chat key: opens the input, or with Enter while typing sends the line.</summary>
		void OnChatKey(bool enter);

		/// <summary>Tab in the input: leaves the field, keeping the line, and returns the cursor to the game.</summary>
		bool LeaveInputToAimMode();
	}

	/// <summary>
	/// Where the client's lines go: the chat window when the Chat module installs one, and otherwise system lines as
	/// notices (toasts), so a game without chat still tells the player what the server and the UI have to say. Split from
	/// direct <c>UITKChat</c> calls with the Chat module.
	/// </summary>
	public static class ChatWindow
	{
		/// <summary>The toast overlay's panel name.</summary>
		public const string ToastPanelName = "UIToast";

		/// <summary>The window, or null when the Chat module is not installed (or its window is not loaded yet).</summary>
		public static IChatWindow Current { get; private set; }

		/// <summary>The window registers itself as it starts.</summary>
		public static void Register(IChatWindow window)
		{
			Current = window;
		}

		/// <summary>The window unregisters itself as it is destroyed.</summary>
		public static void Unregister(IChatWindow window)
		{
			if (ReferenceEquals(Current, window))
			{
				Current = null;
			}
		}

		/// <summary>Shows a line: in the chat window, or a system line as a notice when there is none.</summary>
		public static void ShowLine(ChatChannel channel, string speaker, string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return;
			}
			if (Current != null)
			{
				Current.ShowLine(channel, speaker, text);
			}
			else if (channel == ChatChannel.System)
			{
				ShowNotice(text);
			}
		}

		/// <summary>Shows a system line.</summary>
		public static void ShowSystem(string text) => ShowLine(ChatChannel.System, "", text);

		/// <summary>Starts a tell to <paramref name="displayName"/> in the chat input. False when there is no chat window.</summary>
		public static bool BeginTell(string displayName)
		{
			if (Current == null)
			{
				return false;
			}
			Current.SetInputText(ChatTellAddress.FormatCommand(displayName));
			return true;
		}

		/// <summary>
		/// The client's own receiver of chat messages, registered once with the network. Only used with no chat window: the
		/// window receives every message itself, so this then does nothing.
		/// </summary>
		public static void OnChatBroadcastFallback(ChatBroadcast msg, Channel channel)
		{
			if (Current == null && msg.Channel == ChatChannel.System)
			{
				ShowNotice(msg.Text);
			}
		}

		private static void ShowNotice(string text)
		{
			if (!string.IsNullOrEmpty(text) && UIManager.TryGetTK(ToastPanelName, out UITKToast toast))
			{
				toast.Show(text);
			}
		}
	}
}
