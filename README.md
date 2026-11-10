# CloudHub AI Monitor

Ein Lernprojekt für Windows-Monitoring und Cloud-Betrieb mit C# und ASP.NET Core. Ein C#-Agent sendet drei Prozentwerte an eine lokale API. Im Web-Dashboard siehst du aktuelle Messwerte, ihren Verlauf und Warnungen.

## Lokal ausprobieren

Voraussetzung ist das .NET-10-SDK. Öffne PowerShell im Projektordner.

```powershell
dotnet build
dotnet run --project .\src\CloudHub.Api -- --urls http://127.0.0.1:5080
```

Lass dieses Terminal geöffnet. Rufe den Endpunkt in einem zweiten Terminal auf.

```powershell
curl.exe -i http://127.0.0.1:5080/health
```

Erwartet werden der HTTP-Status `200 OK`, der Inhaltstyp `application/json` und der Antworttext `{"status":"ok"}`. Öffne anschließend [das Dashboard](http://127.0.0.1:5080/) im Browser. Die Startseite zeigt zunächst „Warte auf Agent“. Sobald du Daten sendest, aktualisiert sie sich alle fünf Sekunden. Mit `Strg+C` im ersten Terminal beendest du die API.

## Einen CPU-Messwert senden und wieder lesen

Rufe direkt nach dem Start `curl.exe -i http://127.0.0.1:5080/api/metrics/latest` auf. Du bekommst `404 Not Found`, weil der Speicher noch leer ist.

```powershell
$url = 'http://127.0.0.1:5080/api/metrics'
Invoke-RestMethod -Method Post -Uri $url -ContentType 'application/json' -Body '{"cpuPercent":42.5}'
Invoke-RestMethod -Uri "$url/latest"
```

Beide Aufrufe liefern denselben CPU-Wert. Mit `POST` sendest du Daten im Anfragekörper, mit `GET` liest du den aktuellen Stand. PowerShell zeigt die JSON-Antwort als Objekt an. Im Browser kannst du `http://127.0.0.1:5080/api/metrics/latest` öffnen, um das JSON direkt zu sehen.

Wiederhole den POST mit `73` und aktualisiere den Browser. Versuche anschließend `120`. Die API antwortet darauf mit `400 Bad Request` und liefert beim nächsten GET weiterhin `73`. Nach einem Neustart ist der Speicher wieder leer.

## Windows-Agent ausprobieren

Bei einer bereits laufenden API auf Port 5081 startet `.\scripts\Start-Agent.ps1 -Port 5081` im Projektordner den Agenten mit der passenden Zieladresse. Lass dieses PowerShell-Fenster geöffnet. Für den folgenden Start auf Port 5080 kannst du entsprechend `-Port 5080` verwenden.

Starte die API wie oben beschrieben und öffne ein zweites PowerShell-Terminal im Projektordner. Dieser Aufruf misst einmal und beendet den Agenten nach dem Senden.

```powershell
dotnet run --project .\src\CloudHub.Agent -- --once
Invoke-RestMethod 'http://127.0.0.1:5080/api/metrics/latest'
```

Ohne `-- --once` misst der Agent fortlaufend etwa alle fünf Sekunden. Mit `Strg+C` in seinem Terminal stoppst du ihn. Öffne parallel den Windows-Task-Manager unter **Leistung** und vergleiche CPU und Arbeitsspeicher grob. Die CPU-Anzeigen können durch unterschiedliche Messverfahren und Zeitfenster abweichen.

| JSON-Feld | Was der Agent misst | Wie du den Wert einordnest |
| --- | --- | --- |
| `cpuPercent` | Anteil der beschäftigten CPU-Zeit über eine Sekunde | Der Agent liest die kumulierten Windows-Zähler zweimal. Die Leerlaufzeit wird von der Gesamtzeit im Intervall abgezogen. |
| `ramPercent` | Aktuell belegter Anteil des physischen RAM | Windows liefert den ungefähren Prozentwert über `GlobalMemoryStatusEx`. |
| `diskUsedPercent` | Belegter Speicherplatz auf dem Windows-Systemlaufwerk | Vergleiche Gesamtkapazität und freien Platz im Explorer unter **Dieser PC**. |

Der HTTP-Anfragekörper enthält die drei Systemwerte und die lokal erfassten Top-Prozesse. Der Agent benutzt die feste Loopback-Adresse `127.0.0.1`. Die API ergänzt die Empfangszeit. Der Verlauf zeigt bis zu zwei Minuten und umfasst maximal 120 Messpunkte. Die letzte gültige Messung bleibt bis zum Neustart abrufbar. Die Daten liegen im Arbeitsspeicher.

## Warnung im Dashboard testen

Pausiere für diesen Versuch den Agenten mit `Strg+C` in seinem Terminal, damit er den Testwert nicht gleich überschreibt. Sende in PowerShell eine fiktive Messung.

```powershell
Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:5080/api/metrics' -ContentType 'application/json' -Body '{"cpuPercent":20,"ramPercent":85,"diskUsedPercent":50}'
```

Beim nächsten Abruf zeigt das Dashboard „Arbeitsspeicher erhöht“. Wiederhole den Aufruf mit `84.9` für RAM, um die Warnung aufzuheben. Die Startwerte für die Regeln sind CPU ≥ 90 %, RAM ≥ 85 % und belegter Speicherplatz ≥ 90 %. Nach 15 Sekunden ohne neue Messung kennzeichnet die Oberfläche den letzten Stand als veraltet.

## PC-Assistent mit OpenRouter

Die Prozessliste zeigt die jeweils fünf größten Verbraucher nach CPU und RAM. CPU-Prozentwerte beziehen sich auf die gesamte CPU-Kapazität. RAM wird als Working Set gemessen. Gemeinsam genutzte Speicherseiten können in mehreren Prozessen enthalten sein. Die Liste ist deshalb keine Aufteilung des gesamten RAM. Geschützte oder inzwischen beendete Prozesse können fehlen. In einer eingeschränkten Ausführungsumgebung ist nur eine Teilmenge der Prozesse sichtbar.

1. Beschreibe dein Problem im Fragefeld.
2. Wähle eines der kostenlosen Modelle. Zur Auswahl stehen Xing4.0 29B A4B und Nemotron 3.5 Super VL mit der jeweiligen `:free`-Modell-ID. Kostenpflichtige Modelle werden im Backend abgewiesen.
3. Starte den 60-Sekunden-PC-Check. Der Agent muss währenddessen laufen.
4. Prüfe die eingefrorene Vorschau. Die Anfrage enthält die Frage, Durchschnitts- und Spitzenwerte, die Anzahl von Stichproben über Warnschwellen sowie Prozess-Aliase mit Spitzenwerten. Die Zuordnung mit Namen und PIDs bleibt lokal.
5. Bestätige die Vorschau und klicke auf **Mit OpenRouter analysieren**. Nach einem Verbindungs- oder Anbieterfehler kannst du die gleiche Vorschau innerhalb ihrer Gültigkeit erneut senden. Nach Erfolg ist sie verbraucht. Gleichzeitige Anfragen mit derselben Vorschau sind gesperrt.
6. Prüfe die Antwort und setze eine passende Änderung selbst um. Starte unter vergleichbarer Last erneut einen Check. Das Dashboard vergleicht Durchschnittswerte mit dem vorigen Check auf dieser Seite. Neuladen löscht den Vergleich.

Eine Rückfrage stellst du durch Anpassen des Fragefelds und einen neuen Check. Die API sendet die neue Frage und die aktuelle Zusammenfassung. Die Antwort wird als Text dargestellt.

### Schlüssel lokal einrichten

Führe im Projektordner `.\scripts\Set-OpenRouter.ps1` aus. Das Skript fragt den Schlüssel verdeckt ab und speichert ihn in `src/CloudHub.Api/appsettings.Local.json`. Diese Datei ist ignoriert und wird beim Publish ausgeschlossen. Starte anschließend die API neu. Eine vorhandene Konfiguration überschreibt das Skript nicht.

Alternativ unterstützt das Backend die Umgebungsvariablen `OPENROUTER_API_KEY` und `OpenRouter__ApiKey`. Bei Docker wird der Schlüssel zur Laufzeit als Umgebungsvariable gesetzt, etwa mit `--env OPENROUTER_API_KEY`, wenn sie im startenden Terminal gesetzt ist. Das Image enthält die lokale Schlüsseldatei aufgrund der eingeschränkten Docker-Build-Dateiliste nicht.

Das Dashboard meldet nur, ob ein Schlüssel vorhanden ist. Die Gültigkeit wird erst beim Modellaufruf sichtbar. Deine eigene Frage wird vollständig mitgesendet. Entferne vor der Vorschau private Angaben aus deinem Text.

Standardmäßig ist **Streng: nur Anbieter ohne Datenspeicherung (Zero Data Retention)** aktiviert. Dann setzt die Anfrage `provider.data_collection = "deny"` und `provider.zdr = true`. Ein kostenloses Modell kann dadurch mit HTTP 404 abgelehnt werden. Wenn du die Option vor der Messung bewusst deaktivierst, setzt die Vorschau `data_collection = "allow"` und `zdr = false`. Die Anbieterbedingungen können dann Speicherung und Nutzung der gesendeten Frage und Diagnosedaten zu Trainingszwecken erlauben. Prüfe zusätzlich die Datenschutzvorgaben in deinem OpenRouter-Konto, die strengere Vorgaben erzwingen können.

In beiden Modi gelten Preisobergrenzen von 0 für Eingabe, Ausgabe und Anfrage sowie eine Antwortgrenze von 1200 Tokens. Kostenlose Modelle können Nutzungslimits erreichen (HTTP 429). CloudHub wechselt bei Fehlern nicht automatisch zu einem anderen Modell. [OpenRouter-Dokumentation](https://openrouter.ai/docs/guides/routing/provider-selection)

Checks und Vorschauen liegen vorübergehend im Arbeitsspeicher und verfallen nach zehn Minuten. Die Anwendung bietet manuelle Hinweise und Vergleichsmessungen.

### KI-Ablauf testen

Führe `dotnet run --project .\tests\CloudHub.Checks.Smoke` aus. Der Integrationstest startet einen lokalen Testserver mit simulierter Uhr und simuliertem OpenRouter. Er prüft Messfenster, Datenlücken, Anonymisierung, Übereinstimmung zwischen Vorschau und Anfrage, Einmalversand, fehlenden Schlüssel und Ablauf. Er benötigt keinen echten Schlüssel und sendet keine Anfrage an OpenRouter.

## Den Code verstehen

| Datei | Aufgabe |
| --- | --- |
| `CloudHub.AI.Monitor.slnx` | Bündelt die .NET-Projekte. Dadurch kannst du im Hauptordner mit einem Befehl bauen. |
| `src/CloudHub.Api/CloudHub.Api.csproj` | Wählt das Web-SDK und .NET 10 als Zielplattform. |
| `src/CloudHub.Agent/WindowsMetrics.cs` | Liest Windows-Zähler und berechnet die drei Prozentwerte. |
| `src/CloudHub.Agent/ProcessSnapshot.cs` | Erfasst erreichbare Prozesse zweimal und berechnet deren CPU-Verbrauch im Messintervall. |
| `src/CloudHub.Api/PcChecks.cs` | Erstellt die Zusammenfassung, friert die Vorschau ein und sendet sie auf Anforderung an OpenRouter. |
| `src/CloudHub.Api/MonitoringModels.cs` | Definiert die Datenformen für Messungen, Prozesse und Warnungen. |
| `src/CloudHub.Agent/Program.cs` | Steuert Messintervall und HTTP-Versand. Mit `--once` lässt sich ein Durchlauf isoliert testen. |
| `src/CloudHub.Api/Program.cs` | Liest den JSON-Anfragekörper in einen `SystemMeasurement` ein und prüft die Prozentwerte. Der POST-Handler ersetzt bei Erfolg die letzte Messung. Der GET-Handler liest sie wieder aus. |
| `src/CloudHub.Api/wwwroot/` | Enthält die HTML-Startseite, das Stylesheet und das JavaScript. Der Browser ruft `GET /api/dashboard` ab und zeichnet die Werte und die vom Backend ermittelten Warnungen. |
| `Dockerfile` | Baut die API in einem SDK-Image und kopiert das veröffentlichte Programm in ein kleineres ASP.NET-Laufzeitimage. |

Das Fragezeichen in `decimal?` lässt die API einen fehlenden Wert von einer gültigen Messung von 0 Prozent unterscheiden. Ein `lock` schützt die gemeinsame Messwertliste, wenn Agent und Dashboard gleichzeitig darauf zugreifen.

## Mit Docker ausprobieren

Voraussetzung ist eine laufende Docker Engine für Linux-Container. Führe im Projektordner beide Befehle aus.

```powershell
docker build -t cloudhub-api:local .
docker run --rm --name cloudhub-api -p 127.0.0.1:5081:8080 cloudhub-api:local
```

Prüfe in einem zweiten Terminal mit `curl.exe -i http://127.0.0.1:5081/health` dieselbe Antwort. Docker leitet Port 5081 auf deinem Laptop an Port 8080 im Container weiter. Mit `docker stop cloudhub-api` beendest du den Container.

Der Agent läuft auch bei dieser Variante direkt auf Windows. Um Messwerte an die API auf Port 5081 zu senden, setze in seinem Terminal vor dem Start `$env:CLOUDHUB_API_PORT = '5081'`. In einem neuen Terminal gilt standardmäßig wieder Port 5080.

## Git und private Daten

Die Anwendung hält echte Messwerte im Arbeitsspeicher. Speichere spätere lokale Exporte unter `local/`, `metrics/`, `measurements/` oder `exports/` und persönliche Bildschirmfotos unter `screenshots/`. Diese Ordner werden durch `.gitignore` ausgeschlossen. Auch Dateien wie `laptop.metrics.json` werden anhand ihres Namens ignoriert. Prüfe die Regeln mit `git check-ignore -v -- local/diagnose.json metrics/latest.json screenshots/dashboard.png laptop.metrics.json .env logs/agent.log`. Für jeden Beispielpfad sollte die passende Regel erscheinen.

Git prüft bei diesen Regeln Pfade und Dateinamen. Für Inhalte in einer bereits versionierten Datei gilt weiterhin die Prüfung mit `git diff --cached`. Ein Push überträgt die Commits einschließlich ihrer Historie. Bereits eingecheckte private Daten müssen deshalb gesondert aus der Historie entfernt werden.

Prüfe vor jedem Commit die tatsächlich vorgemerkten Inhalte mit `git diff --cached`. Auch eine versionierte C#-Datei oder README kann versehentlich einen Schlüssel enthalten. Für einen späteren OpenRouter-Schlüssel ist eine lokale Umgebungsvariable vorgesehen. Der Docker-Build erhält über `.dockerignore` ausschließlich die freigegebenen Projekt- und Quellcodedateien.

## Nächste Lernschritte

1. Den Container lokal mit Docker testen.
2. Den Container in einem lokalen Kubernetes-Cluster bereitstellen und den Build mit GitHub Actions automatisieren.
