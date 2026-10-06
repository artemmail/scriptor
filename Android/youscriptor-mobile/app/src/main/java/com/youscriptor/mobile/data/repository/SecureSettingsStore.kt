package com.youscriptor.mobile.data.repository

import android.content.Context
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey
import com.youscriptor.mobile.data.network.DEFAULT_SERVER_BASE_URL
import com.youscriptor.mobile.data.network.normalizeServerBaseUrl

class SecureSettingsStore(context: Context) {
    private val masterKey = MasterKey.Builder(context)
        .setKeyScheme(MasterKey.KeyScheme.AES256_GCM)
        .build()

    private val preferences = EncryptedSharedPreferences.create(
        context,
        "scriptor-mobile-secure",
        masterKey,
        EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
        EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM
    )

    var baseUrl: String
        get() = preferences.getString(KEY_BASE_URL, DEFAULT_BASE_URL) ?: DEFAULT_BASE_URL
        set(value) = preferences.edit().putString(KEY_BASE_URL, normalizeServerBaseUrl(value)).apply()

    var accessToken: String?
        get() = preferences.getString(KEY_ACCESS_TOKEN, null)
        set(value) = preferences.edit().putString(KEY_ACCESS_TOKEN, value).apply()

    var refreshToken: String?
        get() = preferences.getString(KEY_REFRESH_TOKEN, null)
        set(value) = preferences.edit().putString(KEY_REFRESH_TOKEN, value).apply()

    var userId: String?
        get() = preferences.getString(KEY_USER_ID, null)
        set(value) = preferences.edit().putString(KEY_USER_ID, value).apply()

    var userDisplayName: String?
        get() = preferences.getString(KEY_USER_DISPLAY_NAME, null)
        set(value) = preferences.edit().putString(KEY_USER_DISPLAY_NAME, value).apply()

    var userEmail: String?
        get() = preferences.getString(KEY_USER_EMAIL, null)
        set(value) = preferences.edit().putString(KEY_USER_EMAIL, value).apply()

    fun clearSession() {
        preferences.edit()
            .remove(KEY_ACCESS_TOKEN)
            .remove(KEY_REFRESH_TOKEN)
            .remove(KEY_USER_ID)
            .remove(KEY_USER_DISPLAY_NAME)
            .remove(KEY_USER_EMAIL)
            .apply()
    }

    companion object {
        private const val KEY_BASE_URL = "base_url"
        private const val KEY_ACCESS_TOKEN = "access_token"
        private const val KEY_REFRESH_TOKEN = "refresh_token"
        private const val KEY_USER_ID = "user_id"
        private const val KEY_USER_DISPLAY_NAME = "user_display_name"
        private const val KEY_USER_EMAIL = "user_email"

        const val DEFAULT_BASE_URL = DEFAULT_SERVER_BASE_URL
    }
}
