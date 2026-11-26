# CloudHub AI Monitor

Windows-Monitoring mit C#, ASP.NET Core und einem lokalen Web-Dashboard. Ein Agent erfasst die Auslastung des Rechners. Das Backend nimmt die Messwerte über eine REST-API entgegen, prüft Warnschwellen und zeigt den Verlauf im Browser.

Der optionale PC-Assistent fasst eine Minute Messdaten zusammen. Vor einer Analyse mit OpenRouter lässt sich die vollständige Anfrage prüfen.

![CloudHub-Dashboard mit fiktiven Messwerten und Demo-Prozessen](docs/assets/dashboard-demo.png)

*Die Abbildung verwendet ausschließlich fiktive Messwerte, Prozessnamen und Prozess-IDs.*

## Funktionen

- CPU-Auslastung, RAM-Belegung und belegter Speicherplatz des Windows-Systemlaufwerks.
- Prozessübersicht mit den jeweils fünf größten Verbrauchern nach CPU und RAM.
- Dashboard mit automatischer Aktualisierung, Live-Verlauf und Warnungen.
- Gespeicherte Messhistorie in SQLite mit Ansichten für 1 Stunde, 24 Stunden und 7 Tage.
- 60-Sekunden-PC-Check mit Fragefeld, Datenvorschau und optionaler KI-Analyse.
- Vergleich der Durchschnittswerte zweier Checks innerhalb einer Browsersitzung.

## Architektur

```mermaid
flowchart LR
    W[Windows-System] --> A[C# Agent]
    A -->|HTTP POST /api/metrics| API[ASP.NET Core API]
    API --> M[(Arbeitsspeicher)]
    API --> D[(SQLite-Historie)]
    B[Web-Dashboard] -->|HTTP GET /api/dashboard| API
    API -->|Messwerte und Warnungen| B
    B -->|Geprüfte Analyse anfordern| API
    API -->|Optional| O[OpenRouter]
```

Agent und Backend laufen als getrennte Prozesse. Das Dashboard wird von derselben API ausgeliefert und benötigt keinen eigenen JavaScript-Build. Das Backend kann für Docker gebaut werden, während der Agent weiterhin auf Windows misst.

## Lokal starten

Voraussetzungen sind Windows für den Agenten, das [.NET-10-SDK](https://dotnet.microsoft.com/download/dotnet/10.0) und PowerShell. Die Befehle werden im Projektordner ausgeführt.

Im ersten Terminal die API starten.

```powershell
dotnet build
dotnet run --project .\src\CloudHub.Api -- --urls http://127.0.0.1:5081
```

Im zweiten Terminal den Agenten starten und das Fenster geöffnet lassen.

```powershell
.\scripts\Start-Agent.ps1 -Port 5081
```

Das Dashboard ist unter [http://127.0.0.1:5081](http://127.0.0.1:5081) erreichbar. Nach der ersten Messung erscheinen die Werte. Mit `Strg+C` lässt sich der jeweilige Prozess beenden.

## API ausprobieren

| Methode | Pfad | Aufgabe |
| --- | --- | --- |
| GET | `/health` | Erreichbarkeit prüfen |
| POST | `/api/metrics` | Messung validieren und übernehmen |
| GET | `/api/metrics/latest` | Letzte gültige Messung abrufen |
| GET | `/api/dashboard` | Verlauf und Warnungen abrufen |
| GET | `/api/history?range=1h` | Gespeicherten Verlauf abrufen (`1h`, `24h`, `7d`) |
| POST | `/api/checks` | PC-Check beginnen |
| POST | `/api/checks/{id}/preview` | Diagnosevorschau erstellen |
| POST | `/api/checks/{id}/analyze` | Geprüfte Vorschau an OpenRouter senden |

Der Datenfluss lässt sich mit fiktiven Werten testen. Pausiere dafür den Agenten, damit er den Testwert nicht sofort ersetzt.

```powershell
Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:5081/api/metrics' -ContentType 'application/json' -Body '{"cpuPercent":25,"ramPercent":85,"diskUsedPercent":42}'
Invoke-RestMethod 'http://127.0.0.1:5081/api/metrics/latest'
```

Bei RAM ≥ 85 % erscheint eine Warnung. Für CPU und belegten Speicherplatz liegt die Schwelle jeweils bei 90 %. Ungültige Prozentwerte führen zu `400 Bad Request`. Vor der ersten Messung liefert `/api/metrics/latest` den Status `404`.

## Optionaler PC-Assistent

Das Monitoring funktioniert ohne OpenRouter-Key. Für die Analyse lässt sich ein Schlüssel lokal einrichten.

```powershell
.\scripts\Set-OpenRouter.ps1
```

Das Skript fragt den Schlüssel verdeckt ab und speichert ihn in `src/CloudHub.Api/appsettings.Local.json`. Anschließend die API neu starten. Die Datei ist durch Git ausgeschlossen und wird beim Publish ausgelassen. Alternativ unterstützt das Backend `OPENROUTER_API_KEY` oder `OpenRouter__ApiKey` als Umgebungsvariable.

Im Dashboard eine Frage eingeben, ein kostenloses Modell auswählen und den 60-Sekunden-Check starten. Der Agent muss währenddessen laufen. Danach zeigt die Vorschau genau die Anfrage, die durch den Analyse-Button versendet wird.

Prozessnamen und PIDs werden für diese Anfrage durch Aliase ersetzt. Die Zuordnung bleibt lokal sichtbar. **Der selbst eingegebene Fragetext wird vollständig übertragen.** Persönliche Angaben sollten vor der Vorschau entfernt werden.

Vorausgewählt ist `recursive/flint-v1.0:free`. Dieses Modell lieferte im Funktionstest mit fiktiven Daten eine vollständige Antwort bei gemeldeten Kosten von 0. Der Test erforderte deaktiviertes Zero Data Retention. Der ZDR-Haken ist zunächst aktiviert und kann vor der Messung bewusst entfernt werden.

`data_collection: deny` bleibt auch ohne ZDR aktiv. Die Anbieterbedingungen und Kontoeinstellungen gelten weiterhin. Die Anwendung erlaubt ausschließlich die aufgeführten kostenlosen Modelle und fordert Preisobergrenzen von 0 an. Nutzungslimits, regionale Einschränkungen oder Datenschutzvorgaben können einen Aufruf verhindern. [OpenRouter-Anbieterwahl](https://openrouter.ai/docs/guides/routing/provider-selection)

Flint benötigt Budget für interne Verarbeitung und sichtbare Antwort. Die Anfrage erlaubt insgesamt 8192 Tokens und bis zu 120 Sekunden Bearbeitungszeit. Nach einem Anbieterfehler kann dieselbe Vorschau erneut gesendet werden. Nach Erfolg ist sie verbraucht. Änderungen an Frage, Modell oder Datenschutz erfordern einen neuen Check.

## Daten und Sicherheit

- CPU-, RAM- und Speicherplatzwerte werden zusätzlich in einer lokalen SQLite-Datei gespeichert. Prozessnamen, PIDs, Fragen und Analysevorschauen werden nicht gespeichert und bleiben im Arbeitsspeicher.
- Die Datenbank liegt standardmäßig unter `%LOCALAPPDATA%\CloudHub\cloudhub.db` und damit außerhalb des Projektordners. Der Pfad lässt sich mit `Storage__DatabasePath` ändern.
- Gespeicherte Messwerte werden nach sieben Tagen gelöscht. Die Frist lässt sich mit `Storage__RetentionDays` (1 bis 365) ändern. Zum vollständigen Löschen die API beenden und die Datenbankdatei entfernen.
- Für längere Zeiträume liefert die API Durchschnittswerte mit höchstens 240 Punkten. Ist die Datei nicht beschreibbar, läuft die API ohne Speicherung weiter und schreibt eine Warnung ins Log.
- Der Live-Verlauf umfasst bis zu zwei Minuten und maximal 120 Messpunkte. Nach einem Neustart wird er aus der Datenbank gefüllt. Checks verfallen nach zehn Minuten.
- Lokale Schlüsseldateien, Logs und Exportordner werden von Git ausgeschlossen. Der Docker-Build erhält ausschließlich die freigegebenen Quelldateien.
- Persönliche Exporte gehören nach `local/`, `metrics/`, `measurements/` oder `exports/`. Persönliche Bildschirmfotos gehören nach `screenshots/`.
- Die API enthält keine Benutzeranmeldung. Die dokumentierten Startbefehle binden sie an die Loopback-Adresse. Vor einer Bereitstellung über das Internet sind Authentifizierung und weitere Zugriffskontrollen erforderlich.

Vor einem Commit lassen sich die vorgemerkten Inhalte und Ausschlussregeln prüfen.

```powershell
git diff --cached
git check-ignore -v -- src/CloudHub.Api/appsettings.Local.json metrics/latest.json screenshots/dashboard.png
```

Git-Ausschlussregeln prüfen Dateipfade. Bereits versionierte Dateien und sensible Inhalte in Quellcode benötigen eine gesonderte Prüfung.

## Tests

```powershell
dotnet build --configuration Release
dotnet run --project .\tests\CloudHub.Checks.Smoke --configuration Release
```

Der Integrationstest verwendet eine simulierte Uhr und einen simulierten OpenRouter-Handler. Er prüft Messfenster, Datenlücken, Anonymisierung, Datenvorschau, kostenlose Preisgrenzen, Datenschutzwahl, Wiederholung nach Fehlern, Tokenlimit und abgelaufene Checks. Für die Messhistorie prüft er Speichern, erneutes Öffnen, Durchschnittsbildung, Aufbewahrungsfrist und dass keine Prozessdaten in der Datei landen. Ein echter Schlüssel oder eine externe Anfrage wird dafür nicht benötigt.

## Docker

Das Dockerfile baut die API in einem SDK-Image und übernimmt die veröffentlichten Dateien in ein ASP.NET-Laufzeitimage. Der Container läuft mit dem vorgesehenen Nicht-Root-Benutzer.

Zuerst die lokal gestartete API auf Port 5081 beenden. Anschließend mit laufender Docker Engine für Linux-Container ausführen.

```powershell
docker build -t cloudhub-api:local .
docker run --rm --name cloudhub-api -p 127.0.0.1:5081:8080 cloudhub-api:local
```

Ohne Volume liegt die Messhistorie im Container und geht beim Entfernen verloren. Der Container speichert sie unter `/data/cloudhub.db`. Für eine dauerhafte Historie ein Volume einbinden, zum Beispiel mit `-v cloudhub-data:/data` im `docker run`-Befehl.

Der Windows-Agent kann weiterhin über `.\scripts\Start-Agent.ps1 -Port 5081` senden. Für optionale KI-Aufrufe kann der Container den Schlüssel zur Laufzeit als Umgebungsvariable erhalten. Ein bereits im Terminal gesetztes `OPENROUTER_API_KEY` wird mit `--env OPENROUTER_API_KEY` weitergegeben.

**Stand der Prüfung** — .NET-Build, Windows-Messung, lokale API, Dashboard und KI-Ablauf wurden getestet. Der Docker-Start wurde in der bisherigen Entwicklungsumgebung mangels Docker Engine noch nicht ausgeführt.

## Grenzen und nächste Schritte

CPU-Werte beruhen auf einem Messfenster und können von der Task-Manager-Anzeige abweichen. Prozess-RAM bezeichnet das Working Set. Gemeinsam genutzte Speicherseiten können in mehreren Prozessen enthalten sein. Geschützte oder beendete Prozesse können in der Übersicht fehlen.

Die Anwendung ist ein lokaler MVP. Die gespeicherte Historie enthält nur Systemwerte, keine Prozessübersicht. Eine KI-Antwort liefert mögliche Erklärungen; Anpassungen am Rechner werden manuell durchgeführt.

Als nächste Schritte sind ein vollständiger Docker-Funktionstest, GitHub Actions und eine lokale Kubernetes-Bereitstellung vorgesehen.


## Docker- und Kubernetes-Monitoring

Optionaler, separater Collector (Python 3.9+, keine Pakete). Er verwendet ausschließlich
lesende Docker-/kubectl-Kommandos auf dem Host und sendet die letzte Übersicht an die lokale
CloudHub-API. Keine Docker-Socket-Mounts, Kubeconfig oder CLI-Installation im API-Container.
Keine Logs, Umgebungsvariablen, Secrets oder KI-Übertragung. Die bestehenden PC-Messungen bleiben unverändert.

1. Docker Desktop oder eine vertrauenswürdige Docker-CLI-Verbindung einrichten; für Kubernetes
   kubectl und einen bereits funktionierenden Kontext mit Leserechten für Pods und Nodes.
2. Verfügbare Kontexte anzeigen: `docker context ls` und `kubectl config get-contexts`.
3. API wie oben auf 127.0.0.1 starten. In einem zweiten Terminal (Beispiele: die tatsächlichen Kontextnamen einsetzen):

```powershell
python scripts/infrastructure_monitor.py --docker-context desktop-linux --kube-context docker-desktop --namespace default --port 5081
```

Nur Docker: `python scripts/infrastructure_monitor.py --docker-context desktop-linux`.
Nur Kubernetes: `python scripts/infrastructure_monitor.py --kube-context docker-desktop --namespace default`.
`--once` sendet eine einzelne Übersicht. `--interval` (10–30 Sekunden) ist die Pause nach jeder Sammlung.
Der Windows-Agent kann unabhängig weiterlaufen. Das Dashboard zeigt Container (auch gestoppte),
Pods eines gewählten Namespace (Ready/Restarts/Fehlerzustände) und Nodes sowie CPU/RAM, falls
Docker Stats bzw. Kubernetes Metrics API verfügbar sind. Docker zeigt CPU in Prozent und
Speicherverbrauch/Limit; Kubernetes zeigt CPU (z. B. m = Millicores) und Speicher (z. B. Mi).
Kubernetes-Nutzungsmetriken benötigen eine funktionierende Metrics API (z. B. metrics-server)
und passende Leseberechtigungen. Fehlende Werte bleiben leer, fehlende Rechte oder CLI werden
als nicht erreichbar angezeigt. Die Datenquellen werden getrennt ausgewertet; pro Quelle werden
maximal 25 Einträge alphabetisch angezeigt, mit Gesamtzahl. Ein einzelner Collector überschreibt
die letzte Übersicht, keine Historie und kein Multi-Cluster-Aggregat. Nach 90 Sekunden ohne
neue Übersicht erscheint ein Veraltet-Hinweis.

Wenn die API in Docker läuft, Port ausschließlich lokal veröffentlichen (z. B.
`-p 127.0.0.1:5081:8080`) und den Collector weiterhin auf dem Host starten.
Die lokale API ist kein authentifizierter öffentlicher Dienst; nicht für LAN/Internet freigeben.
Docker-Zugriff ist privilegiert: nur vertrauenswürdige Kontexte und Nutzer verwenden, für
Kubernetes einen möglichst eingeschränkten Lesezugang. Der Collector ändert keine Ressourcen.
Live-Tests mit Docker Desktop und einem echten Cluster sind zusätzlich zu den CI-Tests nötig.

Tests: `python -m unittest discover -s tests/infrastructure -v` und
`dotnet run --project tests/CloudHub.Checks.Smoke`.
