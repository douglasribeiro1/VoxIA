package com.voxia.keyboard.service

import android.app.PendingIntent
import android.content.ComponentName
import android.content.Context
import android.os.Build
import android.service.quicksettings.Tile
import android.service.quicksettings.TileService
import android.util.Log

/**
 * Bloco "Balão VoxIA" nas configurações rápidas.
 * Balão parado (ex.: a economia de energia fechou o app e o Android desligou a acessibilidade):
 * o toque abre direto a chave do VoxIA para religar. Balão rodando: o toque mostra/oculta o balão.
 */
class VoxIABubbleTileService : TileService() {

    override fun onStartListening() {
        super.onStartListening()
        updateTile()
    }

    override fun onClick() {
        super.onClick()
        if (VoxIABubbleService.running) {
            val prefs = getSharedPreferences("voxia_settings", Context.MODE_PRIVATE)
            val visible = prefs.getBoolean(VoxIABubbleService.PREF_ENABLED, true)
            prefs.edit().putBoolean(VoxIABubbleService.PREF_ENABLED, !visible).apply()
            updateTile()
        } else {
            openAccessibility()
        }
    }

    private fun openAccessibility() {
        val intent = VoxIABubbleService.accessibilitySettingsIntent(this)
        try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
                val pending = PendingIntent.getActivity(
                    this, 0, intent, PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT
                )
                startActivityAndCollapse(pending)
            } else {
                @Suppress("DEPRECATION")
                startActivityAndCollapse(intent)
            }
        } catch (e: Exception) {
            Log.e("VoxIATile", "Não foi possível abrir a Acessibilidade", e)
        }
    }

    private fun updateTile() {
        val tile = qsTile ?: return
        val running = VoxIABubbleService.running
        val visible = getSharedPreferences("voxia_settings", Context.MODE_PRIVATE)
            .getBoolean(VoxIABubbleService.PREF_ENABLED, true)
        tile.state = if (running && visible) Tile.STATE_ACTIVE else Tile.STATE_INACTIVE
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            tile.subtitle = when {
                !running -> "Toque para religar"
                visible -> "Ligado"
                else -> "Oculto"
            }
        }
        tile.updateTile()
    }

    companion object {
        /** Pede ao sistema para atualizar o bloco (ex.: quando o balão liga, desliga ou é ocultado). */
        fun requestUpdate(context: Context) {
            try {
                TileService.requestListeningState(context, ComponentName(context, VoxIABubbleTileService::class.java))
            } catch (e: Exception) {
                // Sem bloco adicionado ou sistema recusou: nada a fazer
            }
        }
    }
}
