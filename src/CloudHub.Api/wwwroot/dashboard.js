"use strict";

const metrics = [
  { key: "cpuPercent", name: "CPU", css: "cpu", caption: "Prozessorauslastung" },
  { key: "ramPercent", name: "Arbeitsspeicher", css: "ram", caption: "Physischer RAM belegt" },
  { key: "diskUsedPercent", name: "Systemlaufwerk", css: "disk", caption: "Speicherplatz belegt" }
];
const number = new Intl.NumberFormat("de-DE", { maximumFractionDigits: 1 });
const clock = new Intl.DateTimeFormat("de-DE", { timeStyle: "medium" });
const byId = id => document.getElementById(id);
const chart = byId("chart");
let snapshot = null;
let snapshotAt = 0;
let online = false;

byId("metrics").innerHTML = metrics.map(metric => `
  <article id="${metric.key}" class="metric ${metric.css}">
    <div class="metric-head"><h2>${metric.name}</h2><span class="metric-state">Wartet</span></div>
    <p class="value"><span class="digits">–</span><span class="unit">%</span></p>
    <div class="track" aria-hidden="true"><div class="fill" style="width:0%"></div></div>
    <span class="caption">${metric.caption}</span>
  </article>`).join("");

function serverTime() {
  return snapshot ? Date.parse(snapshot.now) + performance.now() - snapshotAt : Date.now();
}

function render() {
  const latest = snapshot?.latest;
  const warnings = snapshot?.warnings ?? [];
  const age = latest ? serverTime() - Date.parse(latest.receivedAt) : Infinity;
  const state = !online ? "offline" : !latest ? "waiting" : age > 15000 ? "stale" : "live";
  byId("agent-help").hidden = !online || state === "live";
  byId("agent-command").textContent = ".\\scripts\\Start-Agent.ps1 -Port " + (location.port || (location.protocol === "https:" ? "443" : "80"));
  const status = byId("status");
  status.dataset.state = state;
  status.textContent = { live: "Messung aktuell", stale: "Messung veraltet", waiting: "Warte auf Agent", offline: "API nicht erreichbar" }[state];
  byId("last-update").textContent = latest
    ? "Letzte Messung um " + clock.format(new Date(latest.receivedAt)) + " Uhr"
    : online ? "Starte den Windows-Agenten für die erste Messung." : "Verbindung zur lokalen API wird erneut versucht.";

  for (const metric of metrics) {
    const card = byId(metric.key);
    const value = latest?.values[metric.key];
    const present = Number.isFinite(value);
    const warn = warnings.some(warning => warning.metric === metric.key);
    card.querySelector(".digits").textContent = present ? number.format(value) : "–";
    card.querySelector(".fill").style.width = (present ? value : 0) + "%";
    const badge = card.querySelector(".metric-state");
    badge.textContent = !present ? "Kein Wert" : state !== "live" ? "Letzter Wert" : warn ? "Warnung" : "Normal";
    badge.classList.toggle("warn", warn && state === "live");
  }

  const count = byId("count");
  count.textContent = latest ? warnings.length : "–";
  count.classList.toggle("active", warnings.length > 0);
  let content;
  if (warnings.length) {
    content = warnings.map(warning => {
      const metric = metrics.find(item => item.key === warning.metric);
      if (!metric) return "";
      return `<div class="warning"><span class="symbol" aria-hidden="true">!</span><div><strong>${metric.name} erhöht</strong>
        <small>Gemessen: ${number.format(latest.values[metric.key])} % · Schwelle: ${number.format(warning.threshold)} %</small></div></div>`;
    }).join("");
  } else {
    const heading = latest ? "Keine Warnungen" : "Noch keine Auswertung";
    const description = latest ? "Alle gemeldeten Werte lagen bei der letzten Messung unter ihrer Warnschwelle." : "Die Regeln werden mit jeder neuen Messung geprüft.";
    content = `<div class="empty-alert"><strong>${heading}</strong><p>${description}</p></div>`;
  }
  if (byId("warnings").innerHTML !== content) byId("warnings").innerHTML = content;
  drawChart();
  renderProcesses(latest?.values.processes ?? []);
}

function drawChart() {
  const ctx = chart.getContext("2d");
  const width = chart.clientWidth, height = chart.clientHeight;
  if (!ctx || width < 80 || height < 80) return;
  const ratio = window.devicePixelRatio || 1;
  chart.width = Math.round(width * ratio);
  chart.height = Math.round(height * ratio);
  ctx.scale(ratio, ratio);
  const left = 36, right = width - 6, top = 10, bottom = height - 24;
  const end = serverTime(), seconds = Number(byId("window").value), start = end - seconds * 1000;
  const history = (snapshot?.history ?? []).filter(point => Date.parse(point.receivedAt) >= start);
  byId("plot-empty").hidden = history.length > 0;
  ctx.font = '11px "Segoe UI", sans-serif';
  for (const percent of [100, 75, 50, 25, 0]) {
    const y = bottom - (bottom - top) * percent / 100;
    ctx.fillStyle = "#b0c1cd";
    ctx.textAlign = "left";
    ctx.fillText(percent + "%", 0, y + 4);
    ctx.beginPath();
    ctx.strokeStyle = "#344a5a";
    ctx.moveTo(left, y);
    ctx.lineTo(right, y);
    ctx.stroke();
  }
  for (const [fraction, label] of [[0, "−" + seconds + " s"], [0.5, "−" + seconds / 2 + " s"], [1, "Jetzt"]]) {
    ctx.textAlign = fraction === 0 ? "left" : fraction === 1 ? "right" : "center";
    ctx.fillText(label, left + fraction * (right - left), height - 4);
  }
  for (const metric of metrics) {
    const legend = document.querySelector(".legend ." + metric.css);
    const color = getComputedStyle(legend).getPropertyValue("--accent").trim();
    ctx.strokeStyle = ctx.fillStyle = color;
    ctx.lineWidth = 2;
    let previous = null;
    for (const point of history) {
      const value = point.values[metric.key];
      if (!Number.isFinite(value)) { previous = null; continue; }
      const time = Date.parse(point.receivedAt);
      const x = left + (time - start) / (end - start) * (right - left);
      const y = bottom - value / 100 * (bottom - top);
      if (previous && time - previous.time <= 15000) {
        ctx.beginPath();
        ctx.moveTo(previous.x, previous.y);
        ctx.lineTo(x, y);
        ctx.stroke();
      }
      ctx.beginPath();
      ctx.arc(x, y, 2.1, 0, 2 * Math.PI);
      ctx.fill();
      previous = { time, x, y };
    }
  }
}

async function refresh() {
  try {
    const response = await fetch("/api/dashboard", { cache: "no-store", signal: AbortSignal.timeout(4500) });
    if (!response.ok) throw new Error("HTTP " + response.status);
    snapshot = await response.json();
    snapshotAt = performance.now();
    online = true;
  } catch {
    online = false;
  }
  render();
  setTimeout(refresh, 5000);
}

byId("window").addEventListener("change", drawChart);
new ResizeObserver(drawChart).observe(chart);
refresh();

function renderProcesses(processes) {
  const rows = [];
  for (const process of [...processes].sort((a, b) => b.cpuPercent - a.cpuPercent || b.ramMb - a.ramMb)) {
    const row = document.createElement("tr");
    for (const value of [process.name, process.pid, number.format(process.cpuPercent) + " %", number.format(process.ramMb) + " MB"]) {
      const cell = document.createElement("td");
      cell.textContent = value;
      row.append(cell);
    }
    rows.push(row);
  }
  if (!rows.length) {
    const row = document.createElement("tr"), cell = document.createElement("td");
    cell.colSpan = 4;
    cell.textContent = "Noch keine Prozessdaten. Starte den aktuellen Windows-Agenten.";
    row.append(cell);
    rows.push(row);
  }
  byId("process-list").replaceChildren(...rows);
}

let activeCheck = null;
let timer = null;
let remaining = 0;
let previousReport = null;
let keyConfigured = false;

async function api(path, body) {
  const response = await fetch(path, {
    method: "POST", headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body ?? {}), signal: AbortSignal.timeout(65000)
  });
  const data = await response.json();
  if (!response.ok) throw new Error(data.message ?? "Die Anfrage ist fehlgeschlagen.");
  return data;
}

function invalidateReview() {
  activeCheck = null;
  byId("review").hidden = true;
  byId("consent").checked = false;
  byId("analyze").disabled = true;
}

for (const id of ["question", "model"]) byId(id).addEventListener("input", () => {
  invalidateReview();
  byId("check-status").textContent = "Frage oder Modell geändert. Bitte einen neuen Check starten.";
});
byId("consent").addEventListener("change", () => {
  byId("analyze").disabled = !byId("consent").checked || !activeCheck || !keyConfigured;
});

async function loadKeyStatus() {
  try {
    const response = await fetch("/api/ai/status", { cache: "no-store" });
    if (!response.ok) throw new Error();
    keyConfigured = (await response.json()).configured;
    byId("key-status").textContent = keyConfigured
      ? "OpenRouter-Key im Backend eingerichtet."
      : "Key fehlt. Richte ihn lokal mit scripts/Set-OpenRouter.ps1 ein und starte die API neu. Messen und Vorschau funktionieren bereits.";
  } catch {
    byId("key-status").textContent = "Schlüsselstatus konnte nicht abgerufen werden.";
  }
}
loadKeyStatus();

byId("start-check").addEventListener("click", async () => {
  const question = byId("question").value.trim(), model = byId("model").value.trim();
  if (!question || !/^[a-zA-Z0-9_.-]+\/[a-zA-Z0-9_.:/-]+$/.test(model)) {
    byId("check-status").textContent = "Bitte beschreibe dein Problem und trage die Modell-ID ein.";
    return;
  }
  if (!online || !snapshot?.latest || serverTime() - Date.parse(snapshot.latest.receivedAt) > 15000) {
    byId("check-status").textContent = "Bitte zuerst den Windows-Agenten starten und eine aktuelle Messung abwarten.";
    return;
  }
  invalidateReview();
  byId("answer-section").hidden = true;
  byId("start-check").disabled = true;
  byId("check-inputs").disabled = true;
  try {
    const check = await api("/api/checks");
    activeCheck = check.id;
    const deadline = performance.now() + 61000;
    remaining = 61;
    byId("cancel-check").hidden = false;
    byId("check-status").textContent = "Messung läuft – noch 60 Sekunden.";
    timer = setInterval(async () => {
      remaining = Math.max(0, Math.ceil((deadline - performance.now()) / 1000));
      byId("check-status").textContent = "Messung läuft – noch " + remaining + " Sekunden.";
      if (remaining > 0) return;
      clearInterval(timer);
      timer = null;
      byId("cancel-check").hidden = true;
      byId("check-status").textContent = "Datenvorschau wird erstellt …";
      try {
        const preview = await api("/api/checks/" + check.id + "/preview", { question, model });
        byId("preview").textContent = JSON.stringify(preview.body, null, 2);
        byId("aliases").textContent = preview.aliases.map(item => item.alias + " = " + item.name + " (PID " + item.pid + ")").join("\n") || "Keine lesbaren Prozesse im Messfenster.";
        const report = JSON.parse(preview.body.messages[1].content);
        byId("comparison").textContent = compareReports(previousReport, report);
        previousReport = report;
        byId("review").hidden = false;
        byId("check-status").textContent = "Messung beendet. Prüfe die Vorschau vor dem Senden.";
        await loadKeyStatus();
      } catch (error) {
        activeCheck = null;
        byId("check-status").textContent = error.message;
      } finally {
        byId("start-check").disabled = false;
        byId("check-inputs").disabled = false;
      }
    }, 250);
  } catch (error) {
    byId("check-status").textContent = error.message;
    byId("start-check").disabled = false;
    byId("check-inputs").disabled = false;
  }
});

byId("cancel-check").addEventListener("click", () => {
  clearInterval(timer);
  timer = null;
  invalidateReview();
  byId("cancel-check").hidden = true;
  byId("start-check").disabled = false;
  byId("check-inputs").disabled = false;
  byId("check-status").textContent = "PC-Check abgebrochen.";
});

function compareReports(before, after) {
  if (!before) return "Starte nach einer manuellen Änderung einen weiteren Check, um die Durchschnittswerte zu vergleichen. Der Vergleich bleibt bis zum Neuladen lokal in dieser Seite.";
  return "Änderung zum vorherigen Check: " + [
    ["CPU", "cpu"], ["RAM", "ram"], ["Speicherplatz", "diskUsed"]
  ].map(([label, key]) => {
    if (before[key].averagePercent === null || after[key].averagePercent === null) return label + " ohne Vergleichswert";
    const delta = after[key].averagePercent - before[key].averagePercent;
    return label + " " + (delta > 0 ? "+" : "") + number.format(delta) + " Prozentpunkte";
  }).join(" · ") + ". Verwende für den Vergleich möglichst dieselbe Aufgabe und Belastung.";
}

byId("analyze").addEventListener("click", async () => {
  if (!activeCheck || !byId("consent").checked || !keyConfigured) return;
  const id = activeCheck;
  activeCheck = null;
  byId("analyze").disabled = true;
  byId("start-check").disabled = true;
  byId("check-inputs").disabled = true;
  byId("check-status").textContent = "Analyse läuft. Die geprüften Daten werden an OpenRouter gesendet …";
  try {
    const result = await api("/api/checks/" + id + "/analyze");
    byId("answer").textContent = result.answer;
    byId("answer-section").hidden = false;
    byId("check-status").textContent = "Analyse abgeschlossen. Für eine Rückfrage kannst du deine Frage anpassen und einen neuen Check starten.";
  } catch (error) {
    byId("check-status").textContent = error.message;
  } finally {
    byId("start-check").disabled = false;
    byId("check-inputs").disabled = false;
  }
});
