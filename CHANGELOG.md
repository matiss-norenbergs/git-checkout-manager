# Changelog

All notable changes to this project are documented in this file. The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [2.8.0] - 2026-10-08

### Added
- Manage tab: "Show only checked-out paths" filter for the folder tree (remembered between sessions) (#33).
- Submodules window: a "Filter by path…" search box (Ctrl+F; comma-separated terms), a column header row with a select-all checkbox for the shown rows, and a narrower URL column. The selection count mentions ticked rows hidden by the search or filters, and the redundant "Clear selection" menu item is gone (#31).

### Fixed
- Disabled checkboxes now look disabled.

## [2.7.0] - 2026-10-06

### Added
- Manage tab: switch the open checkout to another branch from the branch name in the status line. It checks for pending folder changes, local changes and an unreferenced detached HEAD first, and works in single-branch clones (#25).
- Manage tab: a read-only "Local changes" window, opened from the "N local changes" count, lists changed files grouped as Conflicted, Staged, Modified, Deleted, Renamed and Untracked (#24).
- Manage tab: open the checkout or a folder in Explorer or VS Code from the "Open" menu in the checkout bar, the tree context menu and Submodules row actions (#23).

## [2.6.0] - 2026-10-05

### Added
- The app checks for updates every 6 hours while it is open, and "Check for updates" in Settings reports correctly when a check is already running (#18).

### Fixed
- Folder tree: clicking a partly selected folder now selects the whole folder; the partial state can no longer be set by hand (#17).
- Disabled menu items now look disabled and no longer highlight on hover; the Submodules selection menu explains why an item is unavailable (#16).

## [2.5.0] - 2026-10-05

### Added
- Submodules window: bulk Pull, Switch branch and Reset to recorded commit for ticked submodules; Pull all when nothing is ticked.

### Changed
- The main and Submodules windows open larger, limited to the screen's work area.

## [2.4.2] - 2026-10-05

### Fixed
- Submodules window: the "pinned commit" / "latest from branch" radio buttons now follow the saved setting.

## [2.4.1] - 2026-10-04

### Added
- Folder tree search: Enter / Shift+Enter jump to the next / previous match, with an "n / N" counter.

### Fixed
- Manage script cleanup no longer breaks on folder names containing `'`, `[` or `]`.
- Switching tabs no longer hides the loading overlay early, and errors from opening a checkout stay on the Manage tab.
- Submodules refused for safety now show as "Skipped" and get their own section in the report.

## [2.4.0] - 2026-10-04

### Added
- Repository and branch pickers are searchable: type to filter, Up/Down/Enter to pick, Esc to cancel.

## [2.3.1] - 2026-10-04

### Fixed
- The Clone and Manage tabs each keep their own tree, ticks, search, expansion and status; switching tabs no longer shows the other tab's folders or loses an open checkout and its pending changes.

## [2.3.0] - 2026-10-04

### Added
- After a script finishes with missing submodules, a bar offers "Review submodules", which opens the Submodules window filtered to problems.
- Submodules window: nested submodules are listed (up to depth 5) with actions applied to the repository that contains them.
- Submodules window: "Copy report" copies a plain-text summary of missing, failed and locally fixed submodules, with credentials removed from URLs.
- Submodules window: "Switch branch…" for populated submodules (never discards commits) and "Reset to recorded commit"; rows show the current branch or detached HEAD and a note when moved from the recorded commit.

### Changed
- Message dialogs are themed (Enter/Esc supported, optional details box), and permanent actions use red danger buttons.
- Disabled buttons look disabled, and the Submodules list no longer shows a dashed focus rectangle.

### Fixed
- "Initialize selected" refuses dirty populated submodules and asks before leaving a commit that is on no branch.

### Security
- The submodule report removes tokens from URL query strings and only the value of auth headers.

## [2.2.0] - 2026-10-03

### Added
- Submodules window: repair a submodule locally with "Set URL…" (pick the correct repository, kept in `.git/config` only), "Reset URL", and "Clone manually…" for submodules missing from `.gitmodules`; new "Cloned manually" state and "URL overridden locally" label.

### Changed
- Submodule rows use shared column widths so all columns line up.

## [2.1.1] - 2026-10-03

### Changed
- Loading overlay is clearer: stronger backdrop, spinner and current status text.
- Destination shows muted "Select a repository/branch" hints instead of a red error.
- Generated folder names wait until all pattern tokens are known, and separators are trimmed and collapsed.

## [2.1.0] - 2026-10-03

### Added
- Full clone mode on the Clone tab: clone the whole repository without sparse checkout (tree checkboxes are hidden in this mode).

### Changed
- The app is now called Git Checkout Manager; existing data from GitSparseManager is migrated on first start.
- The Clone tab's tree panel is wider by default, and the splitter position is remembered.
- Improved folder tree search.

## [2.0.2] - 2026-10-03

### Added
- Manage tab: "Disable sparse checkout" button.

### Fixed
- Manage no longer deletes kept subfolders when a fully selected folder is narrowed (e.g. "apps" → keep only "apps/web"); only the unselected subfolders are cleaned or removed.

## [2.0.1] - 2026-10-03

### Changed
- Releases are distributed through GitHub Releases.

## [2.0.0] - 2026-10-03

First tagged release, as Git Sparse Checkout Manager (GitSparseManager).

### Added
- Sparse checkouts of part of a GitLab (including self-hosted) or GitHub repository: pick repository, branch and folders from a folder tree loaded without downloading file contents.
- Clone tab generates a `.bat` or `.sh` script to run or save; Manage Checkout tab adds or removes folders of an existing checkout.
- Submodule management window, presets, themes and settings.
- Windows installer with self-updates (Velopack).

[Unreleased]: https://github.com/matiss-norenbergs/git-checkout-manager/compare/v2.8.0...HEAD
[2.8.0]: https://github.com/matiss-norenbergs/git-checkout-manager/compare/v2.7.0...v2.8.0
[2.7.0]: https://github.com/matiss-norenbergs/git-checkout-manager/compare/v2.6.0...v2.7.0
[2.6.0]: https://github.com/matiss-norenbergs/git-checkout-manager/compare/v2.5.0...v2.6.0
[2.5.0]: https://github.com/matiss-norenbergs/git-checkout-manager/compare/v2.4.2...v2.5.0
[2.4.2]: https://github.com/matiss-norenbergs/git-checkout-manager/compare/v2.4.1...v2.4.2
[2.4.1]: https://github.com/matiss-norenbergs/git-checkout-manager/compare/v2.4.0...v2.4.1
[2.4.0]: https://github.com/matiss-norenbergs/git-checkout-manager/compare/v2.3.1...v2.4.0
[2.3.1]: https://github.com/matiss-norenbergs/git-checkout-manager/compare/v2.3.0...v2.3.1
[2.3.0]: https://github.com/matiss-norenbergs/git-checkout-manager/compare/v2.2.0...v2.3.0
[2.2.0]: https://github.com/matiss-norenbergs/git-checkout-manager/compare/v2.1.1...v2.2.0
[2.1.1]: https://github.com/matiss-norenbergs/git-checkout-manager/compare/v2.1.0...v2.1.1
[2.1.0]: https://github.com/matiss-norenbergs/git-checkout-manager/compare/v2.0.2...v2.1.0
[2.0.2]: https://github.com/matiss-norenbergs/git-checkout-manager/compare/v2.0.1...v2.0.2
[2.0.1]: https://github.com/matiss-norenbergs/git-checkout-manager/compare/v2.0.0...v2.0.1
[2.0.0]: https://github.com/matiss-norenbergs/git-checkout-manager/releases/tag/v2.0.0
