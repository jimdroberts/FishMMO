/*
 * Browser-side SRP-6a, built to match FishMMO's server exactly.
 *
 * The conventions below were not assumed — each was extracted from
 * SrpParameters.Create2048<SHA512>() and the srp 1.0.7 library by comparing
 * concrete values (see the spike harness):
 *
 *   group      RFC 5054, 2048-bit, g = 2
 *   hash       SHA-512
 *   PAD        left-pad to 512 hex characters (256 bytes)
 *   k          H(N || PAD(g))
 *   x          H(saltBytes || H("username:password"))     salt is NOT padded
 *   v          g^x mod N
 *   u          H(PAD(A) || PAD(B))
 *   S(client)  (B - k * g^x) ^ (a + u * x) mod N
 *   K          H(S)                                        S is NOT padded
 *   M1         H(H(N) XOR H(g) || H(username) || saltBytes || PAD(A) || PAD(B) || K)
 *              — where H(g) hashes g UNPADDED, unlike the k above
 *   M2         H(PAD(A) || M1 || K)
 *
 * Every value crossing the wire is a lowercase hex string, as the .NET side
 * produces and consumes.
 *
 * BigInt and crypto.subtle are both required. Both are available in every
 * browser the panel targets; neither has an IE-era fallback and none is wanted.
 */

const N_HEX =
	'ac6bdb41324a9a9bf166de5e1389582faf72b6651987ee07fc3192943db56050a37329cbb4a099ed8193e0757767a13d' +
	'd52312ab4b03310dcd7f48a9da04fd50e8083969edb767b0cf6095179a163ab3661a05fbd5faaae82918a9962f0b93b8' +
	'55f97993ec975eeaa80d740adbf4ff747359d041d5c33ea71d281e446b14773bca97b43a23fb801676bd207a436c6481' +
	'f1d2b9078717461a5b9d32e688f87748544523b524b0d57d5ea77a2775d2ecfa032cfbdbf52fb3786160279004e57ae6' +
	'af874e7303ce53299ccc041c7bc308d82a5698f3a8d0c38271ae35f8e9dbfbb694b5c803d89f7ae435de236d525f5475' +
	'9b65e372fcd68ef20fa7111f9e4aff73';

const PAD_HEX_LENGTH = 512;

const N = BigInt('0x' + N_HEX);
const g = 2n;

/* ── Hex and byte helpers ────────────────────────────────────── */

function hexToBytes(hex) {
	const clean = hex.length % 2 ? '0' + hex : hex;
	const out = new Uint8Array(clean.length / 2);
	for (let i = 0; i < out.length; i++) out[i] = parseInt(clean.substr(i * 2, 2), 16);
	return out;
}

function bytesToHex(bytes) {
	let out = '';
	for (const b of bytes) out += b.toString(16).padStart(2, '0');
	return out;
}

function bigToHex(value) {
	const hex = value.toString(16);
	return hex.length % 2 ? '0' + hex : hex;
}

/** Left-pad to the group size, which is what the library's PAD does. */
function pad(value) {
	const hex = typeof value === 'bigint' ? value.toString(16) : value;
	return hex.padStart(PAD_HEX_LENGTH, '0');
}

function concatBytes(...parts) {
	let total = 0;
	for (const p of parts) total += p.length;
	const out = new Uint8Array(total);
	let at = 0;
	for (const p of parts) {
		out.set(p, at);
		at += p.length;
	}
	return out;
}

/* ── Hashing ─────────────────────────────────────────────────── */

async function sha512(bytes) {
	const digest = await crypto.subtle.digest('SHA-512', bytes);
	return new Uint8Array(digest);
}

/** Hash a sequence of hex strings, concatenating their raw bytes. */
async function hashHex(...hexParts) {
	return bytesToHex(await sha512(concatBytes(...hexParts.map(hexToBytes))));
}

const textEncoder = new TextEncoder();

async function hashUtf8(text) {
	return bytesToHex(await sha512(textEncoder.encode(text)));
}

/* ── Modular arithmetic ──────────────────────────────────────── */

function modPow(base, exponent, modulus) {
	let result = 1n;
	let b = ((base % modulus) + modulus) % modulus;
	let e = exponent;
	while (e > 0n) {
		if (e & 1n) result = (result * b) % modulus;
		b = (b * b) % modulus;
		e >>= 1n;
	}
	return result;
}

function mod(value, modulus) {
	return ((value % modulus) + modulus) % modulus;
}

/* ── Protocol ────────────────────────────────────────────────── */

let cachedK = null;

/** k = H(N || PAD(g)). Constant for the group, so it is computed once. */
async function multiplier() {
	if (cachedK === null) cachedK = BigInt('0x' + (await hashHex(N_HEX, pad(g))));
	return cachedK;
}

/**
 * Generates a registration salt.
 *
 * 64 bytes, rendered as 128 hex characters, matching what the server's SRP library
 * produces for SHA-512 parameters. The length matters beyond aesthetics: the login
 * challenge answers an unknown account with a derived fake salt of exactly that
 * length, so a shorter real salt would make real accounts identifiable by response
 * size alone — the very oracle the fake salt exists to close.
 */
export function generateSalt() {
	return bytesToHex(crypto.getRandomValues(new Uint8Array(64)));
}

/** x = H(saltBytes || H("username:password")). The salt is used unpadded. */
export async function derivePrivateKey(saltHex, username, password) {
	const inner = await hashUtf8(`${username}:${password}`);
	return hashHex(saltHex, inner);
}

/** v = g^x mod N, padded to the group size. */
export async function deriveVerifier(privateKeyHex) {
	return pad(modPow(g, BigInt('0x' + privateKeyHex), N));
}

export function generateEphemeral() {
	const secretBytes = crypto.getRandomValues(new Uint8Array(32));
	const secret = BigInt('0x' + bytesToHex(secretBytes));
	return { secret: bigToHex(secret), public: pad(modPow(g, secret, N)) };
}

/**
 * Derives the client session from the server's public ephemeral.
 * Throws if the server ephemeral is illegal (B mod N == 0) or the scrambler is
 * zero, which are the two checks that keep a hostile server from forcing a
 * predictable session.
 */
export async function deriveSession(clientSecretHex, serverPublicHex, saltHex, username, privateKeyHex) {
	const a = BigInt('0x' + clientSecretHex);
	const B = BigInt('0x' + serverPublicHex);
	const x = BigInt('0x' + privateKeyHex);
	const k = await multiplier();

	if (mod(B, N) === 0n) throw new Error('The server sent an illegal ephemeral value.');

	const A = modPow(g, a, N);
	const u = BigInt('0x' + (await hashHex(pad(A), pad(B))));
	if (u === 0n) throw new Error('The scrambling parameter is zero.');

	// S = (B - k * g^x) ^ (a + u * x) mod N
	const S = modPow(mod(B - k * modPow(g, x, N), N), a + u * x, N);
	const K = await hashHex(bigToHex(S));

	// M1 = H(H(N) XOR H(g) || H(username) || salt || A || B || K)
	//
	// NOTE the asymmetry, which was found by comparing against the server and is
	// not guessable: k above hashes g PADDED to the group size, but this XOR term
	// hashes g at its natural length — a single 0x02 byte. Padding g here yields a
	// proof the server rejects, with every other value still matching.
	const hN = await hashHex(N_HEX);
	const hG = await hashHex(bigToHex(g));
	const hXor = bytesToHex(hexToBytes(hN).map((byte, i) => byte ^ hexToBytes(hG)[i]));
	const hUsername = await hashUtf8(username);

	const M1 = await hashHex(hXor, hUsername, saltHex, pad(A), pad(B), K);
	// A and B are already at the group size here, so pad() is a no-op on them;
	// it is kept so the intent survives a change to how they arrive.

	return { key: K, proof: M1, publicEphemeral: pad(A) };
}

/** Verifies the server's proof: M2 = H(PAD(A) || M1 || K). */
export async function verifySession(clientPublicHex, session, serverProofHex) {
	const expected = await hashHex(pad(clientPublicHex), session.proof, session.key);
	if (expected !== serverProofHex) throw new Error('The server proof did not verify.');
	return true;
}

export const parameters = { N: N_HEX, g: bigToHex(g), padHexLength: PAD_HEX_LENGTH, hash: 'SHA-512' };
