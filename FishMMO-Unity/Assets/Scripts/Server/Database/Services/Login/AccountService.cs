using System;
using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Shared;

namespace FishMMO.Server.DatabaseServices
{
    public class AccountService
    {
        public static ClientAuthenticationResult TryCreate(NpgsqlDbContext dbContext, string accountName, string salt, string verifier)
        {
			if (Constants.Authentication.IsAllowedUsername(accountName) && !string.IsNullOrWhiteSpace(salt) && !string.IsNullOrWhiteSpace(verifier))
			{
				var accountEntity = dbContext.Accounts.FirstOrDefault(a => a.Name == accountName);
				if (accountEntity == null)
				{
					dbContext.Accounts.Add(new AccountEntity()
					{
						Name = accountName,
						Salt = salt,
						Verifier = verifier,
						Created = DateTime.UtcNow,
						Lastlogin = DateTime.UtcNow,
						AccessLevel = (byte)AccessLevel.Player,
					});
					dbContext.SaveChanges();
					return ClientAuthenticationResult.AccountCreated;
				}
			}

			return ClientAuthenticationResult.InvalidUsernameOrPassword;
		}

        public static ClientAuthenticationResult Get(NpgsqlDbContext dbContext, string accountName, out string salt, out string verifier, out AccessLevel accessLevel)
        {
			salt = "";
			verifier = "";
			accessLevel = AccessLevel.Banned;

			if (Constants.Authentication.IsAllowedUsername(accountName))
            {
                var accountEntity = dbContext.Accounts.FirstOrDefault(a => a.Name == accountName);
                if (accountEntity == null)
                {
					return ClientAuthenticationResult.InvalidUsernameOrPassword;
                }
				else if ((AccessLevel)accountEntity.AccessLevel == AccessLevel.Banned)
				{
					return ClientAuthenticationResult.Banned;
				}
				else
				{
					salt = accountEntity.Salt;
					verifier = accountEntity.Verifier;
					accessLevel = (AccessLevel)accountEntity.AccessLevel;
					accountEntity.Lastlogin = DateTime.UtcNow;
					dbContext.SaveChanges();

					// proceed to SrpVerify stage
					return ClientAuthenticationResult.SrpVerify;
				}
            }
			return ClientAuthenticationResult.InvalidUsernameOrPassword;
        }
    }
}