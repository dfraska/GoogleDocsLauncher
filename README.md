# Google Docs Launcher

A small Windows utility that opens local `.docx` files in Google Docs with a double-click, using Google Drive for desktop's **Open with Google Docs** Explorer command.

Newly copied files sometimes take a while to become available to Google Drive. The launcher waits until the command is ready and shows a cancelable dialog in the meantime.

## Requirements

- Windows
- Google Drive for desktop, with your documents in its synced folder
- .NET SDK to build the application

## Setup

1. Clone the repository and build/publish the project (for example, with `dotnet publish -c Release`).
2. The default `config.json` should work for most users. Edit the Google Drive folder path only if yours is different.
3. In File Explorer, right-click a `.docx` file and choose **Open with → Choose another app**. Browse to `GoogleDocsLauncher.exe` and set it as the default app for `.docx` files.

Now double-clicking a `.docx` file inside the configured Google Drive folder opens it in Google Docs. Files outside that folder are rejected rather than uploaded automatically.

## Configuration

```json
{
  "googleDriveRoot": "%USERPROFILE%\\My Drive",
  "retryIntervalMs": 250
}
```

`retryIntervalMs` controls how often the launcher checks whether Google Drive is ready. The waiting dialog can be canceled at any time.

## Notes

This utility relies on Google Drive for desktop's Windows Explorer context-menu integration. It is not affiliated with Google.
