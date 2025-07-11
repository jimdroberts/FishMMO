using Discord.Commands;
using FishMMO.Database.Npgsql; // Assuming NpgsqlDbContext is here

namespace FishMMO.DiscordBot.Modules
{
	public class ChatModule : ModuleBase<SocketCommandContext>
	{
		private readonly NpgsqlDbContext dbContext;

		public ChatModule(NpgsqlDbContext dbContext)
		{
			this.dbContext = dbContext;
		}
	}
}