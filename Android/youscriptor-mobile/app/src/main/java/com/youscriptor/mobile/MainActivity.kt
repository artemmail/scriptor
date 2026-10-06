package com.youscriptor.mobile

import android.content.Intent
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.SystemBarStyle
import android.graphics.Color
import androidx.activity.viewModels
import com.youscriptor.mobile.ui.MainViewModel
import com.youscriptor.mobile.ui.ScriptorMobileApp
import com.youscriptor.mobile.ui.theme.YouScriptorTheme

class MainActivity : ComponentActivity() {
    private val viewModel: MainViewModel by viewModels()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge(
            statusBarStyle = SystemBarStyle.light(Color.TRANSPARENT, Color.TRANSPARENT),
            navigationBarStyle = SystemBarStyle.light(Color.TRANSPARENT, Color.rgb(41, 51, 35))
        )
        handleAuthIntent(intent)

        setContent {
            YouScriptorTheme {
                ScriptorMobileApp(viewModel = viewModel)
            }
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        handleAuthIntent(intent)
    }

    private fun handleAuthIntent(intent: Intent?) {
        val uri = intent?.data ?: return
        if (!uri.toString().startsWith("youscriptor://auth/callback")) {
            return
        }

        viewModel.handleAuthCallback(uri)
        intent.data = null
    }
}
