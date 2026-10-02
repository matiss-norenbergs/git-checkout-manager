# Git Sparse Checkout Manager — User Guide

Git Sparse Checkout Manager is a Windows desktop tool that helps you clone **only the folders and files you need** from a repository, instead of downloading the entire repo. It generates ready-to-run Git commands so you never have to write the sparse-checkout syntax by hand.

---

## How It Works — Overview

**Clone mode** builds its tree from the remote repository (GitLab or GitHub) — no local copy needed:

1. Connect to your Git host and pick a repository and branch. The tree loads automatically.
2. Tick the folders you want.
3. Click **Generate Script** to get the Git commands.
4. Copy, save, or run the script.

**Manage Checkout mode** builds its tree from an existing local clone's current commit:

1. Open a checkout (browse to it, or pick one from the recent-checkouts list).
2. Tick or untick folders to change what should be materialised on disk.
3. Review the pending changes, then generate a script or apply them directly.

---

## The Interface at a Glance

| Area | What it does |
|---|---|
| **Mode Tabs** (top-left) | Switch between **Clone** and **Manage Checkout** modes |
| **Theme selector** (top-right) | Choose System, Light, or Dark theme |
| **Connection Bar** (Clone) | Enter a GitLab/GitHub URL and Personal Access Token to load repositories and branches |
| **Repo / Branch Bar** (Clone) | Pick the repository and branch (type to filter), optional new-branch name, target folder, and script options (submodules, CMD window behaviour) |
| **Checkout Bar** (Manage) | Open a Git checkout by browsing to it or picking a recent one; shows a summary of its remote, branch, and local changes |
| **Preset Bar** | Save, load, rename, and delete named path selections, shared between Clone and Manage for the same repository |
| **Repository Tree** (left panel) | Checkboxes to select folders; the full tree is loaded up front |
| **Selected Paths** (Clone, right, top) | Live list of every path you have ticked |
| **Pending Changes** (Manage, right, top) | Folders that will be added or removed relative to the checkout's current sparse state |
| **Generated Script** (right, bottom) | The Git commands, ready to copy or run |
| **Action Buttons** (bottom) | Generate, Copy, Save .bat, Save .sh, Execute (Clone) / Apply Now (Manage) |
| **Status Bar** (very bottom) | Progress messages and error details |

---

## Modes

The app has two modes, selectable via the radio buttons at the top of the window.

| Mode | Purpose |
|---|---|
| **Clone** | Generate a script that clones a new sparse copy of a repository (default) |
| **Manage Checkout** | Update the sparse-checkout paths of an existing full local clone |

---

## Main Workflow

### Connecting to GitLab

A GitLab connection is required to use the app. Enter your GitLab URL and a Personal Access Token in the **Connection Bar** and click **Connect**. All other controls are disabled until the connection succeeds.

**How to create a token:**
1. In GitLab, open your profile menu (top-right avatar) and go to **Edit profile → Access Tokens**.
2. Click **Add new token**, give it a name, and optionally set an expiry date.
3. Under **Scopes**, tick **`read_api`** only — no other permissions are needed.
4. Click **Create personal access token** and copy the value immediately. GitLab will not show it again.

The token is encrypted with DPAPI and saved locally; you only need to enter it once.

---

### Step 1 — Pick a Repository and Branch

Once connected, choose a **Repository** and **Branch**. The app fetches the full folder tree for that branch straight from the server (using a blob-less fetch, so file contents are never downloaded) and displays it in the **Repository Tree**. The tree is cached locally per repository/branch/commit; use **Refresh tree** to re-fetch it, or **Clear tree cache** to drop everything cached.

### Step 2 — Set Script Options (Optional)

In the **Repo / Branch Bar**, you can fill in details that will be written into the generated script:

| Field | Purpose |
|---|---|
| **Repository** | The clone URL to use in the script (populated automatically if you connected to GitLab) |
| **Branch** | The branch to check out. Type in the field to filter the list when connected to GitLab |
| **New Branch** | If filled in, the script appends `git checkout -b <name>` to create a new local branch |
| **Target Folder** | Local path to clone into. Leave blank to use the repository name |
| **Initialize submodules** | Appends a `git submodule update --init --remote --recursive` call for each detected submodule |
| **Keep CMD window open** | Adds `pause` at the end of the `.bat` script so the Command Prompt stays open after the script finishes |

### Step 3 — Browse and Select

- **Check a folder** to select it and all its contents.
- **Uncheck a folder** to deselect everything inside it.
- Checking an individual file selects only that file; the parent folder shows a partial (indeterminate) state.
- Use the **Search tree** box to filter visible nodes by name — useful for large repositories.

### Step 4 — Generate and Export

1. Click **Generate Script**. The **Selected Paths** and **Generated Script** panels populate on the right.
2. Choose what to do with the script:

| Button | Action |
|---|---|
| **Generate Script** | Regenerates the script from current selections |
| **Copy to Clipboard** | Copies the script text so you can paste it into a terminal |
| **Save .bat** | Saves a Windows batch file (`.bat`) you can double-click or run in Command Prompt |
| **Save .sh** | Saves a Bash shell script (`.sh`) for use on Linux / macOS / Git Bash |
| **Execute Locally** | Prompts you to pick a parent folder, then runs the batch script directly on this machine in a Command Prompt window |

> **Execute Locally** asks you to choose a parent folder and then asks for confirmation before running. Ensure Git is installed and accessible in your PATH.

---

## Manage Checkout Mode

Use **Manage Checkout** when you already have a full local clone and want to change which folders are materialised on disk without re-cloning.

### How it works

1. Switch to **Manage Checkout** using the mode tabs at the top.
2. Click **Browse…** and pick a folder inside a Git checkout (the app finds the repository root for you), or pick a recently opened checkout from the dropdown — the most recently used one opens automatically when you switch to this mode.
3. The app reads the checkout's HEAD tree and its current sparse-checkout paths (via `git sparse-checkout list`) and pre-ticks them in the tree. If the checkout is a full (non-sparse) clone, every root folder starts ticked.
4. Check or uncheck folders to reflect what you want materialised. The **Pending Changes** panel shows what will be added and removed.
5. Choose an action:

| Button | Action |
|---|---|
| **Generate Script** | Produces a `manage-sparse-checkout.bat` in the script panel without touching the repo |
| **Save .bat** | Saves the manage script as a `.bat` file |
| **Save .sh** | Saves the manage script as a `.sh` file |
| **Apply Now** | Runs the changes directly in the checkout: restores and cleans removed folders (reviewing what would be deleted first, with an option to keep changed/untracked files), then runs `git sparse-checkout set`/`add` to materialise the added ones |

> **Apply Now** can permanently delete files from removed folders. When any such files exist, a review dialog lists them so you can choose what to keep before confirming.

### What the manage script does

For paths being **removed**, the script:
1. Runs a dry-run (`git clean -ffdxn`) and pauses so you can review what would be deleted.
2. Restores tracked files (`git restore`) and hard-cleans the directory (`git clean -ffdx`).
3. Removes any remaining folder with `Remove-Item -Recurse -Force`.

For all selected paths it then runs `git sparse-checkout set` to update the repo's sparse-checkout config.

---

## Presets

Presets let you save and restore named path selections, shared between Clone and Manage for the same repository.

| Button | Action |
|---|---|
| **Save…** | Prompts for a name and saves the current selection as a preset |
| **Load** | Applies the selected preset's paths to the tree |
| **Rename** | Renames the selected preset |
| **Delete** | Deletes the selected preset |

Presets are stored per repository in `%AppData%\GitSparseManager\presets.json`, keyed by the repository's remote URL (or by its local path when there is no remote), so the same presets show up in Clone and Manage mode for that repository. If a saved path no longer exists in the tree, it is silently skipped.

---

## Understanding the Generated Script

The script uses Git's sparse-checkout "cone mode" for best performance. Example output:

```bat
@echo off

git clone --filter=blob:none --no-checkout "https://gitlab.com/group/repo.git" "MyProject"

cd /d "MyProject"

git sparse-checkout init --cone

git sparse-checkout set ^
  "Assets/Maps/Desert" ^
  "Assets/Maps/Snow"

git checkout main

git checkout -b my-feature-branch
```

| Line | Explanation |
|---|---|
| `git clone --filter=blob:none --no-checkout` | Clones the repository metadata only, skipping all file content until checkout |
| `cd /d "…"` | Changes into the cloned folder |
| `git sparse-checkout init --cone` | Enables cone-mode sparse checkout |
| `git sparse-checkout set …` | Declares which folders/files to materialise on disk |
| `git checkout <branch>` | Checks out the selected branch, downloading only the declared paths |
| `git checkout -b <name>` | (Optional) Creates a new local branch from this point |

---

## Theme

A **Theme** dropdown in the top-right corner lets you choose:

| Option | Behaviour |
|---|---|
| **System** | Follows the Windows dark/light mode setting automatically |
| **Light** | Always uses the light theme |
| **Dark** | Always uses the dark theme |

The title bar colour updates to match the selected theme. Your choice is saved and restored on the next launch.

---

## Requirements

- Windows 10 or later
- .NET 8 Desktop Runtime
- Access to a GitLab or GitHub instance with a Personal Access Token
- Git installed and available in your system PATH (required for **Execute Locally** in Clone mode and **Apply Now** in Manage Checkout mode)

---

## Tips

- **Large repositories**: use the **Search tree** filter to quickly find the paths you need.
- **Cross-platform**: save a `.sh` script from Windows, then run it on a Linux or macOS machine where Git is available.
- **Safe to re-run**: the generated `git clone` command will fail if the target folder already exists — delete or rename the folder before running the script again.
- **Token is saved**: your server URL and Personal Access Token are encrypted and stored between sessions; you do not need to re-enter them each time you launch the app.
- **Reuse selections**: use Presets to save common path combinations and switch between them instantly.
