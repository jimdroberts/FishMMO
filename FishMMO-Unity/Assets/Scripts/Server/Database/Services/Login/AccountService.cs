using System;
using System.Linq;
using FishMMO.Database;
using FishMMO.Database.Entities;
using FishMMO.Shared;

namespace FishMMO.Server.DatabaseServices
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
						AccessLevel = (byte)AccessLevel.Player,
					});
					dbContext.SaveChanges();
					return ClientAuthenticationResult.AccountCreated;
				}
			}

			return ClientAuthenticationResult.InvalidUsernameOrPassword;
		}

        public static ClientAuthenticationResult Get(ServerDbContext dbContext, string accountName, out string salt, out string verifier, out AccessLevel accessLevel)
        {
			salt = "";
			verifier = "";
			accessLevel = AccessLevel.Banned;

			if (!string.IsNullOrWhiteSpace(accountName))
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

					// proceed to SRPVerify stage
					return ClientAuthenticationResult.SrpVerify;
				}
            }
			return ClientAuthenticationResult.InvalidUsernameOrPassword;
        }
    }
}