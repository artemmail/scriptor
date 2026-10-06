package com.youscriptor.mobile.data.local

import androidx.room.Dao
import androidx.room.Database
import androidx.room.Entity
import androidx.room.PrimaryKey
import androidx.room.Query
import androidx.room.RoomDatabase
import androidx.room.TypeConverter
import androidx.room.TypeConverters
import androidx.room.Upsert
import com.youscriptor.mobile.data.model.NoteSyncState
import kotlinx.coroutines.flow.Flow

@Entity(tableName = "notes")
data class NoteEntity(
    @PrimaryKey
    val id: String,
    val title: String,
    val createdAt: Long,
    val updatedAt: Long,
    val audioLocalPath: String,
    val durationSec: Long,
    val rawTranscript: String,
    val formattedTranscript: String,
    val userEditedText: String,
    val syncState: NoteSyncState,
    val remoteTaskId: String?,
    val remoteStatus: Int?,
    val lastSyncAt: Long?,
    val lastError: String?,
    val isTitleCustomized: Boolean
)

class NoteConverters {
    @TypeConverter
    fun fromSyncState(value: NoteSyncState): String = value.name

    @TypeConverter
    fun toSyncState(value: String): NoteSyncState = NoteSyncState.valueOf(value)
}

@Dao
interface NoteDao {
    @Query("SELECT * FROM notes ORDER BY createdAt DESC")
    fun observeAll(): Flow<List<NoteEntity>>

    @Query("SELECT * FROM notes WHERE id = :id LIMIT 1")
    suspend fun findById(id: String): NoteEntity?

    @Upsert
    suspend fun upsert(note: NoteEntity)

    @Query("DELETE FROM notes WHERE id = :id")
    suspend fun deleteById(id: String)
}

@Database(
    entities = [NoteEntity::class],
    version = 2,
    exportSchema = false
)
@TypeConverters(NoteConverters::class)
abstract class NotesDatabase : RoomDatabase() {
    abstract fun noteDao(): NoteDao
}
