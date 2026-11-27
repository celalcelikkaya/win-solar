# Release checklist

Win Solar is not code-signed (see the spec, "Keeping Windows from getting in the way"), so every release follows
these steps. In-app updates depend on steps 5 and 6: the app only offers a release whose assets are named exactly
`WinSolarSetup-<version>.exe` and `SHA256SUMS.txt`, and it refuses an installer whose SHA-256 does not match.

1. **Version.** Set `<Version>` in `Directory.Build.props` (x.y.z, higher than the last release). Commit on a branch,
   merge to `main` after Celal's OK.
2. **Build.** `pwsh build\release.ps1`: runs the tests, publishes, builds `dist\WinSolarSetup-<version>.exe` and
   writes `dist\SHA256SUMS.txt`.
3. **Try it.** Install over the current version with the setup. Check: the tray icon, a switch (Light/Dark from the
   tray menu), Settings → About shows the new version.
4. **Defender.** Submit the setup at <https://www.microsoft.com/en-us/wdsi/filesubmission>. Choose "Software
   developer", product "Microsoft Defender Antivirus", detection "Incorrectly detected as malware/malicious". Also
   submit `%LOCALAPPDATA%\Programs\Win Solar\WinSolar.exe`. Publish after the result comes back clean, or note in the
   release that it is pending.
5. **Publish.** Tag `v<version>`. Upload both files:
   `gh release create v<version> dist\WinSolarSetup-<version>.exe dist\SHA256SUMS.txt --title "Win Solar <version>" --notes-file notes.md`.
   Put the SHA-256 line in the notes too. Never mark an update as a pre-release unless it should not reach users:
   the app skips pre-releases and drafts.
6. **Check the update.** On a PC with the previous version, open Settings → About → Check for updates. The row should
   say "Win Solar <version> is available", and "Restart to update" should come back on the new version.
