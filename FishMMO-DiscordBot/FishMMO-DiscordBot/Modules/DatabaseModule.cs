using System;
using System.Threading.Tasks;
using Discord.Commands;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;

namespace FishMMO.DiscordBot.Modules
{
	public class DatabaseModule : ModuleBase<SocketCommandContext>
	{
		private readonly NpgsqlDbContext dbContext;

		public DatabaseModule(NpgsqlDbContext dbContext)
		{
			this.dbContext = dbContext;
		}

		[Command("getaccount")]
		[Summary("Retrieves an account by its name from the database.")]
		public async Task GetAccountAsync([Remainder] string accountName)
		{
			try
			{
				var account = await this.dbContext.Accounts
											  .FirstOrDefaultAsync((System.Linq.Expressions.Expression<System.Func<AccountEntity, bool>>)(a => a.Name == accountName));

				if (account != null)
				{
					await ReplyAsync($"Account Found: Name: {account.Name}, Created: {account.Created}");
				}
				else
				{
					await ReplyAsync($"Account '{accountName}' not found.");
				}
			}
			catch (Exception ex)
			{
				await ReplyAsync($"An error occurred while fetching the account: {ex.Message}");
				Console.WriteLine($"Database Error: {ex.ToString()}");
			}
		}
	}
}
