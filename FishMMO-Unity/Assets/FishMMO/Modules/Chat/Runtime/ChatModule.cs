using FishMMO.Core;

[assembly: GameModule(typeof(FishMMO.Shared.ChatModule))]

namespace FishMMO.Shared
{
	/// <summary>
	/// The Chat module: conversation between players. On the server its <c>ChatChannelSystem</c> takes the
	/// lines the core chat intake accepts and routes them to their channels; on the client the chat window shows them.
	/// </summary>
	/// <remarks>
	/// The core keeps the message itself (<see cref="ChatBroadcast"/>, <see cref="ChatChannel"/>), the slash-command
	/// registry, the intake with its rate gate and mutes, and system messages, which without this module the client
	/// shows as notices.
	/// </remarks>
	public sealed class ChatModule : GameModule
	{
		/// <summary>The module's id.</summary>
		public const string ModuleId = "chat";

		/// <inheritdoc />
		public override string Id => ModuleId;

		/// <inheritdoc />
		public override void Install(ModuleBuilder builder)
		{
		}
	}
}
