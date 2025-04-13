using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

class Program
{
	private static readonly string CertificateFileName = "certificate.pfx";

	static void Main()
	{
		Console.Write("Enter country name (e.g., US): ");
		string country = Console.ReadLine();

		Console.Write("Enter state or province name: ");
		string state = Console.ReadLine();

		Console.Write("Enter locality or city name: ");
		string locality = Console.ReadLine();

		Console.Write("Enter organization name: ");
		string organization = Console.ReadLine();

		Console.Write("Enter common name (e.g., localhost): ");
		string commonName = Console.ReadLine();

		Console.Write("Enter password for the .pfx file: ");
		string password = Console.ReadLine();

		string subject = $"C={country}, ST={state}, L={locality}, O={organization}, CN={commonName}";

		// Generate RSA key pair
		using RSA rsa = RSA.Create(2048);

		// Define certificate request
		var request = new CertificateRequest(
			new X500DistinguishedName(subject),
			rsa,
			HashAlgorithmName.SHA256,
			RSASignaturePadding.Pkcs1);

		// Self-sign the certificate, valid for 1 year
		var notBefore = DateTimeOffset.UtcNow;
		var notAfter = notBefore.AddYears(1);
		var cert = request.CreateSelfSigned(notBefore, notAfter);

		// Export the certificate and private key to a PFX
		byte[] pfxBytes = cert.Export(X509ContentType.Pfx, password);

		// Save the .pfx file
		File.WriteAllBytes(CertificateFileName, pfxBytes);
		Console.WriteLine($"PFX certificate saved to: {Path.GetFullPath(CertificateFileName)}");
	}
}