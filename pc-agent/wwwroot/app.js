const DEFAULT_PROFILES = {
  "Photoshop": [
    ["Geri Al", "CTRL+Z"],
    ["İleri Al", "CTRL+SHIFT+Z"],
    ["Fırça (B)", "B"],
    ["Silgi (E)", "E"],
    ["Damlalık (I)", "I"],
    ["Seçim (M)", "M"],
    ["Kement (L)", "L"],
    ["Kalem (P)", "P"],
    ["Serbest Dönüştür", "CTRL+T"],
    ["Seçimi Kaldır", "CTRL+D"],
    ["Yeni Katman", "CTRL+SHIFT+N"],
    ["Katmanı Çoğalt", "CTRL+J"],
    ["Fırça Küçült", "["],
    ["Fırça Büyüt", "]"],
    ["Yakınlaş", "CTRL++"],
    ["Uzaklaş", "CTRL+-"],
    ["Tam Oturt", "CTRL+0"],
    ["Kaydet", "CTRL+S"]
  ],
  "Premiere Pro": [
    ["Oynat / Durdur", "SPACE"],
    ["Kes (Razor)", "C"],
    ["Seçim Aracı", "V"],
    ["Kesim Noktası Ekle", "CTRL+K"],
    ["Giriş Noktası (I)", "I"],
    ["Çıkış Noktası (O)", "O"],
    ["Geri Al", "CTRL+Z"],
    ["İleri Al", "CTRL+SHIFT+Z"],
    ["Süreyi Değiştir (R)", "R"],
    ["İleri Sar (L)", "L"],
    ["Geri Sar (J)", "J"],
    ["Kaydet", "CTRL+S"]
  ],
  "After Effects": [
    ["Önizleme (RAM)", "SPACE"],
    ["Klibi Böl", "CTRL+SHIFT+D"],
    ["Pozisyon (P)", "P"],
    ["Ölçek (S)", "S"],
    ["Döndürme (R)", "R"],
    ["Opaklık (T)", "T"],
    ["Anahtar Kareleri Göster (U)", "U"],
    ["Easy Ease (F9)", "F9"],
    ["Geri Al", "CTRL+Z"],
    ["Kaydet", "CTRL+S"]
  ],
  "Illustrator": [
    ["Seçim (V)", "V"],
    ["Doğrudan Seçim (A)", "A"],
    ["Kalem (P)", "P"],
    ["Fırça (B)", "B"],
    ["Şekil Oluşturucu", "SHIFT+M"],
    ["Grupla", "CTRL+G"],
    ["Grubu Çöz", "CTRL+SHIFT+G"],
    ["Geri Al", "CTRL+Z"],
    ["Kaydet", "CTRL+S"]
  ],
  "Blender": [
    ["Taşı (G)", "G"],
    ["Döndür (R)", "R"],
    ["Ölçekle (S)", "S"],
    ["Ekstrüzyon (E)", "E"],
    ["Ekle Menüsü", "SHIFT+A"],
    ["Düzenleme Modu", "TAB"],
    ["Sil (X)", "X"],
    ["Kamera Görünümü", "NUM0"],
    ["Ön Görünüm", "NUM1"],
    ["Yan Görünüm", "NUM3"],
    ["Üst Görünüm", "NUM7"],
    ["Geri Al", "CTRL+Z"]
  ],
  "Stardew Valley": [
    ["Envanter (E)", "E"],
    ["Harita (M)", "M"],
    ["Günlük (F)", "F"],
    ["Etkileşim / Kullan", "C"],
    ["Slot 1", "1"],
    ["Slot 2", "2"],
    ["Slot 3", "3"],
    ["Slot 4", "4"],
    ["Slot 5", "5"]
  ],
  "ARK: Survival Evolved": [
    ["Envanter (I)", "I"],
    ["Kullan (E)", "E"],
    ["Harita (M)", "M"],
    ["Kabile (L)", "L"],
    ["Üretim (V)", "V"],
    ["Evcilleştirme (T)", "T"],
    ["Yere At (O)", "O"],
    ["Slot 1", "1"],
    ["Slot 2", "2"],
    ["Slot 3", "3"]
  ],
  "Valheim": [
    ["Envanter (TAB)", "TAB"],
    ["Etkileşim (E)", "E"],
    ["Harita (M)", "M"],
    ["Silahı Kılıfa Koy (R)", "R"],
    ["Otur (X)", "X"],
    ["Otomatik Koş (Q)", "Q"],
    ["Slot 1", "1"],
    ["Slot 2", "2"],
    ["Slot 3", "3"],
    ["Slot 4", "4"]
  ],
  "Masaüstü & Genel": [
    ["Kopyala", "CTRL+C"],
    ["Yapıştır", "CTRL+V"],
    ["Kes", "CTRL+X"],
    ["Tümünü Seç", "CTRL+A"],
    ["Geri Al", "CTRL+Z"],
    ["Pencere Değiştir", "ALT+TAB"],
    ["Masaüstünü Göster", "WIN+D"],
    ["Görev Görünümü", "WIN+TAB"],
    ["Dosya Gezgini", "WIN+E"],
    ["Ekran Alıntısı", "WIN+SHIFT+S"],
    ["Sekmeyi Kapat", "CTRL+W"],
    ["Yeni Sekme", "CTRL+T"]
  ]
};

const $ = id => document.getElementById(id);

let customButtons = JSON.parse(localStorage.deckCustomButtons || "{}");
let profile = localStorage.deckLastProfile || Object.keys(DEFAULT_PROFILES)[0];
if (!DEFAULT_PROFILES[profile]) profile = Object.keys(DEFAULT_PROFILES)[0];

let ws = null;
let recording = false;
let seq = [];
let token = localStorage.deckToken || sessionStorage.deckToken || "";
let activeTab = localStorage.deckActiveTab || "deck";

// Drawing state
let penMode = localStorage.deckPenMode || "absolute"; // "absolute" | "relative"
let penClick = localStorage.deckPenClick !== "false"; // default true
let penSensitivity = parseFloat(localStorage.deckPenSens || "1.5");
let showGrid = localStorage.deckShowGrid !== "false";
let showTrail = localStorage.deckShowTrail !== "false";
let showBgPreview = localStorage.deckShowBg === "true";

// Screen preview state
let refreshIntervalMs = 800;
let screenTimer = null;
let serverCrop = { enabled: true, x: 0, y: 0, width: 900, height: 700 };
let primaryScreenBounds = { w: 1920, h: 1080 };

// ================= HAPTIC FEEDBACK =================
function vibrate(ms = 12) {
  try {
    if (navigator.vibrate) navigator.vibrate(ms);
  } catch {}
}

// ================= STATUS & CONNECTION =================
function setStatus(text, isOnline = null) {
  $("status").textContent = text;
  if (isOnline === true) {
    $("connDot").classList.add("online");
  } else if (isOnline === false) {
    $("connDot").classList.remove("online");
  }
}

function connect() {
  if (!token) {
    setStatus("PIN ile eşleştirme gerekli", false);
    return;
  }
  if (ws && ws.readyState < 2) return;

  const proto = location.protocol === "https:" ? "wss:" : "ws:";
  ws = new WebSocket(`${proto}//${location.host}/ws?token=${encodeURIComponent(token)}`);

  ws.onopen = () => {
    setStatus("PC bağlı ve hazır", true);
    ws.send(JSON.stringify({ type: "profile", name: profile }));
    fetchServerConfig();
    refreshScreenPreview();
  };

  ws.onmessage = (ev) => {
    try {
      const msg = JSON.parse(ev.data);
      if (msg.type === "pong") {
        // Keep-alive ok
      }
    } catch {}
  };

  ws.onclose = (ev) => {
    if (ev.code === 1008 || ev.code === 401) {
      setStatus("Oturum süresi doldu — PIN ile tekrar eşleştirin", false);
      return;
    }
    setStatus("Bağlantı kesildi; yeniden bağlanılıyor...", false);
    setTimeout(() => {
      ws = null;
      connect();
    }, 2000);
  };

  ws.onerror = () => {
    setStatus("PC bağlantı hatası", false);
  };
}

// Keep-alive ping every 15s
setInterval(() => {
  if (ws && ws.readyState === 1) {
    ws.send(JSON.stringify({ type: "ping" }));
  }
}, 15000);

async function fetchServerConfig() {
  if (!token) return;
  try {
    const r = await fetch(`/api/config?token=${encodeURIComponent(token)}`, {
      headers: { "Authorization": "Bearer " + token }
    });
    if (r.status === 401) {
      setStatus("Yetki hatası — Lütfen PIN ile yeniden eşleştirin", false);
      return;
    }
    if (!r.ok) return;
    const data = await r.json();
    if (data.screenCrop) {
      serverCrop = {
        enabled: !!data.screenCrop.enabled,
        x: data.screenCrop.x ?? 0,
        y: data.screenCrop.y ?? 0,
        width: data.screenCrop.width ?? 900,
        height: data.screenCrop.height ?? 700
      };
      $("cfgScreenEnabled").checked = serverCrop.enabled;
      $("cfgCropX").value = serverCrop.x;
      $("cfgCropY").value = serverCrop.y;
      $("cfgCropW").value = serverCrop.width;
      $("cfgCropH").value = serverCrop.height;
    }
    if (data.monitors && data.monitors.length > 0) {
      const prim = data.monitors.find(m => m.primary) || data.monitors[0];
      if (prim && prim.bounds) {
        primaryScreenBounds = { w: prim.bounds.w, h: prim.bounds.h };
      }
    }
  } catch {}
}

async function checkStatusInfo() {
  try {
    const r = await fetch("/api/status");
    if (r.ok) {
      const d = await r.json();
      if (d.name) {
        $("pcNameBadge").textContent = d.name;
        $("pcNameBadge").hidden = false;
      }
    }
  } catch {}
}
checkStatusInfo();

// ================= TABS NAVIGATION =================
function switchTab(tabName) {
  activeTab = tabName;
  localStorage.deckActiveTab = tabName;

  document.querySelectorAll(".tab-btn").forEach(btn => {
    btn.classList.toggle("active", btn.dataset.tab === tabName);
  });

  document.querySelectorAll(".tab-panel").forEach(panel => {
    panel.classList.toggle("active", panel.id === `tab-${tabName}`);
  });

  if (tabName === "draw") {
    requestAnimationFrame(() => {
      resizeCanvas();
      renderCanvasScene();
    });
  } else if (tabName === "screen") {
    refreshScreenPreview();
  } else if (tabName === "settings") {
    fetchServerConfig();
  }
}

document.querySelectorAll(".tab-btn").forEach(btn => {
  btn.onclick = () => {
    vibrate(8);
    switchTab(btn.dataset.tab);
  };
});

// ================= DECK & PROFILES =================
function sendKey(key, btnEl = null) {
  vibrate(12);
  if (btnEl) {
    btnEl.classList.add("flash");
    setTimeout(() => btnEl.classList.remove("flash"), 140);
  }

  if (ws && ws.readyState === 1) {
    ws.send(JSON.stringify({ type: "key", key }));
  } else {
    setStatus("PC bağlantısı yok — önce eşleştirin", false);
  }

  if (recording) {
    seq.push(key);
    updateMacroSeqPreview();
  }
}

function renderProfilesBar() {
  const nav = $("profiles");
  nav.innerHTML = "";
  Object.keys(DEFAULT_PROFILES).forEach(name => {
    const b = document.createElement("button");
    b.textContent = name;
    b.className = name === profile ? "active" : "";
    b.onclick = () => {
      vibrate(8);
      profile = name;
      localStorage.deckLastProfile = profile;
      renderProfilesBar();
      renderDeckButtons();
      if (ws && ws.readyState === 1) {
        ws.send(JSON.stringify({ type: "profile", name: profile }));
      }
    };
    nav.append(b);
  });
}

function renderDeckButtons() {
  $("title").textContent = profile;
  const container = $("buttons");
  container.innerHTML = "";

  const defaults = DEFAULT_PROFILES[profile] || [];
  const customs = customButtons[profile] || [];

  defaults.forEach(([label, key]) => {
    const b = document.createElement("button");
    b.className = "deck-key-btn";
    b.innerHTML = `<span class="key-title">${escapeHtml(label)}</span><span class="key-combo">${escapeHtml(key)}</span>`;
    b.onclick = () => sendKey(key, b);
    container.append(b);
  });

  customs.forEach(([label, key], idx) => {
    const b = document.createElement("button");
    b.className = "deck-key-btn";
    b.innerHTML = `
      <span class="key-title">${escapeHtml(label)}</span>
      <span class="key-combo">${escapeHtml(key)}</span>
      <span class="del-custom" title="Bu özel tuşu sil">✕</span>
    `;
    b.onclick = (e) => {
      if (e.target.classList.contains("del-custom")) {
        e.stopPropagation();
        customButtons[profile].splice(idx, 1);
        localStorage.deckCustomButtons = JSON.stringify(customButtons);
        renderDeckButtons();
        return;
      }
      sendKey(key, b);
    };
    container.append(b);
  });
}

function escapeHtml(str) {
  return String(str)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}

$("launchBtn").onclick = async () => {
  vibrate(15);
  try {
    const r = await fetch("/api/launch", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "Authorization": "Bearer " + token
      },
      body: JSON.stringify({ name: profile })
    });
    const data = await r.json().catch(() => ({}));
    if (!r.ok) {
      setStatus(data.error || "Program açılamadı");
    } else {
      setStatus(data.message || `${profile} açıldı`);
    }
  } catch {
    setStatus("PC bağlantısı yok", false);
  }
};

// Quick media & system keys
document.querySelectorAll(".quick-key, .draw-key-btn").forEach(btn => {
  btn.onclick = () => sendKey(btn.dataset.key, btn);
});

// Custom Button Modal
$("addBtnModalOpen").onclick = () => {
  $("addBtnProfileName").textContent = profile;
  $("customBtnLabel").value = "";
  $("customBtnKey").value = "";
  $("addBtnModal").hidden = false;
  $("customBtnLabel").focus();
};

$("customBtnCancel").onclick = () => {
  $("addBtnModal").hidden = true;
};

$("customBtnSave").onclick = () => {
  const lbl = $("customBtnLabel").value.trim();
  const key = $("customBtnKey").value.trim().toUpperCase();
  if (!lbl || !key) return;
  if (!customButtons[profile]) customButtons[profile] = [];
  customButtons[profile].push([lbl, key]);
  localStorage.deckCustomButtons = JSON.stringify(customButtons);
  $("addBtnModal").hidden = true;
  renderDeckButtons();
};

// ================= MACROS =================
function updateMacroSeqPreview() {
  const el = $("macroSeqPreview");
  if (!recording && seq.length === 0) {
    el.hidden = true;
    return;
  }
  el.hidden = false;
  el.textContent = seq.length ? `Sıra: ${seq.join("  ➔  ")}` : "Henüz tuşa basılmadı; panelden tuşlara dokunun...";
}

$("rec").onclick = () => {
  recording = !recording;
  if (recording) {
    seq = [];
    $("rec").textContent = "■ Durdur";
    $("rec").classList.add("recording");
    $("recStatus").textContent = "Kayıtta: Tuşlara basın...";
  } else {
    $("rec").textContent = "● Kaydet";
    $("rec").classList.remove("recording");
    $("recStatus").textContent = seq.length ? `${seq.length} adım hazır` : "Kayıt bekleniyor";
  }
  updateMacroSeqPreview();
};

$("save").onclick = () => {
  const name = $("macroName").value.trim();
  if (!name || !seq.length) {
    setStatus("Makro için isim girin ve önce tuşları kaydedin");
    return;
  }
  const all = JSON.parse(localStorage.deckMacros || "{}");
  all[name] = seq.slice();
  localStorage.deckMacros = JSON.stringify(all);
  recording = false;
  seq = [];
  $("macroName").value = "";
  $("rec").textContent = "● Kaydet";
  $("rec").classList.remove("recording");
  $("recStatus").textContent = "Makro kaydedildi";
  updateMacroSeqPreview();
  showMacros();
};

function showMacros() {
  const all = JSON.parse(localStorage.deckMacros || "{}");
  const container = $("macros");
  container.innerHTML = "";
  Object.entries(all).forEach(([name, keys]) => {
    const chip = document.createElement("div");
    chip.className = "macro-chip";

    const playBtn = document.createElement("button");
    playBtn.className = "play-macro";
    playBtn.textContent = `▶ ${name} (${keys.length})`;
    playBtn.onclick = async () => {
      vibrate(15);
      setStatus(`Makro oynatılıyor: ${name}`);
      for (const key of keys) {
        sendKey(key);
        await new Promise(r => setTimeout(r, 150));
      }
      setStatus(`Makro tamamlandı: ${name}`);
    };

    const delBtn = document.createElement("button");
    delBtn.className = "del-macro";
    delBtn.textContent = "✕";
    delBtn.title = "Makroyu sil";
    delBtn.onclick = () => {
      delete all[name];
      localStorage.deckMacros = JSON.stringify(all);
      showMacros();
    };

    chip.append(playBtn, delBtn);
    container.append(chip);
  });
}

// ================= PAIRING MODAL =================
$("pairBtn").onclick = () => {
  $("pairModal").hidden = false;
  $("pairMessage").textContent = "";
  $("pinInput").value = "";
  $("pinInput").focus();
};

$("pairCancel").onclick = () => {
  $("pairModal").hidden = true;
};

$("pairConfirm").onclick = async () => {
  const pin = $("pinInput").value.trim();
  if (!pin) {
    $("pairMessage").textContent = "Lütfen PIN kodunu girin.";
    return;
  }
  try {
    const r = await fetch("/api/pair", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ pin })
    });
    if (!r.ok) {
      $("pairMessage").textContent = "PIN hatalı! PC Eşlikçisi penceresindeki PIN kodunu kontrol edin.";
      return;
    }
    const data = await r.json();
    token = data.token;
    localStorage.deckToken = token;
    if (data.machine) {
      $("pcNameBadge").textContent = data.machine;
      $("pcNameBadge").hidden = false;
    }
    $("pairModal").hidden = true;
    if (ws) {
      ws.close();
      ws = null;
    }
    connect();
  } catch {
    $("pairMessage").textContent = "PC'ye ulaşılamadı. Aynı Wi-Fi ağında olduğunuzu kontrol edin.";
  }
};

$("pinInput").addEventListener("keydown", e => {
  if (e.key === "Enter") $("pairConfirm").click();
});

// Fullscreen toggle
$("fullscreenBtn").onclick = () => {
  if (!document.fullscreenElement) {
    document.documentElement.requestFullscreen?.().catch(() => {});
  } else {
    document.exitFullscreen?.().catch(() => {});
  }
};

// ================= DEDICATED DRAWING & TOUCHPAD ENGINE =================
const canvas = $("pad");
const ctx = canvas.getContext("2d");
let isPointerDown = false;
let lastNormX = 0.5;
let lastNormY = 0.5;
let currentCursor = { x: 0.5, y: 0.5, active: false, pressure: 0.5 };
let strokeTrail = []; // Array of {x, y, pressure, time}

function syncDrawToolbarUI() {
  $("modeAbsBtn").classList.toggle("active", penMode === "absolute");
  $("modeRelBtn").classList.toggle("active", penMode === "relative");
  $("clickOnBtn").classList.toggle("active", penClick);
  $("clickOffBtn").classList.toggle("active", !penClick);
  $("sensSlider").value = penSensitivity;
  $("sensVal").textContent = penSensitivity.toFixed(1) + "x";
  $("toggleGridBtn").classList.toggle("active", showGrid);
  $("toggleTrailBtn").classList.toggle("active", showTrail);
  $("toggleBgPreviewBtn").classList.toggle("active", showBgPreview);
  $("canvasBgImage").hidden = !showBgPreview;
}

$("modeAbsBtn").onclick = () => {
  penMode = "absolute";
  localStorage.deckPenMode = penMode;
  syncDrawToolbarUI();
  renderCanvasScene();
};

$("modeRelBtn").onclick = () => {
  penMode = "relative";
  localStorage.deckPenMode = penMode;
  syncDrawToolbarUI();
  renderCanvasScene();
};

$("clickOnBtn").onclick = () => {
  penClick = true;
  localStorage.deckPenClick = "true";
  syncDrawToolbarUI();
};

$("clickOffBtn").onclick = () => {
  penClick = false;
  localStorage.deckPenClick = "false";
  syncDrawToolbarUI();
};

$("sensSlider").oninput = (e) => {
  penSensitivity = parseFloat(e.target.value);
  localStorage.deckPenSens = String(penSensitivity);
  $("sensVal").textContent = penSensitivity.toFixed(1) + "x";
};

$("toggleGridBtn").onclick = () => {
  showGrid = !showGrid;
  localStorage.deckShowGrid = String(showGrid);
  syncDrawToolbarUI();
  renderCanvasScene();
};

$("toggleTrailBtn").onclick = () => {
  showTrail = !showTrail;
  localStorage.deckShowTrail = String(showTrail);
  if (!showTrail) strokeTrail = [];
  syncDrawToolbarUI();
  renderCanvasScene();
};

$("toggleBgPreviewBtn").onclick = () => {
  showBgPreview = !showBgPreview;
  localStorage.deckShowBg = String(showBgPreview);
  syncDrawToolbarUI();
  if (showBgPreview) refreshScreenPreview();
};

$("clearCanvasBtn").onclick = () => {
  strokeTrail = [];
  renderCanvasScene();
};

$("zenDrawBtn").onclick = () => {
  $("drawWorkspace").classList.add("zen-mode");
  $("exitZenBtn").hidden = false;
  requestAnimationFrame(() => {
    resizeCanvas();
    renderCanvasScene();
  });
};

$("exitZenBtn").onclick = () => {
  $("drawWorkspace").classList.remove("zen-mode");
  $("exitZenBtn").hidden = true;
  requestAnimationFrame(() => {
    resizeCanvas();
    renderCanvasScene();
  });
};

function resizeCanvas() {
  const rect = canvas.getBoundingClientRect();
  if (rect.width === 0 || rect.height === 0) return;
  const dpr = window.devicePixelRatio || 1;
  canvas.width = Math.round(rect.width * dpr);
  canvas.height = Math.round(rect.height * dpr);
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  renderCanvasScene();
}

new ResizeObserver(() => {
  resizeCanvas();
}).observe($("canvasContainer"));

function renderCanvasScene() {
  const rect = canvas.getBoundingClientRect();
  const w = rect.width;
  const h = rect.height;
  if (!w || !h) return;

  ctx.clearRect(0, 0, w, h);

  // Draw guide grid
  if (showGrid) {
    ctx.save();
    ctx.strokeStyle = "rgba(148, 163, 184, 0.12)";
    ctx.lineWidth = 1;

    const cols = 8;
    const rows = 6;
    for (let i = 1; i < cols; i++) {
      const x = (w / cols) * i;
      ctx.beginPath();
      ctx.moveTo(x, 0);
      ctx.lineTo(x, h);
      ctx.stroke();
    }
    for (let j = 1; j < rows; j++) {
      const y = (h / rows) * j;
      ctx.beginPath();
      ctx.moveTo(0, y);
      ctx.lineTo(w, y);
      ctx.stroke();
    }

    // Center crosshair
    ctx.strokeStyle = "rgba(57, 132, 255, 0.25)";
    ctx.setLineDash([6, 6]);
    ctx.beginPath();
    ctx.moveTo(w / 2, 0);
    ctx.lineTo(w / 2, h);
    ctx.moveTo(0, h / 2);
    ctx.lineTo(w, h / 2);
    ctx.stroke();
    ctx.restore();
  }

  // Draw stroke trail
  if (showTrail && strokeTrail.length > 1) {
    ctx.save();
    ctx.lineCap = "round";
    ctx.lineJoin = "round";

    for (let i = 1; i < strokeTrail.length; i++) {
      const p0 = strokeTrail[i - 1];
      const p1 = strokeTrail[i];
      if (p1.breakStroke) continue;

      const pressure = p1.pressure || 0.5;
      ctx.beginPath();
      ctx.moveTo(p0.x * w, p0.y * h);
      ctx.lineTo(p1.x * w, p1.y * h);
      ctx.strokeStyle = penClick ? "rgba(57, 132, 255, 0.85)" : "rgba(16, 185, 129, 0.65)";
      ctx.lineWidth = Math.max(1.5, pressure * 7);
      ctx.stroke();
    }
    ctx.restore();
  }

  // Draw active cursor indicator
  if (currentCursor.active) {
    const cx = currentCursor.x * w;
    const cy = currentCursor.y * h;
    const radius = Math.max(5, (currentCursor.pressure || 0.5) * 14);

    ctx.save();
    ctx.beginPath();
    ctx.arc(cx, cy, radius, 0, Math.PI * 2);
    ctx.fillStyle = penClick ? "rgba(57, 132, 255, 0.35)" : "rgba(16, 185, 129, 0.35)";
    ctx.fill();
    ctx.lineWidth = 2;
    ctx.strokeStyle = penClick ? "#60a5fa" : "#34d399";
    ctx.stroke();
    ctx.restore();
  }
}

function transmitPenEvent(e, phase) {
  const rect = canvas.getBoundingClientRect();
  if (!rect.width || !rect.height) return;

  const normX = Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width));
  const normY = Math.max(0, Math.min(1, (e.clientY - rect.top) / rect.height));
  const pressure = e.pressure > 0 ? e.pressure : 0.5;

  $("hudCoords").textContent = `X: ${Math.round(normX * 100)}% Y: ${Math.round(normY * 100)}%`;
  $("hudPressure").textContent = `Basınç: ${Math.round(pressure * 100)}%`;

  currentCursor = { x: normX, y: normY, active: phase !== "up", pressure };

  if (showTrail) {
    if (phase === "down") {
      strokeTrail.push({ x: normX, y: normY, pressure, breakStroke: true });
    } else if (phase === "move") {
      strokeTrail.push({ x: normX, y: normY, pressure, breakStroke: false });
      if (strokeTrail.length > 600) strokeTrail.shift();
    }
  }
  renderCanvasScene();

  if (!ws || ws.readyState !== 1) return;

  if (phase === "move") {
    const coalesced = typeof e.getCoalescedEvents === "function" ? e.getCoalescedEvents() : [];
    const events = coalesced.length ? coalesced : [e];
    const points = events.map(q => {
      const qx = Math.max(0, Math.min(1, (q.clientX - rect.left) / rect.width));
      const qy = Math.max(0, Math.min(1, (q.clientY - rect.top) / rect.height));
      const dx = qx - lastNormX;
      const dy = qy - lastNormY;
      lastNormX = qx;
      lastNormY = qy;
      return { x: qx, y: qy, dx, dy, pressure: q.pressure || 0.5 };
    });

    if (ws.bufferedAmount > 8192) {
      const last = points[points.length - 1];
      ws.send(JSON.stringify({
        type: "pen",
        mode: penMode,
        click: penClick,
        sensitivity: penSensitivity,
        points: [last]
      }));
    } else {
      ws.send(JSON.stringify({
        type: "pen",
        mode: penMode,
        click: penClick,
        sensitivity: penSensitivity,
        points
      }));
    }
  } else {
    const dx = phase === "down" ? 0 : (normX - lastNormX);
    const dy = phase === "down" ? 0 : (normY - lastNormY);
    lastNormX = normX;
    lastNormY = normY;

    ws.send(JSON.stringify({
      type: "pen",
      phase,
      mode: penMode,
      click: penClick,
      sensitivity: penSensitivity,
      x: normX,
      y: normY,
      dx,
      dy,
      pressure
    }));
  }
}

canvas.addEventListener("pointerdown", e => {
  e.preventDefault();
  isPointerDown = true;
  canvas.setPointerCapture(e.pointerId);
  const rect = canvas.getBoundingClientRect();
  lastNormX = Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width));
  lastNormY = Math.max(0, Math.min(1, (e.clientY - rect.top) / rect.height));
  transmitPenEvent(e, "down");
});

canvas.addEventListener("pointermove", e => {
  e.preventDefault();
  if (isPointerDown) {
    transmitPenEvent(e, "move");
  }
});

canvas.addEventListener("pointerup", e => {
  e.preventDefault();
  if (!isPointerDown) return;
  isPointerDown = false;
  transmitPenEvent(e, "up");
});

canvas.addEventListener("pointercancel", e => {
  e.preventDefault();
  if (!isPointerDown) return;
  isPointerDown = false;
  transmitPenEvent(e, "up");
});

// Mouse Buttons & Scroll Strip in Drawing Workspace
function sendMouseClick(button, action = "click") {
  vibrate(12);
  if (ws && ws.readyState === 1) {
    ws.send(JSON.stringify({ type: "mouse_click", button, action }));
  }
}

$("btnLeftClick").onclick = () => sendMouseClick("left", "click");
$("btnDblClick").onclick = () => sendMouseClick("left", "dblclick");
$("btnRightClick").onclick = () => sendMouseClick("right", "click");
$("btnMiddleClick").onclick = () => sendMouseClick("middle", "click");

const scrollStrip = $("scrollStrip");
let scrollDragging = false;
let lastScrollY = 0;

scrollStrip.addEventListener("pointerdown", e => {
  e.preventDefault();
  scrollDragging = true;
  lastScrollY = e.clientY;
  scrollStrip.setPointerCapture(e.pointerId);
});

scrollStrip.addEventListener("pointermove", e => {
  if (!scrollDragging) return;
  const dy = lastScrollY - e.clientY;
  if (Math.abs(dy) >= 6) {
    lastScrollY = e.clientY;
    if (ws && ws.readyState === 1) {
      ws.send(JSON.stringify({ type: "scroll", deltaY: Math.round(-dy * 4), deltaX: 0 }));
    }
  }
});

scrollStrip.addEventListener("pointerup", () => { scrollDragging = false; });
scrollStrip.addEventListener("pointercancel", () => { scrollDragging = false; });

// ================= SCREEN & INVENTORY PREVIEW =================
const invImg = $("inventory");
const bgImg = $("canvasBgImage");

invImg.onload = () => {
  $("screenPlaceholder").hidden = true;
};

invImg.onerror = () => {
  $("screenPlaceholder").hidden = false;
};

function refreshScreenPreview() {
  if (!token) return;
  const shouldLoad = activeTab === "screen" || (activeTab === "draw" && showBgPreview);
  if (!shouldLoad) return;

  const url = `/api/screen?token=${encodeURIComponent(token)}&t=${Date.now()}`;
  if (activeTab === "screen") {
    invImg.src = url;
  }
  if (activeTab === "draw" && showBgPreview) {
    bgImg.src = url;
  }
}

function setupScreenTimer() {
  if (screenTimer) clearInterval(screenTimer);
  if (refreshIntervalMs > 0) {
    screenTimer = setInterval(refreshScreenPreview, refreshIntervalMs);
  }
}

$("refreshRateSelect").onchange = (e) => {
  refreshIntervalMs = parseInt(e.target.value, 10) || 0;
  setupScreenTimer();
};

$("refreshNowBtn").onclick = () => {
  vibrate(10);
  refreshScreenPreview();
};

// Interactive click on the preview image -> clicks exact point inside the cropped PC region!
invImg.addEventListener("pointerdown", e => {
  if (!$("screenClickToggle").checked) return;
  if (!ws || ws.readyState !== 1) return;

  const rect = invImg.getBoundingClientRect();
  if (!rect.width || !rect.height) return;

  const relX = Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width));
  const relY = Math.max(0, Math.min(1, (e.clientY - rect.top) / rect.height));

  // Map cropped region back to full screen normalized [0..1]
  const absPxX = serverCrop.x + relX * serverCrop.width;
  const absPxY = serverCrop.y + relY * serverCrop.height;

  const normScreenX = Math.max(0, Math.min(1, absPxX / Math.max(1, primaryScreenBounds.w)));
  const normScreenY = Math.max(0, Math.min(1, absPxY / Math.max(1, primaryScreenBounds.h)));

  vibrate(12);
  ws.send(JSON.stringify({
    type: "pen",
    phase: "down",
    mode: "absolute",
    click: true,
    x: normScreenX,
    y: normScreenY
  }));
  setTimeout(() => {
    if (ws && ws.readyState === 1) {
      ws.send(JSON.stringify({
        type: "pen",
        phase: "up",
        mode: "absolute",
        click: true,
        x: normScreenX,
        y: normScreenY
      }));
    }
  }, 35);
});

// ================= SETTINGS TAB ACTIONS =================
$("saveScreenCfgBtn").onclick = async () => {
  if (!token) return;
  const payload = {
    screenCrop: {
      enabled: $("cfgScreenEnabled").checked,
      x: parseInt($("cfgCropX").value, 10) || 0,
      y: parseInt($("cfgCropY").value, 10) || 0,
      width: Math.max(50, parseInt($("cfgCropW").value, 10) || 900),
      height: Math.max(50, parseInt($("cfgCropH").value, 10) || 700)
    }
  };
  try {
    const r = await fetch("/api/config", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "Authorization": "Bearer " + token
      },
      body: JSON.stringify(payload)
    });
    if (r.ok) {
      serverCrop = payload.screenCrop;
      $("cfgSaveMsg").textContent = "✓ Ekran ayarları PC'ye kaydedildi.";
      setTimeout(() => { $("cfgSaveMsg").textContent = ""; }, 3000);
    }
  } catch {}
};

$("setFullScreenCfgBtn").onclick = () => {
  $("cfgScreenEnabled").checked = true;
  $("cfgCropX").value = 0;
  $("cfgCropY").value = 0;
  $("cfgCropW").value = primaryScreenBounds.w || 1920;
  $("cfgCropH").value = primaryScreenBounds.h || 1080;
  $("saveScreenCfgBtn").click();
};

$("sendDirectTextBtn").onclick = () => {
  const txt = $("directTextInput").value;
  if (!txt || !ws || ws.readyState !== 1) return;
  vibrate(10);
  ws.send(JSON.stringify({ type: "text", text: txt }));
  $("directTextInput").value = "";
};

$("sendDirectEnterBtn").onclick = () => {
  const txt = $("directTextInput").value;
  if (!ws || ws.readyState !== 1) return;
  vibrate(10);
  if (txt) ws.send(JSON.stringify({ type: "text", text: txt }));
  setTimeout(() => sendKey("ENTER"), 50);
  $("directTextInput").value = "";
};

$("resetCustomButtonsBtn").onclick = () => {
  if (confirm("Eklediğiniz tüm özel tuşlar silinsin mi?")) {
    customButtons = {};
    localStorage.removeItem("deckCustomButtons");
    renderDeckButtons();
  }
};

$("clearAllMacrosBtn").onclick = () => {
  if (confirm("Kaydedilmiş tüm makrolar silinsin mi?")) {
    localStorage.removeItem("deckMacros");
    showMacros();
  }
};

// ================= INITIALIZATION =================
renderProfilesBar();
renderDeckButtons();
showMacros();
syncDrawToolbarUI();
switchTab(activeTab);
setupScreenTimer();
if (token) connect();
