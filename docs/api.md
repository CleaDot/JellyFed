# JellyFed — API de fédération

Tous les endpoints sont préfixés `/JellyFed/`. Ils coexistent avec l'API Jellyfin standard sur le même port.

**Authentification :** header `Authorization: Bearer <federation_token>` sur les routes protégées.  
Le token correspond à `PluginConfiguration.FederationToken` de l'instance cible.

---

## Endpoints publics (sans authentification)

### `GET /JellyFed/health`

Heartbeat. Utilisé par `PeerHeartbeatService` toutes les 5 minutes.

**Réponse 200 :**
```json
{
  "version": "0.1.0",
  "name": "JellyFed",
  "status": "ok"
}
```

---

### `POST /JellyFed/peer/register`

Enregistrement d'une instance distante comme peer.  
Appelé automatiquement après chaque sync (auto-registration bidirectionnelle).

**Body :**
```json
{
  "name": "instance-a",
  "url": "http://jellyfed-test-a:8096",
  "federationToken": "token-instance-a"
}
```

**Réponses :**
- `200 {"status": "ok", "message": "Peer registered."}` — pair enregistré (ou déjà présent)
- `200 {"status": "blocked", "message": "..."}` — URL dans la blacklist
- `400` — champs manquants (Name, Url ou FederationToken vide)
- `503` — configuration du plugin indisponible

**Comportement :**
- Si l'URL est dans `BlockedPeerUrls` → refus silencieux (status "blocked")
- Si le peer existe déjà (même URL) → no-op (pas de doublon)
- Sinon → ajout dans `config.Peers` avec `Enabled=true, SyncMovies=true, SyncSeries=true`

---

## Endpoints protégés (federation_token requis)

### `GET /JellyFed/catalog`

Retourne le catalogue de cette instance (films + séries de la bibliothèque locale).  
N'inclut **pas** les `.strm` générés par JellyFed (déduplication côté requêteur).

**Query params :**
| Param | Défaut | Description |
|-------|--------|-------------|
| `type` | (tous) | `"Movie"` ou `"Series"` |
| `since` | (tous) | ISO 8601 — items modifiés après cette date |
| `limit` | 5000 | Nombre max d'items |
| `offset` | 0 | Pagination |

**Réponse 200 :**
```json
{
  "total": 6,
  "items": [
    {
      "jellyfinId": "abc123def456",
      "tmdbId": "872585",
      "imdbId": "tt15398776",
      "type": "Movie",
      "title": "Oppenheimer",
      "originalTitle": "Oppenheimer",
      "year": 2023,
      "overview": "...",
      "genres": ["Drama", "History"],
      "runtimeMinutes": 181,
      "voteAverage": 8.1,
      "posterUrl": "http://peer-b:8096/Items/abc123/Images/Primary?api_key=TOKEN",
      "backdropUrl": "http://peer-b:8096/Items/abc123/Images/Backdrop?api_key=TOKEN",
      "streamUrl": "http://peer-b:8096/Videos/abc123/stream?api_key=TOKEN&Static=true",
      "addedAt": "2026-01-15T10:30:00Z",
      "updatedAt": "2026-01-15T10:30:00Z"
    }
  ]
}
```

Pour les séries, `streamUrl` est `null` (les URLs sont au niveau épisode).

---

### `GET /JellyFed/catalog/series/:seriesId/seasons`

Retourne les saisons et épisodes d'une série.

**Réponse 200 :**
```json
{
  "seriesId": "xyz789",
  "seasons": [
    {
      "jellyfinId": "s01id",
      "seasonNumber": 1,
      "title": "Season 1",
      "episodes": [
        {
          "jellyfinId": "ep001",
          "episodeNumber": 1,
          "title": "Pilot",
          "overview": "...",
          "airDate": "2008-01-20",
          "runtimeMinutes": 47,
          "stillUrl": "http://peer-b:8096/Items/ep001/Images/Primary?api_key=TOKEN",
          "streamUrl": "http://peer-b:8096/Videos/ep001/stream?api_key=TOKEN&Static=true"
        }
      ]
    }
  ]
}
```

**Réponses d'erreur :**
- `400` — seriesId invalide (pas un GUID)
- `401` — token manquant ou invalide
- `404` — série introuvable

---

### `GET /JellyFed/peers`

Liste les peers configurés avec leur statut online/offline.

**Réponse 200 :**
```json
{
  "peers": [
    {
      "name": "instance-b",
      "url": "http://jellyfed-test-b:8096",
      "enabled": true,
      "online": true,
      "lastSeen": "2026-04-13T01:59:00Z",
      "version": "0.1.0",
      "movieCount": 3,
      "seriesCount": 3
    }
  ]
}
```

Le statut `online`, `lastSeen`, `version`, `movieCount` et `seriesCount` sont mis à jour par `PeerHeartbeatService` toutes les 5 minutes.

---

### `POST /JellyFed/peer/sync`

Déclenche une synchronisation manuelle (queue la tâche `FederationSyncTask`).

**Body :**
```json
{ "peerName": "instance-b" }
```

`peerName` peut être `null` pour syncer tous les peers.

**Réponse 202 :**
```json
{ "status": "queued" }
```

---

## Notes d'implémentation

**`streamUrl` dans les `.strm`** : contient l'API key de fédération. Cette clé doit avoir des droits minimaux (lecture stream uniquement) dans Jellyfin. Utiliser une API key dédiée (pas la clé admin).

**Delta sync** : le champ `since` réduit la charge réseau pour les grosses bibliothèques. Non encore exploité dans `FederationSyncTask` (toujours sync complète avec déduplication manifest).

**TMDB IDs** : les URLs dans les `.strm` pointent directement vers le peer. Si le peer change d'URL ou de token, les `.strm` existants deviennent invalides et doivent être re-générés (purge du manifest + nouvelle sync).
