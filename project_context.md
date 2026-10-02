
# 📄 GitSparseManager — AI Build Context File

## 1. Project Overview

Build a **WPF desktop application (.NET 8)** that connects to **GitLab repositories**, visualizes repository structure, allows users to select folders/files, and generates **Git sparse-checkout Git commands or batch scripts**.

The application does NOT replace Git. It is a **visual Git sparse-checkout assistant**.

Primary goals:

* Browse GitLab repository structure visually
* Select folders/files via checkbox tree
* Generate Git sparse-checkout commands
* Copy/save batch scripts (.bat / .sh)
* Optional: execute Git commands locally

---

## 2. Tech Stack Requirements

* .NET 8 WPF Application (`net8.0-windows`)
* MVVM architecture
* CommunityToolkit.Mvvm (mandatory)
* HttpClient for GitLab API
* System.Text.Json
* No external UI frameworks (no Avalonia, no WinUI)
* Optional: Windows Credential Manager for token storage

---

## 3. Core Features

### 3.1 GitLab Integration

The app connects to GitLab using a Personal Access Token.

Must support:

* List user projects
* Select project
* List branches
* Load repository tree (files + folders)

GitLab REST API:

* `/projects`
* `/projects/{id}/repository/branches`
* `/projects/{id}/repository/tree?recursive=true&ref=branch`

Authentication:

* Header: `PRIVATE-TOKEN: <token>`

---

### 3.2 Repository Browser UI

Tree structure:

* Folder/file hierarchy
* Checkbox per node
* Expand/collapse nodes
* Search filter

Example:

```
☐ Assets
☑ Maps
   ☑ Desert
   ☐ Forest
   ☑ Snow
☐ Scripts
```

---

### 3.3 Selection System

Rules:

* Selecting folder selects all children
* Selecting child may partially select parent
* Maintain list of selected full paths:

Example:

```
Maps/Desert
Maps/Snow
```

---

### 3.4 Git Command Generation

Generate **Git sparse-checkout commands only**.

Example output:

```bat
git clone --filter=blob:none --no-checkout https://gitlab.com/group/repo.git

cd repo

git sparse-checkout init --cone

git sparse-checkout set ^
Maps/Desert ^
Maps/Snow

git checkout develop
```

Must support:

* Clone URL
* Branch selection
* Sparse paths
* Output format:

  * Copy to clipboard
  * Save `.bat`
  * Save `.sh` (optional future)

---

### 3.5 Optional Execution Mode

User can optionally execute generated commands locally.

Use:

* `System.Diagnostics.Process`

Must:

* Show console output
* Allow cancellation (future enhancement)

---

### 3.6 Tree Sources

The app builds its tree from one of two sources, depending on the active mode:

* **Clone mode** — `RemoteTreeService` fetches the tree straight from the remote repository (a blob-less `git fetch` plus `git ls-tree`) for the selected repository/branch, with no local checkout required. Results are cached on disk per repository/branch/commit.
* **Manage Checkout mode** — `CheckoutService` reads the tree from an already-cloned repository's current `HEAD`, plus its sparse-checkout state via `git sparse-checkout list`. Recently opened checkouts are remembered so the user can reopen one without browsing again.

---

## 4. Architecture (MANDATORY)

Use strict MVVM separation:

```
UI (Views)
   ↓
ViewModels
   ↓
Services
   ↓
GitLab API / Git CLI
```

---

### 4.1 Project Structure

```
GitSparseManager
│
├── Models
│   ├── Repository.cs
│   ├── Branch.cs
│   └── TreeNode.cs
│
├── ViewModels
│   ├── MainViewModel.cs
│   ├── TreeNodeViewModel.cs
│
├── Views
│   ├── MainWindow.xaml
│
├── Services
│   ├── RemoteTreeService.cs      (Clone mode: builds the tree from the remote repository)
│   ├── CheckoutService.cs        (Manage mode: builds the tree from an existing local checkout)
│   ├── CommandGenerator.cs
│   ├── SettingsService.cs
│   ├── ClipboardService.cs
│
└── App.xaml
```

---

## 5. Core Interfaces

### GitLab Service

```csharp
public interface IGitLabService
{
    Task<List<Project>> GetProjectsAsync();
    Task<List<Branch>> GetBranchesAsync(int projectId);
    Task<List<TreeNode>> GetRepositoryTreeAsync(int projectId, string branch);
}
```

---

### Command Generator

```csharp
public interface ICommandGenerator
{
    string GenerateCloneScript(
        string repoUrl,
        string branch,
        IEnumerable<string> sparsePaths,
        string targetFolder);
}
```

---

## 6. Tree Node Model

```csharp
public class TreeNode
{
    public string Name { get; set; }
    public string FullPath { get; set; }
    public bool IsFolder { get; set; }
    public bool IsChecked { get; set; }
    public ObservableCollection<TreeNode> Children { get; set; }
}
```

---

## 7. UI Requirements

Main window layout:

```
---------------------------------------------------
Repository: [ dropdown ]
Branch:     [ dropdown ]
Search:     [ textbox ]
---------------------------------------------------
Tree View (checkboxes)
---------------------------------------------------
Selected Paths Panel
---------------------------------------------------
Generated Git Script Panel
---------------------------------------------------
Buttons:
[Generate] [Copy] [Save .bat] [Execute]
```

---

## 8. Behavior Rules

* App must not modify Git repositories directly unless user clicks "Execute"
* Default behavior is to generate scripts only
* Must never require local clone for browsing
* Tree loads from GitLab API
* Must support large repositories efficiently

---

## 9. Performance Requirements

* Must support repositories with 10,000+ files
* Tree must be lazily built or efficiently constructed
* Search must be fast (in-memory filtering preferred)
* Avoid repeated API calls (cache results per branch)

---

## 10. Security Requirements

* GitLab token must not be stored in plain text
* Use Windows Credential Manager or DPAPI
* Never log tokens
* Never send token outside GitLab API calls

---

## 11. Future Extensions (DO NOT IMPLEMENT NOW)

* GitHub provider
* Azure DevOps provider
* Local Git provider
* Dependency system (auto-add required folders)
* Download size estimation
* GUI themes
* Plugin system for providers

---

## 12. Definition of Done (MVP)

The MVP is complete when:

* User can enter GitLab URL + token
* App loads repository tree
* User can select folders/files
* App generates correct sparse-checkout script
* Script can be copied or saved
* Branch selection works

---

## 13. Key Design Principle

This application is:

> A **visual configuration tool for Git sparse-checkout**, not a Git replacement.

---