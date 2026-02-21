package app.serenada.android.data

import android.content.Context
import android.content.SharedPreferences
import org.json.JSONArray
import org.json.JSONObject

data class SavedRoom(
    val roomId: String,
    val name: String,
    val createdAt: Long
)

class SavedRoomStore(context: Context) {
    private val prefs: SharedPreferences =
        context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)

    fun saveRoom(room: SavedRoom) {
        if (!isValidRoomId(room.roomId)) return
        val cleanName = normalizeName(room.name) ?: return

        val rooms = getSavedRooms().toMutableList()
        rooms.removeAll { it.roomId == room.roomId }
        rooms.add(
            index = 0,
            element = room.copy(name = cleanName, createdAt = room.createdAt.coerceAtLeast(1L))
        )
        persist(rooms.take(MAX_SAVED_ROOMS))
    }

    fun getSavedRooms(): List<SavedRoom> {
        val raw = prefs.getString(KEY_ROOMS, null) ?: return emptyList()
        val parsed = runCatching { JSONArray(raw) }.getOrNull() ?: return emptyList()

        val rooms = mutableListOf<SavedRoom>()
        for (index in 0 until parsed.length()) {
            val item = parsed.optJSONObject(index) ?: continue
            val roomId = item.optString("roomId").orEmpty()
            if (!isValidRoomId(roomId)) continue

            val name = normalizeName(item.optString("name").orEmpty()) ?: continue
            val createdAt = item.optLong("createdAt", 0L).coerceAtLeast(1L)
            rooms.add(
                SavedRoom(
                    roomId = roomId,
                    name = name,
                    createdAt = createdAt
                )
            )
        }

        val deduped = rooms.distinctBy { it.roomId }.take(MAX_SAVED_ROOMS)
        if (deduped.size != rooms.size) {
            persist(deduped)
        }
        return deduped
    }

    fun removeRoom(roomId: String) {
        if (roomId.isBlank()) return
        val rooms = getSavedRooms()
        val filtered = rooms.filterNot { it.roomId == roomId }
        if (rooms.size == filtered.size) return
        persist(filtered)
    }

    private fun persist(rooms: List<SavedRoom>) {
        val json = JSONArray()
        rooms.forEach { room ->
            json.put(
                JSONObject().apply {
                    put("roomId", room.roomId)
                    put("name", room.name)
                    put("createdAt", room.createdAt)
                }
            )
        }
        prefs.edit().putString(KEY_ROOMS, json.toString()).apply()
    }

    companion object {
        private const val PREFS_NAME = "serenada_saved_rooms"
        private const val KEY_ROOMS = "entries"
        private const val MAX_SAVED_ROOMS = 50
        private const val MAX_ROOM_NAME_LENGTH = 120
        private val ROOM_ID_REGEX = Regex("^[A-Za-z0-9_-]{27}$")

        private fun normalizeName(name: String): String? {
            val trimmed = name.trim()
            if (trimmed.isBlank()) return null
            return trimmed.take(MAX_ROOM_NAME_LENGTH)
        }

        private fun isValidRoomId(roomId: String): Boolean = ROOM_ID_REGEX.matches(roomId)
    }
}
