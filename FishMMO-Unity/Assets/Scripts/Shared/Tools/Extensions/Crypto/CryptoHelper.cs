using System;
using System.Security.Cryptography;

namespace FishMMO.Shared
{
	public static class CryptoHelper
	{
		public static byte[] ExportPublicKey(RSA rsa)
		{
			RSAParameters rsaParameters = rsa.ExportParameters(false); // false to get the public key only
			byte[] modulus = rsaParameters.Modulus;
			byte[] exponent = rsaParameters.Exponent;

			// Ensure that modulus and exponent are padded to correct lengths if necessary
			int modulusLength = (rsa.KeySize + 7) / 8; // Convert bits to bytes
			int exponentLength = exponent.Length;

			// Create a structured byte array for the public key
			byte[] publicKeyBytes = new byte[modulusLength + exponentLength];

			// Copy modulus and exponent into the structured byte array
			Buffer.BlockCopy(modulus, 0, publicKeyBytes, 0, modulus.Length);
			Buffer.BlockCopy(exponent, 0, publicKeyBytes, modulus.Length, exponent.Length);

			return publicKeyBytes;
		}

		public static void ImportPublicKey(RSA rsa, byte[] publicKeyBytes)
		{
			int modulusLength = (rsa.KeySize + 7) / 8; // Calculate modulus length in bytes

			// Split the publicKeyBytes into modulus and exponent
			byte[] modulus = new byte[modulusLength];
			byte[] exponent = new byte[publicKeyBytes.Length - modulusLength];

			Buffer.BlockCopy(publicKeyBytes, 0, modulus, 0, modulusLength);
			Buffer.BlockCopy(publicKeyBytes, modulusLength, exponent, 0, exponent.Length);

			// Create RSAParameters from modulus and exponent
			RSAParameters rsaParameters = new RSAParameters
			{
				Modulus = modulus,
				Exponent = exponent
			};

			// Import RSAParameters into RSA instance
			rsa.ImportParameters(rsaParameters);
		}

		public static byte[] GenerateKey(int length)
		{
			using (var rng = new RNGCryptoServiceProvider())
			{
				byte[] key = new byte[length];
				rng.GetBytes(key);
				return key;
			}
		}

		public static byte[] EncryptAES(byte[] symmetricKey, byte[] iv, byte[] input)
		{
			using (Aes aes = Aes.Create())
			{
				aes.Key = symmetricKey;
				aes.IV = iv;
				using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
				using (var ms = new System.IO.MemoryStream())
				{
					using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
					{
						cs.Write(input, 0, input.Length);
					}
					return ms.ToArray();
				}
			}
		}

		public static byte[] DecryptAES(byte[] symmetricKey, byte[] iv, byte[] input)
		{
			using (Aes aes = Aes.Create())
			{
				aes.Key = symmetricKey;
				aes.IV = iv;
				using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
				using (var ms = new System.IO.MemoryStream(input))
				{
					using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
					using (var resultStream = new System.IO.MemoryStream())
					{
						cs.CopyTo(resultStream);
						return resultStream.ToArray();
					}
				}
			}
		}
	}
}
