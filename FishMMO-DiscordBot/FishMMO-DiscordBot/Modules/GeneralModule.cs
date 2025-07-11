using Discord.Commands;
using System.Threading.Tasks;
using FishMMO.Database.Npgsql;

namespace FishMMO.DiscordBot.Modules
{
	[Group("general")]
	public class GeneralModule : ModuleBase<SocketCommandContext>
	{
		private readonly NpgsqlDbContext dbContext;

		public GeneralModule(NpgsqlDbContext dbContext)
		{
			this.dbContext = dbContext;
		}

		[Command("ping")]
		[Summary("Responds with 'Pong!'")]
		public async Task PingAsync()
		{
			await ReplyAsync("Pong!");
		}

		[Command("echo")]
		[Summary("Echoes the provided text.")]
		public async Task EchoAsync([Remainder] string text)
		{
			await ReplyAsync(text);
		}
	}
}