package com.youscriptor.mobile.data.network

import okhttp3.HttpUrl.Companion.toHttpUrlOrNull

const val DEFAULT_SERVER_BASE_URL = "https://youscriptor.com/"

fun normalizeServerBaseUrl(value: String): String {
    val trimmed = value.trim().ifBlank { DEFAULT_SERVER_BASE_URL }
    val candidate = if (
        trimmed.startsWith("http://", ignoreCase = true) ||
        trimmed.startsWith("https://", ignoreCase = true)
    ) {
        trimmed
    } else {
        "https://$trimmed"
    }

    val parsed = candidate.toHttpUrlOrNull() ?: return DEFAULT_SERVER_BASE_URL
    return parsed.newBuilder()
        .encodedPath("/")
        .query(null)
        .fragment(null)
        .build()
        .toString()
}
