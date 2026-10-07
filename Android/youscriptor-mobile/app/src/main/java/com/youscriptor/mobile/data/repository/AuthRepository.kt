package com.youscriptor.mobile.data.repository

import android.app.Activity
import android.app.Application
import android.content.Intent
import android.net.Uri
import com.youscriptor.mobile.data.model.AuthProvider
import com.youscriptor.mobile.data.model.AuthSession
import com.youscriptor.mobile.data.network.MobileLogoutRequest
import com.youscriptor.mobile.data.network.MobileRefreshTokenRequest
import com.youscriptor.mobile.data.network.ServerApi
import com.youscriptor.mobile.data.network.ServerApiProvider
import com.youscriptor.mobile.data.network.normalizeServerBaseUrl
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import retrofit2.HttpException

class AuthRepository(
    private val application: Application,
    private val settingsStore: SecureSettingsStore,
    private val apiProvider: ServerApiProvider
) {
    private val _session = MutableStateFlow(loadSession())
    val session: StateFlow<AuthSession?> = _session.asStateFlow()

    fun updateBaseUrl(value: String) {
        settingsStore.baseUrl = value
    }

    fun currentBaseUrl(): String = settingsStore.baseUrl

    fun startLogin(activity: Activity, provider: AuthProvider) {
        val callback = Uri.encode(CALLBACK_URI)
        val normalizedBaseUrl = normalizeServerBaseUrl(currentBaseUrl()).trimEnd('/')
        val url = "$normalizedBaseUrl/api/account/mobile/signin-${provider.route}?redirectUri=$callback"
        val intent = Intent(Intent.ACTION_VIEW, Uri.parse(url))
        activity.startActivity(intent)
    }

    fun handleAuthCallback(uri: Uri): String? {
        val error = uri.getQueryParameter("error")
        if (!error.isNullOrBlank()) {
            return error
        }

        val accessToken = uri.getQueryParameter("token")
        val refreshToken = uri.getQueryParameter("refreshToken")
        val userId = uri.getQueryParameter("userId")
        val displayName = uri.getQueryParameter("displayName") ?: ""
        val email = uri.getQueryParameter("email") ?: ""

        if (accessToken.isNullOrBlank() || refreshToken.isNullOrBlank() || userId.isNullOrBlank()) {
            return "Auth callback does not contain required tokens."
        }

        saveSession(
            AuthSession(
                accessToken = accessToken,
                refreshToken = refreshToken,
                userId = userId,
                displayName = displayName,
                email = email
            )
        )

        return null
    }

    suspend fun refreshSession(): Boolean {
        val refreshToken = settingsStore.refreshToken ?: return false

        return try {
            val response = apiProvider.public().mobileRefresh(
                MobileRefreshTokenRequest(refreshToken = refreshToken)
            )

            saveSession(
                AuthSession(
                    accessToken = response.accessToken,
                    refreshToken = response.refreshToken,
                    userId = response.user.id,
                    displayName = response.user.displayName,
                    email = response.user.email
                )
            )
            true
        } catch (_: Exception) {
            clearSession()
            false
        }
    }

    suspend fun logout() {
        val refreshToken = settingsStore.refreshToken
        try {
            apiProvider.public().mobileLogout(MobileLogoutRequest(refreshToken))
        } catch (_: Exception) {
            // Best effort.
        }
        clearSession()
    }

    suspend fun <T> withAuthorizedApi(block: suspend (ServerApi) -> T): T {
        if (_session.value == null && !refreshSession()) {
            throw IllegalStateException("Для этого действия войдите в аккаунт.")
        }

        try {
            return block(apiProvider.authenticated())
        } catch (ex: HttpException) {
            if (ex.code() == 401 && refreshSession()) {
                return block(apiProvider.authenticated())
            }
            throw ex
        }
    }

    private fun saveSession(session: AuthSession) {
        settingsStore.accessToken = session.accessToken
        settingsStore.refreshToken = session.refreshToken
        settingsStore.userId = session.userId
        settingsStore.userDisplayName = session.displayName
        settingsStore.userEmail = session.email
        _session.value = session
    }

    private fun clearSession() {
        settingsStore.clearSession()
        _session.value = null
    }

    private fun loadSession(): AuthSession? {
        val accessToken = settingsStore.accessToken ?: return null
        val refreshToken = settingsStore.refreshToken ?: return null
        val userId = settingsStore.userId ?: return null
        return AuthSession(
            accessToken = accessToken,
            refreshToken = refreshToken,
            userId = userId,
            displayName = settingsStore.userDisplayName.orEmpty(),
            email = settingsStore.userEmail.orEmpty()
        )
    }

    companion object {
        const val CALLBACK_URI = "youscriptor://auth/callback"
    }
}
