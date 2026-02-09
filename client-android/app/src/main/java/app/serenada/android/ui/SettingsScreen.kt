package app.serenada.android.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.ArrowBack
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ExposedDropdownMenuBox
import androidx.compose.material3.ExposedDropdownMenuDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.MenuAnchorType
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalUriHandler
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import app.serenada.android.R
import app.serenada.android.data.SettingsStore
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import java.net.HttpURLConnection
import java.net.URL

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SettingsScreen(
    host: String,
    selectedLanguage: String,
    isBackgroundModeEnabled: Boolean,
    isDefaultCameraEnabled: Boolean,
    isDefaultMicrophoneEnabled: Boolean,
    hostError: String?,
    isSaving: Boolean,
    onHostChange: (String) -> Unit,
    onLanguageSelect: (String) -> Unit,
    onBackgroundModeChange: (Boolean) -> Unit,
    onDefaultCameraChange: (Boolean) -> Unit,
    onDefaultMicrophoneChange: (Boolean) -> Unit,
    onSave: () -> Unit,
    onCancel: () -> Unit
) {
    val languageOptions = listOf(
        SettingsStore.LANGUAGE_AUTO to stringResource(R.string.settings_language_auto),
        SettingsStore.LANGUAGE_EN to stringResource(R.string.settings_language_english),
        SettingsStore.LANGUAGE_RU to stringResource(R.string.settings_language_russian),
        SettingsStore.LANGUAGE_ES to stringResource(R.string.settings_language_spanish),
        SettingsStore.LANGUAGE_FR to stringResource(R.string.settings_language_french)
    )

    val selectedLanguageLabel = languageOptions.firstOrNull { it.first == selectedLanguage }?.second
        ?: languageOptions.first().second
    var languageMenuExpanded by remember { mutableStateOf(false) }

    val isDefaultHost = host == SettingsStore.DEFAULT_HOST
    val isRuHost = host == SettingsStore.HOST_RU
    val isCustomHost = !isDefaultHost && !isRuHost

    val uriHandler = LocalUriHandler.current
    val scope = rememberCoroutineScope()
    var pingResult by remember { mutableStateOf<String?>(null) }
    var isPinging by remember { mutableStateOf(false) }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(stringResource(R.string.settings_title)) },
                navigationIcon = {
                    IconButton(onClick = onCancel) {
                        Icon(
                            imageVector = Icons.AutoMirrored.Filled.ArrowBack,
                            contentDescription = stringResource(R.string.common_back)
                        )
                    }
                },
                actions = {
                    TextButton(onClick = onSave, enabled = !isSaving) {
                        Text(stringResource(R.string.settings_save))
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
            Column(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(24.dp)
                    .verticalScroll(rememberScrollState())
            ) {

                Text(
                    text = stringResource(R.string.settings_server_host),
                    style = MaterialTheme.typography.titleMedium,
                    color = MaterialTheme.colorScheme.primary,
                    modifier = Modifier.padding(bottom = 8.dp)
                )

                HostOptionRow(
                    selected = isDefaultHost,
                    label = "Global (${SettingsStore.DEFAULT_HOST})",
                    onClick = { onHostChange(SettingsStore.DEFAULT_HOST) }
                )

                HostOptionRow(
                    selected = isRuHost,
                    label = "Russia (${SettingsStore.HOST_RU})",
                    onClick = { onHostChange(SettingsStore.HOST_RU) }
                )

                HostOptionRow(
                    selected = isCustomHost,
                    label = stringResource(R.string.custom),
                    onClick = { }
                )

                OutlinedTextField(
                    value = host,
                    onValueChange = onHostChange,
                    label = { Text(stringResource(R.string.settings_server_host)) },
                    isError = hostError != null,
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(top = 8.dp),
                    singleLine = true
                )

                // Connection Tools Row
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(top = 8.dp),
                    horizontalArrangement = Arrangement.spacedBy(8.dp)
                ) {
                    OutlinedButton(
                        onClick = {
                            scope.launch(Dispatchers.IO) {
                                isPinging = true
                                pingResult = null
                                try {
                                    val targetUrl = if (host.startsWith("http")) host else "https://$host"
                                    val start = System.currentTimeMillis()
                                    val connection = URL(targetUrl).openConnection() as HttpURLConnection
                                    connection.connectTimeout = 3000
                                    connection.readTimeout = 3000
                                    connection.requestMethod = "HEAD"
                                    connection.connect() // Connect explicitly
                                    val responseCode = connection.responseCode // Trigger request
                                    val end = System.currentTimeMillis()
                                    pingResult = "${end - start} ms"
                                    connection.disconnect()
                                } catch (e: Exception) {
                                    pingResult = "Error"
                                    e.printStackTrace()
                                } finally {
                                    isPinging = false
                                }
                            }
                        },
                        modifier = Modifier.weight(1f),
                        enabled = !isPinging
                    ) {
                        if (isPinging) {
                            CircularProgressIndicator(
                                modifier = Modifier.size(16.dp),
                                strokeWidth = 2.dp,
                                color = MaterialTheme.colorScheme.primary
                            )
                        } else {
                            Text("Ping")
                        }
                    }

                    OutlinedButton(
                        onClick = {
                            val targetUrl = if (host.startsWith("http")) "$host/device-check" else "https://$host/device-check"
                            uriHandler.openUri(targetUrl)
                        },
                        modifier = Modifier.weight(1f)
                    ) {
                        Text("Device Check")
                    }
                }

                if (pingResult != null) {
                    Text(
                        text = "Latency: $pingResult",
                        style = MaterialTheme.typography.bodySmall,
                        color = if (pingResult == "Error") MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.primary,
                        modifier = Modifier.padding(start = 4.dp, top = 4.dp, bottom = 4.dp)
                    )
                }

                TextButton(
                    onClick = { uriHandler.openUri("https://github.com/N0-C0M/serenada") },
                    modifier = Modifier.align(Alignment.End)
                ) {
                    Text(stringResource(R.string.create_custom_server))
                }

                if (!hostError.isNullOrBlank()) {
                    Text(
                        text = hostError,
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.error,
                        modifier = Modifier.padding(top = 4.dp, start = 16.dp)
                    )
                }

                Spacer(modifier = Modifier.height(24.dp))

                Text(
                    text = stringResource(R.string.settings_language),
                    style = MaterialTheme.typography.titleMedium,
                    color = MaterialTheme.colorScheme.primary,
                    modifier = Modifier.padding(bottom = 8.dp)
                )

                ExposedDropdownMenuBox(
                    expanded = languageMenuExpanded,
                    onExpandedChange = { languageMenuExpanded = !languageMenuExpanded },
                    modifier = Modifier.fillMaxWidth()
                ) {
                    OutlinedTextField(
                        value = selectedLanguageLabel,
                        onValueChange = {},
                        readOnly = true,
                        trailingIcon = {
                            ExposedDropdownMenuDefaults.TrailingIcon(expanded = languageMenuExpanded)
                        },
                        modifier = Modifier
                            .menuAnchor(type = MenuAnchorType.PrimaryNotEditable)
                            .fillMaxWidth()
                    )
                    ExposedDropdownMenu(
                        expanded = languageMenuExpanded,
                        onDismissRequest = { languageMenuExpanded = false }
                    ) {
                        languageOptions.forEach { (languageCode, languageLabel) ->
                            DropdownMenuItem(
                                text = { Text(languageLabel) },
                                onClick = {
                                    onLanguageSelect(languageCode)
                                    languageMenuExpanded = false
                                }
                            )
                        }
                    }
                }

                Spacer(modifier = Modifier.height(24.dp))

                Text(
                    text = "General",
                    style = MaterialTheme.typography.titleMedium,
                    color = MaterialTheme.colorScheme.primary,
                    modifier = Modifier.padding(bottom = 8.dp)
                )

                Spacer(modifier = Modifier.height(24.dp))

                Text(
                    text = "Call Defaults",
                    style = MaterialTheme.typography.titleMedium,
                    color = MaterialTheme.colorScheme.primary,
                    modifier = Modifier.padding(bottom = 8.dp)
                )

                SettingsSwitchRow(
                    label = stringResource(R.string.camera_enabled),
                    subLabel = stringResource(R.string.camera_enabled_info),
                    checked = isDefaultCameraEnabled,
                    onCheckedChange = onDefaultCameraChange
                )

                SettingsSwitchRow(
                    label = stringResource(R.string.microphone_enabled),
                    subLabel = stringResource(R.string.microphone_enabled_info),
                    checked = isDefaultMicrophoneEnabled,
                    onCheckedChange = onDefaultMicrophoneChange
                )
            }
        }
    }
}

@Composable
private fun SettingsSwitchRow(
    label: String,
    subLabel: String?,
    checked: Boolean,
    onCheckedChange: (Boolean) -> Unit
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clickable { onCheckedChange(!checked) }
            .padding(vertical = 4.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween
    ) {
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = label,
                style = MaterialTheme.typography.bodyLarge
            )
            if (subLabel != null) {
                Text(
                    text = subLabel,
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
        }
        Switch(
            checked = checked,
            onCheckedChange = onCheckedChange
        )
    }
}

@Composable
private fun HostOptionRow(
    selected: Boolean,
    label: String,
    onClick: () -> Unit
) {
    Row(
        verticalAlignment = Alignment.CenterVertically,
        modifier = Modifier
            .fillMaxWidth()
            .clickable(onClick = onClick)
            .padding(vertical = 4.dp)
    ) {
        RadioButton(
            selected = selected,
            onClick = onClick
        )
        Text(
            text = label,
            style = MaterialTheme.typography.bodyLarge,
            modifier = Modifier.padding(start = 8.dp)
        )
    }
}