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

Der HTTP-Anfragekörper besteht aus diesen drei Zahlen. Der Agent benutzt die feste Loopback-Adresse `127.0.0.1`. Die API ergänzt die Empfangszeit. Der Verlauf zeigt bis zu zwei Minuten und umfasst maximal 120 Messpunkte. Die letzte gültige Messung bleibt bis zum Neustart abrufbar. Die Daten liegen im Arbeitsspeicher.

## Warnung im Dashboard testen

Pausiere für diesen Versuch den Agenten mit `Strg+C` in seinem Terminal, damit er den Testwert nicht gleich überschreibt. Sende in PowerShell eine fiktive Messung.

```powershell
Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:5080/api/metrics' -ContentType 'application/json' -Body '{"cpuPercent":20,"ramPercent":85,"diskUsedPercent":50}'
```

Beim nächsten Abruf zeigt das Dashboard „Arbeitsspeicher erhöht“. Wiederhole den Aufruf mit `84.9` für RAM, um die Warnung aufzuheben. Die Startwerte für die Regeln sind CPU ≥ 90 %, RAM ≥ 85 % und belegter Speicherplatz ≥ 90 %. Nach 15 Sekunden ohne neue Messung kennzeichnet die Oberfläche den letzten Stand als veraltet.

## Den Code verstehen

| Datei | Aufgabe |
| --- | --- |
| `CloudHub.AI.Monitor.slnx` | Bündelt die .NET-Projekte. Dadurch kannst du im Hauptordner mit einem Befehl bauen. |
| `src/CloudHub.Api/CloudHub.Api.csproj` | Wählt das Web-SDK und .NET 10 als Zielplattform. |
| `src/CloudHub.Agent/WindowsMetrics.cs` | Liest Windows-Zähler und berechnet die drei Prozentwerte. |
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

Speichere lokale Diagnoseexporte unter `local/`. Die Datei `.gitignore` schließt diesen Ordner sowie unter anderem `.env`, private Schlüsseldateien und Logs aus. Prüfe die Regeln mit `git check-ignore -v -- local/diagnose.json .env logs/agent.log`. Für jeden Beispielpfad sollte die passende Regel erscheinen.

Prüfe vor jedem Commit die tatsächlich vorgemerkten Inhalte mit `git diff --cached`. Auch eine versionierte C#-Datei oder README kann versehentlich einen Schlüssel enthalten. Für einen späteren OpenRouter-Schlüssel ist eine lokale Umgebungsvariable vorgesehen. Der Docker-Build erhält über `.dockerignore` ausschließlich die freigegebenen Projekt- und Quellcodedateien.

## Nächste Lernschritte

1. Die optionale Analyse ergänzen. Eine Vorschau im Dashboard zeigt die freigegebenen Prozentwerte und ausgelösten Regelkennungen. Erst der Klick auf **Mit OpenRouter analysieren** soll diese Angaben über das Backend an OpenRouter senden. Der API-Schlüssel wird als lokale Umgebungsvariable des Backends gesetzt.
2. Den Container in einem lokalen Kubernetes-Cluster bereitstellen und den Build mit GitHub Actions automatisieren.
