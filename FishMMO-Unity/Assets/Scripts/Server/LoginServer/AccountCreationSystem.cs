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
		public override void InitializeOnce()
		{
			if (Server != null)
			{
				Server.RegisterBroadcast<CreateAccountBroadcast>(OnServerCreateAccountBroadcastReceived, false);
			}
			else
			{
				enabled = false;
			}
		}

		public override void Destroying()
		{
			if (Server != null)
			{
				Server.UnregisterBroadcast<CreateAccountBroadcast>(OnServerCreateAccountBroadcastReceived);
			}
		}

		private void OnServerCreateAccountBroadcastReceived(NetworkConnection conn, CreateAccountBroadcast msg, Channel channel)
		{
			ClientAuthenticationResult result = ClientAuthenticationResult.InvalidUsernameOrPassword;
			if (Server.NpgsqlDbContextFactory != null)
			{
				if (!AccountManager.GetConnectionEncryptionData(conn, out ConnectionEncryptionData encryptionData))
				{
					conn.Disconnect(true);
					return;
				}

				using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
				if (dbContext != null)
				{
					byte[] decryptedUsername = CryptoHelper.DecryptAES(encryptionData.SymmetricKey, encryptionData.IV, msg.username);
					byte[] decryptedSalt = CryptoHelper.DecryptAES(encryptionData.SymmetricKey, encryptionData.IV, msg.salt);
					byte[] decryptedVerifier = CryptoHelper.DecryptAES(encryptionData.SymmetricKey, encryptionData.IV, msg.verifier);

					string username = Encoding.UTF8.GetString(decryptedUsername);
					string salt = Encoding.UTF8.GetString(decryptedSalt);
					string verifier = Encoding.UTF8.GetString(decryptedVerifier);

					result = AccountService.TryCreate(dbContext, username, salt, verifier);
				}
			}
			Server.Broadcast(conn, new ClientAuthResultBroadcast() { result = result }, false, Channel.Reliable);
		}
	}
}