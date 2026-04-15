# JellyFed — API de fédération

Tous les endpoints sont préfixés `/JellyFed/`. Ils coexistent avec l'API Jellyfin standard sur le même port.

**Authentification :** header `Authorization: Bearer <token>` sur les routes protégées.
Le token est soit l'`AccessToken` per-peer (post auto-registration), soit le `FederationToken` global (bootstrap).

---

## Endpoints publics (sans authentification)

### `GET /JellyFed/health`

Heartbeat. Utilisé par `PeerHeartbeatService` toutes les 5 minutes.

**Réponse 200 :**
```json
{ "version": "0.1.0", "name": "JellyFed", "status": "ok" }
```

---

### `GET /JellyFed/stream/{itemId}?token={federationToken}`

Sert ou redirige le flux vidéo d'un item. Utilisé par les fichiers `.strm` — les players ne peuvent pas envoyer de headers d'auth, d'où le token en query param.

**Comportement :**
- Si `JellyfinApiKey` configurée → `302` vers `/Videos/{itemId}/stream?api_key={key}&Static=true`
  (`Static=true` pour le support des range requests → seeking fonctionnel)
- Sinon → `PhysicalFile` du fichier avec `enableRangeProcessing: true`

**Réponses :**
- `200` / `206` — stream ou fichier
- `302` — redirect vers Jellyfin natif (si `JellyfinApiKey` configurée)
- `401` — token manquant ou invalide
- `404` — item introuvable

---

### `GET /JellyFed/image/{itemId}/{imageType}?token={federationToken}`

Sert une image d'item (poster ou backdrop). Utilisé quand `JellyfinApiKey` n'est pas configurée.

`{imageType}` : `Primary` ou `Backdrop`

**Comportement :** lit `item.ImageInfos` et sert le fichier image via `PhysicalFile`.

**Réponses :** `200`, `400` (type invalide), `401`, `404`

---

### `POST /JellyFed/peer/register`

Enregistrement d'une instance distante. Appelé automatiquement après chaque sync (auto-registration bidirectionnelle).

**Body :**
```json
{
  "name": "instance-a",
  "url": "https://jellyfin-a.example.com",
  "federationToken": "token-instance-a"
}
```

**Réponses :**
- `200 {"status": "ok", "accessToken": "xyz..."}` — peer enregistré. Stocker `accessToken` pour tous les appels futurs vers cette instance.
- `200 {"status": "blocked"}` — URL dans la blacklist
- `400` — champs manquants
- `503` — config indisponible

---

## Endpoints protégés (Bearer requis)

### `GET /JellyFed/catalog`

Catalogue de cette instance (films + séries de la bibliothèque locale). Les `.strm` de la jellyfed-library sont exclus.

**Query params :**
| Param | Défaut | Description |
|-------|--------|-------------|
| `type` | tous | `"Movie"` ou `"Series"` |
| `since` | tous | ISO 8601 — items modifiés après cette date |
| `limit` | 5000 | Items max |
| `offset` | 0 | Pagination |

**Réponse 200 :**
```json
{
  "total": 3,
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
      "posterUrl": "https://peer-b/Items/abc123/Images/Primary?api_key=KEY",
      "backdropUrl": "https://peer-b/Items/abc123/Images/Backdrop?api_key=KEY",
      "streamUrl": "https://peer-b/JellyFed/stream/abc123?token=FED_TOKEN",
      "addedAt": "2026-01-15T10:30:00Z",
      "updatedAt": "2026-01-15T10:30:00Z",
      "container": "mkv",
      "videoCodec": "hevc",
      "width": 1920,
      "height": 1080,
      "audioCodec": "eac3",
      "mediaStreams": [
        { "type": "Audio", "codec": "eac3", "language": "eng", "title": "English (Atmos)", "isDefault": true, "isForced": false },
        { "type": "Audio", "codec": "aac", "language": "fre", "title": "Français", "isDefault": false, "isForced": false },
        { "type": "Subtitle", "codec": "subrip", "language": "eng", "title": "English", "isDefault": false, "isForced": false },
        { "type": "Subtitle", "codec": "pgs", "language": "fre", "title": "Français", "isDefault": false, "isForced": false }
      ]
    }
  ]
}
```

Pour les séries, `streamUrl`, `container`, `videoCodec`, `width`, `height`, `audioCodec`, `mediaStreams` sont `null`/vides (les URLs et codecs sont au niveau épisode).

---

### `GET /JellyFed/catalog/series/{seriesId}/seasons`

Saisons et épisodes d'une série. Codec info incluse par épisode.

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
          "stillUrl": "https://peer-b/Items/ep001/Images/Primary?api_key=KEY",
          "streamUrl": "https://peer-b/JellyFed/stream/ep001?token=FED_TOKEN",
          "container": "mkv",
          "videoCodec": "h264",
          "width": 1920,
          "height": 1080,
          "audioCodec": "aac",
          "mediaStreams": [
            { "type": "Audio", "codec": "aac", "language": "eng", "isDefault": true, "isForced": false },
            { "type": "Subtitle", "codec": "subrip", "language": "eng", "isDefault": false, "isForced": false }
          ]
        }
      ]
    }
  ]
}
```

**Erreurs :** `400` (GUID invalide), `401`, `404`

---

### `GET /JellyFed/peers`

Peers configurés avec statut online/offline.

**Réponse 200 :**
```json
{
  "peers": [
    {
      "name": "instance-b",
      "url": "https://peer-b.example.com",
      "enabled": true,
      "online": true,
      "lastSeen": "2026-04-15T20:00:00Z",
      "version": "0.1.0",
      "movieCount": 3,
      "seriesCount": 3
    }
  ]
}
```

---

### `POST /JellyFed/peer/sync`

Déclenche une sync manuelle (queue `FederationSyncTask`).

**Body :** `{ "peerName": "instance-b" }` — `null` pour syncer tous les peers.

**Réponse 202 :** `{ "status": "queued" }`

---

### `GET /JellyFed/manifest/stats`

Stats du manifest par peer (items synced).

**Réponse 200 :**
```json
{
  "peers": [
    { "name": "instance-b", "movieCount": 3, "seriesCount": 2 }
  ]
}
```

---

### `POST /JellyFed/peer/purge`

Supprime tous les `.strm` d'un peer du manifest et du filesystem.

**Body :** `{ "peerName": "instance-b" }`

**Réponse 200 :** `{ "status": "ok", "deletedMovies": 3, "deletedSeries": 2 }`

---

### `POST /JellyFed/network/reset`

Reset total : nouveau token de fédération, suppression de tous les peers et `.strm`.
Les peers ayant l'ancien token recevront `401` lors de leur prochain accès.

**Réponse 200 :** `{ "status": "ok", "newToken": "nouveau_token" }`

---

## Notes d'implémentation

**URLs d'images :** si `JellyfinApiKey` est configurée sur l'instance source, le catalogue retourne des URLs directes vers l'API Jellyfin (`/Items/{id}/Images/...?api_key=KEY`). Sinon, il retourne des URLs vers le proxy JellyFed (`/JellyFed/image/{id}/{type}?token=...`). Dans les deux cas, l'artwork est aussi téléchargé localement lors de la sync (`poster.jpg`, `fanart.jpg`).

**Delta sync :** le champ `since` permet de ne synchroniser que les nouveautés. Non encore exploité dans `FederationSyncTask` (toujours sync complète avec déduplication via manifest).

**`JellyfinApiKey` :** clé API dédiée à créer dans Jellyfin (Dashboard → API Keys). Doit avoir accès en lecture aux médias. Ne jamais utiliser la clé admin. Elle n'apparaît jamais dans les fichiers `.strm`.
