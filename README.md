# Git Sparse Checkout Manager — User Guide

Git Sparse Checkout Manager is a Windows desktop tool that helps you clone **only the folders and files you need** from a repository, instead of downloading the entire repo. It generates ready-to-run Git commands so you never have to write the sparse-checkout syntax by hand.

---

## How It Works — Overview

1. Scan a local folder to load the repository structure as a tree.
2. Tick the folders and files you want.
3. Click **Generate Script** to get the Git commands.
4. Copy, save, or run the script.

---

## The Interface at a Glance

| Area | What it does |
|---|---|
| **Mode Tabs** (top-left) | Switch between **Clone** and **Manage Checkout** modes |
| **Theme selector** (top-right) | Choose System, Light, or Dark theme |
| **Connection Bar** | Enter a GitLab URL and Personal Access Token to load repositories and branches from GitLab |
| **Repo / Branch Bar** | Set clone URL, branch (type to filter), optional new-branch name, target folder, and script options (submodules, CMD window behaviour) |
| **Scan Bar** | Scan a local folder to load the repository tree, or load a previously saved profile |
| **Preset Bar** | Save, load, rename, and delete named path selections for the current scan |
| **Repository Tree** (left panel) | Checkboxes to select folders and files; deep folders expand on demand |
| **Selected Paths** (right, top) | Live list of every path you have ticked |
| **Generated Script** (right, bottom) | The Git commands, ready to copy or run |
| **Action Buttons** (bottom) | Generate, Copy, Save .bat, Save .sh, Execute |
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

### Step 1 — Scan a Local Folder

1. Enter the path to a local folder (a full clone or any plain copy of the repository) in the **Scan Path** field, or click **Browse…** to pick one.
2. Click **Scan**. The app walks the folder tree (skipping the `.git` directory) and displays it in the **Repository Tree**. The first four levels are loaded immediately; deeper folders are expanded on demand when you open them in the tree.
3. A file named `sparse-checkout-cache.json` is automatically saved inside the scanned folder. You can reload it later without rescanning (see [Loading a Saved Profile](#loading-a-saved-profile)).

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
2. Enter the path to your local Git repository in the **Repo Path** field (the folder that contains `.git`).
3. Optionally fill in **Tree Source Path** to scan a different folder for the tree structure — useful when a separate full copy is available for browsing but you want to apply changes to a different repo.
4. Click **Scan**. The app reads the repo's current sparse-checkout paths with `git sparse-checkout list` and pre-ticks them in the tree.
5. Check or uncheck folders and files to reflect what you want materialised.
6. Choose an action:

| Button | Action |
|---|---|
| **Generate Script** | Produces a `manage-sparse-checkout.bat` in the script panel without touching the repo |
| **Save .bat** | Saves the manage script as a `.bat` file |
| **Save .sh** | Saves the manage script as a `.sh` file |
| **Apply** | Runs the changes directly: discards local changes in removed paths, cleans them from disk (`git restore` + `git clean -ffdx`), then runs `git sparse-checkout set` |

> **Apply** will permanently delete files from removed paths (including `node_modules` and other ignored files). A confirmation prompt lists how many paths will be removed before proceeding.

### What the manage script does

For paths being **removed**, the script:
1. Runs a dry-run (`git clean -ffdxn`) and pauses so you can review what would be deleted.
2. Restores tracked files (`git restore`) and hard-cleans the directory (`git clean -ffdx`).
3. Removes any remaining folder with `Remove-Item -Recurse -Force`.

For all selected paths it then runs `git sparse-checkout set` to update the repo's sparse-checkout config.

---

## Loading a Saved Profile

If you previously scanned a folder, a `sparse-checkout-cache.json` file was saved there.

Click **Load Profile**, select that JSON file, and the tree reloads instantly — no internet connection or rescan required.

If the original scan folder is still present on disk, lazy loading continues to work after loading a profile.

---

## Presets

Presets let you save and restore named path selections for a scan.

| Button | Action |
|---|---|
| **Save…** | Prompts for a name and saves the current selection as a preset |
| **Load** | Applies the selected preset's paths to the tree |
| **Rename** | Renames the selected preset |
| **Delete** | Deletes the selected preset |

Presets are stored per scan folder in `%AppData%\GitSparseManager\presets.json` and are available whenever you open or rescan the same folder. If a saved path no longer exists in the tree (e.g. after a rescan that picked up changes), it is silently skipped.

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
- Access to a GitLab instance with a Personal Access Token
- Git installed and available in your system PATH (required for **Execute Locally** and **Apply** in Manage Checkout mode)

---

## Tips

- **Large repositories**: use the **Search tree** filter to quickly find the paths you need. Deep folders load on demand when expanded.
- **Share with teammates**: scan once, save the cache JSON, and share it — teammates can use **Load Profile** to load the same tree without having a local copy of the repository.
- **Cross-platform**: save a `.sh` script from Windows, then run it on a Linux or macOS machine where Git is available.
- **Safe to re-run**: the generated `git clone` command will fail if the target folder already exists — delete or rename the folder before running the script again.
- **Token is saved**: your GitLab URL and Personal Access Token are encrypted and stored between sessions; you do not need to re-enter them each time you launch the app.
- **Reuse selections**: use Presets to save common path combinations and switch between them instantly.
