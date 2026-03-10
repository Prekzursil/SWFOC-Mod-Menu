const gallery = document.getElementById("gallery");
const params = new URLSearchParams(window.location.search);
const stateFilter = params.get("state");

const response = await fetch("/states.json");
const states = await response.json();
const visibleStates = stateFilter ? states.filter((state) => state.id === stateFilter) : states;

if (visibleStates.length === 0) {
  gallery.innerHTML = `<section class="state-card"><div class="card-body"><h2>Unknown state</h2><p>No adapter state matched <code>${stateFilter}</code>.</p></div></section>`;
} else {
  gallery.innerHTML = visibleStates.map(renderState).join("");
}

document.title = stateFilter ? `SWFOC Desktop Adapter - ${visibleStates[0]?.title ?? "Unknown"}` : "SWFOC Desktop Adapter Gallery";

function renderState(state) {
  return `
    <article class="state-card" data-state-id="${escapeHtml(state.id)}">
      <div class="window-bar">
        <div>
          <h2 class="window-title">${escapeHtml(state.title)}</h2>
          <p class="window-status">${escapeHtml(state.subtitle)}</p>
        </div>
        <div class="meta-chip">${escapeHtml(state.windowStatus)}</div>
      </div>
      <div class="card-body">
        <div class="toolbar">
          ${state.toolbar.map((item) => `<span class="toolbar-chip">${escapeHtml(item)}</span>`).join("")}
        </div>
        <div class="panel-grid">
          ${state.panels.map(renderPanel).join("")}
        </div>
        <div class="state-footer">
          <div>
            <span class="small-label">Mapped MainWindow seam</span>
            <strong class="value">${escapeHtml(state.seam)}</strong>
          </div>
          <div>
            <span class="small-label">Deterministic source</span>
            <strong class="value">${escapeHtml(state.source)}</strong>
          </div>
        </div>
      </div>
    </article>`;
}

function renderPanel(panel) {
  const body = panel.lines
    ? `<ul>${panel.lines.map((line) => `<li>${escapeHtml(line)}</li>`).join("")}</ul>`
    : `<p>${escapeHtml(panel.copy)}</p>`;
  return `<section class="panel ${panel.variant || ""}"><h3>${escapeHtml(panel.title)}</h3>${body}</section>`;
}

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}
