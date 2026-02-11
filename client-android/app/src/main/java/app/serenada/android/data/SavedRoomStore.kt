package app.serenada.android.data

import android.content.Context
import android.content.SharedPreferences
import org.json.JSONArray
import org.json.JSONObject

data class SavedRoom(
    val roomId: String,
    val name: String,
    val addedAt: Long = System.currentTimeMillis()
)

class SavedRoomStore(context: Context) {
    private val prefs: SharedPreferences =
        context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)

    fun saveRoom(room: SavedRoom) {
        val rooms = getSavedRooms().toMutableList()
        rooms.removeAll { it.roomId == room.roomId }
        rooms.add(0, room)
        persist(rooms)
    }

    fun getSavedRooms(): List<SavedRoom> {
        val raw = prefs.getString(KEY_ROOMS, null) ?: return emptyList()
        val parsed = runCatching { JSONArray(raw) }.getOrNull() ?: return emptyList()

        val rooms = mutableListOf<SavedRoom>()
        for (i in 0 until parsed.length()) {
            val item = parsed.optJSONObject(i) ?: continue
            val roomId = item.optString("roomId").orEmpty()
            val name = item.optString("name").orEmpty()
            val addedAt = item.optLong("addedAt", 0L)
            
            if (roomId.isNotBlank()) {
                rooms.add(SavedRoom(roomId, name, addedAt))
            }
        }
        return rooms
    }

    fun removeRoom(roomId: String) {
        val rooms = getSavedRooms().filterNot { it.roomId == roomId }
        persist(rooms)
    }

    fun renameRoom(roomId: String, newName: String) {
        val rooms = getSavedRooms().map {
            if (it.roomId == roomId) it.copy(name = newName) else it
        }
        persist(rooms)
    }

    private fun persist(rooms: List<SavedRoom>) {
        val json = JSONArray()
        rooms.forEach { room ->
            json.put(
                JSONObject().apply {
                    put("roomId", room.roomId)
                    put("name", room.name)
                    put("addedAt", room.addedAt)
                }
            )
        }
        prefs.edit().putString(KEY_ROOMS, json.toString()).apply()
    }

    companion object {
        private const val PREFS_NAME = "serenada_saved_rooms"
        private const val KEY_ROOMS = "rooms"
    }
}
