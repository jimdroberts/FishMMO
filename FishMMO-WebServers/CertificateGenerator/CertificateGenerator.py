from cryptography import x509
from cryptography.x509.oid import NameOID
from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.backends import default_backend
from cryptography.hazmat.primitives.asymmetric import rsa
from cryptography.hazmat.primitives.serialization import Encoding, PrivateFormat, NoEncryption
from datetime import datetime, timedelta, timezone
import os

# Generate a new private key
private_key = rsa.generate_private_key(
    public_exponent=65537,
    key_size=2048,
    backend=default_backend()
)

# Read input for subject attributes
country_name = input("Enter country name (e.g., US): ").strip()
state_name = input("Enter state or province name: ").strip()
locality_name = input("Enter locality or city name: ").strip()
organization_name = input("Enter organization name: ").strip()
common_name = input("Enter common name (e.g., localhost): ").strip()

# Specify the subject of the certificate
subject = x509.Name([
    x509.NameAttribute(NameOID.COUNTRY_NAME, country_name),
    x509.NameAttribute(NameOID.STATE_OR_PROVINCE_NAME, state_name),
    x509.NameAttribute(NameOID.LOCALITY_NAME, locality_name),
    x509.NameAttribute(NameOID.ORGANIZATION_NAME, organization_name),
    x509.NameAttribute(NameOID.COMMON_NAME, common_name),
])

print("Certificate Subject:")
print(subject)

# Specify the current time in UTC
now = datetime.now(timezone.utc)

# Create a self-signed certificate
certificate = x509.CertificateBuilder().subject_name(
    subject
).issuer_name(
    subject
).public_key(
    private_key.public_key()
).serial_number(
    x509.random_serial_number()
).not_valid_before(
    now
).not_valid_after(
    now + timedelta(days=365)
).sign(private_key, hashes.SHA256(), default_backend())

# Get the current working directory
cwd = os.path.dirname(__file__)

# Write the private key to a file (PEM format)
private_key_path = os.path.join(cwd, "privatekey.pem")
with open(private_key_path, "wb") as f:
    f.write(private_key.private_bytes(
        encoding=Encoding.PEM,
        format=PrivateFormat.TraditionalOpenSSL,
        encryption_algorithm=NoEncryption()
    ))
print(f"Private Key saved to: {private_key_path}")

# Write the certificate to a file (PEM format)
certificate_path = os.path.join(cwd, "certificate.pem")
with open(certificate_path, "wb") as f:
    f.write(certificate.public_bytes(Encoding.PEM))
print(f"Certificate saved to: {certificate_path}")