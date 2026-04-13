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
| 3d — UI : Blocked Peers, Sync Now, Catalogue stats | ✅ |
| 3e — Tokens d'accès par peer (révocation à la suppression) | ✅ |
| 3f — Exclusion des .strm du catalogue exposé | ✅ |
| 3g — SelfName configurable | ✅ |
| 4 — Multi-source / IMediaSourceProvider | 🔜 |
| 5 — Gossip protocol | 🔜 |
| 6 — Distribution publique | 🔜 |

---

## Tests à valider — suppression de peer

### TEST-01 — Fichiers distants supprimés chez les peers
**Contexte :** Quand A supprime le peer B, les `.strm` de B doivent disparaître de la bibliothèque de A.
**À vérifier :**
- Cliquer "Remove" sur le peer B depuis l'interface de A
- Vérifier que `{LibraryPath}/Films/` et `{LibraryPath}/Series/` sont vidés des items de B
- Vérifier que les items disparaissent de l'interface Jellyfin de A (sans rescan manuel)
- Vérifier que le manifest `.jellyfed-manifest.json` ne contient plus d'entrées pour B

### TEST-02 — Accès au catalogue révoqué
**Contexte :** Après suppression, B ne doit plus pouvoir accéder au catalogue de A.
**À vérifier :**
- Après que A supprime B, appeler `GET /JellyFed/catalog` sur A avec le token que B utilisait
- Attendre que le token per-peer soit actif (après au moins une sync avec auto-registration)
- Vérifier que la réponse est `401 Unauthorized`
- Vérifier que le token global de A ne fonctionne pas non plus si B n'a jamais fait d'auto-registration

### TEST-03 — Catalogue distant mis à jour
**Contexte :** Les items de B qui étaient visibles chez A doivent disparaître de l'interface Jellyfin sans intervention manuelle.
**À vérifier :**
- La purge (`RemoveLibraryItems`) supprime les items via `ILibraryManager.DeleteItem()`
- L'interface Jellyfin de A ne montre plus les films/séries de B immédiatement
- Aucun rescan manuel nécessaire
- Si `DeleteItem` échoue sur certains items : les items disparaissent lors du prochain scan

---

## Bugs corrigés

### BUG-01 — Bouton Remove Peer : carré blanc sans texte
**Fix :** `class="emby-button raised button-cancel"` + wrapper `<span>`.
**Statut :** ✅

### BUG-02 — Propagation en chaîne des titres (année dupliquée)
**Symptôme :** `Titre (2025) (2025) (2025)` après plusieurs hops de sync.
**Cause :** `GET /JellyFed/catalog` exposait aussi les items `.strm` de la jellyfed-library. Jellyfin utilise le nom de dossier `Titre (2025)` comme titre. Lors du hop suivant, le folder devient `Titre (2025) (2025)`.
**Fix :** `QueryItems()` exclut les items dont le path commence par `LibraryPath`.
**Statut :** ✅

### BUG-03 — Peer supprimé conserve l'accès au catalogue
**Symptôme :** Après que A supprime B, B peut encore appeler `GET /JellyFed/catalog` sur A avec le token global.
**Cause :** Token global partagé entre tous les peers — pas révocable individuellement.
**Fix :** Tokens d'accès par peer. À l'auto-registration, chaque peer reçoit un token unique généré par l'instance cible. Supprimer un peer invalide son token. Fallback token global pour les peers sans auto-registration.
**Statut :** ✅

---

## Features à implémenter

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
**Statut :** ✅ Partiellement implémenté
**Implémenté :**
- `GET /JellyFed/manifest/stats` : stats par peer (nb films, nb séries)
- `POST /JellyFed/peer/purge` : purge complète d'un peer (fichiers + DB Jellyfin)
- Section "Synced Catalogue" dans la page config avec bouton "Purge Catalog" par peer

**Restant :**
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
