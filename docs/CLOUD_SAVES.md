# Savegame-Verwaltung & Google-Drive-Cloud-Sync

Diese Erweiterung ergänzt DosBoxxer um eine Savegame-Konfiguration pro Spiel und um optionale
Cloud-Synchronisation der Spielstände über Google Drive. Ohne eingerichtete Cloud verhält sich
der Launcher exakt wie zuvor — es werden keine OAuth-Dialoge, keine Netzwerkabfragen und keine
Verzögerungen ausgelöst.

## 1. Geänderte / neue Dateien

**Core – Modelle**
- `Models/Savegame/SavegameConfig.cs` (SavegameConfig, SavegameEntry, SavegameEntryKind)
- `Models/Savegame/CatalogEntry.cs`, `TitleMatch.cs`, `LocalSavegameFile.cs`
- `Models/Cloud/SyncModels.cs` (SyncTrigger, SyncItemState, SyncPlan, SyncOutcome, …)
- `Models/Cloud/GameSyncMetadata.cs` (FileSyncRecord)
- `Models/Game.cs` (+ `SavegameConfig`), `Models/AppSettings.cs` (+ `GoogleDriveSettings`, `SavegameCatalogPath`)

**Core – Abstraktionen**
- `Abstractions/ISavegameCatalog.cs`, `ITitleMatcher.cs`, `ILocalSavegameScanner.cs`
- `Abstractions/ICloudStorage.cs`, `ICloudAuthService.cs`, `ISyncMetadataStore.cs`, `ICloudSyncService.cs`, `CloudExceptions.cs`
- `Abstractions/ISecretStore.cs` (+ Google-Keys), `IAppPaths.cs` (+ Sync-/Backup-Verzeichnis), `IPlatformService.cs` (+ `OpenUrl`)

**Core – Helfer**
- `Helpers/TitleNormalizer.cs`, `RomanNumerals.cs`, `StringSimilarity.cs`, `PhoneticEncoder.cs`

**Core – Infrastructure**
- `Infrastructure/Savegame/{SavegameCatalog,PhoneticTitleMatcher,SavegamePathParser,LocalSavegameScanner}.cs`
- `Infrastructure/Cloud/{CloudSyncService,SyncMetadataStore}.cs`
- `Infrastructure/Cloud/Google/{GoogleOAuthService,GoogleDriveStorage}.cs`
- `Infrastructure/Database/DatabaseInitializer.cs` (Migration **v2**)
- `Infrastructure/Repositories/GameRepository.cs`, `Settings/SettingsService.cs`, `AppPaths.cs`, `PlatformService.cs`, `GameLibraryService.cs`
- `Assets/dos_savegame_pfade.json` (eingebettet)

**App (UI)**
- `ViewModels/SavegameConfigEditorViewModel.cs` (geteilt von Wizard + Edit)
- `ViewModels/{ChoiceDialogViewModel,SyncConflictViewModel}.cs`
- `Views/{ChoiceDialogWindow,SyncConflictWindow}.axaml(.cs)`
- `Services/DialogConflictResolver.cs`, `Services/IDialogService.cs`+`DialogService.cs`
- `ViewModels/{MainWindowViewModel,AddGameWizardViewModel,EditGameViewModel,SettingsViewModel}.cs`
- `Views/{AddGameWizardWindow,EditGameWindow,SettingsWindow,MainWindow}.axaml`
- `CompositionRoot.cs` (DI), `Localization/Strings/Strings.*.json` (78 neue Keys je Sprache)

**Tests**
- `TitleMatchingTests.cs`, `SavegamePathTests.cs`, `SavegameCatalogTests.cs`
- `Cloud/{InMemoryCloudStorage,CloudSyncTestHarness,CloudSyncServiceTests}.cs`
- `LibraryTests.cs` (+ Savegame-Persistenz-Round-Trip)

## 2. Architekturentscheidungen

- **Strikte Schichtung** entsprechend der Vorgabe: getrennte Services für Katalog
  (`ISavegameCatalog`), Matching (`ITitleMatcher`), lokale Erkennung (`ILocalSavegameScanner`),
  Cloud-Zugriff (`ICloudStorage`), OAuth (`ICloudAuthService`), Sync-Planung/-Ausführung/-Koordination
  (`ICloudSyncService`) und Metadaten (`ISyncMetadataStore`). UI, Drive-API und Vergleichslogik sind
  nicht vermischt.
- **Ein einziger Sync-Service** für manuell *und* alle Auto-Trigger. `syncGame(gameId, trigger)`
  erzeugt zuerst einen seiteneffektfreien `SyncPlan`, bevor Dateien verändert werden.
- **Google Drive als REST-Client** (kein SDK), analog zu den vorhandenen MobyGames/IGDB/RAWG-Clients
  auf `HttpClient`-Basis → leichtgewichtig, keine schweren Transitiv-Dependencies, trivial mockbar.
- **Stabile Game-ID**: `Game.Id` (bereits vorhandene `Guid`) dient als Cloud-Identität
  → Drive-Ordner `dosboxxer/<game-id>/`. Kein Verlass auf den sichtbaren Titel.
- **Rückwärtskompatibilität**: additive DB-Migration v2; Spiele ohne Savegame-Zeile laden als
  „nicht konfiguriert“ und lösen keinen Auto-Sync aus.

## 3. Neue Dependencies

**Keine.** Bewusst nur BCL: `System.Net.Http`/`System.Text.Json` (Drive-REST + OAuth),
`System.Net.HttpListener` (Loopback-Redirect), `System.Security.Cryptography` (PKCE, MD5).
Phonetik/Fuzzy-Matching ist selbst implementiert (Metaphone, Jaro-Winkler, Levenshtein,
Token-Jaccard). Der Savegame-Katalog ist als `EmbeddedResource` in `DosBoxxer.Core.dll` gebündelt
und damit bei jedem Release automatisch dabei.

## 4. Google-OAuth-Client

Die OAuth-Anmeldedaten vom Typ **„Desktop app“** (Client-ID und Client-Secret) sind fest im
Programm hinterlegt (`GoogleDriveSettings.HardcodedClientId` / `HardcodedClientSecret`) — es ist
keine Konfiguration in den Einstellungen nötig. In DosBoxxer: *Einstellungen → Google Drive* →
**Verbinden**. Die Autorisierung öffnet sich im Browser (Loopback-Redirect + PKCE); es wird
**nur** der Scope `https://www.googleapis.com/auth/drive.file` angefordert (Zugriff ausschließlich
auf von der App erstellte Dateien).

## 5. Wo OAuth-Daten/Tokens liegen

- *Client-ID* und *Client-Secret*: hartcodiert im Programm (`GoogleDriveSettings`), weder in
  `settings.json` noch im Secret-Store.
- Verbundene E-Mail: `settings.json` (nicht sensibel).
- *Refresh-Token*: verschlüsselter Secret-Store (`ISecretStore`, AES-GCM,
  Key `googledrive.refreshtoken`), niemals im Klartext-JSON, nie geloggt.
- *Access-Token*: nur im Speicher, wird bei Ablauf automatisch über das Refresh-Token erneuert.
  Bei `invalid_grant` (widerrufene Freigabe) wird der lokale Token-Zustand gelöscht → UI zeigt
  „nicht verbunden“.
- Sync-Metadaten (inkl. Drive-Ordner-ID): `<DataRoot>/sync/<game-id>.json`.
- Backups vor Cloud→Lokal-Überschreibung: `<DataRoot>/savegame-backups/<game-id>/<zeitstempel>/`.

## 6. Wie der Sync entscheidet, welche Version gewinnt

Für jede Datei (lokale ∪ Cloud) wird ein Plan-Item gebildet:
- nur lokal → **UPLOAD**, nur Cloud → **DOWNLOAD** (fehlende Dateien führen nie zum Löschen der
  anderen Seite — Löschungen konservativ).
- beide identisch (Größe + MD5) → **UNCHANGED**.
- beide unterschiedlich:
  - Mit Sync-Metadaten (letzter gemeinsamer Stand): geänderte Seite gewinnt; **beide** geändert →
    **CONFLICT**.
  - Ohne Metadaten: nur bei **klarem** Zeitunterschied (> 2 s Toleranz, UTC) „neuere gewinnt“;
    bei nahezu gleicher Zeit + abweichendem Inhalt → **CONFLICT**.

Der Trigger beeinflusst die Richtung **nicht** — Pre-Launch kann hochladen, Post-Exit herunterladen,
falls die Metadaten das sagen.

## 7. Konfliktfälle

Ein echter Konflikt (beide Seiten geändert bzw. nicht sicher auflösbar) wird nie stillschweigend
überschrieben. Der Dialog zeigt Dateiname, relativen Pfad, lokale/Cloud-Änderungszeit und Größe.
Der Benutzer wählt pro Datei **Lokale verwenden / Cloud verwenden / Überspringen**; „für alle
anwenden“ ist möglich. Abbrechen verändert keine Datei. Vor dem Spielstart wird ein Konflikt vor
dem Start aufgelöst; schlägt der Pre-Launch-Sync fehl oder wird abgebrochen, bietet ein Dialog
**Erneut versuchen / Trotzdem starten / Abbrechen**.

## 8. Integration der Trigger

Alle Trigger rufen denselben `ICloudSyncService.SyncGameAsync(gameId, trigger, resolver, progress)`.
- **GAME_ADDED**: `MainWindowViewModel.AddGameAsync` nach erfolgreichem Speichern.
- **PRE_LAUNCH**: `MainWindowViewModel.PlayAsync` **vor** `IDosBoxLauncher.LaunchAsync`. DOSBox
  startet erst nach abgeschlossenem/übersprungenem Sync bzw. Benutzerentscheidung.
- **POST_EXIT**: nach vollständigem Prozessende (der Launcher `await`et `WaitForExitAsync`), sodass
  Savegame-Dateien geschlossen sind, bevor hochgeladen wird.
Jeder Auto-Trigger prüft zuerst `IsCloudUsable` (= `cloudConfigured && cloudAuthenticated`) und die
Savegame-Konfiguration; sonst wird still übersprungen.

## 9. Verhinderung paralleler Syncs

Pro Spiel ein `SemaphoreSlim(1,1)` in `CloudSyncService`. Überlappende Trigger (mehrfaches „Start“,
gleichzeitiger manueller + automatischer Sync) serialisieren; nie laufen zwei Sync-Vorgänge für
dasselbe Spiel gleichzeitig (Test 30/31 prüft `MaxConcurrentOps == 1`).

## 10. Datensicherheit

- Download zuerst in Temp-Datei, dann atomarer `File.Move`. Lokale Datei wird erst ersetzt, wenn
  der Ersatz vollständig vorliegt; vorher Backup.
- Upload-/Download-Fehler pro Datei werden isoliert (andere Dateien bleiben konsistent); das lokale
  Savegame wird nie beschädigt oder auf einen älteren Stand zurückgesetzt. Fehlgeschlagener
  Post-Game-Upload → lokale Kopie bleibt unverändert, Benutzerhinweis, späterer Sync möglich.

## 11. Tests (Auswahl)

335 Tests grün. Neu u. a.: Titel-Matching (exakt, Groß-/Kleinschreibung, Satzzeichen, Tippfehler,
Phonetik, Zahlen, verschiedene Kandidaten, unähnliche Titel), Savegame-Pfade (Einzeldatei, mehrere,
Verzeichnis, Unterverzeichnisse, fehlend, ungültig/Traversal), sowie die Sync-Szenarien §19 (1–18,
21–22, 26–31) gegen den In-Memory-`ICloudStorage`-Fake, inkl. Konfliktentscheidungen, Upload-/
Download-/Netzwerk-/Auth-Fehler und Locking.

## Bekannte Einschränkungen

- Kontostand-E-Mail wird nicht angezeigt (Scope bewusst auf `drive.file` beschränkt, kein
  `openid/email`).
- Der bundled Katalog deckt DOS-Spiele aus dem Ludusavi-Manifest ab; unbekannte Titel werden manuell
  konfiguriert.
- Kein automatischer Hintergrund-Sync per Timer — Sync erfolgt an den drei Workflow-Punkten und
  manuell.
