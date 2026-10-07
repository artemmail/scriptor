package com.youscriptor.mobile.ui

import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.navigation.NavType
import androidx.navigation.compose.*
import androidx.navigation.navArgument

@Composable
fun ScriptorMobileApp(viewModel: MainViewModel = viewModel()) {
    val navController = rememberNavController()
    val snackbar = remember { SnackbarHostState() }
    val notes by viewModel.notes.collectAsStateWithLifecycle()
    LaunchedEffect(viewModel) { viewModel.messages.collect { snackbar.showSnackbar(it) } }
    LaunchedEffect(viewModel, navController) {
        viewModel.loginRequests.collect {
            if (navController.currentDestination?.route != "login") {
                navController.navigate("login") { launchSingleTop = true }
            }
        }
    }
    val session by viewModel.session.collectAsStateWithLifecycle()
    LaunchedEffect(session) {
        if (session != null && navController.currentDestination?.route == "login") navController.popBackStack()
    }
    Scaffold(snackbarHost = { SnackbarHost(snackbar) }, contentWindowInsets = WindowInsets.safeDrawing) { padding ->
        NavHost(navController, startDestination = "notes", modifier = Modifier
            .padding(padding).consumeWindowInsets(padding)) {
            composable("notes") {
                NotesScreen(viewModel, { navController.navigate("record") },
                    { navController.navigate("settings") }, { navController.navigate("note/$it") })
            }
            composable("record") { RecordScreen(viewModel) { navController.popBackStack() } }
            composable("note/{noteId}", arguments = listOf(navArgument("noteId") { type = NavType.StringType })) { entry ->
                NoteDetailScreen(notes.firstOrNull { it.id == entry.arguments?.getString("noteId") },
                    viewModel) { navController.popBackStack() }
            }
            composable("settings") { SettingsScreen(viewModel) { navController.popBackStack() } }
            composable("login") { LoginScreen(viewModel) {
                viewModel.cancelPendingSync()
                navController.popBackStack()
            } }
        }
    }
}
