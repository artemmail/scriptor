package com.youscriptor.mobile.data.network

import retrofit2.HttpException
import java.io.IOException
import java.net.SocketTimeoutException
import java.net.UnknownHostException
import javax.net.ssl.SSLException

fun Throwable.toUserFacingMessage(): String {
    val root = rootCause()
    return when (root) {
        is UnknownHostException -> {
            val host = HOST_REGEX.find(root.message.orEmpty())?.groupValues?.getOrNull(1)
            if (host.isNullOrBlank()) {
                "Cannot resolve the server host. Check Private DNS, VPN/ad blocker, or reset Server base URL in Settings."
            } else {
                "Cannot resolve $host. Check Private DNS, VPN/ad blocker, or reset Server base URL in Settings."
            }
        }

        is SocketTimeoutException -> "Server timeout. Check the connection and try again."
        is SSLException -> "Secure connection to the server failed. Check the device date/time and try again."
        is HttpException -> "Server returned HTTP ${root.code()}."
        is IOException -> root.message ?: "Network request failed."
        else -> message ?: root.message ?: "Request failed."
    }
}

private tailrec fun Throwable.rootCause(): Throwable =
    if (cause != null && cause !== this) {
        cause!!.rootCause()
    } else {
        this
    }

private val HOST_REGEX = Regex("""Unable to resolve host "?([^":\s]+)""")
