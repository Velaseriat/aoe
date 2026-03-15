//! AOE Receiver (Alpha): Listens for TCP stream from Beta, plays audio via cpal, system tray status.

#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use aoe_protocol::{decode_chunk_len, StreamHeader, DEFAULT_PORT};
use cpal::traits::{DeviceTrait, HostTrait, StreamTrait};
use image::{ImageBuffer, Rgba};
use std::io::Read;
use std::net::TcpListener;
use std::sync::atomic::{AtomicBool, AtomicU8, Ordering};
use std::sync::Arc;
use std::thread;
use std::time::Duration;

const STATUS_IDLE: u8 = 0;
const STATUS_CONNECTED: u8 = 1;
const STATUS_ERROR: u8 = 2;

fn main() {
    let port = std::env::args()
        .nth(1)
        .and_then(|s| s.parse().ok())
        .unwrap_or(DEFAULT_PORT);

    let status = Arc::new(AtomicU8::new(STATUS_IDLE));
    let running = Arc::new(AtomicBool::new(true));
    let status_clone = Arc::clone(&status);
    let running_clone = Arc::clone(&running);

    let tray_handle = thread::spawn(move || run_tray(status_clone, running_clone));

    if let Err(e) = run_listen_and_play(port, Arc::clone(&status), Arc::clone(&running)) {
        status.store(STATUS_ERROR, Ordering::SeqCst);
        eprintln!("Error: {}", e);
    }

    running.store(false, Ordering::SeqCst);
    let _ = tray_handle.join();
}

fn run_listen_and_play(
    port: u16,
    status: Arc<AtomicU8>,
    running: Arc<AtomicBool>,
) -> Result<(), Box<dyn std::error::Error + Send + Sync>> {
    let listener = TcpListener::bind(format!("0.0.0.0:{}", port))?;
    listener.set_nonblocking(true)?;
    eprintln!("AOE Receiver listening on port {}", port);

    let sample_queue: Arc<std::sync::Mutex<std::collections::VecDeque<f32>>> =
        Arc::new(std::sync::Mutex::new(std::collections::VecDeque::new()));

    let host = cpal::default_host();
    let device = host
        .default_output_device()
        .ok_or("no default output device")?;
    let default_config = device.default_output_config()?;
    let config = default_config.config().clone();
    let queue = Arc::clone(&sample_queue);
    let stream = device.build_output_stream(
        &config,
        move |data: &mut [f32], _: &cpal::OutputCallbackInfo| {
            let mut q = queue.lock().unwrap();
            for s in data.iter_mut() {
                *s = q.pop_front().unwrap_or(0.0);
            }
        },
        |err| eprintln!("playback error: {}", err),
        None,
    )?;
    stream.play()?;

    let mut read_buf = vec![0u8; 64 * 1024];
    let mut decode_buf: Vec<u8> = Vec::new();

    while running.load(Ordering::SeqCst) {
        match listener.accept() {
            Ok((mut stream, addr)) => {
                eprintln!("Connection from {}", addr);
                stream.set_read_timeout(Some(Duration::from_millis(500))).ok();
                stream.set_write_timeout(Some(Duration::from_millis(500))).ok();

                let mut header = [0u8; StreamHeader::SIZE];
                if stream.read_exact(&mut header).is_err() {
                    continue;
                }
                let h = match StreamHeader::decode(&header) {
                    Ok(h) => h,
                    Err(_) => continue,
                };
                eprintln!("Stream format: {} Hz, {} channels", h.sample_rate, h.channels);
                status.store(STATUS_CONNECTED, Ordering::SeqCst);
                decode_buf.clear();

                while running.load(Ordering::SeqCst) {
                    let n = match stream.read(&mut read_buf) {
                        Ok(0) => break,
                        Ok(n) => n,
                        Err(_) => {
                            thread::sleep(Duration::from_millis(10));
                            continue;
                        }
                    };
                    decode_buf.extend_from_slice(&read_buf[..n]);
                    loop {
                        let (chunk_len, prefix_len) = match decode_chunk_len(&decode_buf) {
                            Ok(Some(x)) => x,
                            Ok(None) | Err(_) => break,
                        };
                        if decode_buf.len() < prefix_len + chunk_len {
                            break;
                        }
                        let chunk = decode_buf[prefix_len..prefix_len + chunk_len].to_vec();
                        decode_buf.drain(..prefix_len + chunk_len);
                        let samples: Vec<f32> = chunk
                            .chunks_exact(4)
                            .map(|b| f32::from_le_bytes([b[0], b[1], b[2], b[3]]))
                            .collect();
                        let mut q = sample_queue.lock().unwrap();
                        for s in samples {
                            q.push_back(s);
                        }
                        while q.len() > 44100 * 2 {
                            q.pop_front();
                        }
                    }
                }
                status.store(STATUS_IDLE, Ordering::SeqCst);
            }
            Err(ref e) if e.kind() == std::io::ErrorKind::WouldBlock => {
                thread::sleep(Duration::from_millis(100));
            }
            Err(e) => return Err(e.into()),
        }
    }
    Ok(())
}

fn run_tray(status: Arc<AtomicU8>, running: Arc<AtomicBool>) {
    use tao::event_loop::{ControlFlow, EventLoopBuilder};
    use tray_icon::menu::{Menu, MenuEvent, MenuItem};
    use tray_icon::{TrayIconBuilder, TrayIconEvent};

    let event_loop = EventLoopBuilder::with_user_event().build();
    let proxy = event_loop.create_proxy();

    let icon = create_icon([20, 180, 100, 255]);
    let quit_i = MenuItem::with_id("quit", "Quit", true, None);
    let menu = Menu::new();
    menu.append(&quit_i).unwrap();
    let tray = TrayIconBuilder::new()
        .with_menu(Box::new(menu))
        .with_tooltip("AOE Receiver - Waiting")
        .with_icon(icon)
        .build()
        .expect("tray");

    let tray_clone = tray.clone();
    let proxy_poll = event_loop.create_proxy();
    let running_poll = Arc::clone(&running);
    let running_menu = Arc::clone(&running);
    let proxy_menu = proxy.clone();
    thread::spawn(move || {
        while running_poll.load(Ordering::SeqCst) {
            let _ = proxy_poll.send_event(());
            thread::sleep(Duration::from_millis(500));
        }
    });
    TrayIconEvent::set_event_handler(Some(move |_| {
        let _ = proxy.send_event(());
    }));
    MenuEvent::set_event_handler(Some(move |e: tray_icon::menu::MenuEvent| {
        if e.id.as_ref() == "quit" {
            running_menu.store(false, Ordering::SeqCst);
            let _ = proxy_menu.send_event(());
        }
    }));

    let mut last_status = STATUS_IDLE;
    event_loop.run(move |event, _, control_flow| {
        *control_flow = ControlFlow::Wait;
        let s = status.load(Ordering::SeqCst);
        if s != last_status {
            last_status = s;
            let tip = match s {
                STATUS_CONNECTED => "AOE Receiver - Connected",
                STATUS_ERROR => "AOE Receiver - Error",
                _ => "AOE Receiver - Waiting",
            };
            let _ = tray_clone.set_tooltip::<&str>(Some(tip));
        }
        match event {
            tao::event::Event::UserEvent(_) => {}
            tao::event::Event::LoopDestroyed => return,
            _ => {}
        }
        if !running.load(Ordering::SeqCst) {
            *control_flow = ControlFlow::Exit;
        }
    });
}

fn create_icon(rgba: [u8; 4]) -> tray_icon::Icon {
    let (w, h) = (16, 16);
    let mut im = ImageBuffer::new(w, h);
    for y in 0..h {
        for x in 0..w {
            im.put_pixel(x, y, Rgba(rgba));
        }
    }
    let rgba = im.into_raw();
    tray_icon::Icon::from_rgba(rgba, 16, 16).expect("icon")
}
