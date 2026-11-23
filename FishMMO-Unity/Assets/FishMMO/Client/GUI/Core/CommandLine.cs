using FishNet.Transporting;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// The core command line: with no chat window (the Chat module is not installed), the chat key opens an input dialog
	/// for a slash command (/help, /unstuck, /leaveinstance, and every module's commands), sent the way the chat window
	/// sends them. Plain text is not sent: there is nobody to talk to without chat. Added with the Chat module.
	/// </summary>
	public static class CommandLine
	{
		/// <summary>The chat key with no chat window: opens the command input (Enter while it is open is the dialog's own).</summary>
		public static void OnChatKey(bool enter)
		{
			if (!UIManager.TryGetTK(UITKControl.InputDialogPanelName, out UITKDialogInputBox input) || input.Visible)
			{
				return;
			}
			input.Open("Command (for example /help):", Send);
		}

		/// <summary>Sends one command line, as the chat window would.</summary>
		public static void Send(string line)
		{
			line = line?.Trim();
			if (string.IsNullOrEmpty(line))
			{
				return;
			}
			if (line[0] != '/')
			{
				ChatWindow.ShowSystem("Only commands can be sent here; they start with /. Try /help.");
				return;
			}
			if (line.Length > ChatBroadcast.MaxTextLength)
			{
				ChatWindow.ShowSystem($"Not sent: a command may be at most {ChatBroadcast.MaxTextLength} characters.");
				return;
			}
			Client.Broadcast(new ChatBroadcast() { Text = line }, Channel.Reliable);
		}
	}
}
