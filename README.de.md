<p align="center"><a href="https://github.com/IamAngusU/bf6-crash-helper"><img src="assets/brand-header.svg" width="1100" alt="BF6 Crash Helper — Befunde prüfen, jeden Schritt selbst wählen, Änderungen zurücknehmen."></a></p>

<p align="center"><a href="README.md">English</a> · <a href="#loslegen">Loslegen</a> · <a href="docs/README.md">Dokumentation</a> · <a href="https://github.com/IamAngusU/bf6-crash-helper/releases">Downloads</a></p>

<p align="center">
  <a href="docs/privacy.md"><img src="assets/badge-local.svg" height="36" alt="Lokal · kein automatischer Upload"></a>
  <a href="docs/rapid-workaround.md"><img src="assets/badge-undo.svg" height="36" alt="Geprüfte Sicherungen und Rückgängig"></a>
  <a href="LICENSE"><img src="assets/badge-mit.svg" height="36" alt="MIT-Lizenz"></a>
</p>

Battlefield schließt sich beim Rapid-Royale-Beitritt. **BF6 Crash Helper** bietet einen auf den geprüften Build begrenzten, rückgängig machbaren Mirak-Tal-Workaround und lokale Diagnose. Jede Änderung braucht deine ausdrückliche Entscheidung.

**Experimentell, kein bestätigter allgemeiner Fix.** Nach dem Zwei-Dateien-Versuch gelangen auf dem untersuchten PC mehrere Beitritte. Mit dem offiziellen Update allein war derselbe Fehler noch aufgetreten. Direkter Nachweis des Basiskarten-Fallbacks und breitere Tests fehlen weiterhin. [Befunde und Grenzen](docs/case-evidence.md) · [Genaue Änderung und Rücknahme](docs/rapid-workaround.md).

## Loslegen

Ein kleines Fenster, gestaltet nach der Gaze-Lab-Oberfläche des Autors. **Englisch ist voreingestellt**, oben kannst du direkt **Deutsch** wählen. Prüfen, Zustimmung und Anwenden/Rückgängig bleiben in derselben Ansicht. Optionale Diagnose liegt hinter **Weitere Werkzeuge**.

<img src="assets/rapid-workaround-de.png" width="564" alt="Kompaktes deutsches Fenster: Kompatibilität prüfen, Erklärung und nicht vorausgewählte Zustimmung. Anwenden ist vor der Prüfung deaktiviert.">

<a href="https://github.com/IamAngusU/bf6-crash-helper/releases/tag/v0.3.0"><img src="assets/badge-download.svg" height="42" alt="Für Windows herunterladen"></a>

**Eine Zeile in PowerShell; kein Git, Python, Node oder Paketmanager nötig:**

```powershell
& { $ErrorActionPreference='Stop'; $p=Join-Path $env:TEMP ('BF6CrashHelper-'+[guid]::NewGuid().ToString('N')+'.exe'); Invoke-WebRequest -UseBasicParsing 'https://github.com/IamAngusU/bf6-crash-helper/releases/download/v0.3.0/BF6CrashHelper.exe' -OutFile $p; if((Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash -ne '308dc04afd412e512b1ad2fae7f010bad5dc40c559946e0b51a5bb0f9a2322fd'){throw 'SHA-256 mismatch; do not run this download.'}; Start-Process -FilePath $p }
```

Die Zeile lädt **v0.3.0**, prüft den fest eingetragenen SHA-256 und öffnet die App. Sie wendet **keinen** Workaround an, fordert keine Administratorrechte an und schaltet keinen Windows-Schutz ab. Prüfe vor dem Ausführen gern den [lesbaren Starter](https://github.com/IamAngusU/bf6-crash-helper/blob/v0.3.0/start.ps1), den [Änderungscode](https://github.com/IamAngusU/bf6-crash-helper/blob/v0.3.0/src/MirakRecovery.cs) und den [Administrator-Worker](https://github.com/IamAngusU/bf6-crash-helper/blob/v0.3.0/src/MirakWorker.cs). Der passende Hash bestätigt die konkrete Release-Datei, keine Unbedenklichkeit des Codes.

Alternativ: [v0.3.0 öffnen](https://github.com/IamAngusU/bf6-crash-helper/releases/tag/v0.3.0), **BF6CrashHelper.exe** herunterladen und doppelklicken.

1. BF6 regulär schließen und **Kompatibilität prüfen** wählen. Der Steam-Ordner wird automatisch gesucht; bei Bedarf kannst du ihn über **Spielordner …** auswählen.
2. Erklärung und Sicherungsort lesen. Der erste Workaround unterstützt nur **Steam-Build 25851446** mit exakt passenden Hashes für EXE, Layout, Basiskarte und die zwei SAN2-Indizes sowie eine lokale NTFS-Bibliothek.
3. Das nicht vorausgewählte Zustimmungsfeld aktivieren und **Sichern und Workaround anwenden** wählen. Erst dann öffnet sich die Administratorabfrage. Dafür dasselbe Windows-Konto verwenden.
4. Mehrere relevante Rapid-Beitritte testen. Hilft es nicht: BF6 schließen und den Helper erneut öffnen. Bei einer gespeicherten Änderung erscheint **Workaround rückgängig machen** an derselben Stelle, mit eigener, nicht vorausgewählter Zustimmung. Auch unterbrochene Vorgänge lassen sich so wiederherstellen.

Beim Öffnen gibt es keinen Spielscan, keine Änderung und keinen Upload. Die App liest ihre eigene lokale Wiederherstellungs-/Aufnahmehistorie, damit unterbrochene Vorgänge erreichbar bleiben. Steam-Dateiprüfung kann die beiden optionalen Indizes erneut herunterladen; danach erneut prüfen, keinen neuen Build erzwingen.

Für Windows 10/11 mit vorhandenem .NET Framework 4.8. Auf einem aktuellen unterstützten Windows ist keine zusätzliche Laufzeitinstallation nötig. Die Release-Datei ist unsigniert: Falls SmartScreen erscheint, prüfe Herkunft und Datei, bevor du entscheidest, ob du sie öffnest. Lass den Windows-Schutz aktiviert.

Normale Prüfungen brauchen keine Administratorrechte. Workaround und Rücknahme benötigen die Berechtigung für die zwei Steam-Dateien. Die separate Dateiaufzeichnung hat ihre eigene Zustimmung und Administratorabfrage. **Weitere Werkzeuge** öffnet die optionale Diagnose; dadurch startet weder eine Prüfung noch eine Aufzeichnung oder Änderung.

## Wähle den nächsten Schritt

- **Prüfen:** Installationsdaten und vorhandene Absturzmetadaten lesen sowie ausgewählte Dateien mit verfügbaren lokalen Steam-Manifesten vergleichen. Das ist eine gezielte Prüfung, keine vollständige Steam-Dateiprüfung.
- **Rapid-Workaround:** nur bei exakt passendem Build beide optionalen SAN2-Mirak-Indizes dauerhaft sichern und prüfen, bevor die erste Originaldatei entfernt wird. Unbekannte Builds, veränderte Dateien, verknüpfte Pfade und ein laufendes Spiel stoppen den Vorgang. Rückgängig behält Sicherungen und überschreibt keine veränderten Originale. [Verhalten bei Fehlern und Updates](docs/rapid-workaround.md).
- **Bericht speichern:** eine lokal bereinigte Zusammenfassung erstellen und vor dem Teilen prüfen. Es gibt keinen automatischen Upload.
- **Cache-Test:** Battlefield schließen und den Test ausdrücklich bestätigen. Der bisherige Cache bleibt als Sicherung erhalten. Steam darf geöffnet bleiben. Beim Rückgängigmachen bleibt auch ein inzwischen neu aufgebauter Cache erhalten.
- **Steam-Dateiprüfung starten:** ein zusätzlicher OK/Abbrechen-Dialog erklärt, dass Steam Originaldateien erneut herunterladen kann. Erst mit deiner Bestätigung startet sie; der Helper kann Steams Änderungen nicht rückgängig machen.
- **Kurze Dateiaufzeichnung:** separat eine zeitlich begrenzte Windows-Aufzeichnung aktivieren. Die private ETL kann Pfade und Prozessdaten enthalten und bleibt außerhalb des teilbaren Berichts.

Wenn Windows das Ende einer Aufzeichnung nicht bestätigt, zeigt die App **Bereinigung ausstehend** statt Erfolg. **Bereinigung wiederholen** hat eine eigene Bestätigung und Administratorabfrage und betrifft nur die benannte Aufnahme des Helpers. Beim erneuten Öffnen prüft die App ihre jüngere lokale Aufnahmehistorie, damit du eine offene Aufnahme wieder steuern kannst. Sie startet dabei keine neue Aufzeichnung. [Aufzeichnungsstatus und Wiederherstellung](docs/privacy.md#recording-status-and-recovery).

Ein Cache-Reset kann Neuberechnung und vorübergehendes Ruckeln verursachen. Er behebt keinen nachgewiesenen Enginefehler und verändert keine Kontoberechtigungen. [Sicherung und Rückgängigmachen](docs/recovery.md) · [Welche Daten geteilt werden](docs/privacy.md).

## Befunde statt Vermutungen

Ausgangspunkt war ein wiederkehrender Ladeabsturz in REDSEC Rapid Royale. Die Offlineanalyse konnte eine konkrete CAS-Dateianforderung zuordnen; die Datei entsprach Steams aktivem Manifest. Warum der passende Laufzeiteintrag fehlte, ist offen. Weder ein Entschlüsselungsfehler noch eine Ablehnung durch Steam- oder EA-Lizenzen wurde nachgewiesen.

Rapid Royale gehört zum kostenlosen REDSEC. Battlefield 6 zu kaufen ist kein vorgeschlagener Fix. [Fall und Quellen](docs/case-evidence.md) · [Tiefere Analyse, REA und Grenzen der Aufzeichnung](docs/deeper-analysis.md).

## Mithelfen

Melde nachvollziehbare Beobachtungen über [GitHub Issues](https://github.com/IamAngusU/bf6-crash-helper/issues). Hilfreich sind Spielversion, Karte/Modus, Ladephase und eine geprüfte Zusammenfassung. Rohe Dumps, Aufzeichnungen, Kontodaten und Spielprogramme bleiben privat. Absturzsignaturen gelten nur für passende Programmversionen; unbekannte Ergebnisse werden nicht als bestätigter Fehler ausgegeben.

Entwickler können `./build.ps1 -Test` unter Windows ausführen. Die Tests nutzen künstliche Dumps und temporäre Cache-/Steam-Bibliotheken, auch für Abbrüche, defekte/fehlende Backups, Dateikonflikte, Updates, Sperren, Verknüpfungen, lange Pfade und ungültige Zustimmungsanfragen. Sie ändern keine echte Spielinstallation, bestätigen keine Administratorabfrage und starten keine ETW-Aufnahme. Zum Benutzen reicht die Release-Datei. [Prüfumfang](docs/rapid-workaround.md#verification).

---

Erstellt von [IamAngusU](https://github.com/IamAngusU). [MIT](LICENSE) · Unabhängiges Community-Projekt, ohne Verbindung zu EA, DICE, Battlefield Studios oder Valve. Battlefield und Steam sind Marken ihrer jeweiligen Inhaber.
