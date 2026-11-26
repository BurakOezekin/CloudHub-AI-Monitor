"use strict";

(() => {
  const root = document.getElementById("infrastructure");
  const status = document.getElementById("infrastructure-status");
  const sources = [["docker", "Docker-Container"], ["pods", "Kubernetes-Pods"], ["nodes", "Kubernetes-Nodes"]];
  const labels = { ok: "Erreichbar", disabled: "Nicht aktiviert", unavailable: "Nicht erreichbar / keine Berechtigung" };
  function section(key, label, source) {
    const panel = document.createElement("section");
    panel.className = "infra-source";
    const title = document.createElement("h3");
    title.textContent = label;
    const summary = document.createElement("p");
    summary.className = "help";
    summary.textContent = labels[source.status] + (source.status === "ok"
      ? " · " + source.total + " insgesamt" + (source.total > source.rows.length ? " · erste " + source.rows.length + " angezeigt" : "")
        + (source.metrics === "unavailable" ? " · Ressourcenmetriken fehlen" : "")
      : "");
    panel.append(title, summary);
    if (!source.rows.length) return panel;
    const scroll = document.createElement("div");
    scroll.className = "table-scroll";
    const table = document.createElement("table");
    const head = table.createTHead().insertRow();
    for (const label of ["Name", "Status", "Details", "CPU", "RAM"]) {
      const th = document.createElement("th");
      th.scope = "col";
      th.textContent = label;
      head.append(th);
    }
    const body = table.createTBody();
    for (const item of source.rows) {
      const tr = body.insertRow();
      for (const value of [item.name, item.state, item.detail, item.cpu, item.memory])
        tr.insertCell().textContent = value || "–";
    }
    scroll.append(table);
    panel.append(scroll);
    return panel;
  }
  async function refresh() {
    try {
      const response = await fetch("/api/infrastructure", { cache: "no-store", signal: AbortSignal.timeout(5000) });
      if (response.status === 404) {
        root.replaceChildren();
        status.textContent = "Noch keine Messung. Starte den Infrastruktur-Collector (siehe README).";
      } else {
        if (!response.ok) throw new Error();
        const data = await response.json();
        root.replaceChildren(...sources.map(([key, label]) => section(key, label, data[key])));
        status.textContent = (data.stale ? "Veraltet – Collector prüfen. Letzte Messung: " : "Letzte Messung: ")
          + new Date(data.receivedAt).toLocaleString("de-DE");
      }
    } catch {
      root.replaceChildren();
      status.textContent = "Infrastruktur-API nicht erreichbar.";
    }
    setTimeout(refresh, 10000);
  }
  refresh();
})();
