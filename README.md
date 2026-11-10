# CloudHub AI Monitor

Ein Lernprojekt für Windows-Monitoring und Cloud-Betrieb mit C# und ASP.NET Core. Im ersten Schritt beantwortet die API den Aufruf `GET /health`. Ein erfolgreicher Aufruf zeigt, dass der Webserver läuft, Anfragen annimmt und JSON zurückgibt.

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

Erwartet werden der HTTP-Status `200 OK`, der Inhaltstyp `application/json` und der Antworttext `{"status":"ok"}`. Mit `Strg+C` im ersten Terminal beendest du die API. Wiederhole den Aufruf und beobachte, wie sich die Antwort verändert.

## Den Code verstehen

| Datei | Aufgabe |
| --- | --- |
| `CloudHub.AI.Monitor.slnx` | Bündelt die .NET-Projekte. Dadurch kannst du im Hauptordner mit einem Befehl bauen. |
| `src/CloudHub.Api/CloudHub.Api.csproj` | Wählt das Web-SDK und .NET 10 als Zielplattform. |
| `src/CloudHub.Api/Program.cs` | Erstellt die Anwendung. `MapGet` verknüpft den Pfad mit einer C#-Funktion. `Results.Ok` liefert den Status 200 und das Ergebnis als JSON. `Run` startet den Server. |
| `Dockerfile` | Baut die API in einem SDK-Image und kopiert das veröffentlichte Programm in ein kleineres ASP.NET-Laufzeitimage. |

Zum Nachvollziehen ändere lokal den Wert `"ok"` in `"bereit"`, starte die API neu und wiederhole den HTTP-Aufruf. So kannst du verfolgen, wie eine einzelne C#-Zeile die Antwort im Terminal bestimmt.

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

1. Einen synthetischen Messwert per `POST /api/metrics` senden und den letzten Wert per `GET /api/metrics/latest` wieder lesen. Damit lässt sich der Datenfluss unabhängig vom Laptop testen.
2. Einen separaten C#-Agenten unter `src/CloudHub.Agent/` auf Windows ausführen. Er soll CPU-Auslastung, RAM-Belegung und belegten Speicherplatz des Systemlaufwerks messen. Die API kann dabei weiterhin im Linux-Container laufen.
3. Die letzten Werte im Browser anzeigen und mit festen Schwellenwerten Warnungen auslösen. Mit synthetischen Grenzwerten lassen sich die Regeln gezielt testen.
4. Eine optionale Analyse per Button ergänzen. Die API soll dafür ausschließlich freigegebene numerische Messwerte und Regelkennungen an OpenRouter senden.
5. Den getesteten Container in einem lokalen Kubernetes-Cluster bereitstellen und den Build mit GitHub Actions automatisieren.
