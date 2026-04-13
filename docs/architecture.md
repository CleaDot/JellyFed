# JellyFed — Architecture technique

## Vue d'ensemble

JellyFed est un plugin Jellyfin (C# / .NET 8) qui implémente la fédération de bibliothèques entre instances. Il s'appuie exclusivement sur les interfaces publiques de Jellyfin : pas de fork, pas de patch, compat avec les mises à jour Jellyfin.

---

## Principe fondamental : les fichiers `.strm`

Jellyfin supporte nativement les fichiers `.strm` : un fichier texte qui contient une URL. Quand le scanner trouve un `.strm`, il l'indexe comme un média normal (film, épisode) et envoie l'URL comme source de lecture au client.

```
{LibraryPath}/
  Films/
    Oppenheimer (2023)/
      Oppenheimer (2023).strm         → "https://peer-b/Videos/abc123/stream?api_key=..."
      Oppenheimer (2023).nfo          → métadonnées XML (titre, année, TMDB ID, synopsis...)
      poster.jpg                      → téléchargé depuis peer-b
      backdrop.jpg
  Series/
    Breaking Bad (2008)/
      Season 01/
        S01E01 - Pilot.strm           → URL stream épisode
        S01E02 - ...strm
      Season 02/
        ...
  .jellyfed-manifest.json             → état de toutes les syncs (clé TMDB → path + peerName)
  .jellyfed-peers.json                → statut online/offline des peers
```

**Avantages :**
- Aucune modification des clients Jellyfin (ils voient des médias normaux)
- Jellyfin gère le transcodage local si nécessaire (ou DirectPlay vers l'URL distante)
- Les métadonnées sont dans les `.nfo` localement
- Pas de proxy applicatif : le client streame directement depuis le peer distant

---

## Structure du plugin

```
Jellyfin.Plugin.JellyFed/
  Plugin.cs                        Point d'entrée, IPlugin + BasePlugin<PluginConfiguration>
  PluginServiceRegistrator.cs      Enregistrement DI (HttpClient, Services, Filters)

  Configuration/
    PluginConfiguration.cs         Paramètres (Peers[], SyncIntervalHours, LibraryPath,
                                   FederationToken, SelfUrl, BlockedPeerUrls[])
    PeerConfiguration.cs           Modèle d'un peer (Name, Url, FederationToken,
                                   Enabled, SyncMovies, SyncSeries)
    configPage.html                Page admin Jellyfin (JS vanilla, API ApiClient)

  Api/
    FederationController.cs        Endpoints /JellyFed/* (catalog, peers, register, sync)
    FederationAuthFilter.cs        ServiceFilter : vérifie Bearer token de fédération
    Dto/
      CatalogItemDto.cs            Film ou série du catalogue
      CatalogResponseDto.cs        Total + Items[]
      SeasonDto.cs / EpisodeDto.cs Structure saisons/épisodes
      SeasonsResponseDto.cs
      PeerDto.cs / PeersResponseDto.cs
      RegisterPeerRequestDto.cs
      SyncPeerRequestDto.cs

  Sync/
    FederationSyncTask.cs          IScheduledTask — sync périodique + pruning
    PeerClient.cs                  Client HTTP vers instances distantes
    StrmWriter.cs                  Génère .strm + .nfo + télécharge artwork
    PeerHeartbeatService.cs        IHostedService — ping périodique des peers
    PeerStateStore.cs              Lecture/écriture .jellyfed-peers.json
    Manifest.cs                    Modèles manifest (Manifest, ManifestEntry)
```

---

## Interfaces Jellyfin utilisées

### `IScheduledTask` — sync périodique

```csharp
public class FederationSyncTask : IScheduledTask
{
    // S'exécute toutes les N heures (configurable)
    // Pour chaque peer enabled :
    //   1. Construire les TMDB IDs locaux (pour dédup)
    //   2. GET /JellyFed/catalog → items distants
    //   3. Pour chaque item : skip si TMDB ID local, skip si dans manifest, sinon écrire .strm
    //   4. Pour les séries : GET /JellyFed/catalog/series/:id/seasons → écrire épisodes
    //   5. Pruning : supprimer les .strm dont la clé manifest n'est plus dans le catalogue
    //   6. Auto-registration : POST /JellyFed/peer/register sur le peer (SelfUrl)
    // Fin : QueueLibraryScan() → Jellyfin indexe les nouveaux .strm
}
```

### `IHostedService` — heartbeat peers

```csharp
public class PeerHeartbeatService : IHostedService
{
    // Toutes les 5 minutes : GET /JellyFed/health sur chaque peer
    // Écrit le résultat (online/offline, version, movieCount, seriesCount) dans .jellyfed-peers.json
}
```

### `ILibraryManager` — accès bibliothèque locale

Utilisé dans :
- `FederationController.GetCatalog()` → `GetItemList()` pour construire le catalogue exposé
- `FederationController.GetSeriesSeasons()` → `GetItemList()` pour saisons/épisodes
- `FederationSyncTask.BuildLocalTmdbIds()` → `GetItemList()` pour les TMDB IDs locaux (dédup)
- `FederationSyncTask.ExecuteAsync()` → `QueueLibraryScan()` après sync

---

## Flux de synchronisation

```
1. Admin configure peer-b (URL + FederationToken) dans le panneau JellyFed
2. IScheduledTask se déclenche (ou sync manuelle via POST /JellyFed/peer/sync)
3. BuildLocalTmdbIds() → liste des TMDB IDs présents localement (hors jellyfed-library)
4. GET /JellyFed/catalog sur peer-b (avec Authorization: Bearer <peer-b-token>)
5. Pour chaque item du catalogue :
   - TMDB ID dans localTmdbIds → skip (déduplication)
   - Clé dans manifest → skip (déjà synchée)
   - Sinon : StrmWriter.WriteMovieAsync() ou WriteSeriesAsync()
     - Pour une série : GET /JellyFed/catalog/series/:id/seasons → écrire un .strm par épisode
6. Pruning : clés manifest absentes du catalogue → StrmWriter.DeleteItem()
7. SaveManifest() → écrire .jellyfed-manifest.json
8. Si SelfUrl configuré : PeerClient.RegisterOnPeerAsync() → peer-b nous ajoute comme peer
9. ILibraryManager.QueueLibraryScan() → Jellyfin indexe les nouveaux .strm
```

---

## Manifest

Le manifest `.jellyfed-manifest.json` est la source de vérité de l'état des syncs.

**Clé de manifest :**
- Item avec TMDB ID → `"tmdb:{tmdbId}"` (ex: `"tmdb:872585"`)
- Item sans TMDB ID → `"no-tmdb:{peerName}:{jellyfinId}"`

**Entrée manifest :**
```json
{
  "movies": {
    "tmdb:872585": {
      "path": "/config/jellyfed-library/Films/Oppenheimer (2023)",
      "peerName": "instance-b",
      "jellyfinId": "abc123",
      "syncedAt": "2026-04-13T01:58:14Z"
    }
  },
  "series": { ... }
}
```

Le `peerName` dans le manifest permet d'identifier quels items viennent de quel peer (base de FEAT-06 et FEAT-07).

---

## Authentification inter-instances

```
[Instance A]  →  GET /JellyFed/catalog  →  [Instance B]
                 Authorization: Bearer <token-instance-b>
```

Le `FederationAuthFilter` (ServiceFilter) vérifie que le token Bearer correspond à `Plugin.Instance.Configuration.FederationToken`. Token incorrect → 401.

L'endpoint `POST /JellyFed/peer/register` est ouvert (pas de token requis) car c'est lui qui permet à un nouveau peer de s'annoncer.

---

## Auto-registration bidirectionnelle

```
1. A configure B comme peer (URL + token de B)
2. A synce depuis B → récupère le catalogue de B
3. Après sync : A POST /JellyFed/peer/register sur B
   Body: { "name": "JellyFed", "url": "http://jellyfed-test-a:8096", "federationToken": "token-a" }
4. B vérifie que l'URL n'est pas blacklistée
5. B ajoute A comme peer (Enabled: true, SyncMovies: true, SyncSeries: true)
6. Prochain cycle de sync de B → B sync depuis A
```

---

## Déduplication

La déduplication évite de créer des `.strm` pour des items déjà présents localement :

```csharp
private HashSet<string> BuildLocalTmdbIds(string federatedLibraryPath)
{
    // GetItemList(Movie + Series, IsVirtualItem=false)
    // Exclure les items dont le path commence par federatedLibraryPath (ce sont les .strm)
    // Retourner les TMDB IDs des items restants (bibliothèque locale réelle)
}
```

Si un item du catalogue distant a le même TMDB ID qu'un item local → skip. Cela évite d'avoir le même film en double (local + .strm).

---

## Configuration

```xml
<PluginConfiguration>
  <FederationToken>token-instance-a</FederationToken>
  <SyncIntervalHours>6</SyncIntervalHours>
  <LibraryPath>/config/jellyfed-library</LibraryPath>
  <SelfUrl>http://jellyfed-test-a:8096</SelfUrl>
  <Peers>
    <PeerConfiguration>
      <Name>instance-b</Name>
      <Url>http://jellyfed-test-b:8096</Url>
      <FederationToken>token-instance-b</FederationToken>
      <Enabled>true</Enabled>
      <SyncMovies>true</SyncMovies>
      <SyncSeries>true</SyncSeries>
    </PeerConfiguration>
  </Peers>
  <BlockedPeerUrls>
    <!-- URLs de peers supprimés manuellement — ne peuvent plus s'auto-enregistrer -->
  </BlockedPeerUrls>
</PluginConfiguration>
```

---

## Ce que JellyFed N'est PAS

- **Pas un proxy** : le stream va directement du client au peer (ou via l'instance locale si transcodage)
- **Pas un remplaçant de Jellyfin** : le plugin vit dans Jellyfin, les clients restent inchangés
- **Pas une sync de fichiers** : les fichiers restent chez le peer, seules les métadonnées sont copiées
- **Pas P2P au sens BitTorrent** : architecture fédérée (chaque instance = serveur autonome)
