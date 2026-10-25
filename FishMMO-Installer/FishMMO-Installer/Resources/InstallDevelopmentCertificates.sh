#!/bin/bash
# Installed by FishMMO-Installer. No public DNS, nginx or ACME service is used.
set -euo pipefail
umask 077
account=''
trust_ca=0
generate_only=0
output_root=''
while (( $# )); do
    case "$1" in
        --user) account=${2:?Missing account}; shift 2 ;;
        --trust) trust_ca=1; shift ;;
        --generate-only) generate_only=1; output_root=${2:?Missing output directory}; shift 2 ;;
        *) echo "Unknown option: $1" >&2; exit 2 ;;
    esac
done
for tool in openssl flock install; do command -v "$tool" >/dev/null || { echo "Required command missing: $tool" >&2; exit 1; }; done
if (( generate_only )); then
    [[ $trust_ca == 0 && -z $account ]] || { echo '--generate-only cannot change accounts or trust.' >&2; exit 2; }
    mkdir -p "$output_root"
    output_root=$(cd "$output_root" && pwd)
    ca_dir="$output_root/ca"
    cert_dir="$output_root/certs"
    reader_group=$(id -gn)
else
    [[ $EUID == 0 ]] || { echo 'Run with sudo, or use --generate-only DIRECTORY.' >&2; exit 1; }
    [[ -n $account ]] || { echo '--user must name the existing game-server account.' >&2; exit 2; }
    id -u "$account" >/dev/null
    ca_dir=/etc/fishmmo/dev-ca
    cert_dir=/etc/fishmmo/certs
    reader_group=fishmmo-certs
    # Do not replace the production deployment or let a renewal hook overwrite development files.
    [[ ! -e /etc/letsencrypt/renewal-hooks/deploy/fishmmo-certificates ]] || {
        echo 'A production certificate deployment hook already exists. Development installation cancelled.' >&2; exit 1;
    }
fi
if [[ ( -e "$cert_dir/fullchain.pem" || -L "$cert_dir/fullchain.pem" || -e "$cert_dir/privkey.pem" || -L "$cert_dir/privkey.pem" ) && ! -f "$cert_dir/.fishmmo-development" ]]; then
    echo 'Existing unmanaged/production certificates found; refusing to overwrite them.' >&2
    exit 1
fi
if (( ! generate_only )); then
    groupadd -f "$reader_group"
    usermod -a -G "$reader_group" "$account"
    install -d -o root -g root -m 0755 /etc/fishmmo
fi
install -d -m 0700 "$ca_dir"
install -d -g "$reader_group" -m 0750 "$cert_dir"
exec 9>"$ca_dir/.generate.lock"
flock -x 9
# Persist one local CA so leaf rotation does not require trusting a new CA every 13 days.
if [[ ! -e "$ca_dir/ca.pem" && ! -e "$ca_dir/privkey.pem" ]]; then
    openssl req -x509 -newkey ec -pkeyopt ec_paramgen_curve:P-256 -nodes -sha256 -days 1825 \
        -subj '/CN=FishMMO Local Development CA' \
        -addext 'basicConstraints=critical,CA:TRUE,pathlen:0' \
        -addext 'keyUsage=critical,keyCertSign,cRLSign' \
        -keyout "$ca_dir/privkey.pem" -out "$ca_dir/ca.pem"
fi
openssl x509 -in "$ca_dir/ca.pem" -noout -checkend 1209600
ca_public=$(openssl x509 -in "$ca_dir/ca.pem" -pubkey -noout | openssl pkey -pubin -outform DER | openssl dgst -sha256)
key_public=$(openssl pkey -in "$ca_dir/privkey.pem" -pubout -outform DER | openssl dgst -sha256)
[[ "$ca_public" == "$key_public" ]] || { echo 'Development CA certificate and key do not match.' >&2; exit 1; }
chmod 0600 "$ca_dir/privkey.pem"
release=$(mktemp -d "$cert_dir/dev-release.XXXXXXXX")
published=0
cleanup() {
    rm -f "$cert_dir/.current.next"
    if [[ "$published" == 0 ]]; then rm -rf "$release"; fi
}
trap cleanup EXIT
openssl req -new -newkey ec -pkeyopt ec_paramgen_curve:P-256 -nodes \
    -subj '/CN=localhost' -keyout "$release/privkey.pem" -out "$release/request.csr"
cat > "$release/extensions.cnf" <<'EXTENSIONS'
basicConstraints=critical,CA:FALSE
keyUsage=critical,digitalSignature
extendedKeyUsage=serverAuth
subjectAltName=DNS:localhost,IP:127.0.0.1,IP:::1
EXTENSIONS
openssl x509 -req -in "$release/request.csr" -CA "$ca_dir/ca.pem" -CAkey "$ca_dir/privkey.pem" \
    -set_serial "0x$(openssl rand -hex 16)" -days 13 -sha256 \
    -extfile "$release/extensions.cnf" -out "$release/leaf.pem"
openssl verify -CAfile "$ca_dir/ca.pem" -purpose sslserver "$release/leaf.pem"
cat "$release/leaf.pem" "$ca_dir/ca.pem" > "$release/fullchain.pem"
openssl x509 -in "$release/leaf.pem" -noout -fingerprint -sha256 \
    | sed 's/.*=//; s/://g' > "$release/server-certificate.sha256"
install -m 0644 "$ca_dir/ca.pem" "$release/development-ca.crt"
rm "$release/request.csr" "$release/extensions.cnf"
chgrp -R "$reader_group" "$release"
chmod 0750 "$release"
chmod 0640 "$release/privkey.pem" "$release/fullchain.pem" "$release/leaf.pem"
chmod 0644 "$release/server-certificate.sha256"
# Only trust when explicitly requested by the user; validation stays enabled in clients.
if (( trust_ca )); then
    if command -v trust >/dev/null; then
        trust anchor "$ca_dir/ca.pem"
    elif command -v update-ca-certificates >/dev/null; then
        install -m 0644 "$ca_dir/ca.pem" /usr/local/share/ca-certificates/fishmmo-development-ca.crt
        update-ca-certificates
    elif command -v update-ca-trust >/dev/null; then
        install -m 0644 "$ca_dir/ca.pem" /etc/pki/ca-trust/source/anchors/fishmmo-development-ca.crt
        update-ca-trust extract
    else
        echo 'No supported system CA trust tool found. Install p11-kit or ca-certificates, then retry.' >&2
        exit 1
    fi
fi
ln -sfn "$(basename "$release")" "$cert_dir/.current.next"
mv -Tf "$cert_dir/.current.next" "$cert_dir/current"
published=1
for name in fullchain.pem privkey.pem server-certificate.sha256 development-ca.crt; do
    ln -sfn "current/$name" "$cert_dir/$name"
done
touch "$cert_dir/.fishmmo-development"
chmod 0640 "$cert_dir/.fishmmo-development"
echo "Development certificate installed: $cert_dir/fullchain.pem"
openssl x509 -in "$release/leaf.pem" -noout -dates
echo 'WebGL SHA-256 certificate fingerprint (leaf DER, not an API SPKI pin):'
cat "$release/server-certificate.sha256"
echo 'Renew by rerunning this action before expiry. Update WebGL certificate hashes after each renewal.'
if (( ! generate_only )); then
    echo "Start a new login session for '$account' to acquire the fishmmo-certs group; restart Unity and game servers."
    if (( ! trust_ca )); then echo 'CA trust was not installed. Native clients need to trust development-ca.crt to connect.'; fi
fi
