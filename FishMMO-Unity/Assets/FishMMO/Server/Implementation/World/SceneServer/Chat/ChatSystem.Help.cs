using FishNet.Connection;
using System.Collections.Generic;
using FishMMO.Auth.Core;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// <c>/help</c>: lists, in the chat window, every command the caller may run.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Registered at <see cref="AccessLevel.Player"/>, and therefore not audited.</b> The audit hook
	/// at the access gate records commands registered above Player; reading a list of what you can
	/// already run is not an operator action.
	/// </para>
	/// <para>
	/// <b>Decided on the server, from the caller's own level.</b> The listing is built by
	/// <see cref="ChatCommandHelpListing"/> against the character's server-loaded
	/// <see cref="IPlayerCharacter.AccessLevel"/> — never from anything the client sends — so a player
	/// is never shown a game master or administrator command, and a game master never an
	/// administrator one.
	/// </para>
	/// <para>
	/// Registered here, with the chat system, rather than beside the operator sets: those are pinned
	/// to exactly one registration each.
	/// </para>
	/// </remarks>
	public partial class ChatSystem
	{
		/// <summary>The words that run <see cref="OnHelpCommand"/>.</summary>
		private static readonly string[] HelpCommandWords = { "/help" };

		/// <summary>Registers <c>/help</c>. Called beside the support commands.</summary>
		private void RegisterHelpCommand()
		{
			ChatHelper.AddCommands(new Dictionary<string, ChatCommand>()
			{
				{ "/help", OnHelpCommand },
			}, AccessLevel.Player);

			ChatHelper.SetCommandHelp("/help", new ChatCommandHelp()
			{
				Category = "General",
				Arguments = "[command]",
				Summary = "Lists the commands you can use, or describes one.",
			});
		}

		/// <summary>Unregisters <c>/help</c>. The registry is static and outlives this object.</summary>
		private void UnregisterHelpCommand()
		{
			ChatHelper.RemoveCommands(HelpCommandWords);
		}

		/// <summary><c>/help [command]</c> — lists the caller's commands, or describes one of them.</summary>
		/// <returns>Always <c>true</c>: the command is consumed, never echoed to chat.</returns>
		private bool OnHelpCommand(IPlayerCharacter character, ChatBroadcast msg)
		{
			NetworkConnection conn = character?.Owner;
			if (conn == null || !conn.IsActive)
			{
				return true;
			}

			foreach (string line in ChatCommandHelpListing.Build(character.AccessLevel, msg.Text))
			{
				OnSendSystemMessage(conn, line);
			}
			return true;
		}
	}
}
