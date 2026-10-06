package com.youscriptor.mobile.data.network

import com.google.gson.annotations.SerializedName
import com.youscriptor.mobile.data.repository.SecureSettingsStore
import okhttp3.Dns
import okhttp3.HttpUrl.Companion.toHttpUrl
import okhttp3.Interceptor
import okhttp3.MultipartBody
import okhttp3.OkHttpClient
import okhttp3.ResponseBody
import okhttp3.logging.HttpLoggingInterceptor
import okhttp3.dnsoverhttps.DnsOverHttps
import retrofit2.Response
import retrofit2.Retrofit
import retrofit2.converter.gson.GsonConverterFactory
import retrofit2.http.Body
import retrofit2.http.GET
import retrofit2.http.Multipart
import retrofit2.http.POST
import retrofit2.http.PUT
import retrofit2.http.Part
import retrofit2.http.Path
import java.net.InetAddress
import java.net.UnknownHostException

data class UserProfileDto(
    @SerializedName("id")
    val id: String,
    @SerializedName("email")
    val email: String,
    @SerializedName("displayName")
    val displayName: String
)

data class MobileAuthResultDto(
    @SerializedName("accessToken")
    val accessToken: String,
    @SerializedName("refreshToken")
    val refreshToken: String,
    @SerializedName("user")
    val user: UserProfileDto
)

data class MobileRefreshTokenRequest(
    @SerializedName("refreshToken")
    val refreshToken: String
)

data class MobileLogoutRequest(
    @SerializedName("refreshToken")
    val refreshToken: String?
)

data class OpenAiTaskDto(
    @SerializedName("id")
    val id: String,
    @SerializedName("title")
    val title: String?,
    @SerializedName("status")
    val status: Int?,
    @SerializedName("done")
    val done: Boolean,
    @SerializedName("error")
    val error: String?
)

data class OpenAiTaskDetailsDto(
    @SerializedName("id")
    val id: String,
    @SerializedName("title")
    val title: String?,
    @SerializedName("status")
    val status: Int?,
    @SerializedName("done")
    val done: Boolean,
    @SerializedName("error")
    val error: String?,
    @SerializedName("recognizedText")
    val recognizedText: String?,
    @SerializedName("processedText")
    val processedText: String?,
    @SerializedName("markdownText")
    val markdownText: String?
)

data class UpdateMarkdownRequest(
    @SerializedName("markdown")
    val markdown: String
)

data class UpdateTitleRequest(
    @SerializedName("title")
    val title: String
)

data class SubscriptionSummaryDto(
    @SerializedName("hasActiveSubscription")
    val hasActiveSubscription: Boolean,
    @SerializedName("hasLifetimeAccess")
    val hasLifetimeAccess: Boolean,
    @SerializedName("planName")
    val planName: String?,
    @SerializedName("remainingTranscriptionMinutes")
    val remainingTranscriptionMinutes: Int,
    @SerializedName("remainingVideos")
    val remainingVideos: Int,
    @SerializedName("billingUrl")
    val billingUrl: String
)

interface ServerApi {
    @POST("api/account/mobile/refresh")
    suspend fun mobileRefresh(@Body request: MobileRefreshTokenRequest): MobileAuthResultDto

    @POST("api/account/mobile/logout")
    suspend fun mobileLogout(@Body request: MobileLogoutRequest): Response<ResponseBody>

    @Multipart
    @POST("api/OpenAiTranscription")
    suspend fun uploadAudio(@Part file: MultipartBody.Part): OpenAiTaskDto

    @GET("api/OpenAiTranscription/{id}")
    suspend fun getTask(@Path("id") taskId: String): OpenAiTaskDetailsDto

    @PUT("api/OpenAiTranscription/{id}/markdown")
    suspend fun updateMarkdown(
        @Path("id") taskId: String,
        @Body request: UpdateMarkdownRequest
    ): Response<ResponseBody>

    @PUT("api/OpenAiTranscription/{id}/title")
    suspend fun updateTitle(
        @Path("id") taskId: String,
        @Body request: UpdateTitleRequest
    ): Response<ResponseBody>

    @GET("api/OpenAiTranscription/{id}/export/srt")
    suspend fun exportSrt(@Path("id") taskId: String): Response<ResponseBody>

    @GET("api/Payments/subscription/summary")
    suspend fun getSubscriptionSummary(): SubscriptionSummaryDto
}

class ServerApiProvider(
    private val settingsStore: SecureSettingsStore
) {
    @Volatile
    private var cachedBaseUrl: String = ""

    @Volatile
    private var authenticatedApi: ServerApi? = null

    @Volatile
    private var publicApi: ServerApi? = null

    private val resilientDns: Dns by lazy { buildResilientDns() }

    fun authenticated(): ServerApi = getOrCreate(authenticated = true)

    fun public(): ServerApi = getOrCreate(authenticated = false)

    @Synchronized
    private fun getOrCreate(authenticated: Boolean): ServerApi {
        val baseUrl = normalizeServerBaseUrl(settingsStore.baseUrl)
        if (baseUrl != cachedBaseUrl) {
            cachedBaseUrl = baseUrl
            authenticatedApi = null
            publicApi = null
        }

        val current = if (authenticated) authenticatedApi else publicApi
        if (current != null) {
            return current
        }

        val logging = HttpLoggingInterceptor().apply {
            level = HttpLoggingInterceptor.Level.BASIC
        }

        val clientBuilder = OkHttpClient.Builder()
            .addInterceptor(logging)
            .dns(resilientDns)

        if (authenticated) {
            clientBuilder.addInterceptor(AuthHeaderInterceptor(settingsStore))
        }

        val retrofit = Retrofit.Builder()
            .baseUrl(baseUrl)
            .client(clientBuilder.build())
            .addConverterFactory(GsonConverterFactory.create())
            .build()

        return retrofit.create(ServerApi::class.java).also {
            if (authenticated) {
                authenticatedApi = it
            } else {
                publicApi = it
            }
        }
    }
}

private class AuthHeaderInterceptor(
    private val settingsStore: SecureSettingsStore
) : Interceptor {
    override fun intercept(chain: Interceptor.Chain): okhttp3.Response {
        val token = settingsStore.accessToken
        val request = if (token.isNullOrBlank()) {
            chain.request()
        } else {
            chain.request().newBuilder()
                .header("Authorization", "Bearer $token")
                .build()
        }

        return chain.proceed(request)
    }
}

private fun buildResilientDns(): Dns {
    val bootstrapClient = OkHttpClient.Builder().build()
    val dohDns = DnsOverHttps.Builder()
        .client(bootstrapClient)
        .url("https://dns.google/dns-query".toHttpUrl())
        .bootstrapDnsHosts(
            InetAddress.getByName("8.8.8.8"),
            InetAddress.getByName("8.8.4.4")
        )
        .includeIPv6(false)
        .build()

    return FallbackDns(primary = Dns.SYSTEM, secondary = dohDns)
}

private class FallbackDns(
    private val primary: Dns,
    private val secondary: Dns
) : Dns {
    override fun lookup(hostname: String): List<InetAddress> {
        return try {
            primary.lookup(hostname)
        } catch (_: UnknownHostException) {
            secondary.lookup(hostname)
        }
    }
}
