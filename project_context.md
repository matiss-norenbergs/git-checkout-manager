# GitCheckoutManager: project context for AI coding agents

Read this before changing code. It describes how the app **currently** works and the rules that keep it correct and safe. User-facing documentation is in `README.md`.

---

## 1. What the app is

A WPF desktop tool (Windows) that creates and edits **Git sparse checkouts** (cone mode) for repositories on **GitLab** (incl. self-hosted) and **GitHub**. It is a visual assistant around the `git` CLI, not a Git replacement.

Two tabs:
- **Clone:** pick repo + branch + folders, then generate or run a script that creates a new sparse checkout.
- **Manage Checkout:** open an existing local checkout, add or remove folders, and manage submodules.

---

## 2. Tech stack

- .NET 8, WPF (`net8.0-windows`), MVVM with **CommunityToolkit.Mvvm** (`[ObservableProperty]`, `[RelayCommand]`)
- `System.Text.Json`, `HttpClient` (host APIs only), `System.Security.Cryptography.ProtectedData` (DPAPI)
- Git CLI is called through **one runner**, `IGitService.RunAsync`
- **Velopack** handles installs and updates. The `Velopack` package version in `GitCheckoutManager.csproj` and the `vpk` tool version in `.github/workflows/release.yml` (`dotnet tool install -g vpk --version …`) must be bumped together. The csproj `<Version>` is overridden from the tag by the release workflow.
- No third-party UI frameworks. DI is manual in `MainWindow.xaml.cs`.

---

## 3. Project layout

```
Models/        AppSettings, Repository, Branch, TreeNode, TreePreset, RecentCheckout,
               SubmoduleInfo, RemovalReviewModel, GitHostType, ThemeMode, AppMode
Services/      GitService (runner), GitLabHostService, GitHubHostService, GitHostServiceFactory,
               RemoteTreeService, CheckoutService, SubmoduleService, GitTreeParser,
               CommandGenerator, SettingsService, PresetService, DialogService, ClipboardService,
               TitleBarColorizer   (+ an I* interface for most)
ViewModels/    MainViewModel (both tabs), TreeNodeViewModel, SettingsViewModel, SubmodulesViewModel
Views/         SettingsWindow, RemovalReviewWindow, SubmodulesWindow, SubmoduleUrlWindow, SubmoduleBranchWindow
Themes/        LightTheme.xaml, DarkTheme.xaml (brushes + implicit control styles),
               ButtonStyles.xaml (shared keyed/implicit styles: buttons, expander, scrollbar)
_bin/          prebuilt binaries, intentionally committed for now
```

---

## 4. How the main features work

### Hosts (`IGitHostService`)
- Used **only** to list repositories (`GetRepositoriesAsync`) and to give the HTTP username for Git auth (`GitHttpUsername`: GitLab `oauth2`, GitHub `x-access-token`).
- GitLab: `/api/v4/projects`, `PRIVATE-TOKEN`, `X-Total-Pages` pagination. GitHub: `api.github.com` (or `{url}/api/v3` for Enterprise), Bearer + `User-Agent`, sequential `Link rel="next"` pagination.
- Each host maps its own private DTO to the provider-neutral `Repository` model.
- **Branches come from Git, not the API:** `git ls-remote --heads <url>`.

### Clone tab tree (`RemoteTreeService`)
- Per repo, a **blobless, no-checkout partial clone** cache: `%LocalAppData%\GitCheckoutManager\tree-cache\<sha256-prefix>\repo`.
- Flow: `ls-remote` gets the tip sha. If `trees\<sha>.json` is cached, use it. Otherwise clone (`--filter=blob:none --no-checkout --depth 1 --branch`) or `fetch --depth 1 --filter=blob:none`, then `ls-tree -r -t -z <sha>`, parse with `GitTreeParser`, and cache as JSON.
- Mode `160000` (gitlink) becomes a folder node with `IsSubmodule = true`.
- **Never run checkout, grep, diff, log -p, blame or anything that reads file contents in the cache repo:** it would download blobs.
- Branch changes are debounced (~400 ms) and cancel the previous load (`CancellationToken`, process tree kill). This protects the server.

### Manage tab (`CheckoutService`)
- `OpenAsync`: `rev-parse --show-toplevel`, remote URL, branch, HEAD, `core.sparseCheckout`/`core.sparseCheckoutCone`, `sparse-checkout list`, `status --porcelain` count.
- Tree = `git ls-tree -r -t -z HEAD` **in the checkout itself** (offline; a blobless clone has all trees).
- Baseline = current sparse paths. Pending changes = Added/Removed vs baseline (with "covered by ancestor" logic).
- Apply (`ExecuteManageApplyAsync`): review removed folders via `git status --porcelain=v1 -z --ignored=matching --untracked-files=all`, then the RemovalReviewWindow (ignored deleted by default, untracked/changed kept by default, second confirmation for destructive choices), then optional `git restore`, `git sparse-checkout set` (with auth), selective `git clean -ffdX/-ffd/-ffdx`, empty-dir cleanup, last-resort delete. Finally it **re-reads the checkout and reports what is actually on disk**. `sparse-checkout set` exits 0 even when it leaves files behind, so the report never trusts the exit code alone.
- Leftovers that couldn't be deleted go into `CleanupLeftovers` and the **Retry cleanup** button.
- **Open in Explorer / VS Code** (Manage tab tree menu, checkout bar, Submodules row ⋯ menu) goes through `IShellLauncher`/`ShellLauncher` (+ `FolderOpener`, fakeable `IShellEnvironment`): path normalised with `Path.GetFullPath`, one `ArgumentList` entry, `UseShellExecute = false`, never waited on, explorer.exe's exit code ignored (1 = success). VS Code is `Code.exe` resolved from `code.cmd` on PATH (one folder above `bin\`), then `%LocalAppData%`, then `%ProgramFiles%`; **never start `code.cmd`** (cmd.exe interprets `%`, `&`, `^`). "On disk" (`Directory.Exists`) is checked when the context menu opens (`TreeNodeViewModel.IsOnDisk`), Manage tree only.

### Submodules (`SubmoduleService`, `SubmodulesWindow`)
- **Never use `git submodule status`:** it aborts on the first gitlink without a `.gitmodules` entry.
- Detection: `ls-files -s -z` (mode 160000) + `git config -f .gitmodules -z --get-regexp "^submodule\."` (names may contain dots: split at the first `submodule.` and the last dot) + folder existence + `.git` inside + `rev-parse HEAD`.
- States: Ready, DifferentCommit, NotInitialized, MissingFromGitmodules, ManuallyCloned, OutsideCheckout (+ runtime Failed/Queued/Working). A path without a `.gitmodules` entry is `ManuallyCloned` when its folder holds a repository with a readable HEAD, else `MissingFromGitmodules`.
- **Local fixes (never touch `.gitmodules`):** `SetUrlAndInitAsync` writes `submodule.<name>.url` and the marker `submodule.<name>.gcmUrlOverride=true` to the checkout's local `.git/config`, then runs the normal init/update (`submodule init` keeps an existing local URL, so the override wins). `SubmoduleInfo.UrlOverridden`/`EffectiveUrl` are read with `git config --local --get`. `ResetUrlAsync` unsets only the marker (exit 5 = not set is fine), then runs `git submodule sync -- <path>`, which writes the `.gitmodules` URL back into `.git/config` and the submodule's own origin; unsetting `submodule.<name>.url` instead would leave the submodule looking uninitialized. Any other `git submodule sync` likewise overwrites a local URL override while the marker stays, so the UI can show a stale "overridden" flag. A `ManuallyCloned` row has no name; its `EffectiveUrl` is the clone's `origin` and is shown in the URL column.
- `CloneManuallyAsync` (MissingFromGitmodules only): refuses a non-empty folder, `git clone --no-checkout <url> <path>`, then `checkout --detach <PinnedSha>`; on failure (or cancel) it deletes what was created and restores the original empty folder. The result is not a registered submodule, so `git submodule` commands ignore it.
- **Branch switching** (`SwitchBranchAsync`, `ResetToRecordedAsync`, `CheckSwitchSafetyAsync`, `ListRemoteBranchesAsync`; all `git -C <RepoRoot>/<Path>`, so nested rows work). Safety rules: **never** `checkout -B`, `reset --hard` or anything that discards commits; **dirty check first** (`status --porcelain`, any entry blocks; the VM never calls the switch then, and `ResetToRecordedAsync` re-checks itself). Switch = `fetch origin +refs/heads/<b>:refs/remotes/origin/<b>` (explicit refspec so single-branch clones work), then no local branch → `checkout -b <b> --track origin/<b>`; local branch → `checkout <b> --` + `merge --ff-only origin/<b>`. A failed ff where HEAD is not an ancestor of origin (`merge-base --is-ancestor` exit 1) returns exit 0 with a "left as is" message in `StdOut`; other merge failures are real errors. `CheckSwitchSafetyAsync` warns (`UnreferencedCommits`) when HEAD is detached, != PinnedSha and no `refs/*` contains it (`branch -a --contains HEAD --format=%(refname)`; the "(HEAD detached at …)" pseudo-entry is ignored). `SubmoduleInfo.CurrentBranch` = `symbolic-ref --short -q HEAD` (null = detached). Auth for fetch/ls-remote is resolved from the submodule's own `origin` URL. UI: `SubmoduleBranchWindow` via `IDialogService.ShowSubmoduleBranch`; the VM remembers switched rows (`_switched`) so the "Moved from the recorded commit" note survives reloads. After a switch the main repo sees the submodule as changed; the app never commits.
- URL picker: `SubmoduleUrlWindow` via `IDialogService.ShowSubmoduleUrl`; it tests the URL with `TestUrlAsync` (`ls-remote --heads`) before returning it.
- Init runs **one submodule at a time**: `submodule init -- <path>`, read the resolved URL, choose auth for **that** URL's host, then `submodule update [--remote] [--recursive] -- <path>` with `allowInteractiveAuth: true` (lets Git Credential Manager prompt for unknown hosts).

### Script generation (`CommandGenerator`)
- `.bat`: `chcp 65001` on line 2, UTF-8 **without BOM**, `%` escaped as `%%` (`BatEscape`), values quoted, `if errorlevel 1 goto :failed` after each essential step.
- `.sh`: **LF line endings** (`ToLf`), all values single-quoted (`ShQuote`), `|| fail` after each essential step.
- Submodule loop is driven by `.gitmodules` and updates each path individually. Gitlinks missing from `.gitmodules` print a `WARNING:`.
- Exit codes: `0` ok, `1` failed (always pauses), `2` submodule failures (always pauses, after the summary line). `ExecuteScriptAsync` maps these to status messages and only adds exit 0/2 checkouts to recent checkouts.
- Saved scripts use the folder **name** (relative, portable). Execute Locally uses the full `CloneParentFolder\FolderName` path.

### Cone-mode rule
Only **folders** can be selected. `GetCheckedPaths` never yields files, and a defensive filter (`DropFilePaths`) runs before every `sparse-checkout set`. Files show `IsIncluded` (root files always; a folder's direct files whenever it or a descendant is selected).

### Presets
`%AppData%\GitCheckoutManager\presets.json`, keyed `remote:<normalized url>` (same key in both tabs). Paths that resolve to files or don't exist are skipped on load.

---

## 5. Security rules (do not break)

1. **Tokens never appear** in URLs, scripts, `.git/config`, cache folders, logs or status messages.
2. Git auth is passed only via environment: `GIT_CONFIG_COUNT/KEY/VALUE` with a **URL-scoped** key `http.<scheme>://<host>[:port]/.extraHeader` (`Authorization: Basic base64(user:token)`), so the header is never sent to another host.
3. Always set `GIT_TERMINAL_PROMPT=0`. Set `GCM_INTERACTIVE=Never` unless the call is an explicit, user-triggered action (`allowInteractiveAuth`).
4. Tokens are stored per host type with DPAPI (`CurrentUser`) in `settings.json`.
5. Use `ProcessStartInfo.ArgumentList`, never string-concatenated arguments.

---

## 6. UI conventions

- Colors only via `DynamicResource` brushes from the theme dictionaries. Both `LightTheme.xaml` and `DarkTheme.xaml` must define every brush.
- The themes contain an **implicit `TextBlock` style** that sets the foreground. Text inside coloured buttons gets its colour from `ColoredActionButtonBase` (a local TextBlock style using `OnAccentBrush`). Use plain `Content="…"` on buttons.
- `ButtonStyles.xaml` styles are `BasedOn` the theme's implicit Button style, resolved once at startup. Keep the Button style **identical** in both themes, or move it into `ButtonStyles.xaml`.
- New windows: modal, owner = main window, themed via DynamicResource, `ItemsControl` + row templates rather than `DataGrid`.
- Primary action = `AccentActionButton`, right-aligned in action bars. Secondary actions = `ActionButton`.
- Settings that should survive restarts go into `AppSettings` (saved through `SettingsService`, written atomically: temp file, then `File.Move(..., overwrite: true)`).

---

## 7. Storage

| Data | Location |
|---|---|
| Settings, tokens, recent checkouts | `%AppData%\GitCheckoutManager\settings.json` |
| Presets | `%AppData%\GitCheckoutManager\presets.json` |
| Tree cache | `%LocalAppData%\GitCheckoutManager\tree-cache\` |

All paths come from `AppPaths`. The project was renamed from GitSparseManager; `AppPaths.MigrateLegacyData()` runs once at startup (before settings load): if the new `%AppData%` folder is missing and `%AppData%\GitSparseManager` exists, it copies `settings.json` and `presets.json` (never moves, never touches the old folder), and it best-effort deletes the old `%LocalAppData%\GitSparseManager\tree-cache`. Repo: `matiss-norenbergs/git-checkout-manager`.

---

## 8. Planned / not yet built

- Branch switching in the Manage tab: deliberately out of scope for now.