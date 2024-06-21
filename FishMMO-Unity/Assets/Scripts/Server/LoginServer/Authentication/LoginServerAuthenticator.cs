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

namespace FishMMO.Server
{
	// Login Server Authenticator that allows Clients to connect with basic password authentication.
	public class LoginServerAuthenticator : Authenticator
	{
		/// <summary>
		/// Server Authentication event. Subscribe to this if you want something to happen immediately after client authentication success.
		/// </summary>
		public override event Action<NetworkConnection, bool> OnAuthenticationResult;
		public event Action<NetworkConnection, bool> OnClientAuthenticationResult;

		public NpgsqlDbContextFactory NpgsqlDbContextFactory { get; set; }

		public override void InitializeOnce(NetworkManager networkManager)
		{
			base.InitializeOnce(networkManager);

			networkManager.ServerManager.OnRemoteConnectionState += ServerManager_OnRemoteConnectionState;

			// Listen for broadcast from clients.
			networkManager.ServerManager.RegisterBroadcast<ClientHandshake>(OnServerClientHandshakeReceived, false);
			networkManager.ServerManager.RegisterBroadcast<SrpVerifyBroadcast>(OnServerSrpVerifyBroadcastReceived, false);
			networkManager.ServerManager.RegisterBroadcast<SrpProofBroadcast>(OnServerSrpProofBroadcastReceived, false);
			networkManager.ServerManager.RegisterBroadcast<SrpSuccess>(OnServerSrpSuccessBroadcastReceived, false);
		}

		internal void OnServerClientHandshakeReceived(NetworkConnection conn, ClientHandshake msg, Channel channel)
		{
			/* If client is already authenticated this could be an attack. Connections
			 * are removed when a client disconnects so there is no reason they should
			 * already be considered authenticated. */
			if (conn.IsAuthenticated ||
				msg.publicKey == null)
			{
				conn.Disconnect(true);
				return;
			}

			// Generate encryption keys for the connection
			AccountManager.AddConnectionEncryptionData(conn, msg.publicKey);

			// Get the encryption data
			if (AccountManager.GetConnectionEncryptionData(conn, out ConnectionEncryptionData encryptionData))
			{
				using (var rsa = RSA.Create(2048))
				{
					// Add public key to RSA
					CryptoHelper.ImportPublicKey(rsa, msg.publicKey);

					// Encrypt symmetric key and iv with client's public key
					byte[] encryptedSymmetricKey = rsa.Encrypt(encryptionData.SymmetricKey, RSAEncryptionPadding.Pkcs1);
					byte[] encryptedIV = rsa.Encrypt(encryptionData.IV, RSAEncryptionPadding.Pkcs1);

					// Send the encrypted symmetric key
					ServerHandshake handshake = new ServerHandshake()
					{
						key = encryptedSymmetricKey,
						iv = encryptedIV,
					};
					Server.Broadcast(conn, handshake, false, Channel.Reliable);
				}
			}
			else
			{
				// Something weird happened... Adding a connection IV should not be an issue.
				conn.Disconnect(true);
			}
		}

		/// <summary>
		/// Received on server when a Client sends the SrpVerify broadcast message.
		/// </summary>
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

			if (!AccountManager.GetConnectionEncryptionData(conn, out ConnectionEncryptionData encryptionData))
			{
				conn.Disconnect(true);
				return;
			}

			// Decrypt username
			byte[] decryptedRawUsername = CryptoHelper.DecryptAES(encryptionData.SymmetricKey, encryptionData.IV, msg.s);
			string username = Encoding.UTF8.GetString(decryptedRawUsername);

			// if the database is unavailable
			if (NpgsqlDbContextFactory == null)
			{
				result = ClientAuthenticationResult.ServerFull;
			}
			else
			{
				using var dbContext = NpgsqlDbContextFactory.CreateDbContext();

				// check if any characters are online already
				if (CharacterService.TryGetOnline(dbContext, username))
				{
					KickRequestService.Save(dbContext, username);
					result = ClientAuthenticationResult.AlreadyOnline;
				}
				else
				{
					// get account salt and verifier if no one is online
					result = AccountService.Get(dbContext, username, out string salt, out string verifier, out AccessLevel accessLevel);
					if (result != ClientAuthenticationResult.Banned &&
						result == ClientAuthenticationResult.SrpVerify)
					{
						// prepare account
						AccountManager.AddConnectionAccount(conn, username, msg.publicEphemeral, salt, verifier, accessLevel);

						// verify SrpState equals SrpVerify and then send account public data
						if (AccountManager.TryUpdateSrpState(conn, SrpState.SrpVerify, SrpState.SrpVerify, (a) =>
							{
								//UnityEngine.Debug.Log("SrpVerify");

								SrpVerifyBroadcast srpVerify = new SrpVerifyBroadcast()
								{
									s = Convert.FromBase64String(a.SrpData.Salt),
									publicEphemeral = a.SrpData.ServerEphemeral.Public,
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
			ClientAuthResultBroadcast authResult = new ClientAuthResultBroadcast()
			{
				result = result,
			};
			Server.Broadcast(conn, authResult, false, Channel.Reliable);
		}

		/// <summary>
		/// Received on server when a Client sends the SrpProof broadcast message.
		/// </summary>
		internal void OnServerSrpProofBroadcastReceived(NetworkConnection conn, SrpProofBroadcast msg, Channel channel)
		{
			/* If client is already authenticated this could be an attack. Connections
			 * are removed when a client disconnects so there is no reason they should
			 * already be considered authenticated. */
			if (conn.IsAuthenticated ||
				!AccountManager.TryUpdateSrpState(conn, SrpState.SrpVerify, SrpState.SrpProof, (a) =>
				{
					if (a.SrpData.GetProof(msg.proof, out string serverProof))
					{
						//UnityEngine.Debug.Log("SrpProof");

						SrpProofBroadcast msg2 = new SrpProofBroadcast()
						{
							proof = serverProof,
						};
						Server.Broadcast(conn, msg2, false, Channel.Reliable);
						return true;
					}
					return false;
				}))
			{
				ClientAuthResultBroadcast authResult = new ClientAuthResultBroadcast()
				{
					result = ClientAuthenticationResult.InvalidUsernameOrPassword,
				};
				Server.Broadcast(conn, authResult, false, Channel.Unreliable);
				conn.Disconnect(false);
			}
		}

		/// <summary>
		/// Received on server when a Client sends the SrpSuccess broadcast message.
		/// </summary>
		internal void OnServerSrpSuccessBroadcastReceived(NetworkConnection conn, SrpSuccess msg, Channel channel)
		{
			/* If client is already authenticated this could be an attack. Connections
			 * are removed when a client disconnects so there is no reason they should
			 * already be considered authenticated. */
			if (conn.IsAuthenticated ||
				!AccountManager.TryUpdateSrpState(conn, SrpState.SrpProof, SrpState.SrpSuccess, (a) =>
				{
					using var dbContext = NpgsqlDbContextFactory.CreateDbContext();
					// attempt to complete login authentication and return a result broadcast
					ClientAuthenticationResult result = TryLogin(dbContext, ClientAuthenticationResult.LoginSuccess, a.SrpData.UserName);

					bool authenticated = result != ClientAuthenticationResult.InvalidUsernameOrPassword &&
										 result != ClientAuthenticationResult.ServerFull;

					// tell the connecting client the result of the authentication
					ClientAuthResultBroadcast authResult = new ClientAuthResultBroadcast()
					{
						result = result,
					};
					Server.Broadcast(conn, authResult, false, Channel.Reliable);

					//UnityEngine.Debug.Log("Authorized: " + authResult);

					/* Invoke result. This is handled internally to complete the connection authentication or kick client.
					 * It's important to call this after sending the broadcast so that the broadcast
					 * makes it out to the client before the kick. */
					OnAuthentication(conn, authenticated);
					OnClientAuthenticationResult?.Invoke(conn, authenticated);
					return true;
				}))
			{
				conn.Disconnect(true);
			}
		}

		public virtual void OnAuthentication(NetworkConnection conn, bool authenticated)
		{
			OnAuthenticationResult?.Invoke(conn, authenticated);
		}

		/// <summary>
		/// Login Server TryLogin function.
		/// </summary>
		internal virtual ClientAuthenticationResult TryLogin(NpgsqlDbContext dbContext, ClientAuthenticationResult result, string username)
		{
			return ClientAuthenticationResult.LoginSuccess;
		}

		// remove the connection from the AccountManager
		private void ServerManager_OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
		{
			if (args.ConnectionState == RemoteConnectionState.Stopped)
			{
				AccountManager.RemoveConnectionAccount(conn);
			}
		}
	}
}