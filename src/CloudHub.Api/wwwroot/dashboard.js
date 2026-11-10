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
