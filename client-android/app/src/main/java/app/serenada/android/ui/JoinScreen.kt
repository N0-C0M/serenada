package app.serenada.android.ui

import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material.icons.filled.VideoCall
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExtendedFloatingActionButton
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.disabled
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import app.serenada.android.R
import app.serenada.android.data.RecentCall
import app.serenada.android.data.SavedRoom
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.time.format.FormatStyle
import java.util.Locale
import kotlinx.coroutines.delay

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun JoinScreen(
    isBusy: Boolean,
    statusMessage: String,
    recentCalls: List<RecentCall>,
    savedRooms: List<SavedRoom>,
    areSavedRoomsShownFirst: Boolean,
    roomStatuses: Map<String, Int>,
    onOpenJoinWithCode: () -> Unit,
    onOpenSettings: () -> Unit,
    onStartCall: () -> Unit,
    onJoinRecentCall: (String) -> Unit,
    onJoinSavedRoom: (SavedRoom) -> Unit,
    onRemoveRecentCall: (String) -> Unit,
    onSaveRoom: (String, String) -> Unit,
    onRemoveSavedRoom: (String) -> Unit
) {
    var showBusyOverlay by remember { mutableStateOf(false) }
    var saveDialogRoomId by remember { mutableStateOf<String?>(null) }
    var saveDialogName by remember { mutableStateOf("") }

    val savedRoomNameById = remember(savedRooms) {
        savedRooms.associate { it.roomId to it.name }
    }

    LaunchedEffect(isBusy) {
        if (!isBusy) {
            showBusyOverlay = false
            return@LaunchedEffect
        }
        showBusyOverlay = false
        delay(100)
        showBusyOverlay = true
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = {
                    Box(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(end = 16.dp)
                            .height(48.dp)
                            .clip(RoundedCornerShape(24.dp))
                            .background(MaterialTheme.colorScheme.surfaceVariant)
                            .clickable { onOpenJoinWithCode() }
                            .padding(horizontal = 16.dp),
                        contentAlignment = Alignment.CenterStart
                    ) {
                        Text(
                            text = stringResource(R.string.join_enter_code_or_link),
                            style = MaterialTheme.typography.bodyLarge,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }
                },
                actions = {
                    IconButton(onClick = onOpenSettings, enabled = !isBusy) {
                        Icon(
                            imageVector = Icons.Default.Settings,
                            contentDescription = stringResource(R.string.join_settings)
                        )
                    }
                }
            )
        }
    ) { paddingValues ->
        Box(
            modifier = Modifier
                .fillMaxSize()
                .padding(paddingValues)
        ) {
            val startCallEnabled = !isBusy
            Column(
                modifier = Modifier
                    .fillMaxSize()
                    .verticalScroll(rememberScrollState())
                    .padding(24.dp),
                horizontalAlignment = Alignment.CenterHorizontally
            ) {
                Spacer(modifier = Modifier.height(20.dp))
                Text(stringResource(R.string.app_name), fontSize = 40.sp, fontWeight = FontWeight.Bold)
                Spacer(modifier = Modifier.height(8.dp))
                Text(
                    text = stringResource(R.string.join_subtitle),
                    textAlign = TextAlign.Center
                )
                Spacer(modifier = Modifier.height(28.dp))

                val hasSavedRooms = savedRooms.isNotEmpty()
                val hasRecentCalls = recentCalls.isNotEmpty()
                if (hasSavedRooms || hasRecentCalls) {
                    Spacer(modifier = Modifier.height(36.dp))
                }

                if (areSavedRoomsShownFirst) {
                    if (hasSavedRooms) {
                        SavedRoomsSection(
                            rooms = savedRooms,
                            roomStatuses = roomStatuses,
                            isBusy = isBusy,
                            onJoinSavedRoom = onJoinSavedRoom,
                            onRenameSavedRoom = { roomId ->
                                saveDialogRoomId = roomId
                                saveDialogName = savedRoomNameById[roomId].orEmpty()
                            },
                            onRemoveSavedRoom = onRemoveSavedRoom
                        )
                        if (hasRecentCalls) Spacer(modifier = Modifier.height(16.dp))
                    }
                    if (hasRecentCalls) {
                        RecentCallsSection(
                            calls = recentCalls,
                            roomStatuses = roomStatuses,
                            savedRoomNameById = savedRoomNameById,
                            isBusy = isBusy,
                            onJoinRecentCall = onJoinRecentCall,
                            onSaveRecentCall = { roomId ->
                                saveDialogRoomId = roomId
                                saveDialogName = savedRoomNameById[roomId].orEmpty()
                            },
                            onRemoveRecentCall = onRemoveRecentCall
                        )
                    }
                } else {
                    if (hasRecentCalls) {
                        RecentCallsSection(
                            calls = recentCalls,
                            roomStatuses = roomStatuses,
                            savedRoomNameById = savedRoomNameById,
                            isBusy = isBusy,
                            onJoinRecentCall = onJoinRecentCall,
                            onSaveRecentCall = { roomId ->
                                saveDialogRoomId = roomId
                                saveDialogName = savedRoomNameById[roomId].orEmpty()
                            },
                            onRemoveRecentCall = onRemoveRecentCall
                        )
                        if (hasSavedRooms) Spacer(modifier = Modifier.height(16.dp))
                    }
                    if (hasSavedRooms) {
                        SavedRoomsSection(
                            rooms = savedRooms,
                            roomStatuses = roomStatuses,
                            isBusy = isBusy,
                            onJoinSavedRoom = onJoinSavedRoom,
                            onRenameSavedRoom = { roomId ->
                                saveDialogRoomId = roomId
                                saveDialogName = savedRoomNameById[roomId].orEmpty()
                            },
                            onRemoveSavedRoom = onRemoveSavedRoom
                        )
                    }
                }

                Spacer(modifier = Modifier.height(120.dp))
            }

            ExtendedFloatingActionButton(
                onClick = {
                    if (startCallEnabled) {
                        onStartCall()
                    }
                },
                expanded = true,
                icon = {
                    Icon(
                        imageVector = Icons.Default.VideoCall,
                        contentDescription = null
                    )
                },
                text = { Text(stringResource(R.string.join_start_call)) },
                modifier = Modifier
                    .align(Alignment.BottomEnd)
                    .padding(end = 20.dp, bottom = 20.dp)
                    .semantics {
                        if (!startCallEnabled) disabled()
                    },
                containerColor = MaterialTheme.colorScheme.primary,
                contentColor = MaterialTheme.colorScheme.onPrimary
            )

            if (showBusyOverlay) {
                Box(
                    modifier = Modifier
                        .fillMaxSize()
                        .background(Color.Black.copy(alpha = 0.24f))
                        .clickable(
                            interactionSource = remember { MutableInteractionSource() },
                            indication = null
                        ) { }
                        .padding(horizontal = 32.dp),
                    contentAlignment = Alignment.Center
                ) {
                    Surface(
                        shape = RoundedCornerShape(20.dp),
                        color = MaterialTheme.colorScheme.surface.copy(alpha = 0.97f)
                    ) {
                        Column(
                            modifier = Modifier
                                .padding(horizontal = 28.dp, vertical = 24.dp),
                            horizontalAlignment = Alignment.CenterHorizontally
                        ) {
                            CircularProgressIndicator()
                            if (statusMessage.isNotBlank()) {
                                Spacer(modifier = Modifier.height(12.dp))
                                Text(
                                    text = statusMessage,
                                    style = MaterialTheme.typography.bodyMedium
                                )
                            }
                        }
                    }
                }
            }
        }
    }

    val activeSaveDialogRoomId = saveDialogRoomId
    if (activeSaveDialogRoomId != null) {
        SaveRoomDialog(
            initialName = saveDialogName,
            isRenaming = !savedRoomNameById[activeSaveDialogRoomId].isNullOrBlank(),
            onDismiss = {
                saveDialogRoomId = null
                saveDialogName = ""
            },
            onConfirm = { name ->
                onSaveRoom(activeSaveDialogRoomId, name)
                saveDialogRoomId = null
                saveDialogName = ""
            }
        )
    }
}

@Composable
private fun RecentCallsSection(
    calls: List<RecentCall>,
    roomStatuses: Map<String, Int>,
    savedRoomNameById: Map<String, String>,
    isBusy: Boolean,
    onJoinRecentCall: (String) -> Unit,
    onSaveRecentCall: (String) -> Unit,
    onRemoveRecentCall: (String) -> Unit
) {
    val atText = stringResource(R.string.recent_calls_at)
    Surface(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(16.dp),
        color = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.4f)
    ) {
        Column(modifier = Modifier.fillMaxWidth()) {
            Text(
                text = stringResource(R.string.recent_calls_title),
                style = MaterialTheme.typography.labelLarge,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(horizontal = 16.dp, vertical = 14.dp)
            )
            HorizontalDivider(color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.45f))
            calls.forEachIndexed { index, call ->
                RecentCallRow(
                    call = call,
                    count = roomStatuses[call.roomId] ?: 0,
                    enabled = !isBusy,
                    atText = atText,
                    isSaved = savedRoomNameById.containsKey(call.roomId),
                    onClick = { onJoinRecentCall(call.roomId) },
                    onSave = { onSaveRecentCall(call.roomId) },
                    onRemove = { onRemoveRecentCall(call.roomId) }
                )
                if (index < calls.lastIndex) {
                    HorizontalDivider(
                        color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.35f),
                        modifier = Modifier.padding(start = 16.dp)
                    )
                }
            }
        }
    }
}

@Composable
private fun SavedRoomsSection(
    rooms: List<SavedRoom>,
    roomStatuses: Map<String, Int>,
    isBusy: Boolean,
    onJoinSavedRoom: (SavedRoom) -> Unit,
    onRenameSavedRoom: (String) -> Unit,
    onRemoveSavedRoom: (String) -> Unit
) {
    Surface(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(16.dp),
        color = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.4f)
    ) {
        Column(modifier = Modifier.fillMaxWidth()) {
            Text(
                text = stringResource(R.string.saved_rooms_title),
                style = MaterialTheme.typography.labelLarge,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(horizontal = 16.dp, vertical = 14.dp)
            )
            HorizontalDivider(color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.45f))
            rooms.forEachIndexed { index, room ->
                SavedRoomRow(
                    room = room,
                    count = roomStatuses[room.roomId] ?: 0,
                    enabled = !isBusy,
                    onClick = { onJoinSavedRoom(room) },
                    onRename = { onRenameSavedRoom(room.roomId) },
                    onRemove = { onRemoveSavedRoom(room.roomId) }
                )
                if (index < rooms.lastIndex) {
                    HorizontalDivider(
                        color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.35f),
                        modifier = Modifier.padding(start = 16.dp)
                    )
                }
            }
        }
    }
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun RecentCallRow(
    call: RecentCall,
    count: Int,
    enabled: Boolean,
    atText: String,
    isSaved: Boolean,
    onClick: () -> Unit,
    onSave: () -> Unit,
    onRemove: () -> Unit
) {
    var menuExpanded by remember { mutableStateOf(false) }

    Box(modifier = Modifier.fillMaxWidth()) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .combinedClickable(
                    enabled = enabled,
                    onClick = onClick,
                    onLongClick = { menuExpanded = true }
                )
                .padding(horizontal = 16.dp, vertical = 14.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            StatusDot(count = count)
            Spacer(modifier = Modifier.width(12.dp))
            Text(
                text = formatDateTime(call.startTime, atText),
                modifier = Modifier.weight(1f),
                style = MaterialTheme.typography.bodyMedium
            )
            Spacer(modifier = Modifier.width(12.dp))
            Text(
                text = formatDuration(call.durationSeconds),
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
        }

        DropdownMenu(
            expanded = menuExpanded,
            onDismissRequest = { menuExpanded = false }
        ) {
            DropdownMenuItem(
                text = {
                    Text(
                        if (isSaved) stringResource(R.string.saved_rooms_rename)
                        else stringResource(R.string.saved_rooms_save)
                    )
                },
                onClick = {
                    menuExpanded = false
                    onSave()
                }
            )
            DropdownMenuItem(
                text = { Text(stringResource(R.string.recent_calls_remove)) },
                onClick = {
                    menuExpanded = false
                    onRemove()
                }
            )
        }
    }
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun SavedRoomRow(
    room: SavedRoom,
    count: Int,
    enabled: Boolean,
    onClick: () -> Unit,
    onRename: () -> Unit,
    onRemove: () -> Unit
) {
    var menuExpanded by remember { mutableStateOf(false) }
    Box(modifier = Modifier.fillMaxWidth()) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .combinedClickable(
                    enabled = enabled,
                    onClick = onClick,
                    onLongClick = { menuExpanded = true }
                )
                .padding(horizontal = 16.dp, vertical = 14.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            StatusDot(count = count)
            Spacer(modifier = Modifier.width(12.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = room.name,
                    style = MaterialTheme.typography.bodyMedium
                )
                Spacer(modifier = Modifier.height(2.dp))
                Text(
                    text = room.roomId,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
        }

        DropdownMenu(
            expanded = menuExpanded,
            onDismissRequest = { menuExpanded = false }
        ) {
            DropdownMenuItem(
                text = { Text(stringResource(R.string.saved_rooms_rename)) },
                onClick = {
                    menuExpanded = false
                    onRename()
                }
            )
            DropdownMenuItem(
                text = { Text(stringResource(R.string.saved_rooms_remove)) },
                onClick = {
                    menuExpanded = false
                    onRemove()
                }
            )
        }
    }
}

@Composable
private fun SaveRoomDialog(
    initialName: String,
    isRenaming: Boolean,
    onDismiss: () -> Unit,
    onConfirm: (String) -> Unit
) {
    var value by remember(initialName) { mutableStateOf(initialName) }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text(
                text = if (isRenaming) {
                    stringResource(R.string.saved_rooms_dialog_title_rename)
                } else {
                    stringResource(R.string.saved_rooms_dialog_title_new)
                }
            )
        },
        text = {
            OutlinedTextField(
                value = value,
                onValueChange = { value = it },
                singleLine = true,
                label = { Text(stringResource(R.string.saved_rooms_name_label)) },
                placeholder = { Text(stringResource(R.string.saved_rooms_name_placeholder)) }
            )
        },
        confirmButton = {
            TextButton(
                onClick = { onConfirm(value) },
                enabled = value.trim().isNotEmpty()
            ) {
                Text(stringResource(R.string.settings_save))
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(stringResource(android.R.string.cancel))
            }
        }
    )
}

@Composable
private fun StatusDot(count: Int) {
    val color =
        when {
            count == 1 -> Color(0xFF3FB950)
            count >= 2 -> Color(0xFFD29922)
            else -> Color.Transparent
        }
    Box(
        modifier = Modifier
            .size(8.dp)
            .clip(RoundedCornerShape(50))
            .background(color)
    )
}

private fun formatDateTime(timestamp: Long, atText: String): String {
    val instant = Instant.ofEpochMilli(timestamp)
    val zonedDateTime = instant.atZone(ZoneId.systemDefault())
    val locale = Locale.getDefault()
    val date = DateTimeFormatter.ofPattern("MMM d", locale).format(zonedDateTime)
    val time = DateTimeFormatter.ofLocalizedTime(FormatStyle.SHORT).withLocale(locale).format(zonedDateTime)
    return "$date $atText $time"
}

private fun formatDuration(durationSeconds: Int): String {
    val seconds = durationSeconds.coerceAtLeast(0)
    if (seconds < 60) return "${seconds}s"
    val mins = seconds / 60
    val secs = seconds % 60
    return "${mins}m ${secs}s"
}
