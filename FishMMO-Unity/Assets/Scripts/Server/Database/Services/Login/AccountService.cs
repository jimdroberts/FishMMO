using System;
using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Shared;

namespace FishMMO.Server.DatabaseServices
{
		/// <summary>
		/// Provides methods for managing accounts, including authentication, creation, and login operations.
		/// </summary>
		public class AccountService
	{
		/// <summary>
		/// Attempts to get the last login time for an account.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="accountName">The account name.</param>
		/// <param name="lastLogin">The last login time, if found.</param>
		/// <returns>True if the last login was found; otherwise, false.</returns>
		public static bool TryGetLastLogin(NpgsqlDbContext dbContext, string accountName, out DateTime lastLogin)
		{
			lastLogin = default;
			if (Constants.Authentication.IsAllowedUsername(accountName))
			{
				var accountEntity = dbContext.Accounts.FirstOrDefault(a => a.Name == accountName);
				if (accountEntity != null)
				{
					lastLogin = accountEntity.Lastlogin;

					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Attempts to create a new account with the specified credentials.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="accountName">The account name.</param>
		/// <param name="salt">The salt for password hashing.</param>
		/// <param name="verifier">The verifier for password hashing.</param>
		/// <returns>The result of the account creation attempt.</returns>
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

		/// <summary>
		/// Attempts to log in to an account and retrieves authentication data if successful.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="accountName">The account name.</param>
		/// <param name="salt">The salt for password hashing, if found.</param>
		/// <param name="verifier">The verifier for password hashing, if found.</param>
		/// <param name="accessLevel">The access level of the account, if found.</param>
		/// <returns>The result of the login attempt.</returns>
		public static ClientAuthenticationResult TryLogin(NpgsqlDbContext dbContext, string accountName, out string salt, out string verifier, out AccessLevel accessLevel)
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