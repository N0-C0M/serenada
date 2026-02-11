package app.serenada.android.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.ExperimentalFoundationApi
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
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExtendedFloatingActionButton
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TextField
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.semantics.disabled
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
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
    roomStatuses: Map<String, Int>,
    onOpenJoinWithCode: () -> Unit,
    onOpenSettings: () -> Unit,
    onStartCall: () -> Unit,
    onJoinRecentCall: (String) -> Unit,
    onRemoveRecentCall: (String) -> Unit,
    onSaveRecentCall: (String, String) -> Unit,
    onJoinSavedRoom: (String) -> Unit,
    onRemoveSavedRoom: (String) -> Unit,
    onRenameSavedRoom: (String, String) -> Unit
) {
    var showBusyOverlay by remember { mutableStateOf(false) }
    var showSaveDialog by remember { mutableStateOf<String?>(null) }
    var showRenameDialog by remember { mutableStateOf<SavedRoom?>(null) }

    LaunchedEffect(isBusy) {
        if (!isBusy) {
            showBusyOverlay = false
            return@LaunchedEffect
        }
        showBusyOverlay = false
        delay(100)
        showBusyOverlay = true
    }

    showSaveDialog?.let {
        SaveRoomDialog(
            roomId = it,
            onDismiss = { showSaveDialog = null },
            onSave = { roomId, name -> onSaveRecentCall(roomId, name) }
        )
    }

    showRenameDialog?.let {
        RenameRoomDialog(
            room = it,
            onDismiss = { showRenameDialog = null },
            onRename = { roomId, newName -> onRenameSavedRoom(roomId, newName) }
        )
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

                if (savedRooms.isNotEmpty()) {
                    Spacer(modifier = Modifier.height(36.dp))
                    SavedRoomsSection(
                        rooms = savedRooms,
                        roomStatuses = roomStatuses,
                        isBusy = isBusy,
                        onJoin = onJoinSavedRoom,
                        onRename = { showRenameDialog = it },
                        onRemove = onRemoveSavedRoom
                    )
                }

                if (recentCalls.isNotEmpty()) {
                    Spacer(modifier = Modifier.height(36.dp))
                    RecentCallsSection(
                        calls = recentCalls,
                        roomStatuses = roomStatuses,
                        isBusy = isBusy,
                        onJoin = onJoinRecentCall,
                        onRemove = onRemoveRecentCall,
                        onSave = { showSaveDialog = it }
                    )
                }

                // Keep content clear of the floating action button.
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
}

@Composable
private fun RecentCallsSection(
    calls: List<RecentCall>,
    roomStatuses: Map<String, Int>,
    isBusy: Boolean,
    onJoin: (String) -> Unit,
    onRemove: (String) -> Unit,
    onSave: (String) -> Unit
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
                    onClick = { onJoin(call.roomId) },
                    onRemove = { onRemove(call.roomId) },
                    onSave = { onSave(call.roomId) }
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
    onJoin: (String) -> Unit,
    onRename: (SavedRoom) -> Unit,
    onRemove: (String) -> Unit
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
                    onClick = { onJoin(room.roomId) },
                    onRename = { onRename(room) },
                    onRemove = { onRemove(room.roomId) }
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
    onClick: () -> Unit,
    onRemove: () -> Unit,
    onSave: () -> Unit
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
                text = { Text(stringResource(R.string.recent_calls_save)) },
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
    onRemove: () -> Unit,
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
                text = room.name,
                modifier = Modifier.weight(1f),
                style = MaterialTheme.typography.bodyMedium
            )
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
    roomId: String,
    onDismiss: () -> Unit,
    onSave: (String, String) -> Unit
) {
    var name by rememberSaveable { mutableStateOf("") }

    Dialog(onDismissRequest = onDismiss) {
        Surface(
            shape = RoundedCornerShape(16.dp),
            color = MaterialTheme.colorScheme.surface
        ) {
            Column(
                modifier = Modifier.padding(20.dp),
                horizontalAlignment = Alignment.CenterHorizontally
            ) {
                Text(
                    text = stringResource(R.string.saved_rooms_dialog_title),
                    style = MaterialTheme.typography.titleLarge
                )
                Spacer(modifier = Modifier.height(16.dp))
                TextField(
                    value = name,
                    onValueChange = { name = it },
                    label = { Text(stringResource(R.string.saved_rooms_dialog_placeholder)) },
                    singleLine = true
                )
                Spacer(modifier = Modifier.height(16.dp))
                Row {
                    TextButton(onClick = onDismiss) {
                        Text(stringResource(R.string.common_cancel))
                    }
                    Spacer(modifier = Modifier.width(8.dp))
                    TextButton(
                        onClick = { 
                            onSave(roomId, name)
                            onDismiss()
                        },
                        enabled = name.isNotBlank()
                    ) {
                        Text(stringResource(R.string.common_save))
                    }
                }
            }
        }
    }
}

@Composable
private fun RenameRoomDialog(
    room: SavedRoom,
    onDismiss: () -> Unit,
    onRename: (String, String) -> Unit
) {
    var name by rememberSaveable(room.name) { mutableStateOf(room.name) }

    Dialog(onDismissRequest = onDismiss) {
        Surface(
            shape = RoundedCornerShape(16.dp),
            color = MaterialTheme.colorScheme.surface
        ) {
            Column(
                modifier = Modifier.padding(20.dp),
                horizontalAlignment = Alignment.CenterHorizontally
            ) {
                Text(
                    text = stringResource(R.string.saved_rooms_dialog_rename_title),
                    style = MaterialTheme.typography.titleLarge
                )
                Spacer(modifier = Modifier.height(16.dp))
                TextField(
                    value = name,
                    onValueChange = { name = it },
                    label = { Text(stringResource(R.string.saved_rooms_dialog_placeholder)) },
                    singleLine = true
                )
                Spacer(modifier = Modifier.height(16.dp))
                Row {
                    TextButton(onClick = onDismiss) {
                        Text(stringResource(R.string.common_cancel))
                    }
                    Spacer(modifier = Modifier.width(8.dp))
                    TextButton(
                        onClick = { 
                            onRename(room.roomId, name)
                            onDismiss()
                         },
                        enabled = name.isNotBlank() && name != room.name
                    ) {
                        Text(stringResource(R.string.common_save))
                    }
                }
            }
        }
    }
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
