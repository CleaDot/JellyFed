# JellyFed — Fichiers `.strm` dans Jellyfin

## Qu'est-ce qu'un fichier `.strm` ?

Un `.strm` est un fichier texte contenant une seule URL. Jellyfin le supporte nativement (héritage Kodi/XBMC). Quand le scanner le trouve, il crée un item de bibliothèque normal — le `.strm` est transparent pour les clients.

```
# Oppenheimer (2023).strm
https://peer-b.example.com/JellyFed/stream/abc123def456?token=fed_token_xyz
```

Le token de fédération dans l'URL permet au serveur source d'authentifier la requête sans que la clé API Jellyfin n'apparaisse dans le fichier.

---

## Structure de dossier générée par JellyFed

### Films

```
{LibraryPath}/
  Films/
    Oppenheimer (2023)/
      Oppenheimer (2023).strm
      Oppenheimer (2023).nfo
      sources.json
      poster.jpg
      fanart.jpg
```

### Séries

```
{LibraryPath}/
  Series/
    Breaking Bad (2008)/
      tvshow.nfo
      sources.json
      poster.jpg
      fanart.jpg
      Season 01/
        S01E01 - Pilot.strm
        S01E01 - Pilot.nfo
        S01E02 - Cat's in the Bag.strm
        S01E02 - Cat's in the Bag.nfo
      Season 02/
        ...
```

---

## Fichiers `.nfo` — métadonnées locales

Jellyfin lit les `.nfo` au format Kodi. Les métadonnées sont stockées localement pour éviter de re-requêter le peer à chaque accès.

**Film NFO (`Oppenheimer (2023).nfo`) :**
```xml
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<movie>
  <title>Oppenheimer</title>
  <originaltitle>Oppenheimer</originaltitle>
  <year>2023</year>
  <plot>The story of J. Robert Oppenheimer...</plot>
  <runtime>181</runtime>
  <rating>8.1</rating>
  <genre>Drama</genre>
  <genre>History</genre>
  <uniqueid type="tmdb" default="true">872585</uniqueid>
  <uniqueid type="imdb">tt15398776</uniqueid>
  <jellyfed_peer>instance-b</jellyfed_peer>
  <jellyfed_id>abc123def456</jellyfed_id>
  <fileinfo>
    <streamdetails>
      <video>
        <codec>hevc</codec>
        <width>1920</width>
        <height>1080</height>
      </video>
      <audio>
        <codec>eac3</codec>
        <language>eng</language>
        <title>English (Atmos)</title>
      </audio>
      <audio>
        <codec>aac</codec>
        <language>fre</language>
        <title>Français</title>
      </audio>
      <subtitle>
        <language>eng</language>
        <title>English</title>
      </subtitle>
      <subtitle>
        <language>fre</language>
        <title>Français</title>
      </subtitle>
    </streamdetails>
  </fileinfo>
</movie>
```

La section `<fileinfo><streamdetails>` est critique : sans elle, Jellyfin ne connaît pas le codec et suppose que le navigateur peut lire le format directement → fatal player error pour les fichiers MKV/HEVC.

Les tags `jellyfed_peer` et `jellyfed_id` sont des extensions custom ignorées par Jellyfin, utilisées par JellyFed pour le suivi manifest.

Depuis la slice v1 provenance/multi-source, JellyFed ajoute aussi :

- `<studio>JellyFed:peer-a</studio>` pour chaque source connue ;
- `<tag>JellyFed:primary:peer-a</tag>` pour la source actuellement matérialisée ;
- `<tag>JellyFed:source:peer-b</tag>` pour toutes les sources ;
- `<tag>JellyFed:multi-source</tag>` si plusieurs peers exposent le même TMDB ID.

Ça reste un fallback visible, mais le player Jellyfin peut maintenant aussi proposer directement les sources alternatives via `IMediaSourceProvider`.

**Épisode NFO (`S01E01 - Pilot.nfo`) :**
```xml
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<episodedetails>
  <title>Pilot</title>
  <season>1</season>
  <episode>1</episode>
  <plot>Walt starts cooking meth...</plot>
  <aired>2008-01-20</aired>
  <runtime>47</runtime>
  <jellyfed_peer>instance-b</jellyfed_peer>
  <jellyfed_id>ep001jellyfinid</jellyfed_id>
  <fileinfo>
    <streamdetails>
      <video>
        <codec>h264</codec>
        <width>1280</width>
        <height>720</height>
      </video>
      <audio>
        <codec>aac</codec>
        <language>eng</language>
      </audio>
      <subtitle>
        <language>eng</language>
      </subtitle>
    </streamdetails>
  </fileinfo>
</episodedetails>
```

---

## Comportement Jellyfin avec un `.strm`

### Lors du scan de bibliothèque

1. Scanner trouve `Oppenheimer (2023).strm`
2. Jellyfin lit le `.nfo` → remplit titre, année, TMDB ID, synopsis, genres, artwork
3. Jellyfin lit `<fileinfo><streamdetails>` → stocke codec, résolution, pistes audio/sous-titres en base
4. Sans `.nfo` → Jellyfin tente une recherche TMDB via le nom de fichier (fallback)

### Lors de la lecture

```
1. Client Jellyfin appelle /Items/{id}/PlaybackInfo sur le serveur LOCAL
2. Serveur local retourne MediaSource avec :
   - Path = "https://peer-b/JellyFed/stream/abc123?token=..."
   - MediaStreams = [video:hevc, audio:eac3/eng, audio:aac/fre, sub:eng, sub:fre]
3. Client vérifie les capacités du navigateur vs codec détecté
4a. H264/AAC → direct-play possible → URL envoyée directement au browser
4b. HEVC/MKV → transcoding HLS requis → serveur local lance FFmpeg
5. FFmpeg lit depuis https://peer-b/JellyFed/stream/abc123?token=...
   → peer-b sert le fichier brut avec range request support (seekable)
   → FFmpeg transcode en H264/AAC → HLS segments
6. Browser joue les segments HLS depuis le serveur LOCAL

Si plusieurs peers exposent le même item, `FederationMediaSourceProvider` renvoie plusieurs `MediaSourceInfo` à Jellyfin :
- pour les **films**, le provider lit directement `sources.json` à côté du dossier du film ;
- pour les **épisodes**, il remonte du fichier `SxxExx.strm` vers le dossier de série, lit `episodeSources[]`, puis filtre le groupe correspondant à la saison/épisode courant.

Le `.strm` principal continue à pointer vers la source primaire pour les clients / chemins qui ignorent le provider.
```

---

## Transcodage et sous-titres

### PGS (bitmap Blu-ray)
FFmpeg brûle les sous-titres PGS dans l'image vidéo lors du transcodage (hard-sub). Ils sont toujours visibles si la piste était activée, mais ne peuvent pas être désactivés. La sélection soft-sub n'est pas supportée pour les PGS en HLS.

### SRT/ASS (texte)
⚠️ **Bug connu (BUG-05)** : les sous-titres texte ne s'affichent pas actuellement. L'extraction WebVTT depuis une source HTTP distante pour les HLS soft-subs est à investiguer.

### Pistes audio
Avec les infos de `<streamdetails>`, Jellyfin présente le sélecteur de langue audio dans le player. Le changement de piste redémarre le transcodage sur la piste sélectionnée.

---

## Artwork

JellyFed télécharge posters et backdrops lors de la sync et les stocke localement :
- `poster.jpg` — poster principal
- `fanart.jpg` — backdrop/fanart

**Avantages :** rapidité (servi depuis le disque local), résilience (disponible même si le peer est hors ligne).

L'URL source dépend de la config :
- `JellyfinApiKey` configurée → `/Items/{id}/Images/{type}?api_key={key}` (qualité native)
- Sinon → `/JellyFed/image/{id}/{type}?token={fedToken}` (proxy, lit `ImageInfos`)

---

## Mise à jour des `.strm` existants

Lors de chaque sync, JellyFed met à jour pour les items déjà en manifest :
- **URL du `.strm`** : mise à jour si l'URL a changé (migration de format, changement de token)
- **`.nfo`** : toujours réécrit avec les dernières infos codec/pistes
- **`sources.json`** : réécrit avec la source primaire courante + toutes les sources alternatives connues

`sources.json` ressemble à ceci :

```json
{
  "schemaVersion": 1,
  "itemKey": "tmdb:872585",
  "itemType": "Movie",
  "primaryPeerName": "instance-b",
  "primaryJellyfinId": "abc123def456",
  "path": "/data/jellyfin/data/jellyfed-library/Films/instance-b/Oppenheimer (2023)",
  "syncedAt": "2026-04-24T18:00:00Z",
  "sources": [
    {
      "peerName": "instance-b",
      "jellyfinId": "abc123def456",
      "streamUrl": "https://peer-b/JellyFed/stream/abc123def456?token=...",
      "videoCodec": "hevc",
      "audioCodec": "eac3",
      "width": 1920,
      "height": 1080
    },
    {
      "peerName": "instance-c",
      "jellyfinId": "xyz987",
      "streamUrl": "https://peer-c/JellyFed/stream/xyz987?token=...",
      "videoCodec": "h264",
      "audioCodec": "aac",
      "width": 1280,
      "height": 720
    }
  ]
}
```

Pour une **série**, le même sidecar contient aussi un mapping par épisode :

```json
{
  "schemaVersion": 1,
  "itemKey": "tmdb:1396",
  "itemType": "Series",
  "primaryPeerName": "instance-b",
  "primaryJellyfinId": "series001",
  "path": "/data/jellyfin/data/jellyfed-library/Series/instance-b/Breaking Bad (2008)",
  "syncedAt": "2026-04-27T16:00:00Z",
  "sources": [
    {
      "peerName": "instance-b",
      "jellyfinId": "series001"
    },
    {
      "peerName": "instance-c",
      "jellyfinId": "series999"
    }
  ],
  "episodeSources": [
    {
      "seasonNumber": 1,
      "episodeNumber": 1,
      "title": "Pilot",
      "sources": [
        {
          "peerName": "instance-b",
          "jellyfinId": "ep001",
          "streamUrl": "https://peer-b/JellyFed/stream/ep001?token=...",
          "videoCodec": "h264",
          "audioCodec": "aac",
          "width": 1280,
          "height": 720
        },
        {
          "peerName": "instance-c",
          "jellyfinId": "epAAA",
          "streamUrl": "https://peer-c/JellyFed/stream/epAAA?token=...",
          "videoCodec": "hevc",
          "audioCodec": "eac3",
          "width": 1920,
          "height": 1080
        }
      ]
    }
  ]
}
```

⚠️ Un rescan de bibliothèque dans Jellyfin est nécessaire après la sync pour que les nouvelles infos du `.nfo` soient prises en compte.

---

## Pruning (suppression automatique)

Lors d'une resync, si un item disparaît du catalogue du peer :
1. JellyFed détecte l'absence de la **source** du peer dans `sources[]`
2. si d'autres sources restent :
   - le manifest garde l'item logique ;
   - une autre source devient primaire si nécessaire ;
   - `sources.json` et les marqueurs NFO sont rafraîchis ;
3. si c'était la dernière source :
   - `StrmWriter.DeleteItem()` supprime le dossier (`.strm`, `.nfo`, artwork, `sources.json`) ;
   - `RemoveLibraryItems()` retire l'item de la DB Jellyfin sans attendre le prochain scan ;
4. l'item disparaît seulement quand la dernière source a disparu.

Note : si le peer était temporairement hors ligne lors d'une sync, ses items ne sont pas prunés (le catalogue vide = peer injoignable, pas de pruning). La grace period est implicite par la détection d'absence dans le catalogue retourné.
