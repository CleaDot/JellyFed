# JellyFed — Roadmap

## État d'avancement

| Phase | Statut |
|-------|--------|
| 0 — Scaffolding | ✅ |
| 1 — API catalogue | ✅ |
| 2 — Sync + .strm | ✅ |
| 3 — Gestion peers (base) | ✅ |
| 3b — Auto-registration bidirectionnelle | ✅ |
| 3c — Blacklist peers | ✅ |
| 4 — Multi-source / IMediaSourceProvider | 🔜 |
| 5 — Gossip protocol | 🔜 |
| 6 — Distribution publique | 🔜 |

---

## Bugs connus

### BUG-01 — Bouton Remove Peer : carré blanc sans texte
**Symptôme :** Dans la page de configuration, le bouton "Remove" d'un peer s'affiche comme un grand carré blanc sans texte.
**Cause :** Le bouton `.emby-button` sans classe `raised` et sans `<span>` n'affiche pas son texte dans le CSS Jellyfin.
**Fix :** Ajouter `raised` et wrapper le texte dans `<span>` → `class="emby-button raised button-cancel"`.
**Statut :** ✅ Corrigé dans configPage.html

---

## Features à implémenter

### FEAT-01 — Page config : liste des peers bloqués + déblocage
**Contexte :** Les peers supprimés sont ajoutés à `BlockedPeerUrls`. Il n'y a actuellement aucun moyen de voir ou de retirer un peer de cette liste depuis l'UI.
**Comportement souhaité :**
- Section "Blocked Peers" dans la page de config
- Liste des URLs bloquées avec un bouton "Unblock" par entrée
- Retirer l'URL de `BlockedPeerUrls` et sauvegarder

---

### FEAT-02 — Bouton "Sync All" dans la page de config
**Contexte :** La sync manuelle se fait via l'API (`POST /JellyFed/peer/sync`). Il n'y a pas de bouton dans l'UI admin.
**Comportement souhaité :**
- Bouton "Sync Now" global dans la page de configuration
- Optionnellement : bouton "Sync" par peer dans chaque fieldset
- Feedback visuel (spinner / message "Sync en cours...")

---

### FEAT-03 — Partage de peers (gossip simplifié)
**Contexte :** Si A est peer avec B et C, B et C ne se connaissent pas encore.
**Comportement souhaité :**
- Option dans la config : `SharePeers` (activé/désactivé)
- Quand A se connecte à B et à C, A envoie à B la liste de ses autres peers (C) et vice versa
- B et C sont ajoutés en mode "pending" chez les autres peers (l'admin doit approuver ou auto-approve si configuré)
- Propagation limitée à 1 hop pour éviter l'explosion de peers

**Endpoints concernés :**
- `GET /JellyFed/peers` renvoie déjà la liste → exploiter cette liste dans la sync
- `POST /JellyFed/peer/register` gère déjà l'enregistrement d'un nouveau peer

---

### FEAT-04 — Rapatriement de catalogue ("recall")
**Contexte :** Si A est peer avec B et C, et que le contenu de A est présent chez B et C sous forme de `.strm`, A peut vouloir "rappeler" ses propres items depuis les autres instances (utile si le disque local de A a été perdu/migré).
**Comportement souhaité :**
- Endpoint ou bouton "Recall my content from peers"
- A interroge ses peers, identifie les `.strm` qui pointent vers A, et récupère les métadonnées
- Usage principal : reconstruction d'une bibliothèque après migration

---

### FEAT-05 — Suppression propagée du catalogue
**Contexte :** Si A supprime B de ses peers, les `.strm` de B restent dans la bibliothèque de A (et potentiellement dans les bibliothèques des peers de A si FEAT-03 est actif).
**Comportement souhaité :**
- Quand A supprime B : A envoie un signal `DELETE` à B (`POST /JellyFed/peer/remove` ou `DELETE /JellyFed/peer/:name`)
- B reçoit le signal et supprime les `.strm` de A de sa propre bibliothèque
- Si `SharePeers` est actif : B propage le signal de suppression à ses propres peers qui connaissent A

**Points d'attention :**
- Le signal ne doit être accepté que si l'émetteur est bien le peer qui se supprime lui-même (auth par token)
- Implémenter un mécanisme de confirmation pour éviter les suppressions involontaires

---

### FEAT-06 — Traçabilité des `.strm` par peer (tagging)
**Contexte :** Les `.strm` sont éparpillés dans `{LibraryPath}/Films/` et `{LibraryPath}/Series/`. Il est difficile d'identifier quels fichiers viennent de quel peer, sauf à lire le manifest.
**Comportement souhaité :**
- Le manifest `.jellyfed-manifest.json` existe déjà et contient `peerName` par entrée → à exploiter dans l'UI
- Section dans la page config : "Catalogue par peer" → liste les items synchés de chaque peer avec leur statut
- Bouton "Purge peer catalog" : supprimer tous les `.strm` d'un peer spécifique sans toucher aux autres
- Optionnel : tag dans le `.nfo` (`<studio>JellyFed:peer-b</studio>`) pour que Jellyfin puisse filtrer par peer

---

### FEAT-07 — Organisation modulaire des bibliothèques
**Contexte :** Certains utilisateurs veulent mélanger le contenu de tous les peers dans une seule grande bibliothèque. D'autres préfèrent une bibliothèque séparée par peer. Les deux approches doivent être supportées.

**Mode 1 — Bibliothèque unifiée (comportement actuel)**
- Tout dans `{LibraryPath}/Films/` et `{LibraryPath}/Series/`
- L'utilisateur configure une seule paire de bibliothèques Jellyfin
- Déduplication active : un seul `.strm` par item même s'il vient de plusieurs peers

**Mode 2 — Bibliothèque par peer**
- Chaque peer a son propre sous-dossier : `{LibraryPath}/{PeerName}/Films/` et `{LibraryPath}/{PeerName}/Series/`
- L'utilisateur configure une bibliothèque Jellyfin par peer
- Pas de déduplication inter-peers : le même film peut apparaître plusieurs fois (depuis des peers différents)

**Configuration suggérée :**
```xml
<LibraryMode>unified</LibraryMode>  <!-- "unified" ou "per-peer" -->
```

**Impact :**
- `StrmWriter.cs` : modifier `GetMovieFolder()` et `GetSeriesFolder()` selon le mode
- Page config : radio button "Unified library / One library per peer"
- Migration : si le mode change, proposer de réorganiser les fichiers existants

---

## Améliorations techniques

### TECH-01 — Rechargement de config à chaud
**Problème :** Après modification du fichier XML de config (hors UI Jellyfin), le plugin garde l'ancienne config en mémoire jusqu'au redémarrage.
**Solution :** Utiliser `Plugin.Instance!.SaveConfiguration()` / `LoadConfiguration()` et s'assurer que l'API `POST /Plugins/{id}/Configuration` force bien le rechargement en mémoire.

### TECH-02 — Gestion d'erreurs réseau plus robuste dans PeerClient
**Problème :** Les erreurs de connexion à un peer sont loguées mais ne remontent pas d'informations utiles dans l'UI.
**Solution :** Stocker le dernier message d'erreur dans `PeerStateStore` et l'afficher dans la section peers de la page config.

### TECH-03 — Tests d'intégration
**Contexte :** Aucun test unitaire/intégration pour l'instant.
**Priorité :** Tester `FederationSyncTask` avec un mock `PeerClient`, `StrmWriter` avec un filesystem en mémoire, et `FederationController` avec WebApplicationFactory.

---

## Phases futures

### Phase 4 — Multi-source (`IMediaSourceProvider`)

Même film disponible chez plusieurs peers → plusieurs sources proposées au client.

- `sources.json` par item (stocké à côté du `.strm`)
- `FederationMediaSourceProvider.cs` implémente `IMediaSourceProvider`
- Tri des sources : qualité décroissante, peer préféré en premier
- Le client Jellyfin voit toutes les sources et peut choisir

### Phase 5 — Gossip protocol complet

Voir FEAT-03. Découverte automatique via `GET /JellyFed/peers` périodique.

### Phase 6 — Distribution publique

- Packaging `manifest.json` pour le dépôt de plugins Jellyfin
- Releases avec binaires versionnés
- Documentation d'installation
- Tests CI
