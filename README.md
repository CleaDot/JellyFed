# JellyFed

Plugin Jellyfin pour la fédération native d'instances.

Connecte plusieurs serveurs Jellyfin entre eux : depuis un seul client, on accède aux bibliothèques de toutes les instances fédérées — sans proxy, sans frontend custom, de façon transparente pour les clients officiels.

---

## Concept

```
[Client Jellyfin]
       │
       ▼
[Instance A  ←──── JellyFed ────→  Instance B]
       │                                 │
  Bibliothèque A                   Bibliothèque B
  (locale)                         (partagée via .strm)
```

Instance A installe JellyFed. Elle se connecte à l'Instance B (un ami, un serveur communautaire). Le plugin synchronise le catalogue de B dans A sous forme de fichiers `.strm` dans une bibliothèque virtuelle. Les clients Jellyfin voient les médias de B exactement comme s'ils étaient locaux — avec artwork, métadonnées, lecture directe.

---

## Fonctionnalités implémentées

### Fédération de catalogue
- Exposition du catalogue local via `GET /JellyFed/catalog` (films + séries)
- Exposition des saisons/épisodes via `GET /JellyFed/catalog/series/:id/seasons`
- Authentification par token de fédération (`Authorization: Bearer <token>`)
- Delta sync : paramètre `?since=` pour ne synchroniser que les nouveautés
- Pagination : `?limit=` et `?offset=`

### Génération de fichiers `.strm`
- Un `.strm` par film avec l'URL de stream du peer
- Un `.strm` par épisode avec son URL de stream
- Fichiers `.nfo` avec métadonnées (titre, année, TMDB ID, synopsis, genres, notes)
- Téléchargement du poster et du backdrop depuis le peer
- Organisation : `{LibraryPath}/Films/{Titre (Année)}/` et `{LibraryPath}/Series/{Titre}/Season XX/`

### Synchronisation
- Tâche planifiée `IScheduledTask` (intervalle configurable, défaut 6h)
- Manifest JSON (`.jellyfed-manifest.json`) — évite la re-création des `.strm` déjà présents
- Pruning automatique des `.strm` dont les items ont disparu du catalogue du peer
- Déduplication par TMDB ID : les items déjà présents dans la bibliothèque locale ne sont pas re-créés en `.strm`
- Déclenchement d'un rescan Jellyfin après chaque sync

### Gestion des peers
- Configuration via le panneau admin Jellyfin (page config custom)
- Par peer : Nom, URL, Token de fédération, Enabled, SyncMovies, SyncSeries
- Endpoint `GET /JellyFed/peers` : liste les peers avec statut online/offline
- Endpoint `POST /JellyFed/peer/sync` : sync manuelle
- Service heartbeat (`PeerHeartbeatService`) toutes les 5 minutes → statut online/offline
- Stockage du statut peers dans `{LibraryPath}/.jellyfed-peers.json`

### Auto-registration bidirectionnelle
- Après chaque sync, l'instance s'annonce au peer via `POST /JellyFed/peer/register`
- Le peer ajoute l'instance comme peer en retour (activé et prêt à syncer)
- La `SelfUrl` de l'instance doit être configurée pour que cela fonctionne

### Blacklist peers
- Les peers supprimés manuellement sont ajoutés à `BlockedPeerUrls`
- Un peer blacklisté ne peut pas se re-enregistrer via `POST /JellyFed/peer/register`
- Réponse `{"status": "blocked"}` renvoyée au peer refusé

---

## Installation

1. Compiler : `dotnet build -c Release`
2. Copier `bin/Release/net8.0/Jellyfin.Plugin.JellyFed.dll` dans le dossier plugins Jellyfin :
   `{config}/plugins/JellyFed_0.1.0.0/Jellyfin.Plugin.JellyFed.dll`
3. Redémarrer Jellyfin
4. Administration → Plugins → JellyFed → Configurer

### Configuration minimale

```
Federation Token : <token aléatoire>
Sync Interval    : 6 (heures)
Library Path     : /config/jellyfed-library
Self URL         : http://mon-jellyfin:8096
```

Puis ajouter un peer (URL + token du peer distant) et cliquer Save.

### Bibliothèques Jellyfin

Après la première sync, ajouter les bibliothèques dans Jellyfin :
- `{LibraryPath}/Films` → type Films
- `{LibraryPath}/Series` → type Séries

---

## Documentation

- [`docs/architecture.md`](docs/architecture.md) — Architecture technique complète
- [`docs/api.md`](docs/api.md) — API de fédération (endpoints)
- [`docs/roadmap.md`](docs/roadmap.md) — État d'avancement et features à venir
- [`docs/strm.md`](docs/strm.md) — Fonctionnement des fichiers .strm dans Jellyfin

---

## Repo

Projet privé. Pas de push GitHub.
