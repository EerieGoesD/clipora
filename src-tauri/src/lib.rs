use std::path::PathBuf;
use std::sync::Mutex;

use tauri::menu::{Menu, MenuItem, PredefinedMenuItem};
use tauri::tray::{MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent};
use tauri::{AppHandle, Emitter, Manager, State};

use tauri_plugin_clipboard_manager::ClipboardExt;

// Remembers the last clipboard text we have already seen, so the watcher does
// not re-capture text that the user just copied from inside Clipora, and so it
// does not capture whatever happened to be on the clipboard before launch.
struct AppState {
    last_clipboard: Mutex<String>,
}

fn data_dir(app: &AppHandle) -> PathBuf {
    let dir = app
        .path()
        .app_local_data_dir()
        .expect("could not resolve app data dir");
    std::fs::create_dir_all(&dir).ok();
    dir
}

fn snippets_path(app: &AppHandle) -> PathBuf {
    data_dir(app).join("snippets.json")
}

fn settings_path(app: &AppHandle) -> PathBuf {
    data_dir(app).join("settings.json")
}

#[tauri::command]
fn get_snippets(app: AppHandle) -> String {
    std::fs::read_to_string(snippets_path(&app)).unwrap_or_else(|_| "[]".to_string())
}

#[tauri::command]
fn set_snippets(app: AppHandle, data: String) -> Result<(), String> {
    std::fs::write(snippets_path(&app), data).map_err(|e| e.to_string())
}

#[tauri::command]
fn get_settings(app: AppHandle) -> String {
    std::fs::read_to_string(settings_path(&app))
        .unwrap_or_else(|_| "{\"historyCap\":50,\"autoCapture\":true}".to_string())
}

#[tauri::command]
fn set_settings(app: AppHandle, data: String) -> Result<(), String> {
    std::fs::write(settings_path(&app), data).map_err(|e| e.to_string())
}

#[tauri::command]
fn copy_to_clipboard(app: AppHandle, text: String, state: State<AppState>) -> Result<(), String> {
    app.clipboard()
        .write_text(text.clone())
        .map_err(|e| e.to_string())?;
    // Record it so the watcher does not treat our own copy as a new capture.
    *state.last_clipboard.lock().unwrap() = text;
    Ok(())
}

#[tauri::command]
async fn export_to_file(app: AppHandle, data: String) -> Result<bool, String> {
    use tauri_plugin_dialog::DialogExt;
    let file = app
        .dialog()
        .file()
        .set_file_name("clipora-export.json")
        .add_filter("JSON", &["json"])
        .blocking_save_file();
    match file {
        Some(path) => {
            let p = path.into_path().map_err(|e| e.to_string())?;
            std::fs::write(p, data).map_err(|e| e.to_string())?;
            Ok(true)
        }
        None => Ok(false),
    }
}

#[tauri::command]
async fn import_from_file(app: AppHandle) -> Result<Option<String>, String> {
    use tauri_plugin_dialog::DialogExt;
    let file = app
        .dialog()
        .file()
        .add_filter("JSON", &["json"])
        .blocking_pick_file();
    match file {
        Some(path) => {
            let p = path.into_path().map_err(|e| e.to_string())?;
            let content = std::fs::read_to_string(p).map_err(|e| e.to_string())?;
            Ok(Some(content))
        }
        None => Ok(None),
    }
}

#[tauri::command]
fn open_url(app: AppHandle, url: String) -> Result<(), String> {
    use tauri_plugin_opener::OpenerExt;
    app.opener()
        .open_url(url, None::<&str>)
        .map_err(|e| e.to_string())
}

// The app version (from tauri.conf.json), shown in the footer.
#[tauri::command]
fn app_version(app: AppHandle) -> String {
    app.package_info().version.to_string()
}

// ----- licensing / trial + one-time unlock -----
//
// Two App Store in-app purchases:
//   - TRIAL_ID  : free ($0) non-consumable. "Buying" it starts the 7-day trial,
//                 tied to the Apple ID, so reinstalling does not reset it.
//   - UNLOCK_ID : the paid one-time unlock.
// On macOS the source of truth is StoreKit; license.json is just a local cache.

#[allow(dead_code)]
const UNLOCK_ID: &str = "com.eeriegoesd.clipora.unlock";
#[allow(dead_code)]
const TRIAL_ID: &str = "com.eeriegoesd.clipora.trial";

fn license_path(app: &AppHandle) -> PathBuf {
    data_dir(app).join("license.json")
}

// Returns (owned, trial_start_ms). trial_start_ms is 0 when the trial has not
// been started.
fn read_license(app: &AppHandle) -> (bool, i64) {
    if let Ok(s) = std::fs::read_to_string(license_path(app)) {
        if let Ok(v) = serde_json::from_str::<serde_json::Value>(&s) {
            let owned = v.get("owned").and_then(|x| x.as_bool()).unwrap_or(false);
            let trial = v.get("trialStartMs").and_then(|x| x.as_i64()).unwrap_or(0);
            return (owned, trial);
        }
    }
    (false, 0)
}

#[allow(dead_code)]
fn write_license(app: &AppHandle, owned: bool, trial_start_ms: i64) {
    let _ = std::fs::write(
        license_path(app),
        format!("{{\"owned\":{},\"trialStartMs\":{}}}", owned, trial_start_ms),
    );
}

// Current time in ms since the Unix epoch (fallback for the trial start time).
#[allow(dead_code)]
fn now_ms() -> i64 {
    use std::time::{SystemTime, UNIX_EPOCH};
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_millis() as i64)
        .unwrap_or(0)
}

// owned = paid unlock; trialStartMs = when the free trial began (0 if not yet).
// On macOS this refreshes from StoreKit (the source of truth) and caches it.
#[tauri::command]
async fn iap_status(app: AppHandle) -> String {
    #[cfg(target_os = "macos")]
    {
        use tauri_plugin_iap::IapExt;
        let (mut owned, mut trial) = read_license(&app);
        if let Ok(st) = app
            .iap()
            .get_product_status(UNLOCK_ID.to_string(), "inapp".to_string())
            .await
        {
            owned = st.is_owned;
        }
        if let Ok(st) = app
            .iap()
            .get_product_status(TRIAL_ID.to_string(), "inapp".to_string())
            .await
        {
            if st.is_owned {
                if let Some(pt) = st.purchase_time {
                    trial = pt;
                }
            }
        }
        write_license(&app, owned, trial);
        return format!("{{\"owned\":{},\"trialStartMs\":{}}}", owned, trial);
    }
    #[cfg(not(target_os = "macos"))]
    {
        let (owned, trial) = read_license(&app);
        format!("{{\"owned\":{},\"trialStartMs\":{}}}", owned, trial)
    }
}

// Localized App Store price of the unlock (e.g. "9,99 EUR"). Empty if unknown.
// Never hardcode a price; the store returns the right currency/amount per region.
#[tauri::command]
async fn iap_price(app: AppHandle) -> String {
    #[cfg(target_os = "macos")]
    {
        use tauri_plugin_iap::IapExt;
        if let Ok(resp) = app
            .iap()
            .get_products(vec![UNLOCK_ID.to_string()], "inapp".to_string())
            .await
        {
            if let Some(p) = resp.products.first() {
                return p.formatted_price.clone();
            }
        }
        return String::new();
    }
    #[cfg(not(target_os = "macos"))]
    {
        let _ = &app;
        String::new()
    }
}

// Starts the free trial by "buying" the $0 TRIAL_ID. Returns the trial start (ms).
#[tauri::command]
async fn iap_start_trial(app: AppHandle) -> Result<i64, String> {
    #[cfg(target_os = "macos")]
    {
        use tauri_plugin_iap::{IapExt, PurchaseRequest};
        app.iap()
            .purchase(PurchaseRequest {
                product_id: TRIAL_ID.to_string(),
                product_type: "inapp".to_string(),
                options: None,
            })
            .await
            .map_err(|e| e.to_string())?;
        let mut start = now_ms();
        if let Ok(st) = app
            .iap()
            .get_product_status(TRIAL_ID.to_string(), "inapp".to_string())
            .await
        {
            if let Some(pt) = st.purchase_time {
                start = pt;
            }
        }
        let owned = read_license(&app).0;
        write_license(&app, owned, start);
        return Ok(start);
    }
    #[cfg(not(target_os = "macos"))]
    {
        let _ = &app;
        Err("Purchases are only available in the Mac App Store build.".into())
    }
}

// Buys the paid one-time unlock (UNLOCK_ID). Returns true if owned afterward.
#[tauri::command]
async fn iap_buy(app: AppHandle) -> Result<bool, String> {
    #[cfg(target_os = "macos")]
    {
        use tauri_plugin_iap::{IapExt, PurchaseRequest};
        app.iap()
            .purchase(PurchaseRequest {
                product_id: UNLOCK_ID.to_string(),
                product_type: "inapp".to_string(),
                options: None,
            })
            .await
            .map_err(|e| e.to_string())?;
        let mut owned = false;
        if let Ok(st) = app
            .iap()
            .get_product_status(UNLOCK_ID.to_string(), "inapp".to_string())
            .await
        {
            owned = st.is_owned;
        }
        if owned {
            let trial = read_license(&app).1;
            write_license(&app, true, trial);
        }
        return Ok(owned);
    }
    #[cfg(not(target_os = "macos"))]
    {
        let _ = &app;
        Err("Purchases are only available in the Mac App Store build.".into())
    }
}

// Restores previous purchases (unlock and/or trial) for this Apple ID.
#[tauri::command]
async fn iap_restore(app: AppHandle) -> Result<(), String> {
    #[cfg(target_os = "macos")]
    {
        use tauri_plugin_iap::IapExt;
        app.iap()
            .restore_purchases("inapp".to_string())
            .await
            .map_err(|e| e.to_string())?;
        let mut owned = false;
        if let Ok(st) = app
            .iap()
            .get_product_status(UNLOCK_ID.to_string(), "inapp".to_string())
            .await
        {
            owned = st.is_owned;
        }
        let mut trial = 0i64;
        if let Ok(st) = app
            .iap()
            .get_product_status(TRIAL_ID.to_string(), "inapp".to_string())
            .await
        {
            if st.is_owned {
                if let Some(pt) = st.purchase_time {
                    trial = pt;
                }
            }
        }
        write_license(&app, owned, trial);
        return Ok(());
    }
    #[cfg(not(target_os = "macos"))]
    {
        let _ = &app;
        Err("Purchases are only available in the Mac App Store build.".into())
    }
}

// Shows, restores and focuses the main window, placing it at the top-right.
fn show_main_window(app: &AppHandle) {
    if let Some(win) = app.get_webview_window("main") {
        position_top_right(&win);
        let _ = win.show();
        let _ = win.unminimize();
        let _ = win.set_focus();
        let _ = app.emit("show-window", ());
    }
}

fn position_top_right(win: &tauri::WebviewWindow) {
    let monitor = match win.current_monitor() {
        Ok(Some(m)) => Some(m),
        _ => win.primary_monitor().ok().flatten(),
    };
    if let Some(monitor) = monitor {
        let m_pos = monitor.position();
        let m_size = monitor.size();
        if let Ok(win_size) = win.outer_size() {
            let margin = 12i32;
            let x = m_pos.x + m_size.width as i32 - win_size.width as i32 - margin;
            let y = m_pos.y + margin;
            let _ = win.set_position(tauri::PhysicalPosition::new(x.max(m_pos.x), y));
        }
    }
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    let mut builder = tauri::Builder::default();

    #[cfg(desktop)]
    {
        builder = builder.plugin(tauri_plugin_single_instance::init(|app, _argv, _cwd| {
            show_main_window(app);
        }));
    }

    #[cfg(target_os = "macos")]
    {
        builder = builder.plugin(tauri_plugin_iap::init());
    }

    builder
        .plugin(tauri_plugin_opener::init())
        .plugin(tauri_plugin_clipboard_manager::init())
        .plugin(tauri_plugin_dialog::init())
        .plugin(
            tauri_plugin_global_shortcut::Builder::new()
                .with_handler(|app, _shortcut, event| {
                    use tauri_plugin_global_shortcut::ShortcutState;
                    if event.state() == ShortcutState::Pressed {
                        show_main_window(app);
                    }
                })
                .build(),
        )
        .manage(AppState {
            last_clipboard: Mutex::new(String::new()),
        })
        .invoke_handler(tauri::generate_handler![
            get_snippets,
            set_snippets,
            get_settings,
            set_settings,
            copy_to_clipboard,
            export_to_file,
            import_from_file,
            open_url,
            app_version,
            iap_status,
            iap_price,
            iap_start_trial,
            iap_buy,
            iap_restore
        ])
        .setup(|app| {
            let handle = app.handle().clone();

            // Seed last-seen clipboard with whatever is there now, so we do not
            // auto-capture pre-existing clipboard content on launch.
            if let Ok(cur) = handle.clipboard().read_text() {
                *handle.state::<AppState>().last_clipboard.lock().unwrap() = cur;
            }

            // System tray icon with Open / Exit menu.
            let open_i = MenuItem::with_id(app, "open", "Open", true, None::<&str>)?;
            let exit_i = MenuItem::with_id(app, "exit", "Exit", true, None::<&str>)?;
            let sep = PredefinedMenuItem::separator(app)?;
            let menu = Menu::with_items(app, &[&open_i, &sep, &exit_i])?;

            TrayIconBuilder::new()
                .icon(app.default_window_icon().unwrap().clone())
                .tooltip("Clipora (Ctrl+Shift+V)")
                .menu(&menu)
                .show_menu_on_left_click(false)
                .on_menu_event(|app, event| match event.id().as_ref() {
                    "open" => show_main_window(app),
                    "exit" => app.exit(0),
                    _ => {}
                })
                .on_tray_icon_event(|tray, event| {
                    if let TrayIconEvent::Click {
                        button: MouseButton::Left,
                        button_state: MouseButtonState::Up,
                        ..
                    } = event
                    {
                        show_main_window(tray.app_handle());
                    }
                })
                .build(app)?;

            // Global hotkey: Cmd+Shift+V on macOS, Ctrl+Shift+V on Windows.
            #[cfg(desktop)]
            {
                use tauri_plugin_global_shortcut::GlobalShortcutExt;
                app.global_shortcut().register("CmdOrCtrl+Shift+V")?;
            }

            // Closing the window hides it to the tray instead of quitting.
            if let Some(win) = app.get_webview_window("main") {
                let w = win.clone();
                win.on_window_event(move |event| {
                    if let tauri::WindowEvent::CloseRequested { api, .. } = event {
                        api.prevent_close();
                        let _ = w.hide();
                    }
                });
                position_top_right(&win);
            }

            // Clipboard watcher: poll for changes and tell the UI to auto-capture.
            let mon = app.handle().clone();
            std::thread::spawn(move || loop {
                std::thread::sleep(std::time::Duration::from_millis(700));
                if let Ok(text) = mon.clipboard().read_text() {
                    if text.is_empty() {
                        continue;
                    }
                    let state = mon.state::<AppState>();
                    let changed = {
                        let mut last = state.last_clipboard.lock().unwrap();
                        if *last != text {
                            *last = text.clone();
                            true
                        } else {
                            false
                        }
                    };
                    if changed {
                        let _ = mon.emit("clipboard-text", text);
                    }
                }
            });

            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
