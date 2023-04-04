using System;
using SQLite;

namespace Server
{
	public partial class Database
	{
		public const bool AUTO_ACCOUNT_CREATION = true;

		class accounts
		{
			[PrimaryKey] // important for performance: O(log n) instead of O(n)
			public string name { get; set; }
			public string password { get; set; }
			public DateTime created { get; set; }
			public DateTime lastlogin { get; set; }
			public bool banned { get; set; }
		}

		public ClientAuthenticationResult TryLogin(string account, string password)
		{
			if (!string.IsNullOrWhiteSpace(account) && !string.IsNullOrWhiteSpace(password))
			{
				// demo feature: create account if it doesn't exist yet.
				// note: sqlite-net has no InsertOrIgnore so we do it in two steps
				if (AUTO_ACCOUNT_CREATION && connection.FindWithQuery<accounts>("SELECT * FROM accounts WHERE name=?", account) == null)
				{
					connection.Insert(new accounts { name = account, password = password, created = DateTime.UtcNow, lastlogin = DateTime.Now, banned = false });
					return ClientAuthenticationResult.LoginSuccess;
				}

				// check account name, password, banned status
				if (connection.FindWithQuery<accounts>("SELECT * FROM accounts WHERE name=? AND password=? and banned=0", account, password) != null)
				{
					// save last login time and return true
					connection.Execute("UPDATE accounts SET lastlogin=? WHERE name=?", DateTime.UtcNow, account);
					return ClientAuthenticationResult.LoginSuccess;
				}
			}
			return ClientAuthenticationResult.InvalidUsernameOrPassword;
		}
	}
}