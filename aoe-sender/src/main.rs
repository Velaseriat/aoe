//! AOE Sender (Beta): Captures Windows system audio via WASAPI loopback and streams to Alpha over TCP.
//! System tray shows status: Idle / Streaming.

#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use aoe_protocol::{encode_chunk, StreamHeader, DEFAULT_PORT, CHANNELS, SAMPLE_RATE};
use image::{ImageBuffer, Rgba};
use std::io::Write;
use std::net::TcpStream;
use std::sync::atomic::{AtomicBool, AtomicU8, Ordering};
use std::sync::Arc;
use std::thread;
use std::time::Duration;

const CHUNK_FRAMES: usize = 2048;
const STATUS_IDLE: u8 = 0;
const STATUS_STREAMING: u8 = 1;
const STATUS_ERROR: u8 = 2;

fn main() {
    let connect_addr = std::env::args()
        .nth(1)
        .unwrap_or_else(|| "127.0.0.1".to_string());
    let port = std::env::args()
        .nth(2)
        .and_then(|s| s.parse().ok())
        .unwrap_or(DEFAULT_PORT);
    let target = format!("{}:{}", connect_addr, port);

    let status = Arc::new(AtomicU8::new(STATUS_IDLE));
    let running = Arc::new(AtomicBool::new(true));
    let status_clone = Arc::clone(&status);
    let running_clone = Arc::clone(&running);

    let tray_handle = thread::spawn(move || run_tray(status_clone, running_clone));

    // Capture + stream on Windows
    #[cfg(windows)]
    {
        if let Err(e) = run_capture_and_stream(target, Arc::clone(&status), Arc::clone(&running)) {
            status.store(STATUS_ERROR, Ordering::SeqCst);
            eprintln!("Error: {}", e);
        }
    }

    #[cfg(not(windows))]
    {
        eprintln!("aoe-sender is only supported on Windows (WASAPI loopback)");
        status.store(STATUS_ERROR, Ordering::SeqCst);
    }

    running.store(false, Ordering::SeqCst);
    let _ = tray_handle.join();
}

#[cfg(windows)]
fn run_capture_and_stream(
    target: String,
    status: Arc<AtomicU8>,
    running: Arc<AtomicBool>,
) -> Result<(), Box<dyn std::error::Error + Send + Sync>> {
    let _ = wasapi::initialize_mta().ok();
    let enumerator = wasapi::DeviceEnumerator::new()?;
    // Loopback: default *render* (playback) device, then capture from it
    let device = enumerator.get_default_device(&wasapi::Direction::Render)?;
    let mut audio_client = device.get_iaudioclient()?;
    let desired_format = wasapi::WaveFormat::new(
        32,
        32,
        &wasapi::SampleType::Float,
        SAMPLE_RATE as usize,
        CHANNELS as usize,
        None,
    );
    let blockalign = desired_format.get_blockalign();
    let (_, min_time) = audio_client.get_device_period()?;
    let mode = wasapi::StreamMode::EventsShared {
        autoconvert: true,
        buffer_duration_hns: min_time,
    };
    // Capture direction on a render device = loopback
    audio_client.initialize_client(&desired_format, &wasapi::Direction::Capture, &mode)?;
    let h_event = audio_client.set_get_eventhandle()?;
    let buffer_frame_count = audio_client.get_buffer_size()?;
    let capture_client = audio_client.get_audiocaptureclient()?;
    let mut sample_queue = std::collections::VecDeque::with_capacity(
        blockalign as usize * (CHUNK_FRAMES * 4 + 2 * buffer_frame_count as usize),
    );
    audio_client.start_stream()?;

    let chunk_bytes = CHUNK_FRAMES * (blockalign as usize);
    loop {
        if !running.load(Ordering::SeqCst) {
            break;
        }
        // Try to connect and stream
        match TcpStream::connect_timeout(
            &target.parse().unwrap(),
            std::time::Duration::from_secs(2),
        ) {
            Ok(mut stream) => {
                stream.set_read_timeout(Some(Duration::from_millis(100))).ok();
                stream.set_write_timeout(Some(Duration::from_millis(500))).ok();
                let header = StreamHeader::default_format();
                let mut hdr = bytes::BytesMut::with_capacity(StreamHeader::SIZE);
                header.encode(&mut hdr);
                stream.write_all(&hdr)?;
                status.store(STATUS_STREAMING, Ordering::SeqCst);
                while running.load(Ordering::SeqCst) {
                    while sample_queue.len() < chunk_bytes {
                        capture_client.read_from_device_to_deque(&mut sample_queue)?;
                        if h_event.wait_for_event(200).is_err() {
                            break;
                        }
                    }
                    if sample_queue.len() < chunk_bytes {
                        continue;
                    }
                    let mut c = vec![0u8; chunk_bytes];
                    for b in c.iter_mut() {
                        *b = sample_queue.pop_front().unwrap_or(0);
                    }
                    let mut buf = bytes::BytesMut::new();
                    encode_chunk(&c, &mut buf);
                    if stream.write_all(&buf).is_err() {
                        break;
                    }
                }
                status.store(STATUS_IDLE, Ordering::SeqCst);
            }
            Err(_) => {
                thread::sleep(Duration::from_secs(2));
            }
        }
    }
    audio_client.stop_stream().ok();
    Ok(())
}

fn run_tray(status: Arc<AtomicU8>, running: Arc<AtomicBool>) {
    use tao::event_loop::{ControlFlow, EventLoopBuilder};
    use tray_icon::menu::{Menu, MenuEvent, MenuItem};
    use tray_icon::{TrayIconBuilder, TrayIconEvent};

    let event_loop = EventLoopBuilder::with_user_event().build();
    let proxy = event_loop.create_proxy();

    let icon = create_icon([0, 120, 220, 255]);
    let quit_i = MenuItem::with_id("quit", "Quit", true, None);
    let menu = Menu::new();
    menu.append(&quit_i).unwrap();
    let tray = TrayIconBuilder::new()
        .with_menu(Box::new(menu))
        .with_tooltip("AOE Sender - Idle")
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
                STATUS_STREAMING => "AOE Sender - Streaming",
                STATUS_ERROR => "AOE Sender - Error",
                _ => "AOE Sender - Idle",
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
