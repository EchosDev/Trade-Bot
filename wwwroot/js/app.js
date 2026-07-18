/* ==========================================================================
   GRIDTRADE — frontend logic (real backend, simulyasiya YOXDUR)
   ========================================================================== */

const API_BASE = "/api";

const el = (id) => document.getElementById(id);

const state = {
    orders: [],
    selectedId: null,
    botRunning: false,
    backendOnline: false,
    pollTimer: null,
    lastLogId: 0,
};

/* ---------------------------------------------------------------------
   API helper
--------------------------------------------------------------------- */
async function api(path, options = {}) {
    try {
        const res = await fetch(API_BASE + path, {
            headers: { "Content-Type": "application/json" },
            ...options,
        });
        const text = await res.text();
        const data = text ? JSON.parse(text) : null;

        if (!res.ok) {
            const msg = (data && data.message) ? data.message : `HTTP ${res.status}`;
            throw new Error(msg);
        }

        setBackendStatus(true);
        return data;
    } catch (err) {
        setBackendStatus(false);
        throw err;
    }
}

function setBackendStatus(online) {
    if (state.backendOnline === online) return;
    state.backendOnline = online;
    const dot = document.querySelector("#connPill .dot");
    if (dot) dot.className = "dot " + (online ? "dot-ok" : "dot-muted");
    const txt = el("connText");
    if (txt) txt.textContent = online ? "Backend qoşulub" : "Backend oflayn";
}

/* ---------------------------------------------------------------------
   Logging — YALNIZ backend-dən gələn real BotLogs göstərilir
--------------------------------------------------------------------- */
function appendLog(message, level, createdAt) {
    const box = el("logBox");
    const time = new Date(createdAt).toLocaleTimeString("az-AZ", { hour12: false });
    const line = document.createElement("div");
    line.className = "log-line";
    line.innerHTML = `<span class="log-time">[${time}]</span> <span class="log-${level}">${escapeHtml(message)}</span>`;
    box.appendChild(line);
    box.scrollTop = box.scrollHeight;
}

// Sırf lokal UI mesajları üçün (validasiya xətaları və s.) — backend-ə getmir
function localLog(message, level = "info") {
    const box = el("logBox");
    const time = new Date().toLocaleTimeString("az-AZ", { hour12: false });
    const line = document.createElement("div");
    line.className = "log-line";
    line.innerHTML = `<span class="log-time">[${time}]</span> <span class="log-${level}">${escapeHtml(message)}</span>`;
    box.appendChild(line);
    box.scrollTop = box.scrollHeight;
}

function escapeHtml(str) {
    const d = document.createElement("div");
    d.textContent = str;
    return d.innerHTML;
}

async function pollLogs() {
    try {
        const logs = await api(`/logs?afterId=${state.lastLogId}`);
        if (Array.isArray(logs) && logs.length) {
            logs.forEach((l) => {
                appendLog(l.message, l.level, l.createdAt);
                state.lastLogId = l.id;
            });
        }
    } catch {
        /* backend əlçatan deyil, sakitcə keç */
    }
}

/* ---------------------------------------------------------------------
   API keys
--------------------------------------------------------------------- */
async function loadApiKeys() {
    try {
        const data = await api("/keys");
        if (data) {
            el("apiKey").value = data.apiKey || "";
            if (data.hasSecret) {
                el("apiSecret").placeholder = "•••••••••••• (saxlanılıb, dəyişmək üçün yenisini yaz)";
            }
        }
    } catch {
        localLog("API açarları yüklənmədi.", "warn");
    }
}

async function saveApiKeys() {
    const payload = { apiKey: el("apiKey").value.trim(), apiSecret: el("apiSecret").value.trim() };
    if (!payload.apiKey || !payload.apiSecret) {
        localLog("API Key və API Secret sahələri boş ola bilməz.", "warn");
        return;
    }
    try {
        await api("/keys", { method: "POST", body: JSON.stringify(payload) });
        localLog("API açarları yadda saxlanıldı.");
    } catch {
        localLog("Açarlar yadda saxlanmadı — backend cavab vermir.", "warn");
    }
}

/* ---------------------------------------------------------------------
   Orders — CRUD
--------------------------------------------------------------------- */
function readForm() {
    return {
        symbol: el("symbol").value.trim().toUpperCase(),
        buy: parseFloat(el("buyPrice").value),
        qty: parseFloat(el("buyQty").value),
        sell: parseFloat(el("sellPrice").value),
        s_qty: parseFloat(el("sellQty").value),
        repeat: el("repeatToggle").checked,
        marketType: el("marketType").value,
        leverage: parseInt(el("leverageInput")?.value || "1", 10), // yeni
    };
}

function validateForm(f) {
    if (!f.symbol) return "Simvol boş ola bilməz.";
    if ([f.buy, f.qty, f.sell, f.s_qty].some((v) => Number.isNaN(v) || v <= 0)) {
        return "Bütün qiymət və miqdar sahələri düzgün ədəd olmalıdır.";
    }
    if (f.buy >= f.sell) return "BUY qiyməti SELL qiymətindən böyük və ya bərabər ola bilməz!";
    return null;
}

async function addOrder() {
    const form = readForm();
    const err = validateForm(form);
    if (err) return localLog(err, "warn");

    try {
        await api("/orders", { method: "POST", body: JSON.stringify(form) });
        localLog(`Əlavə edildi: ${form.symbol} BUY ${form.qty} @ ${form.buy} → SELL ${form.s_qty} @ ${form.sell}`);
        await refreshOrders();
    } catch {
        localLog("Sifariş backendə göndərilmədi.", "error");
    }
}

async function editOrder() {
    if (!state.selectedId) return localLog("Dəyişmək üçün cədvəldən bir sifariş seçin.", "warn");

    const form = readForm();
    const err = validateForm(form);
    if (err) return localLog(err, "warn");

    try {
        await api(`/orders/${state.selectedId}`, { method: "PUT", body: JSON.stringify(form) });
        localLog(`Sifariş yeniləndi: ${form.symbol}`);
        await refreshOrders();
    } catch (e) {
        localLog(e.message || "Dəyişiklik backenddə saxlanmadı.", "error");
    }
}

async function removeOrder() {
    if (!state.selectedId) return localLog("Silmək üçün cədvəldən bir sifariş seçin.", "warn");

    try {
        await api(`/orders/${state.selectedId}`, { method: "DELETE" });
        localLog("Sifariş silindi.");
        state.selectedId = null;
        await refreshOrders();
    } catch {
        localLog("Backenddə silinmə uğursuz oldu.", "error");
    }
}

/* ---------------------------------------------------------------------
   Selecting a row fills the form
--------------------------------------------------------------------- */
function selectOrder(id) {
    state.selectedId = id;
    const order = state.orders.find((o) => o.id === id);
    if (!order) return;

    setSymbolValue(order.symbol);

    el("buyPrice").value = order.buy;
    el("buyQty").value = order.qty;
    el("sellPrice").value = order.sell;
    el("sellQty").value = order.s_qty;
    el("repeatToggle").checked = order.repeat;
    el("selectedHint").textContent = `Seçilib: ${order.symbol} (status: ${order.status})`;

    renderOrders();
}

// Sifarişin simvolunu dropdown-a təyin edir — panel-də yoxdursa, avtomatik yaradır
function setSymbolValue(symbol) {
    const hiddenSelect = el("symbol");
    const panel = document.getElementById("symbolPanel");
    const label = document.getElementById("symbolLabel");
    if (!hiddenSelect || !panel || !label) return;

    let opt = panel.querySelector(`.custom-select-option[data-value="${symbol}"]`);
    const base = symbol.endsWith("USDT") ? symbol.slice(0, -4) : symbol;
    const text = `${base} / USDT`;

    if (!opt) {
        // Panel-də yoxdursa (custom coin idi, refresh-dən sonra itib) — yenidən yarat
        opt = document.createElement("div");
        opt.className = "custom-select-option";
        opt.dataset.value = symbol;
        opt.textContent = text;
        opt.addEventListener("click", () => {
            setSymbolValue(symbol);
            panel.hidden = true;
            document.getElementById("symbolSelect")?.classList.remove("is-open");
        });
        const divider = panel.querySelector(".custom-select-divider");
        panel.insertBefore(opt, divider);
    }

    if (!hiddenSelect.querySelector(`option[value="${symbol}"]`)) {
        const hiddenOpt = document.createElement("option");
        hiddenOpt.value = symbol;
        hiddenOpt.textContent = text;
        hiddenSelect.appendChild(hiddenOpt);
    }

    hiddenSelect.value = symbol;
    label.textContent = opt.textContent;
    panel.querySelectorAll(".custom-select-option").forEach((o) => o.classList.remove("is-selected"));
    opt.classList.add("is-selected");
}
/* ---------------------------------------------------------------------
   Bot control — YALNIZ backend-ə siqnal göndərir, heç bir lokal simulyasiya yoxdur
--------------------------------------------------------------------- */
async function startBot() {
    if (state.botRunning) return localLog("Bot artıq işləyir.", "warn");

    try {
        await api("/bot/start", { method: "POST" });
        state.botRunning = true;
        setBotStatus(true);
        localLog("Bot başladıldı.");
    } catch (err) {
        localLog("Bot başladıla bilmədi — backend xəta qaytardı.", "error");
    }
}

async function stopBot() {
    if (!state.botRunning) return localLog("Bot artıq dayandırılıb.", "warn");

    try {
        await api("/bot/stop", { method: "POST" });
    } catch {
        localLog("Backend dayandırma siqnalını almadı.", "warn");
    }

    state.botRunning = false;
    setBotStatus(false);
    localLog("Bot dayandırıldı.");
}

function setBotStatus(running) {
    const dot = el("botDot");
    const txt = el("botText");
    if (dot) dot.className = "dot " + (running ? "dot-on" : "dot-off");
    if (txt) txt.textContent = running ? "BOT İŞLƏYİR" : "BOT DAYANDIRILIB";
}

/* ---------------------------------------------------------------------
   Polling — real vəziyyəti backend-dən çəkir (orders + logs + bot status)
--------------------------------------------------------------------- */
async function refreshOrders() {
    try {
        const fresh = await api("/orders");
        if (Array.isArray(fresh)) {
            state.orders = fresh;
            renderOrders();
        }
    } catch {
        /* sakitcə keç */
    }
}

async function refreshBotStatus() {
    try {
        const data = await api("/bot/status");
        if (data) {
            state.botRunning = !!data.isRunning;
            setBotStatus(state.botRunning);
        }
    } catch {
        /* sakitcə keç */
    }
}

function startPolling() {
    stopPolling();
    state.pollTimer = setInterval(async () => {
        await refreshOrders();
        await pollLogs();
        await refreshBotStatus();
    }, 3000);
}
function stopPolling() {
    if (state.pollTimer) clearInterval(state.pollTimer);
    state.pollTimer = null;
}

/* ---------------------------------------------------------------------
   Rendering
--------------------------------------------------------------------- */
const STATUS_LABELS = {
    waiting: "Gözləmədə",
    buy_placed: "BUY yerləşdi",
    buy_filled: "BUY icra oldu",
    sell_placed: "SELL yerləşdi",
    sell_filled: "SELL icra oldu",
    updated: "Yeniləndi",
};

function renderMarketBadge(marketType) {
    const isFutures = marketType === "futures";
    const label = isFutures ? "Futures" : "Spot";
    const cls = isFutures ? "badge-market-futures" : "badge-market-spot";
    return `<span class="badge ${cls}">${label}</span>`;
}

function renderOrders() {
    const body = el("ordersBody");
    body.innerHTML = "";
    el("orderCount").textContent = state.orders.length;
    el("emptyState").style.display = state.orders.length ? "none" : "block";

    state.orders.forEach((order) => {
        const tr = document.createElement("tr");
        if (order.id === state.selectedId) tr.classList.add("selected");
        tr.addEventListener("click", () => selectOrder(order.id));

        tr.innerHTML = `
      <td>${escapeHtml(order.symbol)}</td>
      <td>${renderMarketBadge(order.marketType)}</td>
      <td>${order.buy}</td>
      <td>${order.qty}</td>
      <td>${order.sell}</td>
      <td>${order.s_qty}</td>
      <td>${order.repeat ? "Bəli" : "Xeyr"}</td>
      <td><span class="badge badge-${order.status}">${STATUS_LABELS[order.status] || order.status}</span></td>
      <td>${renderLadder(order)}</td>
    `;
        body.appendChild(tr);
    });
}

function renderLadder(order) {
    const positions = {
        waiting: 92,
        buy_placed: 70,
        buy_filled: 50,
        sell_placed: 25,
        sell_filled: 6,
        updated: 92,
    };
    const top = positions[order.status] ?? 50;
    return `
    <div class="ladder" title="${STATUS_LABELS[order.status] || order.status}">
      <div class="ladder-fill" style="height:${100 - top}%"></div>
      <div class="ladder-marker sell" style="top:4px"></div>
      <div class="ladder-marker buy" style="bottom:4px"></div>
      <div class="ladder-cursor" style="top:${top}%"></div>
    </div>
  `;
}

/* ---------------------------------------------------------------------
   Wire up events
--------------------------------------------------------------------- */
function on(id, event, handler) {
    const element = el(id);
    if (element) element.addEventListener(event, handler);
}

on("saveKeysBtn", "click", saveApiKeys);
on("addOrderBtn", "click", addOrder);
on("editOrderBtn", "click", editOrder);
on("removeOrderBtn", "click", removeOrder);
on("startBotBtn", "click", startBot);
on("stopBotBtn", "click", stopBot);
on("clearLogBtn", "click", () => {
    el("logBox").innerHTML = "";
});

on("clearLogDbBtn", "click", () => openConfirmModal(
    "Bütün jurnalı sil",
    "Bu, verilənlər bazasındakı BÜTÜN jurnal qeydlərini həmişəlik siləcək. Geri qaytarıla bilməz. Davam edilsin?",
    clearLogs
));
/* ---------------------------------------------------------------------
   Init
--------------------------------------------------------------------- */
(async function init() {
    if (!el("ordersBody")) return;
    await loadApiKeys();
    await refreshOrders();
    await refreshBotStatus();
    await pollLogs();
    renderOrders();
    startPolling();
})();

/* ---------------------------------------------------------------------
   Custom dropdown (symbol seçici) — gizli <select id="symbol">-i saxlayır
--------------------------------------------------------------------- */
(function initCustomSelect() {
    const wrap = document.getElementById("symbolSelect");
    if (!wrap) return;

    const trigger = document.getElementById("symbolTrigger");
    const label = document.getElementById("symbolLabel");
    const panel = document.getElementById("symbolPanel");
    const hiddenSelect = document.getElementById("symbol");
    const addTrigger = document.getElementById("symbolAddTrigger");
    const addForm = document.getElementById("symbolAddForm");
    const addInput = document.getElementById("symbolAddInput");
    const addConfirm = document.getElementById("symbolAddConfirm");

    function getOptions() {
        return Array.from(panel.querySelectorAll(".custom-select-option"));
    }

    function closePanel() {
        panel.hidden = true;
        wrap.classList.remove("is-open");
        addForm.hidden = true;
    }
    function openPanel() {
        panel.hidden = false;
        wrap.classList.add("is-open");
    }

    function selectValue(value, text) {
        hiddenSelect.value = value;
        label.textContent = text;
        getOptions().forEach((o) => o.classList.remove("is-selected"));
        const match = getOptions().find((o) => o.dataset.value === value);
        if (match) match.classList.add("is-selected");
    }

    function attachOptionHandler(opt) {
        opt.addEventListener("click", () => {
            selectValue(opt.dataset.value, opt.textContent);
            closePanel();
        });
    }

    getOptions().forEach(attachOptionHandler);

    trigger.addEventListener("click", (e) => {
        e.stopPropagation();
        panel.hidden ? openPanel() : closePanel();
    });

    // "+ Digər coin əlavə et" — inline formu göstər
    addTrigger.addEventListener("click", (e) => {
        e.stopPropagation();
        addForm.hidden = !addForm.hidden;
        if (!addForm.hidden) {
            addInput.value = "";
            addInput.focus();
        }
    });

    function addCustomCoin() {
        let raw = addInput.value.trim().toUpperCase();
        if (!raw) return;

        const value = raw.endsWith("USDT") ? raw : raw + "USDT";
        const displayBase = raw.endsWith("USDT") ? raw.slice(0, -4) : raw;
        const text = `${displayBase} / USDT`;

        const existing = getOptions().find((o) => o.dataset.value === value);
        if (existing) {
            selectValue(value, existing.textContent);
        } else {
            // 1) Görünən panelə yeni option əlavə et
            const newOpt = document.createElement("div");
            newOpt.className = "custom-select-option";
            newOpt.dataset.value = value;
            newOpt.textContent = text;
            attachOptionHandler(newOpt);
            panel.insertBefore(newOpt, panel.querySelector(".custom-select-divider"));

            // 2) VACİB: gizli <select>-ə də uyğun <option> əlavə et,
            //    yoxsa hiddenSelect.value = value işə düşməz
            const newHiddenOpt = document.createElement("option");
            newHiddenOpt.value = value;
            newHiddenOpt.textContent = text;
            hiddenSelect.appendChild(newHiddenOpt);

            selectValue(value, text);
        }

        addForm.hidden = true;
        closePanel();
    }

    addConfirm.addEventListener("click", (e) => {
        e.stopPropagation();
        addCustomCoin();
    });
    addInput.addEventListener("keydown", (e) => {
        if (e.key === "Enter") {
            e.preventDefault();
            addCustomCoin();
        }
    });
    addInput.addEventListener("click", (e) => e.stopPropagation());

    document.addEventListener("click", (e) => {
        if (!wrap.contains(e.target)) closePanel();
    });
    document.addEventListener("keydown", (e) => {
        if (e.key === "Escape") closePanel();
    });
})();

document.querySelectorAll(".market-type-btn").forEach((btn) => {
    btn.addEventListener("click", () => {
        document.querySelectorAll(".market-type-btn").forEach((b) => {
            b.classList.remove("is-active", "btn-secondary");
            b.classList.add("btn-ghost");
        });
        btn.classList.add("is-active", "btn-secondary");
        btn.classList.remove("btn-ghost");
        el("marketType").value = btn.dataset.value;

        // Leverage sahəsini yalnız Futures seçiləndə göstər
        const leverageBlock = document.getElementById("leverageBlock");
        if (leverageBlock) {
            leverageBlock.style.display = btn.dataset.value === "futures" ? "block" : "none";
        }
    });
});



/* ---------------------------------------------------------------------
Təsdiq modalı — ümumi istifadə üçün
--------------------------------------------------------------------- */
function openConfirmModal(title, text, onConfirm) {
    const overlay = document.getElementById("confirmModalOverlay");
    if (!overlay) return;

    document.getElementById("confirmModalTitle").textContent = title;
    document.getElementById("confirmModalText").textContent = text;
    overlay.hidden = false;

    const confirmBtn = document.getElementById("confirmModalConfirm");
    const cancelBtn = document.getElementById("confirmModalCancel");

    function close() {
        overlay.hidden = true;
        confirmBtn.removeEventListener("click", handleConfirm);
        cancelBtn.removeEventListener("click", close);
    }
    function handleConfirm() {
        close();
        onConfirm();
    }

    confirmBtn.addEventListener("click", handleConfirm);
    cancelBtn.addEventListener("click", close);
}

async function clearLogs() {
    try {
        await api("/logs", { method: "DELETE" });
        el("logBox").innerHTML = "";
        state.lastLogId = 0;
        localLog("Jurnal təmizləndi.");
    } catch (e) {
        localLog(e.message || "Jurnal təmizlənmədi.", "error");
    }
}