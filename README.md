# Git Checkout Manager

A Windows desktop tool for working with **part of a Git repository**. You pick the folders you need, and the app creates a sparse checkout containing only those. File contents outside your selection are never downloaded.

It works with **GitLab** (including self-hosted servers) and **GitHub**, and handles the tricky parts for you: cone-mode rules, partial clones, submodules, and safely removing folders from an existing checkout.

---

## Contents

- [Requirements](#requirements)
- [The two tabs](#the-two-tabs)
- [Connecting](#connecting)
- [Clone tab: create a new sparse checkout](#clone-tab-create-a-new-sparse-checkout)
- [Manage Checkout tab: change an existing checkout](#manage-checkout-tab-change-an-existing-checkout)
- [Submodules window](#submodules-window)
- [Presets](#presets)
- [Settings](#settings)
- [The generated script](#the-generated-script)
- [Releasing](#releasing)
- [Where data is stored](#where-data-is-stored)
- [Tips and troubleshooting](#tips-and-troubleshooting)

---

## Requirements

- Windows 10 or later
- **Git for Windows**, available on `PATH` (Git 2.36 or newer recommended)
- A GitLab or GitHub account with a Personal Access Token

### Installing

Download `GitCheckoutManager-win-Setup.exe` from [Releases](https://github.com/matiss-norenbergs/git-checkout-manager/releases); the app updates itself (it checks for a new version at startup and every 6 hours while running; you can also check in Settings).

---

## The two tabs

| Tab | Use it to |
|---|---|
| **Clone** | Create a **new** sparse checkout of a repository: pick the repo, branch and folders, then run or save the script. |
| **Manage Checkout** | Change **an existing** checkout on disk: add or remove folders, and manage its submodules. |

The **⚙** button in the top-right corner opens [Settings](#settings).

---

## Connecting

The Clone tab needs a connection to your Git host. The Manage tab works on local checkouts and doesn't need one, but it uses your saved tokens when it has to download files.

1. Choose the **Host**: `GitLab` or `GitHub`.
2. Enter the **Server URL**. For GitLab, use your server's address, e.g. `https://gitlab.example.com`. For GitHub, use `https://github.com` (filled in automatically).
3. Paste your **Personal Access Token** and click **Connect**.

The URL and token are remembered **per host**, so you can switch between GitLab and GitHub without retyping them. Tokens are encrypted with Windows DPAPI and never written into scripts, URLs or Git config files.

### Creating a token

**GitLab:** avatar → **Edit profile** → **Access tokens** → **Add new token**. Tick the scopes **`read_api`** (to list repositories) and **`read_repository`** (to read folder structures and download files).

**GitHub:** **Settings** → **Developer settings** → **Personal access tokens** → **Fine-grained tokens** → **Generate new token**.
- Repository access: *All repositories* (or the ones you need)
- Repository permissions: **Contents → Read-only** (Metadata is added automatically)

A classic token with the `repo` scope also works, but grants much more than the app needs.

> The app never writes to the server, so read-only permissions are enough.

---

## Clone tab: create a new sparse checkout

### 1. Choose the source

Pick a **Repository** and a **Branch**. Both are searchable pickers: type to filter the list, then pick an entry.

The folder tree loads automatically. The app downloads only the repository's **folder structure** (no file contents, typically a few hundred KB to a few MB), so even very large repositories load in seconds. The result is cached per commit, so switching back to a branch you've already viewed is instant.

- **Refresh tree** fetches the latest structure for the selected branch.
- When you switch branches, folders you've ticked **stay ticked** if they also exist on the new branch.

### 2. Choose the folders

Tick folders in the tree. Use **Search** to find folders by name. The tree is filtered as you type and the match count is shown next to the box. Press **Enter** to select and scroll to the next match (it wraps around at the end) and **Shift+Enter** for the previous one; the counter then reads e.g. *3 / 12*. Search works the same in the Clone and Manage tabs.

- **Only folders can be selected.** Files show their status instead:
  - **normal text** means the file will be included
  - **grey text** means it won't
- Files at the repository root are **always** included.
- When you select a folder, the files directly inside each of its **parent folders** come along too. That's how Git's cone mode works.
- Submodules are marked **(submodule)**.

The **Selected Paths** panel lists your selection, and the **Script** panel shows the generated script, updating as you tick.

### 3. Choose the destination and options

| Field | Purpose |
|---|---|
| **Sparse – selected folders / Full clone – everything** | The clone mode, remembered between runs. **Sparse** (default) checks out only the ticked folders. **Full clone** runs a plain `git clone --branch` of the whole repository, with no filter and no sparse-checkout. In Full mode the tree checkboxes and presets are disabled (expanding and search still work), **Selected Paths** shows *All folders*, and you don't need to tick anything before running or saving. **New Branch** and **Initialize submodules** work the same in both modes. |
| **Clone into** | The parent folder for new checkouts. Remembered between runs. |
| **Folder** | The checkout's folder name. It's generated from the naming pattern (default `{repo}_{branch}`, see [Settings](#settings)) and follows your repo and branch choices. Type your own name to override it; **Auto** switches back to the generated name. |
| **New Branch** | Optional. After checkout, creates a **local** branch with this name from the selected branch. Nothing is pushed. Invalid branch names are flagged. |
| **Initialize submodules** | Initializes the submodules inside your selected folders (see [the generated script](#the-generated-script)). |
| **Keep CMD window open** | Keeps the console open after a successful run. After a failure it always stays open. |

The line under the fields shows the **full target path**. It turns red, and Execute is disabled, when the folder already exists and isn't empty.

### 4. Run it

| Button | Action |
|---|---|
| **Execute Locally** | Runs the script on this machine in a console window and creates the checkout at the previewed path. |
| **Copy script** | Copies the script to the clipboard. |
| **Save .bat** / **Save .sh** | Saves the script for Windows or for Bash (Git Bash, Linux, macOS). Saved scripts use only the folder **name**, so they create the checkout wherever you run them. |

After **Execute Locally**, the status bar reports the result:

| Result | Meaning |
|---|---|
| *Checkout created.* | Everything succeeded. |
| *Checkout created, but some submodules failed to initialize.* | The checkout is fine; check the console for which submodules failed. |
| *Script failed, see the console window.* | A step failed; the console stays open with the error. |

Successful checkouts are added to the **recent checkouts** list in the Manage tab.

---

## Manage Checkout tab: change an existing checkout

### Opening a checkout

- Pick one from the **Checkout** dropdown (recently used checkouts), or click **Browse…** and choose any folder inside a checkout. The app finds the repository root itself.
- The most recent checkout opens automatically when you switch to this tab.
- Checkouts that no longer exist on disk are shown greyed out with **(missing)**. Selecting one offers to remove it from the list.

The line under the dropdown summarizes the checkout: remote URL, branch, commit, number of selected folders, and local changes. **Reload** re-reads everything from Git.

The tree is read from the checkout itself, so it needs no network connection and matches exactly what's checked out, including local commits you haven't pushed. Your current folders are pre-ticked. If the checkout isn't sparse yet, every folder starts ticked, and applying turns sparse checkout on.

### Changing folders

Tick or untick folders. **Pending Changes** lists what will happen:

```
+ apps/mobile        ← will be added (files downloaded)
− libs/legacy        ← will be removed from disk
```

Click **Apply Now** to perform the changes.

### Removing folders safely

Before anything is deleted, the app checks what's inside the folders you're removing. If it finds anything besides the repository's own files, the **Review removal** window opens with up to three groups:

| Group | Examples | Deleted by default? |
|---|---|---|
| **Ignored files** | `node_modules/`, build output, logs | **Yes** |
| **Untracked files** | new files you created | No |
| **Changed files** | files you edited | No |

Tick **Delete these** for each group you want removed. Choosing to delete untracked or changed files asks for a second confirmation, because it can't be undone.

> **Tip:** commit your changes before removing a folder. Committed work stays in Git history even after the folder is gone from your disk.

After applying, the app checks the disk and reports what **actually** happened, for example:

- *Kept partly: src/legacy*: you chose to keep some files, so the folder still exists.
- *Could not delete: tools/x (file in use)*: another program (an editor, Explorer) is using the folder. Close it and click **Retry cleanup**.

Git warnings, if any, appear under **Last apply output**.

**Copy script**, **Save .bat** and **Save .sh** produce an equivalent script, for people who prefer to run the commands themselves.

---

## Submodules window

Click **Submodules…** in the Manage tab to see every submodule of the open checkout:

| State | Meaning |
|---|---|
| **Ready** (green) | Checked out at the commit the main repository expects. |
| **On a different commit** (blue) | Checked out, but at another commit, typically after updating to the latest from its branch. |
| **Not initialized** (grey) | Part of your checkout but not downloaded yet. |
| **Missing from .gitmodules** (red) | The repository contains the submodule, but `.gitmodules` has no entry for it, so Git doesn't know where to download it from. Use **Clone manually…** (below), and ask the repository maintainer to fix `.gitmodules`. |
| **Cloned manually** (blue) | No `.gitmodules` entry, but the folder holds a repository (for example from **Clone manually…**). Git doesn't manage it as a submodule, so *Initialize* skips it; it can still be pulled and switched. |
| **Not in your checkout** | Outside your selected folders; shown only with *Show submodules outside my checkout*. |

### After cloning: the review bar

If you run a script from the **Clone** tab and it finishes with exit code 2 (*checkout created, but some submodules failed*), a bar appears at the top of the window: *"Some submodules need attention in <folder>."* Click **Review submodules** to switch to the Manage tab, open that checkout and show the Submodules window with *Show only problems* ticked (for this opening only; your saved preference is unchanged). **Dismiss** hides the bar; it also disappears on the next *Execute* or when you open another checkout.

### Nested submodules

Submodules inside populated submodules (*Ready*, *On a different commit* or *Cloned manually*) are listed too, up to 5 levels deep. They appear as indented rows under their parent, with their path relative to the checkout (for example `external/lib/vendor/x`). Every row is handled on its own, inside the repository that contains it, so a nested problem can be initialized, given a URL or cloned manually like any other. A parent that isn't initialized has no children yet; after you initialize it, the refreshed list shows them. *Include nested submodules (--recursive)* still works as before.

### Initializing submodules

1. Tick the submodules to initialize, or click **Select all with problems**.
2. Choose:
   - **Pinned commit**: the exact commit the main repository expects (recommended).
   - **Latest from branch**: the newest commit on the submodule's branch. The main repository will then show the submodule as changed.
   - **Include nested submodules**: also initializes submodules inside submodules.
3. Click **Initialize selected**.

Submodules are processed **one at a time**, so one broken submodule never blocks the others. Each row shows its progress (*Queued → Working… → Ready*, *Failed* or *Skipped*). Failed rows keep their Git error under **Error details**. A row is shown as *Skipped* (grey, not red) when the safety check left it alone on purpose: it has uncommitted changes, or you answered *No* to the question about a commit that isn't on any branch. The reason is kept under **Error details**, and the summary counts these separately (*"Initialized 1 of 2. 1 skipped: external/lib"*). Only real Git errors are shown as *Failed*. **Cancel** stops the run cleanly.

Submodules on your connected GitLab/GitHub server use your saved token. For a submodule on another server, Git Credential Manager may ask you to sign in.

### Fixing a broken submodule locally

Each row has a **⋯** menu (disabled while a run is active). **None of these actions change or commit anything in the repository**: they only affect your checkout, so the real fix (`.gitmodules`) still has to be made by the repository maintainer.

- **Set URL…** (rows with a `.gitmodules` entry): pick a repository from your connected server, or paste a URL. The URL is checked first (`git ls-remote`); if it can't be reached the dialog stays open and shows Git's error. The URL is then stored in your checkout's local `.git/config` and the submodule is initialized with the current *Pinned/Latest* and *Nested* options. The row shows *URL overridden locally: …*.
- **Reset URL** (only for overridden rows): removes the local override, so the row uses the URL from `.gitmodules` again.
- **Clone manually…** (only for *Missing from .gitmodules* rows): clones the repository you choose into the (missing or empty) submodule folder and checks out the commit the main repository expects. The row becomes *Cloned manually*. If the clone fails, the folder is left empty as before. A non-empty folder is never touched.

### Switching a submodule to a branch

For submodules that are on disk (*Ready*, *On a different commit*, *Cloned manually*) the **⋯** menu also has:

- **Switch branch…**: opens a list of the branches on the submodule's `origin` (loaded when the dialog opens; the current branch is highlighted, and you can filter). Pick one and click **Switch**. The app fetches that branch and checks it out: a new local branch that tracks `origin/<branch>`, or your existing local branch, fast-forwarded to origin. If your local branch has commits that aren't on origin, it is **left as it is** and the result says so. Nothing is ever discarded or overwritten.
  - **Uncommitted changes block the switch.** The files are listed (first 50); commit or discard them in the submodule first.
  - If the submodule is on a detached commit that belongs to no branch, you are asked to confirm, because that commit gets hard to find afterwards.
- **Reset to recorded commit** (only when the submodule is not on the commit the main repository expects): detaches the submodule at that commit again, after the same uncommitted-changes check and a confirmation.

Each row shows **on <branch>** or **detached** under its pinned/current commits. After a switch, a row on another commit says *"Moved from the recorded commit"*: the **main repository will show this submodule as changed**, and committing there would record the new commit for everyone. Use **Reset to recorded commit** to undo that. One submodule is handled at a time.

### Working on several submodules at once

Every populated row (*Ready*, *On a different commit*, *Cloned manually*) can be ticked, not only the ones with problems. The selection actions sit at the right end of the filter row: **N selected**, **Pull**, **Switch branch…** and a **⋯** menu. The counts in the buttons are the rows the action applies to.

- **Pull (n) / Pull all (n)**: fast-forwards the branch each submodule is **on**. It never merges or rebases, and needs no confirmation. With rows ticked it pulls those; with nothing ticked it pulls every submodule shown (*Pull all*). This is different from *Latest from branch*, which detaches the submodule at the tip of the `.gitmodules` branch. Rows that are detached are skipped (*not on a branch*), and a branch that exists only on your machine is skipped (*branch not on its remote*). A branch with commits of its own that aren't on origin is **left as it is**.
- **Switch branch… (n)**: switches the ticked submodules to one branch. Uncommitted changes are checked first; those submodules are skipped and don't block the others. The picker loads the branches **once per distinct remote** and, by default, lists only branches every submodule's remote has. Tick *Show branches missing from some submodules* to see the rest (marked *on 3/5*); submodules whose remote lacks the chosen branch are skipped. A branch is greyed out only when every submodule is already on it; submodules already on the chosen branch are simply fast-forwarded. If some submodules sit on a commit that belongs to no branch, you get **one** question listing them; answering *No* skips only those.
- **⋯ menu**: *Reset selected to recorded commit* (one confirmation, then each submodule is checked for uncommitted changes), *Select all with this remote* (available when the ticked rows share one remote) and *Clear selection*.

> **Note:** switching a submodule to a branch that drops one of its nested submodules leaves that folder behind as untracked files, so the parent then counts as having uncommitted changes and later Pull/Switch runs skip it until you remove the folder.

Like *Initialize selected*, these run **one submodule at a time**, in list order. Each row is re-read right before it is handled (switching a parent can change or remove a nested submodule). Rows that failed or were skipped stay ticked so you can run again. The summary spells out the outcome, for example *"Pulled 3 of 5: 2 updated, 1 already up to date. 1 skipped (uncommitted changes): external/a. 1 left as is (local commits): external/b."* or *"Switched 3 of 4 to develop. 1 skipped (branch not on its remote): external/c."*

With *Latest from branch* selected, ticked *Ready* rows are updated by **Initialize selected** as well; with *Pinned commit* they have nothing to initialize.


### Copy report

**Copy report** (bottom of the window, enabled when any row is *Missing from .gitmodules*, failed, has a locally overridden URL, or was cloned manually) copies a plain-text summary to the clipboard, ready to send to the repository maintainer: the repository, branch and commit, then up to four sections, each shown only if it has entries:

- *Missing from .gitmodules*: paths that need an entry in the repository, with their pinned commit.
- *Failed to initialize*: the URL and the first `fatal:`/`error:` line of Git's message.
- *Skipped (uncommitted changes)*: rows left alone because they have uncommitted changes. Rows skipped because you declined the unreferenced-commit question are not listed.
- *Fixed locally in this checkout*: URL overrides and manual clones that the repository itself still needs fixing for.

Credentials embedded in URLs and token values are removed from the report. The window confirms with *"Report copied to the clipboard."*.

---

## Presets

Presets save a named folder selection for a repository and are shared between both tabs.

| Control | Action |
|---|---|
| **Preset…** dropdown | Choose a saved preset |
| **Load** | Ticks the preset's folders in the tree |
| **Save…** | Saves the current selection under a name |
| **⋯ → Rename / Delete** | Renames or deletes the selected preset |

Folders that don't exist in the current tree (for example on another branch) are skipped when loading.

---

## Settings

Open with **⚙** (top-right).

| Setting | Purpose |
|---|---|
| **Theme** | System, Light or Dark. Applies immediately. |
| **Folder name pattern** | How the Clone tab names new checkout folders. Tokens: `{repo}` repository name, `{branch}` the **New Branch** name if set, otherwise the selected branch, `{base}` always the selected branch. Default: `{repo}_{branch}`. |
| **Tree cache** | Shows the size of the cached folder structures and lets you clear them. |

---

## The generated script

A Clone script (`.bat`) looks like this:

```bat
@echo off
chcp 65001 >nul

git clone --filter=blob:none --no-checkout "https://gitlab.example.com/group/repo.git" "repo_feature-login"
if errorlevel 1 goto :failed

cd /d "repo_feature-login"
if errorlevel 1 goto :failed

git sparse-checkout init --cone
if errorlevel 1 goto :failed

git sparse-checkout set ^
  "apps/web" ^
  "libs/core"
if errorlevel 1 goto :failed

git checkout "main"
if errorlevel 1 goto :failed

git checkout -b "feature-login"
if errorlevel 1 goto :failed

rem …submodule step (only with "Initialize submodules")…

exit /b 0

:failed
echo FAILED — see the output above
pause
exit /b 1
```

| Step | What it does |
|---|---|
| `chcp 65001` | Lets the console handle non-ASCII paths and names (e.g. `Ā`, `ū`). |
| `git clone --filter=blob:none --no-checkout` | Downloads history and folder structure, but **no file contents**. |
| `git sparse-checkout init --cone` / `set …` | Limits the checkout to the selected folders. |
| `git checkout "<branch>"` | Checks out the branch, downloading **only** the files in your folders. |
| `git checkout -b "<new>"` | Optional: creates your local working branch. |
| `if errorlevel 1 goto :failed` | Stops at the first failing step and keeps the window open. |

**Submodule step** (with *Initialize submodules* ticked): every submodule listed in `.gitmodules` that lies inside your selected folders is updated **individually** (`git submodule update --init --remote --recursive -- <path>`), so a broken submodule can't stop the healthy ones. Submodules that exist in the repository but are missing from `.gitmodules` are reported with a `WARNING:` line. If any submodule failed, the script ends with *"Some submodules failed to initialize"* and exit code 2.

> **Note:** the script's submodule step uses `--remote`, i.e. the **latest commit on each submodule's branch**. To get the **pinned** commits instead, leave the box unticked and use the [Submodules window](#submodules-window) after cloning.

**Exit codes:** `0` success · `1` a step failed · `2` checkout created but some submodules failed.

The `.sh` version does the same with Bash syntax.

---

## Running tests

The tests live in `GitCheckoutManager.Tests` (xUnit). The integration tests use a real `git` from `PATH` and temporary folders, and skip themselves when git isn't found.

```
dotnet test
```

They also run in GitHub Actions on every pull request and push to `main`.

---

## Releasing

Releases are built by the `Release` workflow, which packs the app with Velopack and uploads it to a GitHub Release. Versions are `MAJOR.MINOR.PATCH` (for example `2.4.2`). Either way:

- **Push a tag:** `git tag v2.4.2 && git push origin v2.4.2`. The workflow builds the tagged commit.
- **Run it manually:** open **Actions → Release → Run workflow** and enter the version without the `v` (for example `2.4.2`). The workflow builds `main` and creates the `v2.4.2` tag together with the release.

The workflow fails early if the version isn't `MAJOR.MINOR.PATCH`, or (for manual runs) if the tag already exists.

---

## Where data is stored

| What | Location |
|---|---|
| Settings, recent checkouts, encrypted tokens | `%AppData%\GitCheckoutManager\settings.json` |
| Presets | `%AppData%\GitCheckoutManager\presets.json` |
| Cached folder structures | `%LocalAppData%\GitCheckoutManager\tree-cache\` (safe to delete; *Settings → Clear tree cache*) |

The app was previously called *Git Sparse Checkout Manager* (`GitSparseManager`). On first start, settings and presets are copied automatically from `%AppData%\GitSparseManager\` (the old folder is left untouched) and the old tree cache is deleted.

Tokens are encrypted with Windows DPAPI for your user account. They don't appear in scripts, cache folders or Git configuration.

---

## Tips and troubleshooting

- **The Clone tab shows no repositories:** check the host, server URL and token, and that the token has the scopes listed in [Creating a token](#creating-a-token).
- **"Folder already exists and isn't empty":** change the **Folder** name or **Clone into**, or remove the old folder.
- **The tree looks outdated:** click **Refresh tree**.
- **A folder won't go away after Apply:** another program has it open. Close it and click **Retry cleanup**.
- **A submodule fails with "repository not found":** its URL in `.gitmodules` is wrong, or you don't have access to that repository.
- **A submodule is "Missing from .gitmodules":** it can't be initialized until someone adds it to `.gitmodules` in the repository.
- **Large repositories:** the app only downloads folder structures and the files you select, so the size of the full repository doesn't matter.