<p align="center"><a href="https://github.com/IamAngusU/bf6-crash-helper"><img src="assets/brand-header.svg" width="1100" alt="BF6 Crash Helper — Befunde prüfen, jeden Schritt selbst wählen, Änderungen zurücknehmen."></a></p>

<p align="center"><a href="README.md">English</a> · <a href="#loslegen">Loslegen</a> · <a href="docs/README.md">Dokumentation</a> · <a href="https://github.com/IamAngusU/bf6-crash-helper/releases">Downloads</a></p>

<p align="center">
  <a href="docs/privacy.md"><img src="assets/badge-local.svg" height="36" alt="Lokal · kein automatischer Upload"></a>
  <a href="docs/recovery.md"><img src="assets/badge-undo.svg" height="36" alt="Cache sichern und wiederherstellen"></a>
  <a href="LICENSE"><img src="assets/badge-mit.svg" height="36" alt="MIT-Lizenz"></a>
</p>

Battlefield schließt sich beim Rundenbeitritt. BF6 Crash Helper hilft, brauchbare Hinweise zu sammeln und einen rückgängig machbaren Spielcache-Test durchzuführen. Du entscheidest bei jedem Schritt.

**Der aktuelle Ladeabsturz in Rapid Royale wird durch dieses Tool nicht behoben.** Eine erfolgreiche Runde nach einem Cache-Reset beweist keinen Fix. [Was die Untersuchung tatsächlich ergeben hat](docs/case-evidence.md).

## Loslegen

<a href="https://github.com/IamAngusU/bf6-crash-helper/releases/latest"><img src="assets/badge-download.svg" height="42" alt="Für Windows herunterladen"></a>

1. Öffne den [neuesten Release](https://github.com/IamAngusU/bf6-crash-helper/releases/latest) und lade **BF6CrashHelper.exe** herunter.
2. Öffne die Datei per Doppelklick. Du brauchst keinen Installer, kein Konto, keinen Terminalbefehl und kein PowerShell-Skript.
3. Lies die Erklärung, setze das Häkchen für die Prüfung und klicke auf **Jetzt prüfen**. Beim Öffnen der App wird weder geprüft noch etwas verändert.
4. Schau dir die Ergebnisse an. Einen Bericht speicherst du nur auf eigenen Wunsch.

Für Windows 10/11 mit vorhandenem .NET Framework 4.8. Auf einem aktuellen unterstützten Windows ist keine zusätzliche Laufzeitinstallation nötig. Die Release-Datei ist unsigniert: Falls SmartScreen erscheint, prüfe Herkunft und Datei, bevor du entscheidest, ob du sie öffnest. Lass den Windows-Schutz aktiviert.

Normale Prüfungen brauchen keine Administratorrechte. Die optionale Windows-Dateiaufzeichnung hat eine eigene Erklärung, Zustimmung und Administratorabfrage.

## Wähle den nächsten Schritt

- **Prüfen:** Installationsdaten und vorhandene Absturzmetadaten lesen sowie ausgewählte Dateien mit verfügbaren lokalen Steam-Manifesten vergleichen. Das ist eine gezielte Prüfung, keine vollständige Steam-Dateiprüfung.
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

Die Entwickler-Bauanleitung steht im Build-Skript des Repositorys. Zum Benutzen reicht die Release-Datei.

---

Erstellt von [IamAngusU](https://github.com/IamAngusU). [MIT](LICENSE) · Unabhängiges Community-Projekt, ohne Verbindung zu EA, DICE, Battlefield Studios oder Valve. Battlefield und Steam sind Marken ihrer jeweiligen Inhaber.
