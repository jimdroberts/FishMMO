using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Server.DatabaseServices;
using FishMMO.Shared;
using System.Text;

namespace FishMMO.Server
{
	/// <summary>
	/// Account Creation system.
	/// </summary>
	public class AccountCreationSystem : ServerBehaviour
	{
		/// <summary>
		/// Initializes the account creation system and registers the broadcast handler for account creation requests.
		/// </summary>
		public override void InitializeOnce()
		{
			if (Server != null)
			{
				// Register handler for account creation requests from clients.
				Server.RegisterBroadcast<CreateAccountBroadcast>(OnServerCreateAccountBroadcastReceived, false);
			}
			else
			{
				// Disable this system if the server reference is missing.
				enabled = false;
			}
		}

		/// <summary>
		/// Cleans up the account creation system and unregisters the broadcast handler for account creation requests.
		/// </summary>
		public override void Destroying()
		{
			if (Server != null)
			{
				Server.UnregisterBroadcast<CreateAccountBroadcast>(OnServerCreateAccountBroadcastReceived);
			}
		}

		/// <summary>
		/// Handles broadcast to create a new account. Decrypts credentials, validates them, and attempts account creation in the database.
		/// Broadcasts the authentication result to the client.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="msg">CreateAccountBroadcast message containing encrypted credentials.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnServerCreateAccountBroadcastReceived(NetworkConnection conn, CreateAccountBroadcast msg, Channel channel)
		{
			ClientAuthenticationResult result = ClientAuthenticationResult.InvalidUsernameOrPassword;
			// Ensure the database factory is available before proceeding.
			if (Server.NpgsqlDbContextFactory != null)
			{
				// Retrieve encryption data for this connection. If missing, disconnect client.
				if (!AccountManager.GetConnectionEncryptionData(conn, out ConnectionEncryptionData encryptionData))
				{
					conn.Disconnect(true);
					return;
				}

				using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
				if (dbContext != null)
				{
					// Decrypt credentials sent by the client.
					byte[] decryptedUsername = CryptoHelper.DecryptAES(encryptionData.SymmetricKey, encryptionData.IV, msg.Username);
					byte[] decryptedSalt = CryptoHelper.DecryptAES(encryptionData.SymmetricKey, encryptionData.IV, msg.Salt);
					byte[] decryptedVerifier = CryptoHelper.DecryptAES(encryptionData.SymmetricKey, encryptionData.IV, msg.Verifier);

					string username = Encoding.UTF8.GetString(decryptedUsername);
					string salt = Encoding.UTF8.GetString(decryptedSalt);
					string verifier = Encoding.UTF8.GetString(decryptedVerifier);

					// Attempt to create the account in the database.
					result = AccountService.TryCreate(dbContext, username, salt, verifier);
				}
			}
			// Send the result of the account creation attempt back to the client.
			Server.Broadcast(conn, new ClientAuthResultBroadcast() { Result = result }, false, Channel.Reliable);
		}
	}
}