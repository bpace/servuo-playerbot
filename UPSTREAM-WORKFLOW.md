# Upstream workflow

There are two different repositories in this workflow.

`C:\dev\uo-offline\_Klein187` is the personal GitHub fork of Klein187/UO Offline. It tracks the original project's `main` branch and is where upstream changes are fetched and compared.

`C:\dev\servuo\servuo\_mods\playerbot` is the independent ServUO mod. It contains only the ServUO-compatible implementation and is published to its own GitHub repository. Do not merge the ModernUO fork into this repository.

## One-time GitHub setup

Sign in to GitHub, fork `Klein187/uo-offline` under the desired account, then run from `C:\dev\uo-offline\_Klein187`:

```powershell
git remote rename upstream origin
git remote add upstream https://github.com/Klein187/uo-offline.git
git fetch upstream
git branch --set-upstream-to=origin/main main
```

Create a separate empty GitHub repository, for example `servuo-playerbot`, then run from this mod directory:

```powershell
git remote add origin https://github.com/YOUR-ACCOUNT/servuo-playerbot.git
git add .
git commit -m "Initial ServUO PlayerBot mod"
git push -u origin main
```

## Reviewing UO Offline updates

From the fork checkout:

```powershell
git fetch upstream
git switch main
git merge --ff-only upstream/main
git push origin main
git log --oneline HEAD@{1}..HEAD -- playerbots
```

Review each relevant upstream change. Port compatible behavior manually into the ServUO mod, build against ServUO pub57, test it on a non-production shard, then commit and push the mod repository. ModernUO APIs, serialization, routing, gumps, and engine patches must not be merged directly.
