# CloudHub AI Monitor

Ein Lernprojekt für Windows-Monitoring und Cloud-Betrieb mit C# und ASP.NET Core. Die API beantwortet `GET /health` und speichert den zuletzt gesendeten CPU-Prozentwert im Arbeitsspeicher. Zum Ausprobieren übernimmt PowerShell die Rolle des späteren Windows-Agenten.

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

Erwartet werden der HTTP-Status `200 OK`, der Inhaltstyp `application/json` und der Antworttext `{"status":"ok"}`. Lass die API für den nächsten Versuch laufen. Mit `Strg+C` im ersten Terminal beendest du sie.

## Einen CPU-Messwert senden und wieder lesen

Rufe direkt nach dem Start `curl.exe -i http://127.0.0.1:5080/api/metrics/latest` auf. Du bekommst `404 Not Found`, weil der Speicher noch leer ist.

```powershell
$url = 'http://127.0.0.1:5080/api/metrics'
Invoke-RestMethod -Method Post -Uri $url -ContentType 'application/json' -Body '{"cpuPercent":42.5}'
Invoke-RestMethod -Uri "$url/latest"
```

Beide Aufrufe liefern denselben CPU-Wert. Mit `POST` sendest du Daten im Anfragekörper, mit `GET` liest du den aktuellen Stand. PowerShell zeigt die JSON-Antwort als Objekt an. Im Browser kannst du `http://127.0.0.1:5080/api/metrics/latest` öffnen, um das JSON direkt zu sehen.

Wiederhole den POST mit `73` und aktualisiere den Browser. Versuche anschließend `120`. Die API antwortet darauf mit `400 Bad Request` und liefert beim nächsten GET weiterhin `73`. Nach einem Neustart ist der Speicher wieder leer.

## Den Code verstehen

| Datei | Aufgabe |
| --- | --- |
| `CloudHub.AI.Monitor.slnx` | Bündelt die .NET-Projekte. Dadurch kannst du im Hauptordner mit einem Befehl bauen. |
| `src/CloudHub.Api/CloudHub.Api.csproj` | Wählt das Web-SDK und .NET 10 als Zielplattform. |
| `src/CloudHub.Api/Program.cs` | Verknüpft HTTP-Anfragen über `MapPost` und `MapGet` mit C#-Funktionen. ASP.NET Core liest den JSON-Anfragekörper in einen `CpuMeasurement` ein. Der POST-Handler prüft den Wert und ersetzt bei Erfolg die letzte Messung. Der GET-Handler liest sie wieder aus. |
| `Dockerfile` | Baut die API in einem SDK-Image und kopiert das veröffentlichte Programm in ein kleineres ASP.NET-Laufzeitimage. |

Das Fragezeichen in `decimal?` lässt die API einen fehlenden Wert von einer gültigen Messung von 0 Prozent unterscheiden. `Volatile.Write` und `Volatile.Read` machen die aktuelle Referenz auch für Anfragen auf anderen Threads sichtbar.

## Mit Docker ausprobieren

Voraussetzung ist eine laufende Docker Engine für Linux-Container. Führe im Projektordner beide Befehle aus.

```powershell
docker build -t cloudhub-api:local .
docker run --rm --name cloudhub-api -p 127.0.0.1:5081:8080 cloudhub-api:local
```

Prüfe in einem zweiten Terminal mit `curl.exe -i http://127.0.0.1:5081/health` dieselbe Antwort. Docker leitet Port 5081 auf deinem Laptop an Port 8080 im Container weiter. Mit `docker stop cloudhub-api` beendest du den Container.

## Git und private Daten

Speichere lokale Diagnoseexporte unter `local/`. Die Datei `.gitignore` schließt diesen Ordner sowie unter anderem `.env`, private Schlüsseldateien und Logs aus. Prüfe die Regeln mit `git check-ignore -v -- local/diagnose.json .env logs/agent.log`. Für jeden Beispielpfad sollte die passende Regel erscheinen.

Prüfe vor jedem Commit die tatsächlich vorgemerkten Inhalte mit `git diff --cached`. Auch eine versionierte C#-Datei oder README kann versehentlich einen Schlüssel enthalten. Für einen späteren OpenRouter-Schlüssel ist eine lokale Umgebungsvariable vorgesehen. Der Docker-Build erhält über `.dockerignore` ausschließlich die freigegebenen Projekt- und Quellcodedateien.

## Nächste Lernschritte

1. Einen separaten C#-Agenten unter `src/CloudHub.Agent/` auf Windows ausführen. Er soll CPU-Auslastung, RAM-Belegung und belegten Speicherplatz des Systemlaufwerks messen. Die API kann dabei weiterhin im Linux-Container laufen.
2. Die letzten Werte im Browser anzeigen und mit festen Schwellenwerten Warnungen auslösen. Mit synthetischen Grenzwerten lassen sich die Regeln gezielt testen.
3. Eine optionale Analyse per Button ergänzen. Die API soll dafür ausschließlich freigegebene numerische Messwerte und Regelkennungen an OpenRouter senden.
4. Den getesteten Container in einem lokalen Kubernetes-Cluster bereitstellen und den Build mit GitHub Actions automatisieren.
