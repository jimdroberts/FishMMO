using System;
using System.Linq;
using FishMMO.Database;
using FishMMO.Database.Entities;
using FishMMO.Shared;

namespace FishMMO.Server.Services
{
    public class AccountService
    {
        public static ClientAuthenticationResult TryCreate(ServerDbContext dbContext, string accountName, string salt, string verifier)
        {
			if (!string.IsNullOrWhiteSpace(accountName) && !string.IsNullOrWhiteSpace(salt) && !string.IsNullOrWhiteSpace(verifier))
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
						Banned = false
					});
					dbContext.SaveChanges();
					return ClientAuthenticationResult.AccountCreated;
				}
			}

			return ClientAuthenticationResult.InvalidUsernameOrPassword;
		}

        public static ClientAuthenticationResult Get(ServerDbContext dbContext, string accountName, out string salt, out string verifier)
        {
			salt = "";
			verifier = "";
			if (!string.IsNullOrWhiteSpace(accountName))
            {
                var accountEntity = dbContext.Accounts.FirstOrDefault(a => a.Name == accountName);
                if (accountEntity == null)
                {
					return ClientAuthenticationResult.InvalidUsernameOrPassword;
                }
				else if (accountEntity.Banned)
				{
					return ClientAuthenticationResult.Banned;
				}
				else
				{
					salt = accountEntity.Salt;
					verifier = accountEntity.Verifier;

					accountEntity.Lastlogin = DateTime.UtcNow;
					dbContext.SaveChanges();

					// proceed to SRPVerify stage
					return ClientAuthenticationResult.SrpVerify;
				}
            }
			return ClientAuthenticationResult.InvalidUsernameOrPassword;
        }
    }
}