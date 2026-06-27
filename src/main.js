const { invoke } = window.__TAURI__.core;
const { listen } = window.__TAURI__.event;

let allSnippets = [];
let settings = { historyCap: 50, autoCapture: true };
let displayed = [];
let searchText = "";
let categoryFilter = "All";
let lastCaptured = null;
let dragSrcId = null;

const isMac = navigator.userAgent.toLowerCase().includes("mac");
const hotkeyLabel = () => (isMac ? "Cmd+Shift+V" : "Ctrl+Shift+V");

// Licensing / trial
const TRIAL_DAYS = 7;
const UNLOCK_PRICE_TEXT = "9.99";
let owned = false;
let trialActive = false;
let hasAccess = true;

function uuid() {
  if (window.crypto && crypto.randomUUID) return crypto.randomUUID();
  return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    const v = c === "x" ? r : (r & 0x3) | 0x8;
    return v.toString(16);
  });
}

function newSnippet(props) {
  return Object.assign(
    {
      id: uuid(),
      text: "",
      isPinned: false,
      category: "",
      isAutoCaptured: false,
      isDeleted: false,
      createdAt: Date.now(),
      order: 0,
    },
    props
  );
}

// ----- persistence -----
async function save() {
  await invoke("set_snippets", { data: JSON.stringify(allSnippets) });
}
async function saveSettings() {
  await invoke("set_settings", { data: JSON.stringify(settings) });
}

// ----- load -----
async function load() {
  try {
    settings = JSON.parse(await invoke("get_settings"));
  } catch {
    settings = { historyCap: 50, autoCapture: true };
  }
  if (typeof settings.historyCap !== "number") settings.historyCap = 50;
  if (typeof settings.autoCapture !== "boolean") settings.autoCapture = true;

  let snippets = [];
  try {
    snippets = JSON.parse(await invoke("get_snippets")) || [];
  } catch {
    snippets = [];
  }

  let seeded = false;
  if (snippets.length === 0) {
    snippets = [
      newSnippet({ text: "Welcome to Clipora!", order: 0 }),
      newSnippet({ text: "Press " + hotkeyLabel() + " anywhere to open this window.", category: "Tip", order: 1 }),
      newSnippet({ text: "Click + to add a snippet, or just copy text and it shows up here.", category: "Tip", order: 2 }),
    ];
    seeded = true;
  }

  allSnippets = snippets;
  if (seeded) await save();

  updateMenuState();
  rebuildCategoryFilter();
  applyFilter();

  await initLicensing();
}

// ----- ordering / cap -----
function nextTopOrder() {
  if (allSnippets.length === 0) return 0;
  return Math.min(...allSnippets.map((s) => s.order)) - 1;
}

function enforceCap() {
  if (settings.historyCap <= 0) return;
  const captured = allSnippets
    .filter((s) => s.isAutoCaptured && !s.isPinned)
    .sort((a, b) => b.createdAt - a.createdAt);
  for (let i = settings.historyCap; i < captured.length; i++) {
    const idx = allSnippets.indexOf(captured[i]);
    if (idx >= 0) allSnippets.splice(idx, 1);
  }
}

// ----- filtering / rendering -----
function applyFilter() {
  let q = allSnippets.slice();
  if (categoryFilter === "History") q = q.filter((s) => s.isAutoCaptured || s.isDeleted);
  else if (categoryFilter === "Pinned") q = q.filter((s) => s.isPinned && !s.isDeleted);
  else if (categoryFilter === "All") q = q.filter((s) => !s.isDeleted);
  else q = q.filter((s) => s.category === categoryFilter && !s.isDeleted);

  if (searchText) {
    const t = searchText.toLowerCase();
    q = q.filter((s) => s.text.toLowerCase().includes(t));
  }

  q.sort((a, b) => (b.isPinned ? 1 : 0) - (a.isPinned ? 1 : 0) || a.order - b.order);
  displayed = q;
  render(q);
}

function rebuildCategoryFilter() {
  const cats = [...new Set(allSnippets.map((s) => s.category).filter((c) => c))].sort();
  const filters = ["All", "Pinned", "History", ...cats];
  const el = document.getElementById("filters");
  el.innerHTML = "";
  for (const f of filters) {
    const b = document.createElement("button");
    b.className = "chip" + (f === categoryFilter ? " active" : "");
    b.textContent = f;
    b.onclick = () => {
      categoryFilter = f;
      rebuildCategoryFilter();
      applyFilter();
    };
    el.appendChild(b);
  }
}

function render(items) {
  const list = document.getElementById("list");
  list.innerHTML = "";

  if (items.length === 0) {
    const e = document.createElement("div");
    e.className = "empty";
    e.textContent = "Nothing here yet.";
    list.appendChild(e);
    return;
  }

  for (const s of items) {
    const card = document.createElement("div");
    card.className = "card";
    card.draggable = true;
    card.dataset.id = s.id;

    const text = document.createElement("div");
    text.className = "card-text";
    text.textContent = s.text;
    card.appendChild(text);

    const row = document.createElement("div");
    row.className = "card-row";

    if (s.category) {
      const tag = document.createElement("span");
      tag.className = "cat-tag";
      tag.textContent = s.category;
      row.appendChild(tag);
    } else {
      const sp = document.createElement("span");
      sp.className = "spacer";
      row.appendChild(sp);
    }

    const pin = document.createElement("button");
    pin.className = "sbtn" + (s.isPinned ? " pinned" : "");
    pin.textContent = s.isPinned ? "📌" : "📍";
    pin.title = s.isPinned ? "Unpin" : "Pin";
    pin.onclick = () => togglePin(s);
    row.appendChild(pin);

    const copy = document.createElement("button");
    copy.className = "sbtn copy";
    copy.textContent = "📋";
    copy.title = "Copy";
    copy.onclick = () => doCopy(s, copy);
    row.appendChild(copy);

    const edit = document.createElement("button");
    edit.className = "sbtn";
    edit.textContent = "✏️";
    edit.title = "Edit";
    edit.onclick = () => editSnippet(s);
    row.appendChild(edit);

    const del = document.createElement("button");
    del.className = "sbtn del";
    del.textContent = "🗑️";
    del.title = "Delete";
    del.onclick = () => deleteSnippet(s);
    row.appendChild(del);

    card.appendChild(row);
    attachDrag(card);
    list.appendChild(card);
  }
}

// ----- drag reorder -----
function attachDrag(card) {
  card.addEventListener("dragstart", (e) => {
    dragSrcId = card.dataset.id;
    card.classList.add("dragging");
    e.dataTransfer.effectAllowed = "move";
  });
  card.addEventListener("dragend", () => {
    card.classList.remove("dragging");
    document.querySelectorAll(".card.drag-over").forEach((c) => c.classList.remove("drag-over"));
  });
  card.addEventListener("dragover", (e) => {
    e.preventDefault();
    e.dataTransfer.dropEffect = "move";
  });
  card.addEventListener("dragenter", () => {
    if (card.dataset.id !== dragSrcId) card.classList.add("drag-over");
  });
  card.addEventListener("dragleave", () => card.classList.remove("drag-over"));
  card.addEventListener("drop", (e) => {
    e.preventDefault();
    card.classList.remove("drag-over");
    if (!dragSrcId || dragSrcId === card.dataset.id) return;
    reorder(dragSrcId, card.dataset.id);
  });
}

function reorder(srcId, targetId) {
  const ids = displayed.map((s) => s.id);
  const from = ids.indexOf(srcId);
  const to = ids.indexOf(targetId);
  if (from < 0 || to < 0) return;

  const moved = displayed.splice(from, 1)[0];
  displayed.splice(to, 0, moved);

  displayed.forEach((s, i) => (s.order = i));
  const visibleIds = new Set(displayed.map((s) => s.id));
  let next = displayed.length;
  for (const s of allSnippets) {
    if (!visibleIds.has(s.id)) s.order = next++;
  }
  save();
  applyFilter();
}

// ----- actions -----
async function doCopy(s, btn) {
  try {
    await invoke("copy_to_clipboard", { text: s.text });
  } catch {}
  lastCaptured = s.text;
  const orig = btn.textContent;
  btn.textContent = "✓";
  btn.classList.add("done");
  setTimeout(() => {
    btn.textContent = orig;
    btn.classList.remove("done");
  }, 900);
}

function togglePin(s) {
  s.isPinned = !s.isPinned;
  if (s.isPinned) s.order = nextTopOrder();
  save();
  applyFilter();
}

function deleteSnippet(s) {
  if (s.isDeleted || s.isAutoCaptured || categoryFilter === "History") {
    const i = allSnippets.indexOf(s);
    if (i >= 0) allSnippets.splice(i, 1);
  } else {
    s.isDeleted = true;
  }
  save();
  rebuildCategoryFilter();
  applyFilter();
}

function addCapturedSnippet(text) {
  lastCaptured = text;
  const existing = allSnippets.find((s) => s.text === text);
  if (existing) {
    existing.order = nextTopOrder();
    save();
    applyFilter();
    return;
  }
  allSnippets.push(newSnippet({ text, isAutoCaptured: true, order: nextTopOrder() }));
  enforceCap();
  save();
  rebuildCategoryFilter();
  applyFilter();
}

async function addSnippet() {
  const r = await showSnippetDialog(null);
  if (!r) return;
  allSnippets.push(newSnippet({ text: r.text, category: r.category, order: nextTopOrder() }));
  save();
  rebuildCategoryFilter();
  applyFilter();
}

async function editSnippet(s) {
  const r = await showSnippetDialog(s);
  if (!r) return;
  s.text = r.text;
  s.category = r.category;
  save();
  rebuildCategoryFilter();
  applyFilter();
}

function setCap(cap) {
  settings.historyCap = cap;
  saveSettings();
  enforceCap();
  save();
  applyFilter();
  updateMenuState();
}

async function clearHistory() {
  const ok = await showConfirm(
    "Clear history?",
    "This removes all auto-captured snippets. Pinned and manually added snippets are kept.",
    "Clear"
  );
  if (!ok) return;
  allSnippets = allSnippets.filter((s) => !(s.isAutoCaptured && !s.isPinned));
  save();
  rebuildCategoryFilter();
  applyFilter();
}

async function exportSnippets() {
  try {
    await invoke("export_to_file", { data: JSON.stringify(allSnippets, null, 2) });
  } catch {}
}

async function importSnippets() {
  let content;
  try {
    content = await invoke("import_from_file");
  } catch {
    return;
  }
  if (!content) return;

  let imported;
  try {
    imported = JSON.parse(content);
  } catch {
    await showConfirm("Import failed", "Could not read the selected file.", "OK", true);
    return;
  }
  if (!Array.isArray(imported)) {
    await showConfirm("Import failed", "That file is not a Clipora export.", "OK", true);
    return;
  }

  const existingIds = new Set(allSnippets.map((s) => s.id));
  const existingTexts = new Set(allSnippets.map((s) => s.text));
  let top = nextTopOrder();
  for (const raw of imported) {
    if (!raw || typeof raw.text !== "string") continue;
    if (raw.id && existingIds.has(raw.id)) continue;
    if (existingTexts.has(raw.text)) continue;
    top -= 1;
    const merged = newSnippet(raw);
    merged.order = top;
    allSnippets.push(merged);
    existingIds.add(merged.id);
    existingTexts.add(merged.text);
  }
  save();
  rebuildCategoryFilter();
  applyFilter();
}

// ----- dialogs -----
function showSnippetDialog(existing) {
  return new Promise((resolve) => {
    const overlay = document.getElementById("dialogOverlay");
    const title = document.getElementById("dialogTitle");
    const text = document.getElementById("dialogText");
    const cat = document.getElementById("dialogCategory");
    const ok = document.getElementById("dialogConfirm");
    const cancel = document.getElementById("dialogCancel");

    title.textContent = existing ? "Edit Snippet" : "Add Snippet";
    ok.textContent = existing ? "Save" : "Add";
    text.value = existing ? existing.text : "";
    cat.value = existing ? existing.category : "";
    overlay.classList.remove("hidden");
    setTimeout(() => text.focus(), 0);

    function cleanup() {
      overlay.classList.add("hidden");
      ok.onclick = null;
      cancel.onclick = null;
      overlay.onclick = null;
    }
    ok.onclick = () => {
      if (!text.value.trim()) return;
      const result = { text: text.value, category: cat.value.trim() };
      cleanup();
      resolve(result);
    };
    cancel.onclick = () => {
      cleanup();
      resolve(null);
    };
    overlay.onclick = (e) => {
      if (e.target === overlay) {
        cleanup();
        resolve(null);
      }
    };
  });
}

function showConfirm(title, body, okLabel, hideCancel) {
  return new Promise((resolve) => {
    const overlay = document.getElementById("confirmOverlay");
    document.getElementById("confirmTitle").textContent = title;
    document.getElementById("confirmBody").textContent = body;
    const ok = document.getElementById("confirmOk");
    const cancel = document.getElementById("confirmCancel");
    ok.textContent = okLabel || "OK";
    cancel.style.display = hideCancel ? "none" : "";
    overlay.classList.remove("hidden");

    function cleanup() {
      overlay.classList.add("hidden");
      ok.onclick = null;
      cancel.onclick = null;
      overlay.onclick = null;
    }
    ok.onclick = () => {
      cleanup();
      resolve(true);
    };
    cancel.onclick = () => {
      cleanup();
      resolve(false);
    };
    overlay.onclick = (e) => {
      if (e.target === overlay) {
        cleanup();
        resolve(false);
      }
    };
  });
}

// ----- menu -----
function updateMenuState() {
  document.getElementById("autoCaptureCheck").style.opacity = settings.autoCapture ? "1" : "0";
  document.querySelectorAll(".cap-item").forEach((b) => {
    const v = parseInt(b.dataset.cap, 10);
    b.classList.toggle("active", v === settings.historyCap);
  });
}

// ----- licensing / trial -----
let trialStartMs = 0;
let needsTrialStart = false;

// For previewing the screens on non-store builds: set localStorage
// "clipora_dev_license" to "start", "trial", or "expired".
function devLicenseMode() {
  return localStorage.getItem("clipora_dev_license");
}

async function initLicensing() {
  const dev = devLicenseMode();
  const storeBuild = isMac || !!dev;

  // Non-store builds (e.g. Windows dev) stay fully open; Windows monetization is
  // handled separately by the Microsoft Store.
  if (!storeBuild) {
    owned = true;
    hasAccess = true;
    needsTrialStart = false;
    updateLicenseUI(0);
    return;
  }

  if (dev) {
    owned = false;
    if (dev === "start") trialStartMs = 0;
    else if (dev === "trial") trialStartMs = Date.now() - 2 * 86400000;
    else trialStartMs = Date.now() - 8 * 86400000; // expired
    computeAccessAndUI();
    return;
  }

  try {
    const status = JSON.parse(await invoke("iap_status"));
    owned = !!status.owned;
    trialStartMs = Number(status.trialStartMs) || 0;
  } catch {
    owned = false;
    trialStartMs = 0;
  }
  computeAccessAndUI();
}

function computeAccessAndUI() {
  if (owned) {
    hasAccess = true;
    trialActive = false;
    needsTrialStart = false;
    updateLicenseUI(0);
    return;
  }
  if (trialStartMs > 0) {
    const msLeft = trialStartMs + TRIAL_DAYS * 86400000 - Date.now();
    trialActive = msLeft > 0;
    needsTrialStart = false;
    hasAccess = trialActive;
    updateLicenseUI(Math.max(0, Math.ceil(msLeft / 86400000)));
    return;
  }
  // No trial started yet, not owned.
  trialActive = false;
  needsTrialStart = true;
  hasAccess = false;
  updateLicenseUI(0);
}

function updateLicenseUI(daysLeft) {
  const startOverlay = document.getElementById("trialStartOverlay");
  const paywall = document.getElementById("paywallOverlay");
  const bar = document.getElementById("trialBar");
  const buyBtn = document.getElementById("paywallBuy");

  if (buyBtn) buyBtn.textContent = "Unlock Clipora - " + UNLOCK_PRICE_TEXT;

  // First-launch disclosure / start-trial screen.
  startOverlay.classList.toggle("hidden", !needsTrialStart);

  // Paywall only when the trial has ended and the app is not purchased.
  paywall.classList.toggle("hidden", !(!hasAccess && !needsTrialStart));

  // Bottom countdown bar while trialing.
  if (!owned && trialActive) {
    document.getElementById("trialText").textContent =
      daysLeft === 1 ? "1 day left in your free trial" : daysLeft + " days left in your free trial";
    bar.classList.remove("hidden");
  } else {
    bar.classList.add("hidden");
  }
}

async function startTrial() {
  hideLicenseError();
  if (devLicenseMode()) {
    trialStartMs = Date.now();
    computeAccessAndUI();
    return;
  }
  try {
    const ms = await invoke("iap_start_trial");
    trialStartMs = Number(ms) || Date.now();
    computeAccessAndUI();
  } catch (e) {
    showLicenseError(friendlyStoreError(e));
  }
}

async function buyUnlock() {
  hideLicenseError();
  try {
    const ok = await invoke("iap_buy");
    if (ok) {
      owned = true;
      computeAccessAndUI();
    }
  } catch (e) {
    showLicenseError(friendlyStoreError(e));
  }
}

async function restoreUnlock() {
  hideLicenseError();
  try {
    await invoke("iap_restore");
    const status = JSON.parse(await invoke("iap_status"));
    owned = !!status.owned;
    trialStartMs = Number(status.trialStartMs) || 0;
    if (!owned && trialStartMs === 0) {
      showLicenseError("No previous purchase was found for this Apple ID.");
    }
    computeAccessAndUI();
  } catch (e) {
    showLicenseError(friendlyStoreError(e));
  }
}

function friendlyStoreError(e) {
  const msg = String(e || "");
  if (msg.includes("store_unavailable")) return "The store is not reachable right now. Please try again.";
  if (msg.toLowerCase().includes("mac app store build")) return msg;
  return "Something went wrong with the store. Please try again.";
}

function showLicenseError(msg) {
  for (const id of ["paywallError", "trialStartError"]) {
    const el = document.getElementById(id);
    if (el) {
      el.textContent = msg;
      el.classList.remove("hidden");
    }
  }
}

function hideLicenseError() {
  for (const id of ["paywallError", "trialStartError"]) {
    const el = document.getElementById(id);
    if (el) el.classList.add("hidden");
  }
}

// ----- wiring -----
function wire() {
  document.getElementById("hotkeyHint").textContent = hotkeyLabel() + " to open";

  const menu = document.getElementById("menu");
  document.getElementById("menuButton").onclick = (e) => {
    e.stopPropagation();
    menu.classList.toggle("hidden");
  };
  document.addEventListener("click", (e) => {
    if (!menu.contains(e.target) && e.target.id !== "menuButton") menu.classList.add("hidden");
  });

  document.getElementById("menuAutoCapture").onclick = () => {
    settings.autoCapture = !settings.autoCapture;
    saveSettings();
    updateMenuState();
  };
  document.querySelectorAll(".cap-item").forEach((b) => {
    b.onclick = () => setCap(parseInt(b.dataset.cap, 10));
  });
  document.getElementById("menuImport").onclick = () => {
    menu.classList.add("hidden");
    importSnippets();
  };
  document.getElementById("menuExport").onclick = () => {
    menu.classList.add("hidden");
    exportSnippets();
  };
  document.getElementById("menuClear").onclick = () => {
    menu.classList.add("hidden");
    clearHistory();
  };

  document.getElementById("addButton").onclick = () => addSnippet();

  document.getElementById("startTrialBtn").onclick = () => startTrial();
  document.getElementById("startRestoreBtn").onclick = () => restoreUnlock();
  document.getElementById("paywallBuy").onclick = () => buyUnlock();
  document.getElementById("paywallRestore").onclick = () => restoreUnlock();
  document.getElementById("trialUnlock").onclick = () => buyUnlock();
  document.getElementById("searchBox").addEventListener("input", (e) => {
    searchText = e.target.value || "";
    applyFilter();
  });

  document.querySelectorAll(".flink").forEach((a) => {
    a.onclick = (e) => {
      e.preventDefault();
      invoke("open_url", { url: a.dataset.url });
    };
  });

  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape") {
      document.getElementById("dialogOverlay").classList.add("hidden");
      document.getElementById("confirmOverlay").classList.add("hidden");
      menu.classList.add("hidden");
    }
  });

  listen("clipboard-text", (e) => {
    if (!settings.autoCapture) return;
    const text = e.payload;
    if (!text || text === lastCaptured) return;
    addCapturedSnippet(text);
  });

  listen("show-window", () => {
    const sb = document.getElementById("searchBox");
    if (sb) sb.focus();
  });
}

wire();
load();
