package com.tabletdeck.app

import android.app.Activity
import android.graphics.Color
import android.graphics.drawable.GradientDrawable
import android.os.Bundle
import android.view.Gravity
import android.view.View
import android.view.WindowManager
import android.webkit.WebChromeClient
import android.webkit.WebResourceError
import android.webkit.WebResourceRequest
import android.webkit.WebView
import android.webkit.WebViewClient
import android.widget.*
import org.json.JSONObject
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.NetworkInterface
import java.net.SocketTimeoutException

class MainActivity : Activity() {
    private lateinit var web: WebView
    private lateinit var connBar: LinearLayout
    private lateinit var toggleBarBtn: Button

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)

        val dp = resources.displayMetrics.density
        fun px(v: Int) = (v * dp).toInt()

        val root = FrameLayout(this).apply {
            setBackgroundColor(Color.parseColor("#0D1219"))
        }

        val mainCol = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
        }

        // Collapsible connection drawer at top
        connBar = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(Color.parseColor("#161F2C"))
            setPadding(px(14), px(10), px(14), px(10))
        }

        val topRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }

        val status = TextView(this).apply {
            text = "Bilgisayar aranıyor…"
            setTextColor(Color.parseColor("#EDF2F7"))
            textSize = 13.5f
            layoutParams = LinearLayout.LayoutParams(0, -2, 1f)
        }

        val find = Button(this).apply {
            text = "🔍 Otomatik Bul"
            setTextColor(Color.WHITE)
            textSize = 12.5f
            background = GradientDrawable().apply {
                setColor(Color.parseColor("#3984FF"))
                cornerRadius = px(8).toFloat()
            }
            setPadding(px(12), px(6), px(12), px(6))
        }

        val hideBtn = Button(this).apply {
            text = "Gizle ▲"
            setTextColor(Color.parseColor("#94A3B8"))
            textSize = 12f
            background = GradientDrawable().apply {
                setColor(Color.parseColor("#1F2B3D"))
                cornerRadius = px(8).toFloat()
            }
            setPadding(px(10), px(6), px(10), px(6))
            layoutParams = LinearLayout.LayoutParams(-2, -2).apply { leftMargin = px(6) }
        }

        topRow.addView(status)
        topRow.addView(find)
        topRow.addView(hideBtn)

        val manualRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            layoutParams = LinearLayout.LayoutParams(-1, -2).apply { topMargin = px(8) }
        }

        val url = EditText(this).apply {
            hint = "Örn: http://192.168.1.25:47821"
            setHintTextColor(Color.parseColor("#64748B"))
            setTextColor(Color.WHITE)
            textSize = 13.5f
            setSingleLine(true)
            setText(getPreferences(0).getString("url", ""))
            background = GradientDrawable().apply {
                setColor(Color.parseColor("#0A0E14"))
                setStroke(px(1), Color.parseColor("#2C3D55"))
                cornerRadius = px(8).toFloat()
            }
            setPadding(px(10), px(8), px(10), px(8))
            layoutParams = LinearLayout.LayoutParams(0, -2, 1f)
        }

        val manual = Button(this).apply {
            text = "Bağlan"
            setTextColor(Color.WHITE)
            textSize = 12.5f
            background = GradientDrawable().apply {
                setColor(Color.parseColor("#059669"))
                cornerRadius = px(8).toFloat()
            }
            setPadding(px(14), px(8), px(14), px(8))
            layoutParams = LinearLayout.LayoutParams(-2, -2).apply { leftMargin = px(8) }
        }

        manualRow.addView(url)
        manualRow.addView(manual)

        connBar.addView(topRow)
        connBar.addView(manualRow)

        web = WebView(this).apply {
            setBackgroundColor(Color.parseColor("#0D1219"))
            settings.javaScriptEnabled = true
            settings.domStorageEnabled = true
            settings.useWideViewPort = true
            settings.loadWithOverviewMode = true
            webChromeClient = WebChromeClient()
            webViewClient = object : WebViewClient() {
                override fun onPageFinished(view: WebView?, loadedUrl: String?) {
                    super.onPageFinished(view, loadedUrl)
                    if (loadedUrl != null && loadedUrl.startsWith("http")) {
                        status.text = "Bağlandı: $loadedUrl"
                        connBar.visibility = View.GONE
                        toggleBarBtn.visibility = View.VISIBLE
                    }
                }

                override fun onReceivedError(
                    view: WebView?,
                    request: WebResourceRequest?,
                    error: WebResourceError?
                ) {
                    super.onReceivedError(view, request, error)
                    if (request?.isForMainFrame == true) {
                        connBar.visibility = View.VISIBLE
                        toggleBarBtn.visibility = View.GONE
                        status.text = "Bağlantı kurulamadı. PC Eşlikçisinin açık olduğundan emin olun."
                    }
                }
            }
        }

        mainCol.addView(connBar)
        mainCol.addView(web, LinearLayout.LayoutParams(-1, 0, 1f))

        // Small floating button to reopen connection bar when hidden
        toggleBarBtn = Button(this).apply {
            text = "📡"
            textSize = 12f
            visibility = View.GONE
            alpha = 0.7f
            background = GradientDrawable().apply {
                setColor(Color.parseColor("#1F2B3D"))
                setStroke(px(1), Color.parseColor("#334763"))
                cornerRadius = px(18).toFloat()
            }
            setPadding(px(6), px(4), px(6), px(4))
            layoutParams = FrameLayout.LayoutParams(px(36), px(32)).apply {
                gravity = Gravity.BOTTOM or Gravity.END
                rightMargin = px(10)
                bottomMargin = px(10)
            }
            setOnClickListener {
                connBar.visibility = View.VISIBLE
                visibility = View.GONE
            }
        }

        hideBtn.setOnClickListener {
            connBar.visibility = View.GONE
            toggleBarBtn.visibility = View.VISIBLE
        }

        root.addView(mainCol, FrameLayout.LayoutParams(-1, -1))
        root.addView(toggleBarBtn)
        setContentView(root)

        fun connect(address: String) {
            var u = address.trim()
            if (u.isEmpty()) return
            if (!u.startsWith("http://") && !u.startsWith("https://")) u = "http://$u"
            if (!u.substringAfter("://").contains(":")) u = "$u:47821"
            getPreferences(0).edit().putString("url", u).apply()
            status.text = "$u adresine bağlanılıyor…"
            web.loadUrl(u)
        }

        fun getBroadcastAddresses(): List<InetAddress> {
            val list = mutableListOf<InetAddress>()
            try {
                list.add(InetAddress.getByName("255.255.255.255"))
                val interfaces = NetworkInterface.getNetworkInterfaces()
                while (interfaces.hasMoreElements()) {
                    val networkInterface = interfaces.nextElement()
                    if (networkInterface.isLoopback || !networkInterface.isUp) continue
                    for (interfaceAddress in networkInterface.interfaceAddresses) {
                        val broadcast = interfaceAddress.broadcast
                        if (broadcast != null && !list.contains(broadcast)) {
                            list.add(broadcast)
                        }
                    }
                }
            } catch (_: Exception) {}
            return list
        }

        fun discover() {
            status.text = "Aynı Wi-Fi ağındaki PC aranıyor…"
            find.isEnabled = false
            Thread {
                var found: String? = null
                try {
                    DatagramSocket().use { socket ->
                        socket.broadcast = true
                        socket.soTimeout = 900
                        val bytes = "TABLET_DECK_DISCOVER".toByteArray()
                        val targets = getBroadcastAddresses()
                        outer@ for (i in 0..2) {
                            for (bcast in targets) {
                                try {
                                    socket.send(DatagramPacket(bytes, bytes.size, bcast, 47820))
                                } catch (_: Exception) {}
                            }
                            try {
                                val data = ByteArray(1024)
                                val packet = DatagramPacket(data, data.size)
                                socket.receive(packet)
                                val json = JSONObject(String(packet.data, 0, packet.length))
                                found = "http://${json.getString("host")}:${json.getInt("port")}"
                                break@outer
                            } catch (_: SocketTimeoutException) {}
                        }
                    }
                } catch (_: Exception) {}

                runOnUiThread {
                    find.isEnabled = true
                    if (found != null) {
                        status.text = "PC bulundu, bağlanıyor…"
                        url.setText(found)
                        connect(found!!)
                    } else {
                        val saved = getPreferences(0).getString("url", "")
                        if (!saved.isNullOrBlank()) {
                            status.text = "Otomatik bulunamadı; kayıtlı adres deneniyor…"
                            connect(saved)
                        } else {
                            status.text = "PC bulunamadı. Adresi aşağıya elle girip Bağlan'a dokunun."
                        }
                    }
                }
            }.start()
        }

        find.setOnClickListener { discover() }
        manual.setOnClickListener { connect(url.text.toString()) }
        discover()
    }

    override fun onBackPressed() {
        if (web.canGoBack()) {
            web.goBack()
        } else {
            super.onBackPressed()
        }
    }
}
