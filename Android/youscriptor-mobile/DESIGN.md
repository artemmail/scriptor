# Mobile visual design

The Compose interface follows the YouScriptor landing page: cream paper, dark olive,
lime actions, thin borders and italic serif display headings. The theme is intentionally
light, including system bars. It does not use wallpaper-derived dynamic colors.

## Source map

- `ui/theme/Theme.kt`: palette, typography and shapes.
- `ui/NoteIcons.kt`: rounded outline vectors for interface actions.
- `ui/DesignComponents.kt`: shared controls, cards, sync badges and decorative voice motif.
- `ui/NotesScreen.kt`: library, local search, filters, multiple selection and empty states.
- `ui/RecordScreen.kt`: microphone permission, timer, pause, save and discard confirmation.
- `ui/NoteDetailScreen.kt`: playback, title/transcript editing, sharing and deletion.
- `ui/SettingsScreen.kt`: account, quota, billing link and server configuration.
- `ui/DesignPreviews.kt`: sample library and selection previews, including 320 dp / 1.3 font scale.
- `res/mipmap-anydpi` and `res/mipmap-anydpi-v33`: adaptive launcher icon and themed icon.

Voice waves are decorative illustrations, not amplitude or playback progress meters.
Use the timer and explicit recording/playback state labels for current status.

## Interaction details

- Search matches the note title and available transcript locally.
- Long press starts selection; Back and the close action clear selection.
- Transcript edits are retained when background sync updates another field.
- Leaving a dirty editor or discarding a recording requires confirmation.
- Recording pauses when the app leaves the foreground; it does not run a background service.
- Sharing the editor text uses the current draft. Sharing one audio file does not change library selection.
- Server requests, local database schema and OAuth callback remain compatible with the existing backend.

## Build and review

Use JDK 17, Gradle 8.9 and Android SDK platform 35, then run:

```text
gradle assembleDebug lintDebug
```

The repository does not include a Gradle wrapper. Open this directory as a standalone
project in Android Studio to inspect Compose previews. Microphone, audio playback,
browser OAuth and live transcription should also be checked on an Android device.
