package com.youscriptor.mobile.data.model

enum class NoteSyncState {
    LOCAL_ONLY,
    SYNC_PENDING,
    SYNCING,
    SYNCED,
    FAILED
}

enum class AuthProvider(val route: String, val label: String) {
    Google("google", "Google"),
    Yandex("yandex", "Yandex")
}

data class AuthSession(
    val accessToken: String,
    val refreshToken: String,
    val userId: String,
    val displayName: String,
    val email: String
)

data class RecordingDraft(
    val filePath: String,
    val durationSeconds: Long
)
