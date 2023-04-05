using System;
using System.Linq;
using Server.Entities;

namespace Server.Services
{
    public class AccountService
    {
        public const bool AUTO_ACCOUNT_CREATION = true;
        
        public ClientAuthenticationResult TryLogin(string account, string password)
        {
            if (!string.IsNullOrWhiteSpace(account) && !string.IsNullOrWhiteSpace(password))
            {
                var dbContextFactory = new ServerDbContextFactory();
                using var dbContext = dbContextFactory.CreateDbContext(new string[] {});

                var accountEntity = dbContext.Accounts.FirstOrDefault(a => a.Name == account);
                
                // demo feature: create account if it doesn't exist yet.
                // note: sqlite-net has no InsertOrIgnore so we do it in two steps
                if (AUTO_ACCOUNT_CREATION && accountEntity == null)
                {
                    dbContext.Accounts.Add(new AccountEntity()
                    {
                        Name = account,
                        Password = password,
                        Created = DateTime.UtcNow,
                        Lastlogin = DateTime.UtcNow,
                        Banned = false
                    });
                    dbContext.SaveChanges();
                    return ClientAuthenticationResult.LoginSuccess;
                }

                // check account name, password, banned status
                // TODO: don't use plain text password
                if (accountEntity.Password == password && !accountEntity.Banned)
                {
                    // save last login time and return true
                    accountEntity.Lastlogin = DateTime.UtcNow;
                    dbContext.SaveChanges();
                    return ClientAuthenticationResult.LoginSuccess;
                }
            }
            return ClientAuthenticationResult.InvalidUsernameOrPassword;
        }
    }
}