package com.youscriptor.mobile

import android.app.Application
import androidx.room.Room
import androidx.room.migration.Migration
import androidx.sqlite.db.SupportSQLiteDatabase
import com.youscriptor.mobile.data.local.NotesDatabase
import com.youscriptor.mobile.data.network.ServerApiProvider
import com.youscriptor.mobile.data.repository.AuthRepository
import com.youscriptor.mobile.data.repository.NotesRepository
import com.youscriptor.mobile.data.repository.SecureSettingsStore
import com.youscriptor.mobile.data.sync.SyncRepository

class ScriptorApplication : Application() {
    val container: AppContainer by lazy { AppContainer(this) }
}

class AppContainer(application: Application) {
    val settingsStore = SecureSettingsStore(application)

    val apiProvider = ServerApiProvider(settingsStore)

    val database: NotesDatabase = Room.databaseBuilder(
        application,
        NotesDatabase::class.java,
        "youscriptor-mobile.db"
    ).addMigrations(MIGRATION_1_2).build()

    val notesRepository = NotesRepository(database.noteDao())

    val authRepository = AuthRepository(
        application = application,
        settingsStore = settingsStore,
        apiProvider = apiProvider
    )

    val syncRepository = SyncRepository(
        application = application,
        notesRepository = notesRepository,
        authRepository = authRepository
    )

    companion object {
        private val MIGRATION_1_2 = object : Migration(1, 2) {
            override fun migrate(database: SupportSQLiteDatabase) {
                database.execSQL(
                    "ALTER TABLE notes ADD COLUMN isTitleCustomized INTEGER NOT NULL DEFAULT 0"
                )
            }
        }
    }
}
