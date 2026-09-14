/*
 * Administration → Beta codes. Programs, minting, the code list and revocation.
 *
 * WHAT A CODE IS. A twelve-character code (XXXX-XXXX-XXXX) under a program key such as
 * closed_beta. While the shard's registration gate is on, registering needs a code of one of the
 * configured programs; an account that redeemed one keeps the link forever. Expiry ends
 * redemption, not access. Revoking ends both — for every account that redeemed the code.
 *
 * WHAT IS DELIBERATELY NOT HERE. No delete: a revoked code and its links are the record of who
 * was let in. No second look at a minted batch as a batch: the codes are shown once, in the mint
 * result, with copy and download, and the audit row records the program, count, uses and expiry
 * but never the codes — the log is read by more people than this page, and a live code in it is a
 * code anyone reading the log can redeem. They remain listed below, one per row, for the operators
 * who may see this page.
 *
 * Program names and staff notes are typed by operators and codes come from the database, so
 * every one goes through `ui.esc` — including inside attributes.
 */

export async function render(host, ctx) {
	const { api, ui } = ctx;
	const filters = { program: '', includeRevoked: true, page: 1, pageSize: 50 };
	let programs = await api.getBetaPrograms();
	let codes = null;

	host.innerHTML = `
		<div class="page-head">
			<div class="page-head-text">
				<h1>Beta codes</h1>
				<p class="page-head-sub">
					Mint and revoke the codes that admit accounts to a beta program. Minting and revoking
					need a fresh authenticator code, ask for a reason, and are recorded in the audit log.
				</p>
			</div>
		</div>

		<div class="stack-lg">
			<div id="gate"></div>

			<section class="card">
				<header class="card-head"><div class="card-head-text">
					<div class="card-title">Programs</div>
					<div class="card-sub">Every program that has codes. "Active" codes could be redeemed right now.</div>
				</div></header>
				<div class="card-body flush" id="programs"></div>
			</section>

			<section class="card">
				<header class="card-head"><div class="card-head-text">
					<div class="card-title">Mint codes</div>
					<div class="card-sub">The new codes are shown once, below, with copy and download. Keep them somewhere only the people handing them out can reach.</div>
				</div></header>
				<div class="card-body">
					<form id="mint-form" class="stack">
						<div class="field-row">
							<div class="field">
								<label for="mint-program">Program</label>
								<input id="mint-program" name="program" list="mint-programs" required autocomplete="off" spellcheck="false" placeholder="closed_beta" />
								<datalist id="mint-programs"></datalist>
								<div class="field-hint">1 to 32 lowercase letters, digits, hyphens and underscores.</div>
							</div>
							<div class="field">
								<label for="mint-count">How many codes</label>
								<input id="mint-count" name="count" type="number" min="1" max="500" value="10" required />
							</div>
							<div class="field">
								<label for="mint-uses">Accounts per code</label>
								<input id="mint-uses" name="maxUses" type="number" min="1" max="100000" value="1" required />
							</div>
						</div>
						<div class="field-row">
							<div class="field">
								<label for="mint-expires">Redeemable until <span class="faint">(optional, your local time)</span></label>
								<input id="mint-expires" name="expires" type="datetime-local" />
								<div class="field-hint">Expiry ends redemption only. Accounts that redeemed in time keep access until the code is revoked.</div>
							</div>
							<div class="field">
								<label for="mint-note">Note <span class="faint">(optional)</span></label>
								<input id="mint-note" name="note" maxlength="256" placeholder="Who this batch is for" />
							</div>
						</div>
						<div class="field">
							<label for="mint-reason">Reason (recorded in the audit log)</label>
							<textarea id="mint-reason" name="reason" required placeholder="Why are these being minted?"></textarea>
						</div>
						<div id="mint-error"></div>
						<div><button class="btn btn-primary" type="submit">Mint codes</button></div>
					</form>
				</div>
			</section>

			<div id="minted"></div>

			<section class="card">
				<header class="card-head"><div class="card-head-text">
					<div class="card-title">Codes</div>
					<div class="card-sub">Newest first.</div>
				</div></header>
				<div class="toolbar">
					<select id="codes-program"><option value="">Every program</option></select>
					<label class="check-row" style="margin:0">
						<input type="checkbox" id="codes-revoked" checked />
						<span>Include revoked</span>
					</label>
					<span class="grow"></span>
					<span class="small muted" id="codes-count"></span>
				</div>
				<div class="card-body flush" id="codes">${ui.loading()}</div>
				<footer class="card-foot" id="codes-pager"></footer>
			</section>
		</div>`;

	const gateHost = host.querySelector('#gate');
	const programsHost = host.querySelector('#programs');
	const mintedHost = host.querySelector('#minted');
	const codesHost = host.querySelector('#codes');
	const pagerHost = host.querySelector('#codes-pager');

	function paintPrograms() {
		const gate = programs.gate ?? { enabled: false, programs: [] };
		gateHost.innerHTML = gate.enabled
			? ui.banner('info', 'The registration gate is ON',
				(gate.programs ?? []).length
					? `Registering needs a code of: ${(gate.programs ?? []).join(', ')}. Signing in to the panel is never gated, so existing accounts can redeem a code from My account.`
					: 'Registering needs a code of any program. Signing in to the panel is never gated, so existing accounts can redeem a code from My account.')
			: ui.banner('warn', 'The registration gate is OFF',
				'Anybody can register without a code; a code given at registration is still redeemed. The gate is Beta:Enabled in the panel settings.');

		const list = programs.programs ?? [];
		programsHost.innerHTML = list.length
			? ui.table({
				columns: [
					{ label: 'Program', cell: (p) => `<span class="cell-primary">${ui.esc(p.program)}</span>` },
					{ label: 'Codes', align: 'right', cell: (p) => `<span class="tnum">${ui.num(p.codeCount)}</span>` },
					{ label: 'Active', align: 'right', cell: (p) => `<span class="tnum">${ui.num(p.activeCodeCount)}</span>` },
					{ label: 'Accounts admitted', align: 'right', cell: (p) => `<span class="tnum">${ui.num(p.redemptionCount)}</span>` },
				],
				rows: list,
			})
			: ui.empty('No programs yet', 'A program exists once a code has been minted under it.', 'shield');

		host.querySelector('#mint-programs').innerHTML = list.map((p) => `<option value="${ui.esc(p.program)}"></option>`).join('');
		const select = host.querySelector('#codes-program');
		select.innerHTML = `<option value="">Every program</option>${list.map((p) =>
			`<option value="${ui.esc(p.program)}"${filters.program === p.program ? ' selected' : ''}>${ui.esc(p.program)}</option>`).join('')}`;
	}

	function codeState(c) {
		if (c.isRevoked) return ui.badge('revoked', 'danger');
		if (c.isExpired) return ui.badge('expired', 'warn');
		if (c.remainingUses <= 0) return ui.badge('used up');
		return ui.badge('active', 'ok');
	}

	async function loadCodes(spinner = true) {
		if (spinner) codesHost.innerHTML = ui.loading();
		codes = await api.getBetaCodes(filters);
		host.querySelector('#codes-count').textContent = `${ui.num(codes.totalCount)} code${codes.totalCount === 1 ? '' : 's'}`;
		codesHost.innerHTML = (codes.items ?? []).length
			? ui.table({
				columns: [
					{ label: 'Code', cell: (c) => `<code>${ui.esc(c.code)}</code>` },
					{ label: 'Program', cell: (c) => ui.esc(c.program) },
					{ label: 'State', cell: codeState },
					{ label: 'Uses', align: 'right', cell: (c) => `<span class="tnum">${ui.num(c.useCount)} / ${ui.num(c.maxUses)}</span>` },
					{ label: 'Expires', cell: (c) => c.expiresUtc ? `<span class="nowrap" title="${ui.esc(ui.dateTime(c.expiresUtc))}">${ui.esc(ui.ago(c.expiresUtc))}</span>` : '<span class="faint">never</span>' },
					{
						label: 'Minted',
						cell: (c) => `<div class="nowrap">${ui.esc(ui.ago(c.createdUtc))}</div><div class="cell-sub">by ${ui.esc(c.createdBy)}</div>`,
					},
					{ label: 'Note', cell: (c) => c.note ? `<span class="small" style="overflow-wrap:anywhere">${ui.esc(c.note)}</span>` : '<span class="faint">—</span>' },
					{
						label: '',
						align: 'right',
						cell: (c) => c.isRevoked
							? `<span class="cell-sub">by ${ui.esc(c.revokedBy ?? '')}</span>`
							: `<button class="btn btn-sm btn-danger" type="button" data-revoke="${ui.esc(c.id)}">Revoke</button>`,
					},
				],
				rows: codes.items,
			})
			: ui.empty('No codes', filters.program ? 'Nothing matches that filter.' : 'No beta code has been minted yet.', 'shield');

		pagerHost.innerHTML = ui.pager(codes.page, codes.pageSize, codes.totalCount, 'codes-page');
		pagerHost.hidden = !pagerHost.innerHTML.trim();
		pagerHost.querySelectorAll('[data-action="codes-page"]').forEach((b) =>
			b.addEventListener('click', () => {
				filters.page = Number(b.dataset.page);
				loadCodes().catch(showLoadFailure);
			}));

		codesHost.querySelectorAll('[data-revoke]').forEach((b) =>
			b.addEventListener('click', () => revoke(b.dataset.revoke)));
	}

	function showLoadFailure(err) {
		codesHost.innerHTML = ui.banner('danger', 'The codes could not be loaded', err?.message || '');
	}

	async function revoke(id) {
		const row = (codes?.items ?? []).find((c) => String(c.id) === String(id));
		const values = await ui.modal({
			title: 'Revoke this code',
			sub: 'It can no longer be redeemed, and EVERY account that redeemed it loses the access it granted. This cannot be undone; mint a new code to re-admit somebody.',
			confirmLabel: 'Revoke',
			confirmTone: 'danger',
			body: `
				${row ? `<p class="small">Code <code>${ui.esc(row.code)}</code> of <strong>${ui.esc(row.program)}</strong>, redeemed by ${ui.num(row.useCount)} account(s).</p>` : ''}
				<div class="field">
					<label for="revoke-reason">Reason (recorded in the audit log)</label>
					<textarea id="revoke-reason" name="reason" required></textarea>
				</div>`,
			onSubmit: (v) => (String(v.reason ?? '').trim() ? v : false),
		});
		if (!values) return;
		const done = await ctx.attempt(() => api.revokeBetaCode(id, values.reason), 'Code revoked');
		if (!done) return;
		programs = await api.getBetaPrograms().catch(() => programs);
		paintPrograms();
		await loadCodes().catch(showLoadFailure);
	}

	/* The one time a batch is shown. Copy puts every code on the clipboard, one per line; the
	 * download is the same text as a file. Neither is stored anywhere by this page. */
	function showMinted(result) {
		const minted = result.codes ?? [];
		const text = [
			`FishMMO beta codes — ${result.program}`,
			`Minted: ${new Date().toISOString()}`,
			'',
			...minted.map((c) => c.code),
			'',
		].join('\n');

		mintedHost.innerHTML = ui.card({
			title: `New codes for ${result.program}`,
			sub: 'Shown once. Copy or download them now; this card is gone when you leave the page.',
			actions: `
				<button class="btn btn-sm" type="button" data-act="copy-minted">${ui.icon('copy')} Copy all</button>
				<button class="btn btn-sm" type="button" data-act="download-minted">${ui.icon('download')} Download</button>
				<button class="btn btn-sm btn-ghost" type="button" data-act="dismiss-minted">Dismiss</button>`,
			body: `
				${ui.banner('ok', result.message ?? 'Codes minted.')}
				<ul id="minted-codes" style="list-style:none;padding:0;margin:var(--sp-3) 0 0;display:grid;grid-template-columns:repeat(auto-fill,minmax(170px,1fr));gap:var(--sp-2)">
					${minted.map((c) => `
						<li>
							<code class="center" style="display:block">${ui.esc(c.code)}</code>
							<div class="cell-sub center">${ui.esc(c.program)}${c.note ? ` · ${ui.esc(c.note)}` : ''}</div>
						</li>`).join('')}
				</ul>`,
		});
		mintedHost.querySelector('[data-act="copy-minted"]').addEventListener('click', () => ui.copyToClipboard(minted.map((c) => c.code).join('\n')));
		mintedHost.querySelector('[data-act="download-minted"]').addEventListener('click', () =>
			ui.downloadText(`fishmmo-beta-${result.program}-${new Date().toISOString().slice(0, 10)}.txt`, text));
		mintedHost.querySelector('[data-act="dismiss-minted"]').addEventListener('click', () => { mintedHost.innerHTML = ''; });
		mintedHost.scrollIntoView?.({ block: 'nearest' });
	}

	host.querySelector('#mint-form').addEventListener('submit', async (e) => {
		e.preventDefault();
		const formEl = e.target;
		const errorHost = host.querySelector('#mint-error');
		errorHost.innerHTML = '';
		const v = Object.fromEntries(new FormData(formEl).entries());
		if (!String(v.reason ?? '').trim()) {
			errorHost.innerHTML = ui.banner('warn', 'A reason is required.');
			return;
		}
		const body = {
			program: String(v.program ?? '').trim(),
			count: Number(v.count),
			maxUses: Number(v.maxUses),
			// A datetime-local value is the operator's wall clock; the ISO string is that instant in UTC.
			expiresUtc: v.expires ? new Date(v.expires).toISOString() : null,
			note: String(v.note ?? '').trim() || null,
			reason: v.reason,
		};

		const button = formEl.querySelector('button[type="submit"]');
		button.disabled = true;
		try {
			const result = await ctx.withStepUp(() => api.mintBetaCodes(body));
			if (!result) return;
			formEl.querySelector('#mint-reason').value = '';
			showMinted(result);
			programs = await api.getBetaPrograms().catch(() => programs);
			paintPrograms();
			await loadCodes(false).catch(showLoadFailure);
		} catch (err) {
			errorHost.innerHTML = ui.banner('danger', err.message || 'Those codes could not be minted.');
		} finally {
			button.disabled = false;
		}
	});

	host.querySelector('#codes-program').addEventListener('change', (e) => {
		filters.program = e.target.value;
		filters.page = 1;
		loadCodes().catch(showLoadFailure);
	});
	host.querySelector('#codes-revoked').addEventListener('change', (e) => {
		filters.includeRevoked = e.target.checked;
		filters.page = 1;
		loadCodes().catch(showLoadFailure);
	});

	paintPrograms();
	await loadCodes();
}
