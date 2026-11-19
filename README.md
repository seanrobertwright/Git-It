# Git It

Bookmark GitHub repositories from any computer into a private repo, then browse
them from the Windows tray.

- **`extension/`** is a Chrome extension (Manifest V3). It adds a **Git It!** button
  to GitHub repository pages. Clicking it commits the repo to `hits.json` in your
  bookmarks repo through the GitHub API, so no local clone is needed.
- **`tray/`** is a Windows tray app (.NET 11, WinForms). Left-click the tray icon to
  see your hits. Click a name to open it in the browser, or click **Copy link**.

## How the data is stored

`hits.json` in the bookmarks repo (default name `my-gits`), newest first:

```json
{
  "version": 1,
  "hits": [
    {
      "fullName": "owner/repo",
      "url": "https://github.com/owner/repo",
      "description": "Repo description at the time it was saved",
      "addedAt": "2026-11-19T15:04:05.000Z"
    }
  ]
}
```

Each **Git It!** click is one commit. The write includes the file's current sha, so
GitHub rejects it if another computer committed in between. The extension then
re-reads the file and retries. A repo already in the list, compared without regard
to case, is not added twice.

## Setup

1. Create the private bookmarks repo:

   ```sh
   gh repo create my-gits --private
   ```

   Both apps use `my-gits` unless you enter another name in the **Bookmarks repo**
   field in their settings.

2. Create a [fine-grained personal access token](https://github.com/settings/personal-access-tokens/new):
   - Repository access: **Only select repositories** → `my-gits`
   - Permissions: **Contents: Read and write**

   You can use one token for both apps, or give the tray app a separate read-only
   token.

3. Install the extension:
   - Open `chrome://extensions`, turn on **Developer mode**, click **Load unpacked**,
     and pick the `extension` folder.
   - The settings page opens on install. You can reopen it later by clicking the
     toolbar icon. Paste the token and click **Test and save**. Leave **Owner** blank
     to use the token's account.

4. Build and run the tray app (needs the .NET 11 SDK):

   ```sh
   cd tray
   dotnet publish -c Release -o publish
   publish/GitItTray.exe
   ```

   It asks for the token on first run. Right-click the tray icon for **Settings…**,
   **Start with Windows**, and **Exit**.

Repeat steps 3 and 4 on each computer.

## Where secrets live

- Extension: `chrome.storage.local` in that browser profile. It is not synced.
- Tray app: the token is in Windows Credential Manager under **Windows Credentials**
  as `GitIt.Tray:github-token`, stored for this computer only. Owner and repo name are
  in `%APPDATA%\GitIt\settings.json`.

## Tray app keys

- Type to filter by name or description. Press Enter to open the first match.
- Press Down to move into the list. Enter opens the selected repo and Ctrl+C copies
  its link.
- Esc hides the window.
