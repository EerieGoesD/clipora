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
            open_url
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
