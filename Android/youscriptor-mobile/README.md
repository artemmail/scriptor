# YouScriptor Mobile

Android MVP for local voice notes with optional sync to the existing `scriptor` backend.

## Implemented

- local audio note recording with `MediaRecorder`
- local note storage with Room
- edit/share/delete note flows
- browser-based Google/Yandex OAuth through backend deep links
- bearer auth + mobile refresh token flow
- note sync through existing `POST /api/OpenAiTranscription`
- polling of transcription status through `GET /api/OpenAiTranscription/{id}`
- subscription summary loading through `GET /api/Payments/subscription/summary`

## OAuth callback

The app uses the custom callback URI:

`youscriptor://auth/callback`

Backend mobile sign-in endpoints added for this:

- `GET /api/account/mobile/signin-google`
- `GET /api/account/mobile/signin-yandex`
- `GET /api/account/mobile/externallogincallback`
- `POST /api/account/mobile/refresh`
- `POST /api/account/mobile/logout`

## Server URL

Default API base URL:

`https://youscriptor.com/`

It can be changed inside the Settings screen.

## Open in Android Studio

Open the folder:

`Android/youscriptor-mobile`

as a standalone Gradle project.

## Notes

- The project skeleton is created manually in this repository.
- Gradle wrapper files are not generated in this commit.
- Build verification was not run here because Android SDK / Gradle environment was not validated in this workspace.
