//! Shared wire format for audio stream: header (sample rate, channels) then raw f32 PCM chunks.

use bytes::{BufMut, BytesMut};
use std::io;

pub const DEFAULT_PORT: u16 = 38472;
pub const SAMPLE_RATE: u32 = 44100;
pub const CHANNELS: u16 = 2;
pub const BYTES_PER_SAMPLE: usize = 4; // f32

/// Header sent once at stream start.
#[derive(Debug, Clone, Copy)]
pub struct StreamHeader {
    pub sample_rate: u32,
    pub channels: u16,
}

impl StreamHeader {
    pub const SIZE: usize = 4 + 2; // 6 bytes

    pub fn new(sample_rate: u32, channels: u16) -> Self {
        Self { sample_rate, channels }
    }

    pub fn default_format() -> Self {
        Self::new(SAMPLE_RATE, CHANNELS)
    }

    pub fn encode(&self, buf: &mut BytesMut) {
        buf.put_u32(self.sample_rate);
        buf.put_u16(self.channels);
    }

    pub fn decode(buf: &[u8]) -> io::Result<Self> {
        if buf.len() < Self::SIZE {
            return Err(io::Error::new(io::ErrorKind::UnexpectedEof, "header too short"));
        }
        let sample_rate = u32::from_be_bytes(buf[0..4].try_into().unwrap());
        let channels = u16::from_be_bytes(buf[4..6].try_into().unwrap());
        Ok(Self { sample_rate, channels })
    }
}

/// Encode a PCM chunk (raw f32 bytes) with a 4-byte length prefix.
pub fn encode_chunk(chunk: &[u8], buf: &mut BytesMut) {
    buf.put_u32(chunk.len() as u32);
    buf.extend_from_slice(chunk);
}

/// Decode chunk length from the beginning of a buffer. Returns (length, bytes_consumed).
pub fn decode_chunk_len(buf: &[u8]) -> io::Result<Option<(usize, usize)>> {
    if buf.len() < 4 {
        return Ok(None);
    }
    let len = u32::from_be_bytes(buf[0..4].try_into().unwrap()) as usize;
    Ok(Some((len, 4)))
}
