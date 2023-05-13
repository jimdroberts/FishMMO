using System;
using System.Linq;
using FishMMO_DB;
using FishMMO_DB.Entities;

namespace Server.Services
{
    public class AccountService
    {
        public static ClientAuthenticationResult TryLogin(ServerDbContext dbContext, string account, string password, bool autoCreateAccount = false)
        {
            if (!string.IsNullOrWhiteSpace(account) && !string.IsNullOrWhiteSpace(password))
            {
                var accountEntity = dbContext.Accounts.FirstOrDefault(a => a.Name == account);
                
                // demo feature: create account if it doesn't exist yet.
                // note: sqlite-net has no InsertOrIgnore so we do it in two steps
                if (autoCreateAccount && accountEntity == null)
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