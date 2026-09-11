/*
 * A real QR encoder, byte mode, error-correction level M.
 *
 * The panel used to draw a decorative block pattern where a QR code belongs. That is worse
 * than showing nothing: it looks like an enrolment code, and a player who scans it enrols
 * nothing. Two-factor is mandatory on every FishMMO account, so the one moment the secret
 * is ever shown has to work.
 *
 * No dependency, because the panel has no bundler and an admin surface has to render with
 * no network beyond its own origin. Versions 1 to 10 cover an otpauth URI comfortably;
 * anything longer than version 10 at level M throws rather than silently truncating.
 */

const ECC_M = 0;

/* Per-version total codewords and, at level M, the block structure: [ecCodewordsPerBlock,
 * group1Blocks, group1DataCodewords, group2Blocks, group2DataCodewords]. From the tables
 * in ISO/IEC 18004; only level M is carried because only level M is used. */
const VERSIONS = {
	1: [26, 10, 1, 16, 0, 0],
	2: [44, 16, 1, 28, 0, 0],
	3: [70, 26, 1, 44, 0, 0],
	4: [100, 18, 2, 32, 0, 0],
	5: [134, 24, 2, 43, 0, 0],
	6: [172, 16, 4, 27, 0, 0],
	7: [196, 18, 4, 31, 0, 0],
	8: [242, 22, 2, 38, 2, 39],
	9: [292, 22, 3, 36, 2, 37],
	10: [346, 26, 4, 43, 1, 44],
};

/* Alignment-pattern centre coordinates per version. */
const ALIGNMENT = {
	1: [], 2: [6, 18], 3: [6, 22], 4: [6, 26], 5: [6, 30],
	6: [6, 34], 7: [6, 22, 38], 8: [6, 24, 42], 9: [6, 26, 46], 10: [6, 28, 50],
};

/* ── GF(256) arithmetic, the field Reed-Solomon works in ───── */

const EXP = new Uint8Array(512);
const LOG = new Uint8Array(256);
(() => {
	let x = 1;
	for (let i = 0; i < 255; i++) {
		EXP[i] = x;
		LOG[x] = i;
		x <<= 1;
		if (x & 0x100) x ^= 0x11d; // the QR generator polynomial
	}
	for (let i = 255; i < 512; i++) EXP[i] = EXP[i - 255];
})();

const mul = (a, b) => (a === 0 || b === 0 ? 0 : EXP[LOG[a] + LOG[b]]);

/** Builds the generator polynomial for `degree` error-correction codewords. */
function generatorPoly(degree) {
	let poly = [1];
	for (let i = 0; i < degree; i++) {
		const next = new Array(poly.length + 1).fill(0);
		// poly[0] is the highest-degree coefficient, so multiplying by (x + a^i) shifts each
		// term up one index and adds the a^i product at the NEXT index, not this one.
		for (let j = 0; j < poly.length; j++) {
			next[j] ^= poly[j];
			next[j + 1] ^= mul(poly[j], EXP[i]);
		}
		poly = next;
	}
	return poly;
}

/** Reed-Solomon remainder: the error-correction codewords for one block. */
function ecCodewords(data, count) {
	const gen = generatorPoly(count);
	const out = new Array(count).fill(0);
	for (const byte of data) {
		const factor = byte ^ out[0];
		out.shift();
		out.push(0);
		for (let i = 0; i < count; i++) out[i] ^= mul(gen[i + 1], factor);
	}
	return out;
}

/* ── Encoding ───────────────────────────────────────────────── */

/** Picks the smallest version that holds `byteLength` bytes at level M. */
function pickVersion(byteLength) {
	for (let v = 1; v <= 10; v++) {
		const [, ecPerBlock, g1, g1d, g2, g2d] = VERSIONS[v];
		const dataCodewords = g1 * g1d + g2 * g2d;
		// 4 mode bits, plus 8 or 16 length bits, plus the payload.
		const headerBits = 4 + (v < 10 ? 8 : 16);
		if (dataCodewords * 8 >= headerBits + byteLength * 8) return v;
		void ecPerBlock;
	}
	throw new Error('Too much data for a version 10 QR code.');
}

function buildDataCodewords(bytes, version) {
	const [, , g1, g1d, g2, g2d] = VERSIONS[version];
	const totalData = g1 * g1d + g2 * g2d;

	const bits = [];
	const push = (value, length) => {
		for (let i = length - 1; i >= 0; i--) bits.push((value >> i) & 1);
	};

	push(0b0100, 4); // byte mode
	push(bytes.length, version < 10 ? 8 : 16);
	for (const b of bytes) push(b, 8);

	// Terminator, then pad to a byte boundary, then the alternating pad bytes.
	const capacity = totalData * 8;
	for (let i = 0; i < 4 && bits.length < capacity; i++) bits.push(0);
	while (bits.length % 8 !== 0) bits.push(0);

	const codewords = [];
	for (let i = 0; i < bits.length; i += 8) {
		let byte = 0;
		for (let j = 0; j < 8; j++) byte = (byte << 1) | bits[i + j];
		codewords.push(byte);
	}
	const PAD = [0xec, 0x11];
	for (let i = 0; codewords.length < totalData; i++) codewords.push(PAD[i % 2]);
	return codewords;
}

/** Splits into blocks, computes error correction, and interleaves as the spec requires. */
function interleave(dataCodewords, version) {
	const [, ecPerBlock, g1, g1d, g2, g2d] = VERSIONS[version];

	const blocks = [];
	let at = 0;
	for (let i = 0; i < g1; i++) {
		blocks.push(dataCodewords.slice(at, at + g1d));
		at += g1d;
	}
	for (let i = 0; i < g2; i++) {
		blocks.push(dataCodewords.slice(at, at + g2d));
		at += g2d;
	}
	const ecBlocks = blocks.map((b) => ecCodewords(b, ecPerBlock));

	const out = [];
	const longest = Math.max(...blocks.map((b) => b.length));
	for (let i = 0; i < longest; i++) {
		for (const block of blocks) if (i < block.length) out.push(block[i]);
	}
	for (let i = 0; i < ecPerBlock; i++) {
		for (const block of ecBlocks) out.push(block[i]);
	}
	return out;
}

/* ── Matrix ─────────────────────────────────────────────────── */

function emptyMatrix(size) {
	return {
		size,
		// null means "not yet written", which is also how function patterns are told from data.
		cells: Array.from({ length: size }, () => new Array(size).fill(null)),
	};
}

function placeFinder(m, row, col) {
	for (let r = -1; r <= 7; r++) {
		for (let c = -1; c <= 7; c++) {
			const rr = row + r;
			const cc = col + c;
			if (rr < 0 || cc < 0 || rr >= m.size || cc >= m.size) continue;
			const inRing = (r >= 0 && r <= 6 && (c === 0 || c === 6)) || (c >= 0 && c <= 6 && (r === 0 || r === 6));
			const inCore = r >= 2 && r <= 4 && c >= 2 && c <= 4;
			m.cells[rr][cc] = inRing || inCore ? 1 : 0;
		}
	}
}

function placeFunctionPatterns(m, version) {
	const size = m.size;
	placeFinder(m, 0, 0);
	placeFinder(m, 0, size - 7);
	placeFinder(m, size - 7, 0);

	// Timing patterns.
	for (let i = 8; i < size - 8; i++) {
		const bit = i % 2 === 0 ? 1 : 0;
		m.cells[6][i] = bit;
		m.cells[i][6] = bit;
	}

	// Alignment patterns, skipping the three that would sit on a finder.
	const centres = ALIGNMENT[version];
	for (const r of centres) {
		for (const c of centres) {
			const onFinder =
				(r <= 8 && c <= 8) || (r <= 8 && c >= size - 9) || (r >= size - 9 && c <= 8);
			if (onFinder) continue;
			for (let dr = -2; dr <= 2; dr++) {
				for (let dc = -2; dc <= 2; dc++) {
					const ring = Math.max(Math.abs(dr), Math.abs(dc));
					m.cells[r + dr][c + dc] = ring === 1 ? 0 : 1;
				}
			}
		}
	}

	// The dark module, which is always set.
	m.cells[size - 8][8] = 1;

	// Version information, for version 7 and up.
	placeVersion(m.cells, version);

	// Reserve the format-information cells so data placement skips them.
	for (let i = 0; i < 9; i++) {
		if (m.cells[8][i] === null) m.cells[8][i] = 0;
		if (m.cells[i][8] === null) m.cells[i][8] = 0;
	}
	for (let i = 0; i < 8; i++) {
		if (m.cells[8][size - 1 - i] === null) m.cells[8][size - 1 - i] = 0;
		if (m.cells[size - 1 - i][8] === null) m.cells[size - 1 - i][8] = 0;
	}
}

/** Walks the zig-zag data path, skipping every cell a function pattern already took. */
function placeData(m, codewords, reserved) {
	const size = m.size;
	let bitIndex = 0;
	const nextBit = () => {
		const byte = codewords[bitIndex >> 3];
		const bit = byte === undefined ? 0 : (byte >> (7 - (bitIndex & 7))) & 1;
		bitIndex++;
		return bit;
	};

	let upward = true;
	for (let right = size - 1; right > 0; right -= 2) {
		if (right === 6) right = 5; // the vertical timing column is not a data column
		for (let step = 0; step < size; step++) {
			const row = upward ? size - 1 - step : step;
			for (const col of [right, right - 1]) {
				if (reserved[row][col]) continue;
				m.cells[row][col] = nextBit();
			}
		}
		upward = !upward;
	}
}

const MASKS = [
	(r, c) => (r + c) % 2 === 0,
	(r) => r % 2 === 0,
	(r, c) => c % 3 === 0,
	(r, c) => (r + c) % 3 === 0,
	(r, c) => (Math.floor(r / 2) + Math.floor(c / 3)) % 2 === 0,
	(r, c) => ((r * c) % 2) + ((r * c) % 3) === 0,
	(r, c) => (((r * c) % 2) + ((r * c) % 3)) % 2 === 0,
	(r, c) => (((r + c) % 2) + ((r * c) % 3)) % 2 === 0,
];

/** The four penalty rules, used to choose the mask that scans most reliably. */
function penalty(cells) {
	const size = cells.length;
	let score = 0;

	const runScore = (run) => (run >= 5 ? 3 + (run - 5) : 0);
	for (let i = 0; i < size; i++) {
		for (const read of [(j) => cells[i][j], (j) => cells[j][i]]) {
			let run = 1;
			for (let j = 1; j < size; j++) {
				if (read(j) === read(j - 1)) run++;
				else {
					score += runScore(run);
					run = 1;
				}
			}
			score += runScore(run);
		}
	}

	for (let r = 0; r < size - 1; r++) {
		for (let c = 0; c < size - 1; c++) {
			const v = cells[r][c];
			if (v === cells[r][c + 1] && v === cells[r + 1][c] && v === cells[r + 1][c + 1]) score += 3;
		}
	}

	const PATTERN = [1, 0, 1, 1, 1, 0, 1, 0, 0, 0, 0];
	const REVERSED = [0, 0, 0, 0, 1, 0, 1, 1, 1, 0, 1];
	const matches = (read, at) =>
		PATTERN.every((v, k) => read(at + k) === v) || REVERSED.every((v, k) => read(at + k) === v);
	for (let i = 0; i < size; i++) {
		for (let j = 0; j + 11 <= size; j++) {
			if (matches((k) => cells[i][k], j)) score += 40;
			if (matches((k) => cells[k][i], j)) score += 40;
		}
	}

	let dark = 0;
	for (const row of cells) for (const v of row) dark += v;
	const percent = (dark * 100) / (size * size);
	score += Math.floor(Math.abs(percent - 50) / 5) * 10;
	return score;
}

/**
 * Version information: 18 bits carried by version 7 and up, in two 3x6 blocks beside the
 * top-right and bottom-left finders. Versions 1 to 6 have none.
 *
 * These cells are function patterns. Omitting them does not merely lose the version hint —
 * data gets placed on top of them, and every symbol from version 7 up fails to scan.
 */
function versionBits(version) {
	let value = version << 12;
	for (let i = 5; i >= 0; i--) {
		if ((value >> (12 + i)) & 1) value ^= 0b1111100100101 << i;
	}
	return (version << 12) | value;
}

function placeVersion(cells, version) {
	if (version < 7) {
		return;
	}
	const size = cells.length;
	const bits = versionBits(version);
	for (let i = 0; i < 18; i++) {
		const bit = (bits >> i) & 1;
		const a = Math.floor(i / 3);
		const b = size - 11 + (i % 3);
		cells[a][b] = bit;
		cells[b][a] = bit;
	}
}

/** Format information: 5 bits of level and mask, BCH-protected and XOR-masked. */
function formatBits(maskIndex) {
	const data = (ECC_M << 3) | maskIndex;
	let value = data << 10;
	for (let i = 4; i >= 0; i--) {
		if ((value >> (10 + i)) & 1) value ^= 0b10100110111 << i;
	}
	return ((data << 10) | value) ^ 0b101010000010010;
}

function placeFormat(cells, maskIndex) {
	const size = cells.length;
	const bits = formatBits(maskIndex);
	// The most significant bit is placed first, at (8,0). Getting this backwards produces a
	// symbol that looks entirely correct and decodes as nothing at all.
	const bit = (i) => (bits >> (14 - i)) & 1;

	// Copy one, wrapping the top-left finder.
	const copyOne = [
		[8, 0], [8, 1], [8, 2], [8, 3], [8, 4], [8, 5], [8, 7], [8, 8],
		[7, 8], [5, 8], [4, 8], [3, 8], [2, 8], [1, 8], [0, 8],
	];
	copyOne.forEach(([r, c], i) => {
		cells[r][c] = bit(i);
	});

	// Copy two: bits 14..8 up the left column, then bits 7..0 along the top row. The dark
	// module at (size-8, 8) is deliberately not one of them.
	for (let i = 0; i < 7; i++) cells[size - 1 - i][8] = bit(i);
	for (let i = 0; i < 8; i++) cells[8][size - 8 + i] = bit(i + 7);

	cells[size - 8][8] = 1;
}

/**
 * Encodes `text` and returns a square matrix of 0/1, one entry per module.
 */
export function encode(text) {
	const bytes = new TextEncoder().encode(text);
	const version = pickVersion(bytes.length);
	const codewords = interleave(buildDataCodewords(bytes, version), version);

	const size = version * 4 + 17;
	const m = emptyMatrix(size);
	placeFunctionPatterns(m, version);

	// Everything written so far is a function pattern, and data must not land on it.
	const reserved = m.cells.map((row) => row.map((v) => v !== null));
	placeData(m, codewords, reserved);

	let best = null;
	for (let maskIndex = 0; maskIndex < 8; maskIndex++) {
		const cells = m.cells.map((row, r) =>
			row.map((v, c) => (reserved[r][c] ? v : v ^ (MASKS[maskIndex](r, c) ? 1 : 0))),
		);
		placeFormat(cells, maskIndex);
		placeVersion(cells, version);
		const score = penalty(cells);
		if (best === null || score < best.score) best = { score, cells };
	}
	return best.cells;
}

/**
 * Renders `text` as an inline SVG QR code.
 *
 * SVG rather than canvas so it stays sharp when printed, which is what somebody writing
 * their recovery codes onto paper is about to do.
 */
export function svg(text, { size = 168, quietZone = 4, label = 'QR code' } = {}) {
	const cells = encode(text);
	const modules = cells.length + quietZone * 2;
	let path = '';
	for (let r = 0; r < cells.length; r++) {
		for (let c = 0; c < cells.length; c++) {
			if (cells[r][c]) path += `M${c + quietZone} ${r + quietZone}h1v1h-1z`;
		}
	}
	return `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 ${modules} ${modules}" role="img" aria-label="${label}" style="border-radius:8px">
		<rect width="${modules}" height="${modules}" fill="#fff"/>
		<path d="${path}" fill="#000"/>
	</svg>`;
}
