/*
 * Platform → Secrets.
 *
 * An inventory, not a vault. No secret value is readable here and there is no rotation
 * control, both deliberately — the server never sends a value, and rotation of these
 * particular keys is not something a web request can do safely. The page says so rather
 * than leaving an operator hunting for a button that was quietly left out.
 */

export async function render(host, ctx) {
	const { api, ui } = ctx;
	const data = await api.getSecrets();
	const secrets = data.secrets ?? [];
	const missing = data.missingRequired ?? 0;

	const rows = ui.table({
		columns: [
			{
				label: 'Secret',
				cell: (s) => `
					<div class="cell-primary"><code>${ui.esc(s.key)}</code></div>
					${s.protects
						? `<div class="cell-sub" style="max-width:64ch">${ui.esc(s.protects)}</div>`
						: '<div class="cell-sub">Not one of the secrets FishMMO expects. Something in this deployment put it here.</div>'}`,
			},
			{
				label: 'State',
				cell: (s) => (s.present
					? ui.statusBadge('present', 'ok')
					: ui.badge(s.required ? 'MISSING' : 'absent', s.required ? 'danger' : '')),
			},
			{
				/* The length, never the value. A truncated or empty key is a real failure with
				 * no other symptom until something tries to use it and fails somewhere else. */
				label: 'Length',
				align: 'right',
				cell: (s) => (s.present ? `<span class="tnum">${ui.num(s.valueLength)}</span>` : '<span class="faint">—</span>'),
			},
			{ label: 'Added', cell: (s) => (s.present ? ui.dateTime(s.createdUtc) : '<span class="faint">—</span>') },
			{
				label: 'Last changed',
				cell: (s) => (s.present
					? `<span title="${ui.esc(ui.dateTime(s.updatedUtc))}">${ui.ago(s.updatedUtc)}</span>`
					: '<span class="faint">—</span>'),
			},
		],
		rows: secrets,
		rowAttrs: (s) => (s.required && !s.present ? 'class="is-alert"' : ''),
		emptyText: 'No secrets are stored. A deployment cannot run without them.',
	});

	host.innerHTML = `
		<div class="page-head">
			<div class="page-head-text">
				<h1>Secrets</h1>
				<p class="page-head-sub">
					Which deployment secrets exist, how old they are, and what each one protects.
					Their values are never sent to this page.
				</p>
			</div>
			<div class="page-head-actions">
				<button class="btn" data-act="refresh">${ui.icon('refresh')} Refresh</button>
			</div>
		</div>

		<div class="stack-lg">
			${missing > 0
				? ui.banner('danger', `${missing} required secret${missing > 1 ? 's are' : ' is'} missing`,
					'A server that needs one of these will fail when it reaches for it, usually with an error that points somewhere else. Run the installer’s security key step on this deployment.')
				: ui.banner('ok', 'Every required secret is present', 'This checks that they exist and are the right shape, not that they are correct.')}

			<section class="card">
				<div class="card-body flush">${rows}</div>
			</section>

			${ui.card({
				title: 'Why there is no rotate button',
				body: `
					<p class="small muted">${ui.esc(data.rotationIsNotAvailableHere ?? '')}</p>
					<p class="small muted" style="margin-top:var(--sp-3)">
						The panel reads these to check they are there. Changing them is done where the
						machinery to do it safely lives: the installer, and for the signing key the
						login server’s atomic swap, which coordinates with a running server in a
						way an HTTP request cannot join.
					</p>`,
			})}
		</div>`;

	host.querySelector('[data-act="refresh"]').addEventListener('click', () => render(host, ctx));
}
