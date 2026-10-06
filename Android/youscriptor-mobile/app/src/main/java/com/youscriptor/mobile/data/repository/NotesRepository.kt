package com.youscriptor.mobile.data.repository

import com.youscriptor.mobile.data.local.NoteDao
import com.youscriptor.mobile.data.local.NoteEntity
import kotlinx.coroutines.flow.Flow

class NotesRepository(
    private val noteDao: NoteDao
) {
    fun observeNotes(): Flow<List<NoteEntity>> = noteDao.observeAll()

    suspend fun getNote(id: String): NoteEntity? = noteDao.findById(id)

    suspend fun saveNote(note: NoteEntity) = noteDao.upsert(note)

    suspend fun deleteNote(id: String) = noteDao.deleteById(id)
}
