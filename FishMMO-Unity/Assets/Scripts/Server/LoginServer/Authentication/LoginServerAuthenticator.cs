using FishNet.Authenticating;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using FishMMO.Database.Npgsql;
using System;
using System.Security.Cryptography;
using System.Text;
using FishMMO.Server.DatabaseServices;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Server
{
	// Login Server Authenticator that allows Clients to connect with basic password authentication.
	public class LoginServerAuthenticator : Authenticator
	{
		/// <summary>
		/// Event triggered when server authentication completes for a client connection.
		/// Subscribe to this to handle post-authentication logic.
		/// </summary>
		public override event Action<NetworkConnection, bool> OnAuthenticationResult;

		/// <summary>
		/// Event triggered when client authentication completes.
		/// Used for custom client-side authentication result handling.
		/// </summary>
		public event Action<NetworkConnection, bool> OnClientAuthenticationResult;

		/// <summary>
		/// Factory for creating PostgreSQL database contexts.
		/// Used to access account and character data during authentication.
		/// </summary>
		public NpgsqlDbContextFactory NpgsqlDbContextFactory { get; set; }

		/// <summary>
		/// Initializes the authenticator and registers broadcast handlers for client authentication steps.
		/// </summary>
		/// <param name="networkManager">The network manager instance.</param>
		public override void InitializeOnce(NetworkManager networkManager)
		{
			base.InitializeOnce(networkManager);

			// Subscribe to remote connection state changes to clean up accounts on disconnect.
			networkManager.ServerManager.OnRemoteConnectionState += ServerManager_OnRemoteConnectionState;

			// Register handlers for client authentication broadcasts.
			networkManager.ServerManager.RegisterBroadcast<ClientHandshake>(OnServerClientHandshakeReceived, false);
			networkManager.ServerManager.RegisterBroadcast<SrpVerifyBroadcast>(OnServerSrpVerifyBroadcastReceived, false);
			networkManager.ServerManager.RegisterBroadcast<SrpProofBroadcast>(OnServerSrpProofBroadcastReceived, false);
		}

		/// <summary>
		/// Handles the initial handshake broadcast from a client, sets up encryption for the connection.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="msg">The handshake message containing the client's public key.</param>
		/// <param name="channel">The network channel used.</param>
		internal void OnServerClientHandshakeReceived(NetworkConnection conn, ClientHandshake msg, Channel channel)
		{
			/* If client is already authenticated this could be an attack. Connections
			 * are removed when a client disconnects so there is no reason they should
			 * already be considered authenticated. */
			if (conn.IsAuthenticated ||
				msg.PublicKey == null)
			{
				conn.Disconnect(true);
				return;
			}

			// Generate encryption keys for the connection and store them in AccountManager.
			AccountManager.AddConnectionEncryptionData(conn, msg.PublicKey);

			// Retrieve the generated encryption data for this connection.
			if (AccountManager.GetConnectionEncryptionData(conn, out ConnectionEncryptionData encryptionData))
			{
				using (var rsa = RSA.Create(2048))
				{
					// Import the client's public key into the RSA instance.
					CryptoHelper.ImportPublicKey(rsa, msg.PublicKey);

					// Encrypt the symmetric key and IV using the client's public key.
					byte[] encryptedSymmetricKey = rsa.Encrypt(encryptionData.SymmetricKey, RSAEncryptionPadding.Pkcs1);
					byte[] encryptedIV = rsa.Encrypt(encryptionData.IV, RSAEncryptionPadding.Pkcs1);

					// Send the encrypted symmetric key and IV to the client for secure communication.
					ServerHandshake handshake = new ServerHandshake()
					{
						Key = encryptedSymmetricKey,
						IV = encryptedIV,
					};
					Server.Broadcast(conn, handshake, false, Channel.Reliable);
				}
			}
			else
			{
				// This should not happen; encryption data must be generated for every connection.
				Log.Warning("LoginServerAuthenticator", "Failed to generation encryption keys for connection.");
				conn.Disconnect(true);
			}
		}

		/// <summary>
		/// Handles the SrpVerify broadcast from a client, verifies credentials and manages authentication state.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="msg">The SrpVerify broadcast message.</param>
		/// <param name="channel">The network channel used.</param>
		internal void OnServerSrpVerifyBroadcastReceived(NetworkConnection conn, SrpVerifyBroadcast msg, Channel channel)
		{
			/* If client is already authenticated this could be an attack. Connections
			 * are removed when a client disconnects so there is no reason they should
			 * already be considered authenticated. */
			if (conn.IsAuthenticated)
			{
				conn.Disconnect(true);
				return;
			}
			ClientAuthenticationResult result;

			// Retrieve encryption data for this connection.
			if (!AccountManager.GetConnectionEncryptionData(conn, out ConnectionEncryptionData encryptionData))
			{
				conn.Disconnect(true);
				return;
			}

			// Decrypt the username sent by the client.
			byte[] decryptedRawUsername = CryptoHelper.DecryptAES(encryptionData.SymmetricKey, encryptionData.IV, msg.S);
			string username = Encoding.UTF8.GetString(decryptedRawUsername);

			// If the database is unavailable, authentication cannot proceed.
			if (NpgsqlDbContextFactory == null)
			{
				result = ClientAuthenticationResult.ServerFull;
			}
			else
			{
				using var dbContext = NpgsqlDbContextFactory.CreateDbContext();

				// Check if any characters are online already for this username.
				if (CharacterService.TryGetOnline(dbContext, username))
				{
					// Add a kick request for the online character.
					KickRequestService.Save(dbContext, username);
					result = ClientAuthenticationResult.AlreadyOnline;
				}
				else
				{
					// Decrypt the public ephemeral value sent by the client.
					byte[] decryptedRawPublicEphemeral = CryptoHelper.DecryptAES(encryptionData.SymmetricKey, encryptionData.IV, msg.PublicEphemeral);
					string publicEphemeral = Encoding.UTF8.GetString(decryptedRawPublicEphemeral);

					// Attempt to login and retrieve salt, verifier, and access level.
					result = AccountService.TryLogin(dbContext, username, out string salt, out string verifier, out AccessLevel accessLevel);
					if (result != ClientAuthenticationResult.Banned &&
						result == ClientAuthenticationResult.SrpVerify)
					{
						// Prepare account data for SRP verification.
						AccountManager.AddConnectionAccount(conn, username, publicEphemeral, salt, verifier, accessLevel);

						// If SRP state is correct, send account public data to client.
						if (AccountManager.TryUpdateSrpState(conn, SrpState.SrpVerify, SrpState.SrpVerify, (a) =>
							{
								// Encrypt salt and server ephemeral public value for client.
								byte[] encryptedSalt = CryptoHelper.EncryptAES(encryptionData.SymmetricKey, encryptionData.IV, Encoding.UTF8.GetBytes(a.SrpData.Salt));
								byte[] encryptedPublicServerEphemeral = CryptoHelper.EncryptAES(encryptionData.SymmetricKey, encryptionData.IV, Encoding.UTF8.GetBytes(a.SrpData.ServerEphemeral.Public));

								SrpVerifyBroadcast srpVerify = new SrpVerifyBroadcast()
								{
									S = encryptedSalt,
									PublicEphemeral = encryptedPublicServerEphemeral,
								};
								Server.Broadcast(conn, srpVerify, false, Channel.Reliable);
								return true;
							}))
						{
							return;
						}
					}
				}
			}
			// Send authentication result to client.
			ClientAuthResultBroadcast authResult = new ClientAuthResultBroadcast()
			{
				Result = result,
			};
			Server.Broadcast(conn, authResult, false, Channel.Reliable);
		}

		/// <summary>
		/// Handles the SrpProof broadcast from a client, validates proof and finalizes authentication.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="msg">The SrpProof broadcast message.</param>
		/// <param name="channel">The network channel used.</param>
		internal void OnServerSrpProofBroadcastReceived(NetworkConnection conn, SrpProofBroadcast msg, Channel channel)
		{
			// Retrieve encryption data for this connection.
			if (!AccountManager.GetConnectionEncryptionData(conn, out ConnectionEncryptionData encryptionData))
			{
				conn.Disconnect(true);
				return;
			}

			/* If client is already authenticated this could be an attack. Connections
			 * are removed when a client disconnects so there is no reason they should
			 * already be considered authenticated. */
			if (conn.IsAuthenticated ||
				!AccountManager.TryUpdateSrpState(conn, SrpState.SrpVerify, SrpState.SrpProof, (a) =>
				{
					// Decrypt client proof sent by the client.
					byte[] decryptedClientProof = CryptoHelper.DecryptAES(encryptionData.SymmetricKey, encryptionData.IV, msg.Proof);
					string clientProof = Encoding.UTF8.GetString(decryptedClientProof);

					// Validate client proof and update authentication state.
					if (a.SrpData.GetProof(clientProof, out string serverProof) &&
						AccountManager.TryUpdateSrpState(conn, SrpState.SrpProof, SrpState.SrpSuccess, (a) =>
						{
							using var dbContext = NpgsqlDbContextFactory.CreateDbContext();

							// Attempt to complete login authentication and return a result broadcast.
							ClientAuthenticationResult result = TryLogin(dbContext, ClientAuthenticationResult.LoginSuccess, a.SrpData.UserName);

							bool authenticated = result != ClientAuthenticationResult.InvalidUsernameOrPassword &&
												 result != ClientAuthenticationResult.ServerFull;

							// Encrypt server proof for client.
							byte[] encryptedServerProof = CryptoHelper.EncryptAES(encryptionData.SymmetricKey, encryptionData.IV, Encoding.UTF8.GetBytes(serverProof));

							// Send final authentication result and proof to client.
							SrpSuccessBroadcast resultMsg = new SrpSuccessBroadcast()
							{
								Proof = encryptedServerProof,
								Result = result,
							};
							Server.Broadcast(conn, resultMsg, false, Channel.Reliable);

							/* Invoke result. This is handled internally to complete the connection authentication or kick client.
							 * It's important to call this after sending the broadcast so that the broadcast
							 * makes it out to the client before the kick. */
							OnAuthentication(conn, authenticated);
							OnClientAuthenticationResult?.Invoke(conn, authenticated);
							return true;
						}))
					{
						return true;
					}
					return false;
				}))
			{
				// If authentication fails, notify client and disconnect.
				ClientAuthResultBroadcast authResult = new ClientAuthResultBroadcast()
				{
					Result = ClientAuthenticationResult.InvalidUsernameOrPassword,
				};
				Server.Broadcast(conn, authResult, false, Channel.Unreliable);
				conn.Disconnect(false);
			}
		}

		/// <summary>
		/// Invokes the authentication result event for a connection.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="authenticated">True if authentication succeeded, false otherwise.</param>
		public virtual void OnAuthentication(NetworkConnection conn, bool authenticated)
		{
			OnAuthenticationResult?.Invoke(conn, authenticated);
		}

		/// <summary>
		/// Attempts to complete login authentication for a user. Override for custom logic.
		/// </summary>
		/// <param name="dbContext">Database context for account lookup.</param>
		/// <param name="result">Initial authentication result.</param>
		/// <param name="username">Username to authenticate.</param>
		/// <returns>Final authentication result.</returns>
		internal virtual ClientAuthenticationResult TryLogin(NpgsqlDbContext dbContext, ClientAuthenticationResult result, string username)
		{
			return ClientAuthenticationResult.LoginSuccess;
		}

		/// <summary>
		/// Handles remote connection state changes to clean up account data when a connection stops.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="args">Arguments describing the connection state change.</param>
		private void ServerManager_OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
		{
			if (args.ConnectionState == RemoteConnectionState.Stopped)
			{
				AccountManager.RemoveConnectionAccount(conn);
			}
		}
	}
}