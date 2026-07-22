use crate::{
    load_runtime_libraries, load_symbol, MAX_DECODED_FRAME_PIXELS, MESSAGE_CAPACITY,
    STATUS_INVALID_ARGUMENT, STATUS_LIBRARY_LOAD_FAILED, STATUS_OK,
    STATUS_RUNTIME_DIRECTORY_MISSING, STATUS_SYMBOL_LOAD_FAILED,
};
use std::ffi::{CStr, CString};
use std::os::raw::{c_char, c_int, c_void};
use std::path::Path;
#[cfg(test)]
use std::sync::atomic::AtomicUsize;
use std::sync::atomic::{AtomicI32, Ordering};

const STATUS_FILE_OPEN_FAILED: c_int = 5;
const STATUS_STREAM_UNAVAILABLE: c_int = 6;
const STATUS_DECODER_UNAVAILABLE: c_int = 7;
const STATUS_CODEC_CONTEXT_ALLOC_FAILED: c_int = 8;
const STATUS_CODEC_CONTEXT_FAILED: c_int = 9;
const STATUS_PACKET_ALLOC_FAILED: c_int = 10;
const STATUS_FRAME_ALLOC_FAILED: c_int = 11;
const STATUS_PACKET_READ_FAILED: c_int = 12;
const STATUS_PACKET_SEND_FAILED: c_int = 13;
const STATUS_FRAME_RECEIVE_FAILED: c_int = 14;
const STATUS_CANCELLED: c_int = 15;
const STATUS_CONVERSION_FAILED: c_int = 16;
const STATUS_SEEK_FAILED: c_int = 17;
const STATUS_ANCHOR_NOT_FOUND: c_int = 18;
const STATUS_TARGET_NOT_FOUND: c_int = 19;
const STATUS_RESOURCE_LIMIT_EXCEEDED: c_int = 20;

const DEFAULT_MAX_FRAME_BUFFER_BYTES: usize = 256 * 1024 * 1024;
const MAX_DECODE_WINDOW_FRAME_COUNT: usize = 500_000;
#[cfg(test)]
static TEST_FREED_FRAME_BUFFER_COUNT: AtomicUsize = AtomicUsize::new(0);

const AVERROR_EOF: c_int = -541_478_725;
#[cfg(target_os = "macos")]
const AVERROR_EAGAIN: c_int = -35;
#[cfg(not(target_os = "macos"))]
const AVERROR_EAGAIN: c_int = -11;
const AV_NOPTS_VALUE: i64 = i64::MIN;
const AV_FRAME_FLAG_KEY: c_int = 0x0002;
const AVSEEK_FLAG_BACKWARD: c_int = 1;
const AV_PIX_FMT_BGRA: c_int = 28;
const SWS_BILINEAR: c_int = 2;

const AVFORMAT_CONTEXT_NB_STREAMS_OFFSET: usize = 44;
const AVFORMAT_CONTEXT_STREAMS_OFFSET: usize = 48;
const AVSTREAM_CODECPAR_OFFSET: usize = 16;
const AVSTREAM_TIME_BASE_OFFSET: usize = 32;
const AVSTREAM_AVG_FRAME_RATE_OFFSET: usize = 88;
const AVSTREAM_R_FRAME_RATE_OFFSET: usize = 204;
const AVCODEC_PARAMETERS_CODEC_ID_OFFSET: usize = 4;
const AVCODEC_CONTEXT_PKT_TIMEBASE_OFFSET: usize = 92;
const AVCODEC_CONTEXT_FRAMERATE_OFFSET: usize = 100;
const AVCODEC_CONTEXT_MAX_PIXELS_OFFSET: usize = 792;
const AVPACKET_STREAM_INDEX_OFFSET: usize = 36;
const AVFRAME_WIDTH_OFFSET: usize = 104;
const AVFRAME_HEIGHT_OFFSET: usize = 108;
const AVFRAME_FORMAT_OFFSET: usize = 116;
const AVFRAME_PTS_OFFSET: usize = 136;
const AVFRAME_PKT_DTS_OFFSET: usize = 144;
const AVFRAME_FLAGS_OFFSET: usize = 276;
const AVFRAME_BEST_EFFORT_TIMESTAMP_OFFSET: usize = 304;
const AVFRAME_DURATION_OFFSET: usize = 408;

type AvStrErrorFn = unsafe extern "C" fn(c_int, *mut c_char, usize) -> c_int;
type AvformatOpenInputFn =
    unsafe extern "C" fn(*mut *mut c_void, *const c_char, *mut c_void, *mut *mut c_void) -> c_int;
type AvformatFindStreamInfoFn = unsafe extern "C" fn(*mut c_void, *mut *mut c_void) -> c_int;
type AvformatCloseInputFn = unsafe extern "C" fn(*mut *mut c_void);
type AvReadFrameFn = unsafe extern "C" fn(*mut c_void, *mut c_void) -> c_int;
type AvSeekFrameFn = unsafe extern "C" fn(*mut c_void, c_int, i64, c_int) -> c_int;
type AvGuessFrameRateFn = unsafe extern "C" fn(*mut c_void, *mut c_void, *mut c_void) -> AVRational;
type AvGuessSampleAspectRatioFn =
    unsafe extern "C" fn(*mut c_void, *mut c_void, *mut c_void) -> AVRational;
type AvCodecFindDecoderFn = unsafe extern "C" fn(c_int) -> *mut c_void;
type AvCodecAllocContext3Fn = unsafe extern "C" fn(*const c_void) -> *mut c_void;
type AvCodecParametersToContextFn = unsafe extern "C" fn(*mut c_void, *const c_void) -> c_int;
type AvCodecOpen2Fn = unsafe extern "C" fn(*mut c_void, *const c_void, *mut *mut c_void) -> c_int;
type AvCodecFreeContextFn = unsafe extern "C" fn(*mut *mut c_void);
type AvCodecSendPacketFn = unsafe extern "C" fn(*mut c_void, *const c_void) -> c_int;
type AvCodecReceiveFrameFn = unsafe extern "C" fn(*mut c_void, *mut c_void) -> c_int;
type AvCodecFlushBuffersFn = unsafe extern "C" fn(*mut c_void);
type AvPacketAllocFn = unsafe extern "C" fn() -> *mut c_void;
type AvPacketFreeFn = unsafe extern "C" fn(*mut *mut c_void);
type AvPacketUnrefFn = unsafe extern "C" fn(*mut c_void);
type AvFrameAllocFn = unsafe extern "C" fn() -> *mut c_void;
type AvFrameFreeFn = unsafe extern "C" fn(*mut *mut c_void);
type AvFrameUnrefFn = unsafe extern "C" fn(*mut c_void);
type AvFrameApplyCroppingFn = unsafe extern "C" fn(*mut c_void, c_int) -> c_int;
type SwsGetCachedContextFn = unsafe extern "C" fn(
    *mut c_void,
    c_int,
    c_int,
    c_int,
    c_int,
    c_int,
    c_int,
    c_int,
    *mut c_void,
    *mut c_void,
    *const f64,
) -> *mut c_void;
type SwsScaleFn = unsafe extern "C" fn(
    *mut c_void,
    *const *const u8,
    *const c_int,
    c_int,
    c_int,
    *const *mut u8,
    *const c_int,
) -> c_int;
type SwsFreeContextFn = unsafe extern "C" fn(*mut c_void);

#[repr(C)]
#[derive(Clone, Copy)]
pub struct AVRational {
    pub num: c_int,
    pub den: c_int,
}

#[repr(C)]
pub struct FramePlayerRustDecodeCoreIndexEntry {
    pub absolute_frame_index: i64,
    pub presentation_timestamp: i64,
    pub decode_timestamp: i64,
    pub search_timestamp: i64,
    pub seek_anchor_frame_index: i64,
    pub seek_anchor_timestamp: i64,
}

#[repr(C)]
pub struct FramePlayerRustNativeFrame {
    pub absolute_frame_index: i64,
    pub presentation_timestamp: i64,
    pub decode_timestamp: i64,
    pub duration_timestamp: i64,
    pub is_key_frame: c_int,
    pub pixel_buffer: *mut RustFrameBuffer,
    pub pixel_data: *const u8,
    pub pixel_buffer_len: usize,
    pub stride: c_int,
    pub width: c_int,
    pub height: c_int,
    pub display_width: c_int,
    pub display_height: c_int,
    pub source_pixel_format: c_int,
}

#[repr(C)]
pub struct FramePlayerRustDecodeWindowResult {
    pub status: c_int,
    pub frames: *mut FramePlayerRustNativeFrame,
    pub frame_count: u64,
    pub current_index: c_int,
    pub message: [c_char; MESSAGE_CAPACITY],
}

impl Default for FramePlayerRustDecodeWindowResult {
    fn default() -> Self {
        Self {
            status: STATUS_INVALID_ARGUMENT,
            frames: std::ptr::null_mut(),
            frame_count: 0,
            current_index: -1,
            message: [0 as c_char; MESSAGE_CAPACITY],
        }
    }
}

#[repr(C)]
pub struct FramePlayerRustFrameConvertResult {
    pub status: c_int,
    pub frame: FramePlayerRustNativeFrame,
    pub message: [c_char; MESSAGE_CAPACITY],
}

impl Default for FramePlayerRustFrameConvertResult {
    fn default() -> Self {
        Self {
            status: STATUS_INVALID_ARGUMENT,
            frame: FramePlayerRustNativeFrame::default(),
            message: [0 as c_char; MESSAGE_CAPACITY],
        }
    }
}

impl Default for FramePlayerRustNativeFrame {
    fn default() -> Self {
        Self {
            absolute_frame_index: -1,
            presentation_timestamp: AV_NOPTS_VALUE,
            decode_timestamp: AV_NOPTS_VALUE,
            duration_timestamp: AV_NOPTS_VALUE,
            is_key_frame: 0,
            pixel_buffer: std::ptr::null_mut(),
            pixel_data: std::ptr::null(),
            pixel_buffer_len: 0,
            stride: 0,
            width: 0,
            height: 0,
            display_width: 0,
            display_height: 0,
            source_pixel_format: 0,
        }
    }
}

pub struct RustFrameBuffer {
    data: Vec<u8>,
}

pub struct RustFrameConverter {
    _runtime: crate::RuntimeLibraries,
    symbols: Symbols,
    sws_context: *mut c_void,
}

impl Drop for RustFrameConverter {
    fn drop(&mut self) {
        unsafe {
            if !self.sws_context.is_null() {
                (self.symbols.sws_free_context)(self.sws_context);
                self.sws_context = std::ptr::null_mut();
            }
        }
    }
}

struct Symbols {
    av_strerror: AvStrErrorFn,
    avformat_open_input: AvformatOpenInputFn,
    avformat_find_stream_info: AvformatFindStreamInfoFn,
    avformat_close_input: AvformatCloseInputFn,
    av_read_frame: AvReadFrameFn,
    av_seek_frame: AvSeekFrameFn,
    av_guess_frame_rate: AvGuessFrameRateFn,
    av_guess_sample_aspect_ratio: AvGuessSampleAspectRatioFn,
    avcodec_find_decoder: AvCodecFindDecoderFn,
    avcodec_alloc_context3: AvCodecAllocContext3Fn,
    avcodec_parameters_to_context: AvCodecParametersToContextFn,
    avcodec_open2: AvCodecOpen2Fn,
    avcodec_free_context: AvCodecFreeContextFn,
    avcodec_send_packet: AvCodecSendPacketFn,
    avcodec_receive_frame: AvCodecReceiveFrameFn,
    avcodec_flush_buffers: AvCodecFlushBuffersFn,
    av_packet_alloc: AvPacketAllocFn,
    av_packet_free: AvPacketFreeFn,
    av_packet_unref: AvPacketUnrefFn,
    av_frame_alloc: AvFrameAllocFn,
    av_frame_free: AvFrameFreeFn,
    av_frame_unref: AvFrameUnrefFn,
    av_frame_apply_cropping: AvFrameApplyCroppingFn,
    sws_get_cached_context: SwsGetCachedContextFn,
    sws_scale: SwsScaleFn,
    sws_free_context: SwsFreeContextFn,
}

struct DecodeSession {
    _runtime: crate::RuntimeLibraries,
    symbols: Symbols,
    format_context: *mut c_void,
    codec_context: *mut c_void,
    packet: *mut c_void,
    decoded_frame: *mut c_void,
    sws_context: *mut c_void,
    video_stream: *mut c_void,
    video_stream_index: c_int,
    has_pending_video_packet: bool,
    input_exhausted: bool,
    flush_packet_sent: bool,
}

impl Drop for DecodeSession {
    fn drop(&mut self) {
        unsafe {
            if !self.sws_context.is_null() {
                (self.symbols.sws_free_context)(self.sws_context);
                self.sws_context = std::ptr::null_mut();
            }
            if !self.decoded_frame.is_null() {
                (self.symbols.av_frame_free)(&mut self.decoded_frame);
            }
            if !self.packet.is_null() {
                (self.symbols.av_packet_free)(&mut self.packet);
            }
            if !self.codec_context.is_null() {
                (self.symbols.avcodec_free_context)(&mut self.codec_context);
            }
            if !self.format_context.is_null() {
                (self.symbols.avformat_close_input)(&mut self.format_context);
            }
        }
    }
}

struct DecodeWindowState {
    frames_before_target: Vec<FramePlayerRustNativeFrame>,
    retained_buffer_bytes: usize,
    anchor_reached: bool,
    next_absolute_frame_index: i64,
}

struct DecodeWindowRequest {
    video_stream_index: c_int,
    anchor_entry: FramePlayerRustDecodeCoreIndexEntry,
    target_entry: FramePlayerRustDecodeCoreIndexEntry,
    previous_frame_limit: c_int,
    forward_frame_limit: c_int,
    max_frame_buffer_bytes: u64,
    max_window_buffer_bytes: u64,
}

struct DecodeWindowConfig {
    video_stream_index: c_int,
    anchor_entry: FramePlayerRustDecodeCoreIndexEntry,
    target_entry: FramePlayerRustDecodeCoreIndexEntry,
    previous_frame_limit: usize,
    forward_frame_limit: usize,
    max_frame_buffer_bytes: usize,
    max_window_pixel_buffer_bytes: usize,
    max_window_frame_count: usize,
}

impl DecodeWindowState {
    fn new(anchor_entry: &FramePlayerRustDecodeCoreIndexEntry) -> Self {
        let anchor_reached =
            anchor_entry.absolute_frame_index == 0 && anchor_entry.seek_anchor_timestamp <= 0;
        let next_absolute_frame_index = if anchor_reached {
            anchor_entry.absolute_frame_index
        } else {
            -1
        };

        Self {
            frames_before_target: Vec::new(),
            retained_buffer_bytes: 0,
            anchor_reached,
            next_absolute_frame_index,
        }
    }

    unsafe fn release_all(&mut self) {
        free_native_frames(std::mem::take(&mut self.frames_before_target));
        self.retained_buffer_bytes = 0;
    }

    fn next_frame_limit(
        &self,
        max_frame_buffer_bytes: usize,
        max_window_buffer_bytes: usize,
    ) -> Result<usize, c_int> {
        let remaining_window_bytes = max_window_buffer_bytes
            .checked_sub(self.retained_buffer_bytes)
            .ok_or(STATUS_RESOURCE_LIMIT_EXCEEDED)?;
        let next_limit = max_frame_buffer_bytes.min(remaining_window_bytes);
        if next_limit == 0 {
            return Err(STATUS_RESOURCE_LIMIT_EXCEEDED);
        }

        Ok(next_limit)
    }

    unsafe fn read_frame(
        &mut self,
        session: &mut DecodeSession,
        max_frame_buffer_bytes: usize,
        max_window_buffer_bytes: usize,
        cancel_flag: *const c_int,
        result: *mut FramePlayerRustDecodeWindowResult,
    ) -> Result<FramePlayerRustNativeFrame, c_int> {
        let next_frame_limit =
            match self.next_frame_limit(max_frame_buffer_bytes, max_window_buffer_bytes) {
                Ok(value) => value,
                Err(status) => {
                    self.release_all();
                    write_window_message(
                        result,
                        "Rust decode window reached its decoded-frame byte limit.",
                    );
                    return Err(status);
                }
            };

        match read_next_frame(session, next_frame_limit, cancel_flag, result) {
            Ok(Some(frame)) => Ok(frame),
            Ok(None) => {
                self.release_all();
                write_window_message(
                    result,
                    "Rust decode window could not find the requested target frame.",
                );
                Err(if self.anchor_reached {
                    STATUS_TARGET_NOT_FOUND
                } else {
                    STATUS_ANCHOR_NOT_FOUND
                })
            }
            Err(status) => {
                self.release_all();
                Err(status)
            }
        }
    }

    unsafe fn accept_anchor_or_skip(
        &mut self,
        frame: &FramePlayerRustNativeFrame,
        anchor_entry: &FramePlayerRustDecodeCoreIndexEntry,
    ) -> bool {
        if self.anchor_reached {
            return true;
        }

        if !frame_matches_entry(frame, anchor_entry) {
            return false;
        }

        self.anchor_reached = true;
        self.next_absolute_frame_index = anchor_entry.absolute_frame_index;
        true
    }

    fn assign_absolute_frame_index(&self, frame: &mut FramePlayerRustNativeFrame) {
        frame.absolute_frame_index = self.next_absolute_frame_index;
    }

    fn target_reached(&self, target_entry: &FramePlayerRustDecodeCoreIndexEntry) -> bool {
        self.next_absolute_frame_index == target_entry.absolute_frame_index
    }

    unsafe fn remember_before_target(
        &mut self,
        frame: FramePlayerRustNativeFrame,
        previous_frame_limit: usize,
        max_window_frame_count: usize,
    ) -> Result<(), c_int> {
        if let Err(status) = ensure_decode_window_storage_capacity(
            &mut self.frames_before_target,
            max_window_frame_count,
        ) {
            free_native_frame(frame);
            return Err(status);
        }
        self.retained_buffer_bytes += frame.pixel_buffer_len;
        self.frames_before_target.push(frame);
        while self.frames_before_target.len() > previous_frame_limit {
            let removed = self.frames_before_target.remove(0);
            self.retained_buffer_bytes = self
                .retained_buffer_bytes
                .saturating_sub(removed.pixel_buffer_len);
            free_native_frame(removed);
        }

        self.next_absolute_frame_index += 1;
        Ok(())
    }
}

enum DecodeReadAction {
    Continue,
    End,
    ReadPacket,
}

#[no_mangle]
pub unsafe extern "C" fn frameplayer_rust_ffmpeg_frame_buffer_free(buffer: *mut RustFrameBuffer) {
    if buffer.is_null() {
        return;
    }

    drop(Box::from_raw(buffer));
    #[cfg(test)]
    TEST_FREED_FRAME_BUFFER_COUNT.fetch_add(1, Ordering::SeqCst);
}

#[no_mangle]
pub unsafe extern "C" fn frameplayer_rust_ffmpeg_frame_converter_create(
    runtime_directory: *const c_char,
    converter: *mut *mut RustFrameConverter,
    result: *mut FramePlayerRustFrameConvertResult,
) -> c_int {
    if converter.is_null() || result.is_null() {
        return STATUS_INVALID_ARGUMENT;
    }

    *result = FramePlayerRustFrameConvertResult::default();
    *converter = std::ptr::null_mut();
    let status =
        std::panic::catch_unwind(|| create_converter_entry(runtime_directory, converter, result))
            .unwrap_or_else(|_| {
                (*result).status = STATUS_INVALID_ARGUMENT;
                write_convert_message(result, "Rust frame converter creation panicked.");
                STATUS_INVALID_ARGUMENT
            });
    (*result).status = status;
    status
}

#[no_mangle]
pub unsafe extern "C" fn frameplayer_rust_ffmpeg_frame_converter_convert(
    converter: *mut RustFrameConverter,
    source_frame: *mut c_void,
    max_frame_buffer_bytes: u64,
    result: *mut FramePlayerRustFrameConvertResult,
) -> c_int {
    if converter.is_null() || result.is_null() || max_frame_buffer_bytes == 0 {
        return STATUS_INVALID_ARGUMENT;
    }

    *result = FramePlayerRustFrameConvertResult::default();
    let status = std::panic::catch_unwind(|| {
        convert_with_converter_entry(converter, source_frame, max_frame_buffer_bytes, result)
    })
    .unwrap_or_else(|_| {
        (*result).status = STATUS_INVALID_ARGUMENT;
        write_convert_message(result, "Rust frame conversion panicked.");
        STATUS_INVALID_ARGUMENT
    });
    (*result).status = status;
    status
}

#[no_mangle]
pub unsafe extern "C" fn frameplayer_rust_ffmpeg_frame_converter_free(
    converter: *mut RustFrameConverter,
) {
    if converter.is_null() {
        return;
    }

    drop(Box::from_raw(converter));
}

#[no_mangle]
pub unsafe extern "C" fn frameplayer_rust_ffmpeg_convert_frame_to_bgra(
    runtime_directory: *const c_char,
    source_frame: *mut c_void,
    result: *mut FramePlayerRustFrameConvertResult,
) -> c_int {
    if result.is_null() {
        return STATUS_INVALID_ARGUMENT;
    }

    *result = FramePlayerRustFrameConvertResult::default();
    let status =
        std::panic::catch_unwind(|| convert_frame_entry(runtime_directory, source_frame, result))
            .unwrap_or_else(|_| {
                (*result).status = STATUS_INVALID_ARGUMENT;
                write_convert_message(result, "Rust frame conversion panicked.");
                STATUS_INVALID_ARGUMENT
            });
    (*result).status = status;
    status
}

#[no_mangle]
pub unsafe extern "C" fn frameplayer_rust_ffmpeg_decode_window(
    runtime_directory: *const c_char,
    file_path: *const c_char,
    video_stream_index: c_int,
    anchor_entry: FramePlayerRustDecodeCoreIndexEntry,
    target_entry: FramePlayerRustDecodeCoreIndexEntry,
    previous_frame_limit: c_int,
    forward_frame_limit: c_int,
    max_frame_buffer_bytes: u64,
    max_window_buffer_bytes: u64,
    cancel_flag: *const c_int,
    result: *mut FramePlayerRustDecodeWindowResult,
) -> c_int {
    if result.is_null() {
        return STATUS_INVALID_ARGUMENT;
    }

    *result = FramePlayerRustDecodeWindowResult::default();
    let status = std::panic::catch_unwind(|| {
        decode_window_entry(
            runtime_directory,
            file_path,
            DecodeWindowRequest {
                video_stream_index,
                anchor_entry,
                target_entry,
                previous_frame_limit,
                forward_frame_limit,
                max_frame_buffer_bytes,
                max_window_buffer_bytes,
            },
            cancel_flag,
            result,
        )
    })
    .unwrap_or_else(|_| {
        (*result).status = STATUS_INVALID_ARGUMENT;
        write_window_message(result, "Rust decode window panicked.");
        STATUS_INVALID_ARGUMENT
    });
    (*result).status = status;
    status
}

#[no_mangle]
pub unsafe extern "C" fn frameplayer_rust_ffmpeg_decode_window_free(
    frames: *mut FramePlayerRustNativeFrame,
    frame_count: usize,
) {
    if frames.is_null() || frame_count == 0 {
        return;
    }

    let mut boxed = Box::from_raw(std::ptr::slice_from_raw_parts_mut(frames, frame_count));
    for frame in boxed.iter_mut() {
        if !frame.pixel_buffer.is_null() {
            frameplayer_rust_ffmpeg_frame_buffer_free(frame.pixel_buffer);
            frame.pixel_buffer = std::ptr::null_mut();
            frame.pixel_data = std::ptr::null();
            frame.pixel_buffer_len = 0;
        }
    }
}

unsafe fn create_converter_entry(
    runtime_directory: *const c_char,
    converter: *mut *mut RustFrameConverter,
    result: *mut FramePlayerRustFrameConvertResult,
) -> c_int {
    let converter_value = match create_converter(runtime_directory, result) {
        Ok(value) => value,
        Err(status) => return status,
    };

    *converter = Box::into_raw(Box::new(converter_value));
    write_convert_message(result, "Rust frame converter was created.");
    STATUS_OK
}

unsafe fn create_converter(
    runtime_directory: *const c_char,
    result: *mut FramePlayerRustFrameConvertResult,
) -> Result<RustFrameConverter, c_int> {
    let runtime_directory = match read_path(runtime_directory) {
        Ok(value) => value,
        Err(status) => {
            write_convert_message(result, "FFmpeg runtime directory was not valid.");
            return Err(status);
        }
    };

    let runtime = match load_runtime_libraries(Path::new(runtime_directory)) {
        Ok(value) => value,
        Err(status) => {
            write_convert_message(result, "Could not load FFmpeg runtime libraries.");
            return Err(status);
        }
    };
    let symbols = match load_symbols(&runtime) {
        Ok(value) => value,
        Err(status) => {
            write_convert_message(result, "Could not resolve FFmpeg runtime symbols.");
            return Err(status);
        }
    };

    Ok(RustFrameConverter {
        _runtime: runtime,
        symbols,
        sws_context: std::ptr::null_mut(),
    })
}

unsafe fn convert_with_converter_entry(
    converter: *mut RustFrameConverter,
    source_frame: *mut c_void,
    max_frame_buffer_bytes: u64,
    result: *mut FramePlayerRustFrameConvertResult,
) -> c_int {
    if source_frame.is_null() {
        write_convert_message(result, "Source frame pointer was null.");
        return STATUS_INVALID_ARGUMENT;
    }

    let max_frame_buffer_bytes = match usize::try_from(max_frame_buffer_bytes) {
        Ok(value) if value > 0 => value,
        _ => {
            write_convert_message(
                result,
                "Frame byte limit exceeded this platform's address space.",
            );
            return STATUS_INVALID_ARGUMENT;
        }
    };

    match convert_frame(
        &(*converter).symbols,
        &mut (*converter).sws_context,
        std::ptr::null_mut(),
        std::ptr::null_mut(),
        source_frame,
        -1,
        max_frame_buffer_bytes,
    ) {
        Ok(frame) => {
            (*result).frame = frame;
            write_convert_message(result, "Rust converted decoded frame to native BGRA.");
            STATUS_OK
        }
        Err(status) => {
            write_convert_message(result, "Rust frame conversion failed.");
            status
        }
    }
}

unsafe fn convert_frame_entry(
    runtime_directory: *const c_char,
    source_frame: *mut c_void,
    result: *mut FramePlayerRustFrameConvertResult,
) -> c_int {
    if source_frame.is_null() {
        write_convert_message(result, "Source frame pointer was null.");
        return STATUS_INVALID_ARGUMENT;
    }

    let mut converter = match create_converter(runtime_directory, result) {
        Ok(value) => value,
        Err(status) => return status,
    };

    match convert_frame(
        &converter.symbols,
        &mut converter.sws_context,
        std::ptr::null_mut(),
        std::ptr::null_mut(),
        source_frame,
        -1,
        DEFAULT_MAX_FRAME_BUFFER_BYTES,
    ) {
        Ok(frame) => {
            (*result).frame = frame;
            write_convert_message(result, "Rust converted decoded frame to native BGRA.");
            STATUS_OK
        }
        Err(status) => {
            write_convert_message(result, "Rust frame conversion failed.");
            status
        }
    }
}

unsafe fn decode_window_entry(
    runtime_directory: *const c_char,
    file_path: *const c_char,
    request: DecodeWindowRequest,
    cancel_flag: *const c_int,
    result: *mut FramePlayerRustDecodeWindowResult,
) -> c_int {
    if request.video_stream_index < 0
        || request.previous_frame_limit < 0
        || request.forward_frame_limit < 0
        || request.max_frame_buffer_bytes == 0
        || request.max_window_buffer_bytes == 0
    {
        write_window_message(result, "Decode window arguments were invalid.");
        return STATUS_INVALID_ARGUMENT;
    }

    let max_frame_buffer_bytes = match usize::try_from(request.max_frame_buffer_bytes) {
        Ok(value) => value,
        Err(_) => {
            write_window_message(
                result,
                "Decode frame byte limit exceeded this platform's address space.",
            );
            return STATUS_INVALID_ARGUMENT;
        }
    };
    let max_window_buffer_bytes = match usize::try_from(request.max_window_buffer_bytes) {
        Ok(value) => value,
        Err(_) => {
            write_window_message(
                result,
                "Decode window byte limit exceeded this platform's address space.",
            );
            return STATUS_INVALID_ARGUMENT;
        }
    };
    let max_window_frame_count = match resolve_decode_window_frame_count(
        request.previous_frame_limit as usize,
        request.forward_frame_limit as usize,
        max_window_buffer_bytes,
    ) {
        Ok(value) => value,
        Err(_) => {
            write_window_message(result, "Decode window reached its retained-frame limit.");
            return STATUS_RESOURCE_LIMIT_EXCEEDED;
        }
    };
    let metadata_bytes = match max_window_frame_count
        .checked_mul(std::mem::size_of::<FramePlayerRustNativeFrame>())
    {
        Some(value) => value,
        None => {
            write_window_message(result, "Decode window metadata size overflowed.");
            return STATUS_RESOURCE_LIMIT_EXCEEDED;
        }
    };
    let max_window_pixel_buffer_bytes = match max_window_buffer_bytes.checked_sub(metadata_bytes) {
        Some(value) if value > 0 => value,
        _ => {
            write_window_message(
                result,
                "Decode window byte limit could not retain frame metadata and pixels.",
            );
            return STATUS_RESOURCE_LIMIT_EXCEEDED;
        }
    };

    let runtime_directory = match read_path(runtime_directory) {
        Ok(value) => value,
        Err(status) => {
            write_window_message(result, "FFmpeg runtime directory was not valid.");
            return status;
        }
    };
    let file_path = match read_path(file_path) {
        Ok(value) => value,
        Err(status) => {
            write_window_message(result, "Media file path was not valid.");
            return status;
        }
    };

    let config = DecodeWindowConfig {
        video_stream_index: request.video_stream_index,
        anchor_entry: request.anchor_entry,
        target_entry: request.target_entry,
        previous_frame_limit: request.previous_frame_limit as usize,
        forward_frame_limit: request.forward_frame_limit as usize,
        max_frame_buffer_bytes,
        max_window_pixel_buffer_bytes,
        max_window_frame_count,
    };

    match decode_window(runtime_directory, file_path, &config, cancel_flag, result) {
        Ok(()) => STATUS_OK,
        Err(status) => status,
    }
}

unsafe fn decode_window(
    runtime_directory: &str,
    file_path: &str,
    config: &DecodeWindowConfig,
    cancel_flag: *const c_int,
    result: *mut FramePlayerRustDecodeWindowResult,
) -> Result<(), c_int> {
    let mut session = create_decode_session(
        runtime_directory,
        file_path,
        config.video_stream_index,
        result,
    )?;
    seek_decode_session(
        &mut session,
        config.anchor_entry.seek_anchor_timestamp,
        cancel_flag,
        result,
    )?;

    let mut state = DecodeWindowState::new(&config.anchor_entry);

    loop {
        if let Err(status) = ensure_decode_window_not_cancelled(cancel_flag, result) {
            state.release_all();
            return Err(status);
        }

        let mut frame = state.read_frame(
            &mut session,
            config.max_frame_buffer_bytes,
            config.max_window_pixel_buffer_bytes,
            cancel_flag,
            result,
        )?;
        if !state.accept_anchor_or_skip(&frame, &config.anchor_entry) {
            free_native_frame(frame);
            continue;
        }

        state.assign_absolute_frame_index(&mut frame);
        if state.target_reached(&config.target_entry) {
            return finish_decode_window(&mut session, state, frame, config, cancel_flag, result);
        }

        if let Err(status) = state.remember_before_target(
            frame,
            config.previous_frame_limit,
            config.max_window_frame_count,
        ) {
            state.release_all();
            write_window_message(
                result,
                "Rust decode window could not reserve frame metadata.",
            );
            return Err(status);
        }
    }
}

unsafe fn finish_decode_window(
    session: &mut DecodeSession,
    mut state: DecodeWindowState,
    current_frame: FramePlayerRustNativeFrame,
    config: &DecodeWindowConfig,
    cancel_flag: *const c_int,
    result: *mut FramePlayerRustDecodeWindowResult,
) -> Result<(), c_int> {
    let mut window_frames = std::mem::take(&mut state.frames_before_target);
    let current_index = window_frames.len() as c_int;
    if let Err(status) =
        ensure_decode_window_storage_capacity(&mut window_frames, config.max_window_frame_count)
    {
        free_native_frame(current_frame);
        free_native_frames(window_frames);
        write_window_message(
            result,
            "Rust decode window could not reserve frame metadata.",
        );
        return Err(status);
    }
    state.retained_buffer_bytes += current_frame.pixel_buffer_len;
    window_frames.push(current_frame);

    while window_frames.len() - (current_index as usize) - 1 < config.forward_frame_limit {
        if let Err(status) = ensure_decode_window_not_cancelled(cancel_flag, result) {
            free_native_frames(window_frames);
            return Err(status);
        }

        let next_frame_limit = match state.next_frame_limit(
            config.max_frame_buffer_bytes,
            config.max_window_pixel_buffer_bytes,
        ) {
            Ok(value) => value,
            Err(status) => {
                free_native_frames(window_frames);
                write_window_message(
                    result,
                    "Rust decode window reached its decoded-frame byte limit.",
                );
                return Err(status);
            }
        };
        let mut next_frame = match read_next_frame(session, next_frame_limit, cancel_flag, result) {
            Ok(Some(value)) => value,
            Ok(None) => break,
            Err(status) => {
                free_native_frames(window_frames);
                return Err(status);
            }
        };
        if let Err(status) =
            ensure_decode_window_storage_capacity(&mut window_frames, config.max_window_frame_count)
        {
            free_native_frame(next_frame);
            free_native_frames(window_frames);
            write_window_message(
                result,
                "Rust decode window could not reserve frame metadata.",
            );
            return Err(status);
        }
        state.next_absolute_frame_index += 1;
        next_frame.absolute_frame_index = state.next_absolute_frame_index;
        state.retained_buffer_bytes += next_frame.pixel_buffer_len;
        window_frames.push(next_frame);
    }

    (*result).frame_count = window_frames.len() as u64;
    (*result).current_index = current_index;
    let mut boxed = window_frames.into_boxed_slice();
    (*result).frames = boxed.as_mut_ptr();
    std::mem::forget(boxed);
    write_window_message(result, "Rust decoded indexed frame window.");
    Ok(())
}

unsafe fn create_decode_session(
    runtime_directory: &str,
    file_path: &str,
    video_stream_index: c_int,
    result: *mut FramePlayerRustDecodeWindowResult,
) -> Result<DecodeSession, c_int> {
    let runtime = match load_runtime_libraries(Path::new(runtime_directory)) {
        Ok(value) => value,
        Err(STATUS_LIBRARY_LOAD_FAILED) => {
            write_window_message(result, "Could not load FFmpeg runtime libraries.");
            return Err(STATUS_LIBRARY_LOAD_FAILED);
        }
        Err(STATUS_SYMBOL_LOAD_FAILED) => {
            write_window_message(result, "Could not resolve FFmpeg runtime symbols.");
            return Err(STATUS_SYMBOL_LOAD_FAILED);
        }
        Err(status) => return Err(status),
    };
    let symbols = load_symbols(&runtime)?;
    let input_path = CString::new(file_path).map_err(|_| STATUS_INVALID_ARGUMENT)?;

    let mut format_context: *mut c_void = std::ptr::null_mut();
    let open_result = (symbols.avformat_open_input)(
        &mut format_context,
        input_path.as_ptr(),
        std::ptr::null_mut(),
        std::ptr::null_mut(),
    );
    if open_result < 0 {
        write_window_message(
            result,
            &format!(
                "Could not open media for Rust decode core: {}",
                ffmpeg_error(symbols.av_strerror, open_result)
            ),
        );
        return Err(STATUS_FILE_OPEN_FAILED);
    }

    let stream_info_result =
        (symbols.avformat_find_stream_info)(format_context, std::ptr::null_mut());
    if stream_info_result < 0 {
        (symbols.avformat_close_input)(&mut format_context);
        write_window_message(
            result,
            &format!(
                "Could not probe media for Rust decode core: {}",
                ffmpeg_error(symbols.av_strerror, stream_info_result)
            ),
        );
        return Err(STATUS_FILE_OPEN_FAILED);
    }

    let video_stream = match get_stream(format_context, video_stream_index) {
        Ok(value) => value,
        Err(status) => {
            (symbols.avformat_close_input)(&mut format_context);
            write_window_message(
                result,
                "Requested video stream was unavailable for Rust decode core.",
            );
            return Err(status);
        }
    };
    let codec_parameters = read_field::<*mut c_void>(video_stream, AVSTREAM_CODECPAR_OFFSET);
    let codec_id = read_field::<c_int>(codec_parameters, AVCODEC_PARAMETERS_CODEC_ID_OFFSET);
    let decoder = (symbols.avcodec_find_decoder)(codec_id);
    if decoder.is_null() {
        (symbols.avformat_close_input)(&mut format_context);
        write_window_message(result, "No decoder was available for Rust decode core.");
        return Err(STATUS_DECODER_UNAVAILABLE);
    }

    let codec_context = (symbols.avcodec_alloc_context3)(decoder);
    if codec_context.is_null() {
        (symbols.avformat_close_input)(&mut format_context);
        write_window_message(
            result,
            "Could not allocate decoder context for Rust decode core.",
        );
        return Err(STATUS_CODEC_CONTEXT_ALLOC_FAILED);
    }

    let copy_result = (symbols.avcodec_parameters_to_context)(codec_context, codec_parameters);
    if copy_result < 0 {
        let mut codec_context_to_free = codec_context;
        (symbols.avcodec_free_context)(&mut codec_context_to_free);
        (symbols.avformat_close_input)(&mut format_context);
        write_window_message(
            result,
            "Could not copy codec parameters for Rust decode core.",
        );
        return Err(STATUS_CODEC_CONTEXT_FAILED);
    }

    let video_time_base = read_field::<AVRational>(video_stream, AVSTREAM_TIME_BASE_OFFSET);
    write_field(
        codec_context,
        AVCODEC_CONTEXT_PKT_TIMEBASE_OFFSET,
        video_time_base,
    );
    write_field(
        codec_context,
        AVCODEC_CONTEXT_FRAMERATE_OFFSET,
        get_nominal_frame_rate(&symbols, format_context, video_stream),
    );
    write_field(
        codec_context,
        AVCODEC_CONTEXT_MAX_PIXELS_OFFSET,
        MAX_DECODED_FRAME_PIXELS,
    );

    let open_decoder_result = (symbols.avcodec_open2)(codec_context, decoder, std::ptr::null_mut());
    if open_decoder_result < 0 {
        let mut codec_context_to_free = codec_context;
        (symbols.avcodec_free_context)(&mut codec_context_to_free);
        (symbols.avformat_close_input)(&mut format_context);
        write_window_message(result, "Could not open decoder for Rust decode core.");
        return Err(STATUS_CODEC_CONTEXT_FAILED);
    }

    let packet = (symbols.av_packet_alloc)();
    if packet.is_null() {
        let mut codec_context_to_free = codec_context;
        (symbols.avcodec_free_context)(&mut codec_context_to_free);
        (symbols.avformat_close_input)(&mut format_context);
        write_window_message(result, "Could not allocate packet for Rust decode core.");
        return Err(STATUS_PACKET_ALLOC_FAILED);
    }

    let decoded_frame = (symbols.av_frame_alloc)();
    if decoded_frame.is_null() {
        let mut packet_to_free = packet;
        let mut codec_context_to_free = codec_context;
        (symbols.av_packet_free)(&mut packet_to_free);
        (symbols.avcodec_free_context)(&mut codec_context_to_free);
        (symbols.avformat_close_input)(&mut format_context);
        write_window_message(result, "Could not allocate frame for Rust decode core.");
        return Err(STATUS_FRAME_ALLOC_FAILED);
    }

    Ok(DecodeSession {
        _runtime: runtime,
        symbols,
        format_context,
        codec_context,
        packet,
        decoded_frame,
        sws_context: std::ptr::null_mut(),
        video_stream,
        video_stream_index,
        has_pending_video_packet: false,
        input_exhausted: false,
        flush_packet_sent: false,
    })
}

unsafe fn seek_decode_session(
    session: &mut DecodeSession,
    target_timestamp: i64,
    cancel_flag: *const c_int,
    result: *mut FramePlayerRustDecodeWindowResult,
) -> Result<(), c_int> {
    if cancellation_requested(cancel_flag) {
        write_window_message(result, "Rust decode window was cancelled.");
        return Err(STATUS_CANCELLED);
    }

    let seek_result = (session.symbols.av_seek_frame)(
        session.format_context,
        session.video_stream_index,
        target_timestamp,
        AVSEEK_FLAG_BACKWARD,
    );
    if seek_result < 0 {
        write_window_message(
            result,
            &format!(
                "Rust decode core seek failed: {}",
                ffmpeg_error(session.symbols.av_strerror, seek_result)
            ),
        );
        return Err(STATUS_SEEK_FAILED);
    }

    (session.symbols.avcodec_flush_buffers)(session.codec_context);
    session.has_pending_video_packet = false;
    session.input_exhausted = false;
    session.flush_packet_sent = false;
    (session.symbols.av_packet_unref)(session.packet);
    (session.symbols.av_frame_unref)(session.decoded_frame);
    Ok(())
}

unsafe fn read_next_frame(
    session: &mut DecodeSession,
    max_frame_buffer_bytes: usize,
    cancel_flag: *const c_int,
    result: *mut FramePlayerRustDecodeWindowResult,
) -> Result<Option<FramePlayerRustNativeFrame>, c_int> {
    loop {
        ensure_decode_window_not_cancelled(cancel_flag, result)?;

        if let Some(frame) = try_receive_frame(session, max_frame_buffer_bytes, result)? {
            return Ok(Some(frame));
        }

        if submit_pending_decode_packet(session, result)? {
            continue;
        }

        match flush_decode_session(session, result)? {
            DecodeReadAction::Continue => continue,
            DecodeReadAction::End => return Ok(None),
            DecodeReadAction::ReadPacket => {}
        }

        read_decode_packet(session, result)?;
    }
}

unsafe fn ensure_decode_window_not_cancelled(
    cancel_flag: *const c_int,
    result: *mut FramePlayerRustDecodeWindowResult,
) -> Result<(), c_int> {
    if cancellation_requested(cancel_flag) {
        write_window_message(result, "Rust decode window was cancelled.");
        return Err(STATUS_CANCELLED);
    }

    Ok(())
}

unsafe fn submit_pending_decode_packet(
    session: &mut DecodeSession,
    result: *mut FramePlayerRustDecodeWindowResult,
) -> Result<bool, c_int> {
    if !session.has_pending_video_packet {
        return Ok(false);
    }

    let send_pending_result =
        (session.symbols.avcodec_send_packet)(session.codec_context, session.packet);
    if send_pending_result == AVERROR_EAGAIN {
        return Ok(true);
    }

    if send_pending_result < 0 {
        write_window_message(result, "Rust decode core could not submit packet.");
        return Err(STATUS_PACKET_SEND_FAILED);
    }

    session.has_pending_video_packet = false;
    (session.symbols.av_packet_unref)(session.packet);
    Ok(true)
}

unsafe fn flush_decode_session(
    session: &mut DecodeSession,
    result: *mut FramePlayerRustDecodeWindowResult,
) -> Result<DecodeReadAction, c_int> {
    if !session.input_exhausted {
        return Ok(DecodeReadAction::ReadPacket);
    }

    if session.flush_packet_sent {
        return Ok(DecodeReadAction::End);
    }

    let flush_result =
        (session.symbols.avcodec_send_packet)(session.codec_context, std::ptr::null());
    if flush_result == AVERROR_EAGAIN {
        return Ok(DecodeReadAction::Continue);
    }

    if flush_result == AVERROR_EOF {
        session.flush_packet_sent = true;
        return Ok(DecodeReadAction::End);
    }

    if flush_result < 0 {
        write_window_message(result, "Rust decode core could not flush decoder.");
        return Err(STATUS_PACKET_SEND_FAILED);
    }

    session.flush_packet_sent = true;
    Ok(DecodeReadAction::Continue)
}

unsafe fn read_decode_packet(
    session: &mut DecodeSession,
    result: *mut FramePlayerRustDecodeWindowResult,
) -> Result<(), c_int> {
    let read_result = (session.symbols.av_read_frame)(session.format_context, session.packet);
    if read_result == AVERROR_EOF {
        session.input_exhausted = true;
        return Ok(());
    }

    if read_result < 0 {
        write_window_message(result, "Rust decode core could not read packet.");
        return Err(STATUS_PACKET_READ_FAILED);
    }

    if read_field::<c_int>(session.packet, AVPACKET_STREAM_INDEX_OFFSET)
        != session.video_stream_index
    {
        (session.symbols.av_packet_unref)(session.packet);
        return Ok(());
    }

    session.has_pending_video_packet = true;
    Ok(())
}

unsafe fn try_receive_frame(
    session: &mut DecodeSession,
    max_frame_buffer_bytes: usize,
    result: *mut FramePlayerRustDecodeWindowResult,
) -> Result<Option<FramePlayerRustNativeFrame>, c_int> {
    loop {
        let receive_result =
            (session.symbols.avcodec_receive_frame)(session.codec_context, session.decoded_frame);
        if receive_result == AVERROR_EAGAIN || receive_result == AVERROR_EOF {
            return Ok(None);
        }

        if receive_result < 0 {
            write_window_message(result, "Rust decode core could not receive frame.");
            return Err(STATUS_FRAME_RECEIVE_FAILED);
        }

        if read_field::<c_int>(session.decoded_frame, AVFRAME_WIDTH_OFFSET) <= 0
            || read_field::<c_int>(session.decoded_frame, AVFRAME_HEIGHT_OFFSET) <= 0
        {
            (session.symbols.av_frame_unref)(session.decoded_frame);
            continue;
        }

        let crop_result = (session.symbols.av_frame_apply_cropping)(session.decoded_frame, 0);
        if crop_result < 0 {
            // Preserve existing C# behavior: keep the uncropped frame if FFmpeg cannot crop safely.
        }

        let frame_result = convert_frame(
            &session.symbols,
            &mut session.sws_context,
            session.format_context,
            session.video_stream,
            session.decoded_frame,
            -1,
            max_frame_buffer_bytes,
        );
        (session.symbols.av_frame_unref)(session.decoded_frame);
        let frame = frame_result?;
        return Ok(Some(frame));
    }
}

unsafe fn convert_frame(
    symbols: &Symbols,
    sws_context: &mut *mut c_void,
    format_context: *mut c_void,
    video_stream: *mut c_void,
    source_frame: *mut c_void,
    absolute_frame_index: i64,
    max_buffer_bytes: usize,
) -> Result<FramePlayerRustNativeFrame, c_int> {
    let width = read_field::<c_int>(source_frame, AVFRAME_WIDTH_OFFSET);
    let height = read_field::<c_int>(source_frame, AVFRAME_HEIGHT_OFFSET);
    let source_format = read_field::<c_int>(source_frame, AVFRAME_FORMAT_OFFSET);
    if width <= 0 || height <= 0 {
        return Err(STATUS_CONVERSION_FAILED);
    }

    let (stride, buffer_len) = resolve_bgra_layout(width, height, max_buffer_bytes)?;

    let next_context = (symbols.sws_get_cached_context)(
        *sws_context,
        width,
        height,
        source_format,
        width,
        height,
        AV_PIX_FMT_BGRA,
        SWS_BILINEAR,
        std::ptr::null_mut(),
        std::ptr::null_mut(),
        std::ptr::null(),
    );
    if next_context.is_null() {
        return Err(STATUS_CONVERSION_FAILED);
    }
    *sws_context = next_context;

    let mut buffer = Box::new(RustFrameBuffer {
        data: allocate_zeroed_buffer(buffer_len)?,
    });

    let source_data = read_source_data(source_frame);
    let source_linesize = read_source_linesize(source_frame);
    let mut destination_data = [std::ptr::null_mut::<u8>(); 4];
    let destination_linesize = [stride, 0, 0, 0];
    destination_data[0] = buffer.data.as_mut_ptr();

    let scaled_height = (symbols.sws_scale)(
        *sws_context,
        source_data.as_ptr(),
        source_linesize.as_ptr(),
        0,
        height,
        destination_data.as_ptr(),
        destination_linesize.as_ptr(),
    );
    if scaled_height <= 0 {
        return Err(STATUS_CONVERSION_FAILED);
    }

    let pixel_data = buffer.data.as_ptr();
    let pixel_buffer = Box::into_raw(buffer);
    let (display_width, display_height) = resolve_display_dimensions(
        symbols,
        format_context,
        video_stream,
        source_frame,
        width,
        height,
    );

    Ok(FramePlayerRustNativeFrame {
        absolute_frame_index,
        presentation_timestamp: best_presentation_timestamp(source_frame).unwrap_or(AV_NOPTS_VALUE),
        decode_timestamp: timestamp_or_none(read_field::<i64>(
            source_frame,
            AVFRAME_PKT_DTS_OFFSET,
        ))
        .unwrap_or(AV_NOPTS_VALUE),
        duration_timestamp: duration_timestamp(source_frame).unwrap_or(AV_NOPTS_VALUE),
        is_key_frame: if (read_field::<c_int>(source_frame, AVFRAME_FLAGS_OFFSET)
            & AV_FRAME_FLAG_KEY)
            != 0
        {
            1
        } else {
            0
        },
        pixel_buffer,
        pixel_data,
        pixel_buffer_len: buffer_len,
        stride,
        width,
        height,
        display_width,
        display_height,
        source_pixel_format: source_format,
    })
}

fn resolve_bgra_layout(
    width: c_int,
    height: c_int,
    max_buffer_bytes: usize,
) -> Result<(c_int, usize), c_int> {
    if width <= 0 || height <= 0 {
        return Err(STATUS_CONVERSION_FAILED);
    }

    let stride = width.checked_mul(4).ok_or(STATUS_CONVERSION_FAILED)?;
    let buffer_len = stride
        .checked_mul(height)
        .and_then(|value| usize::try_from(value).ok())
        .ok_or(STATUS_CONVERSION_FAILED)?;
    if max_buffer_bytes == 0 || buffer_len > max_buffer_bytes {
        return Err(STATUS_RESOURCE_LIMIT_EXCEEDED);
    }

    Ok((stride, buffer_len))
}

fn allocate_zeroed_buffer(buffer_len: usize) -> Result<Vec<u8>, c_int> {
    if buffer_len == 0 {
        return Err(STATUS_RESOURCE_LIMIT_EXCEEDED);
    }

    let mut data = Vec::new();
    data.try_reserve_exact(buffer_len)
        .map_err(|_| STATUS_RESOURCE_LIMIT_EXCEEDED)?;
    data.resize(buffer_len, 0);
    Ok(data)
}

fn resolve_decode_window_frame_count(
    previous_frame_limit: usize,
    forward_frame_limit: usize,
    max_window_buffer_bytes: usize,
) -> Result<usize, c_int> {
    let frame_count = previous_frame_limit
        .checked_add(forward_frame_limit)
        .and_then(|value| value.checked_add(1))
        .ok_or(STATUS_RESOURCE_LIMIT_EXCEEDED)?;
    let max_frames_by_metadata = max_window_buffer_bytes
        .checked_div(std::mem::size_of::<FramePlayerRustNativeFrame>())
        .ok_or(STATUS_RESOURCE_LIMIT_EXCEEDED)?;
    if frame_count > MAX_DECODE_WINDOW_FRAME_COUNT || frame_count > max_frames_by_metadata {
        return Err(STATUS_RESOURCE_LIMIT_EXCEEDED);
    }

    Ok(frame_count)
}

fn ensure_decode_window_storage_capacity(
    frames: &mut Vec<FramePlayerRustNativeFrame>,
    max_frame_count: usize,
) -> Result<(), c_int> {
    if frames.len() >= max_frame_count {
        return Err(STATUS_RESOURCE_LIMIT_EXCEEDED);
    }

    if frames.len() == frames.capacity() {
        frames
            .try_reserve_exact(1)
            .map_err(|_| STATUS_RESOURCE_LIMIT_EXCEEDED)?;
    }

    Ok(())
}

unsafe fn load_symbols(runtime: &crate::RuntimeLibraries) -> Result<Symbols, c_int> {
    Ok(Symbols {
        av_strerror: load_symbol::<AvStrErrorFn>(&runtime.avutil, "av_strerror")?,
        avformat_open_input: load_symbol::<AvformatOpenInputFn>(
            &runtime.avformat,
            "avformat_open_input",
        )?,
        avformat_find_stream_info: load_symbol::<AvformatFindStreamInfoFn>(
            &runtime.avformat,
            "avformat_find_stream_info",
        )?,
        avformat_close_input: load_symbol::<AvformatCloseInputFn>(
            &runtime.avformat,
            "avformat_close_input",
        )?,
        av_read_frame: load_symbol::<AvReadFrameFn>(&runtime.avformat, "av_read_frame")?,
        av_seek_frame: load_symbol::<AvSeekFrameFn>(&runtime.avformat, "av_seek_frame")?,
        av_guess_frame_rate: load_symbol::<AvGuessFrameRateFn>(
            &runtime.avformat,
            "av_guess_frame_rate",
        )?,
        av_guess_sample_aspect_ratio: load_symbol::<AvGuessSampleAspectRatioFn>(
            &runtime.avformat,
            "av_guess_sample_aspect_ratio",
        )?,
        avcodec_find_decoder: load_symbol::<AvCodecFindDecoderFn>(
            &runtime.avcodec,
            "avcodec_find_decoder",
        )?,
        avcodec_alloc_context3: load_symbol::<AvCodecAllocContext3Fn>(
            &runtime.avcodec,
            "avcodec_alloc_context3",
        )?,
        avcodec_parameters_to_context: load_symbol::<AvCodecParametersToContextFn>(
            &runtime.avcodec,
            "avcodec_parameters_to_context",
        )?,
        avcodec_open2: load_symbol::<AvCodecOpen2Fn>(&runtime.avcodec, "avcodec_open2")?,
        avcodec_free_context: load_symbol::<AvCodecFreeContextFn>(
            &runtime.avcodec,
            "avcodec_free_context",
        )?,
        avcodec_send_packet: load_symbol::<AvCodecSendPacketFn>(
            &runtime.avcodec,
            "avcodec_send_packet",
        )?,
        avcodec_receive_frame: load_symbol::<AvCodecReceiveFrameFn>(
            &runtime.avcodec,
            "avcodec_receive_frame",
        )?,
        avcodec_flush_buffers: load_symbol::<AvCodecFlushBuffersFn>(
            &runtime.avcodec,
            "avcodec_flush_buffers",
        )?,
        av_packet_alloc: load_symbol::<AvPacketAllocFn>(&runtime.avcodec, "av_packet_alloc")?,
        av_packet_free: load_symbol::<AvPacketFreeFn>(&runtime.avcodec, "av_packet_free")?,
        av_packet_unref: load_symbol::<AvPacketUnrefFn>(&runtime.avcodec, "av_packet_unref")?,
        av_frame_alloc: load_symbol::<AvFrameAllocFn>(&runtime.avutil, "av_frame_alloc")?,
        av_frame_free: load_symbol::<AvFrameFreeFn>(&runtime.avutil, "av_frame_free")?,
        av_frame_unref: load_symbol::<AvFrameUnrefFn>(&runtime.avutil, "av_frame_unref")?,
        av_frame_apply_cropping: load_symbol::<AvFrameApplyCroppingFn>(
            &runtime.avutil,
            "av_frame_apply_cropping",
        )?,
        sws_get_cached_context: load_symbol::<SwsGetCachedContextFn>(
            &runtime._swscale,
            "sws_getCachedContext",
        )?,
        sws_scale: load_symbol::<SwsScaleFn>(&runtime._swscale, "sws_scale")?,
        sws_free_context: load_symbol::<SwsFreeContextFn>(&runtime._swscale, "sws_freeContext")?,
    })
}

unsafe fn get_stream(
    format_context: *mut c_void,
    video_stream_index: c_int,
) -> Result<*mut c_void, c_int> {
    let stream_count = read_field::<u32>(format_context, AVFORMAT_CONTEXT_NB_STREAMS_OFFSET);
    if video_stream_index < 0 || video_stream_index as u32 >= stream_count {
        return Err(STATUS_STREAM_UNAVAILABLE);
    }

    let streams = read_field::<*mut *mut c_void>(format_context, AVFORMAT_CONTEXT_STREAMS_OFFSET);
    if streams.is_null() {
        return Err(STATUS_STREAM_UNAVAILABLE);
    }

    let stream = *streams.add(video_stream_index as usize);
    if stream.is_null() {
        Err(STATUS_STREAM_UNAVAILABLE)
    } else {
        Ok(stream)
    }
}

unsafe fn get_nominal_frame_rate(
    symbols: &Symbols,
    format_context: *mut c_void,
    video_stream: *mut c_void,
) -> AVRational {
    let guessed = (symbols.av_guess_frame_rate)(format_context, video_stream, std::ptr::null_mut());
    if rational_is_valid(guessed) {
        return guessed;
    }

    let average = read_field::<AVRational>(video_stream, AVSTREAM_AVG_FRAME_RATE_OFFSET);
    if rational_is_valid(average) {
        return average;
    }

    let real = read_field::<AVRational>(video_stream, AVSTREAM_R_FRAME_RATE_OFFSET);
    if rational_is_valid(real) {
        return real;
    }

    AVRational { num: 0, den: 0 }
}

unsafe fn resolve_display_dimensions(
    symbols: &Symbols,
    format_context: *mut c_void,
    video_stream: *mut c_void,
    source_frame: *mut c_void,
    width: c_int,
    height: c_int,
) -> (c_int, c_int) {
    if format_context.is_null() || video_stream.is_null() {
        return (width, height);
    }

    let sample_aspect_ratio =
        (symbols.av_guess_sample_aspect_ratio)(format_context, video_stream, source_frame);
    if !rational_is_valid(sample_aspect_ratio)
        || sample_aspect_ratio.num <= 0
        || sample_aspect_ratio.den <= 0
        || sample_aspect_ratio.num == sample_aspect_ratio.den
    {
        return (width, height);
    }

    if sample_aspect_ratio.num > sample_aspect_ratio.den {
        let display_width = ((width as f64) * (sample_aspect_ratio.num as f64)
            / (sample_aspect_ratio.den as f64))
            .round()
            .max(1.0) as c_int;
        return (display_width, height);
    }

    let display_height = ((height as f64) * (sample_aspect_ratio.den as f64)
        / (sample_aspect_ratio.num as f64))
        .round()
        .max(1.0) as c_int;
    (width, display_height)
}

unsafe fn read_source_data(source_frame: *mut c_void) -> [*const u8; 4] {
    let mut data = [std::ptr::null::<u8>(); 4];
    for (index, slot) in data.iter_mut().enumerate() {
        *slot = read_field::<*const u8>(source_frame, index * std::mem::size_of::<*const u8>());
    }
    data
}

unsafe fn read_source_linesize(source_frame: *mut c_void) -> [c_int; 4] {
    let mut linesize = [0; 4];
    for (index, slot) in linesize.iter_mut().enumerate() {
        *slot = read_field::<c_int>(source_frame, 64 + (index * std::mem::size_of::<c_int>()));
    }
    linesize
}

unsafe fn frame_matches_entry(
    frame: &FramePlayerRustNativeFrame,
    entry: &FramePlayerRustDecodeCoreIndexEntry,
) -> bool {
    if entry.presentation_timestamp != AV_NOPTS_VALUE
        && frame.presentation_timestamp != AV_NOPTS_VALUE
        && entry.presentation_timestamp == frame.presentation_timestamp
    {
        return true;
    }

    entry.decode_timestamp != AV_NOPTS_VALUE
        && frame.decode_timestamp != AV_NOPTS_VALUE
        && entry.decode_timestamp == frame.decode_timestamp
}

fn rational_is_valid(rational: AVRational) -> bool {
    rational.num != 0 && rational.den != 0
}

unsafe fn best_presentation_timestamp(decoded_frame: *mut c_void) -> Option<i64> {
    timestamp_or_none(read_field::<i64>(
        decoded_frame,
        AVFRAME_BEST_EFFORT_TIMESTAMP_OFFSET,
    ))
    .or_else(|| timestamp_or_none(read_field::<i64>(decoded_frame, AVFRAME_PTS_OFFSET)))
    .or_else(|| timestamp_or_none(read_field::<i64>(decoded_frame, AVFRAME_PKT_DTS_OFFSET)))
}

unsafe fn duration_timestamp(decoded_frame: *mut c_void) -> Option<i64> {
    let duration = read_field::<i64>(decoded_frame, AVFRAME_DURATION_OFFSET);
    if duration > 0 {
        Some(duration)
    } else {
        timestamp_or_none(duration)
    }
}

fn timestamp_or_none(timestamp: i64) -> Option<i64> {
    if timestamp == AV_NOPTS_VALUE {
        None
    } else {
        Some(timestamp)
    }
}

unsafe fn cancellation_requested(cancel_flag: *const c_int) -> bool {
    !cancel_flag.is_null() && (&*cancel_flag.cast::<AtomicI32>()).load(Ordering::Acquire) != 0
}

unsafe fn read_path<'a>(path: *const c_char) -> Result<&'a str, c_int> {
    if path.is_null() {
        return Err(STATUS_INVALID_ARGUMENT);
    }

    match CStr::from_ptr(path).to_str() {
        Ok(value) if !value.trim().is_empty() => Ok(value),
        _ => Err(STATUS_RUNTIME_DIRECTORY_MISSING),
    }
}

unsafe fn read_field<T: Copy>(base: *const c_void, offset: usize) -> T {
    (base as *const u8).add(offset).cast::<T>().read_unaligned()
}

unsafe fn write_field<T>(base: *mut c_void, offset: usize, value: T) {
    (base as *mut u8)
        .add(offset)
        .cast::<T>()
        .write_unaligned(value);
}

unsafe fn ffmpeg_error(av_strerror: AvStrErrorFn, error_code: c_int) -> String {
    let mut buffer = [0 as c_char; MESSAGE_CAPACITY];
    if av_strerror(error_code, buffer.as_mut_ptr(), buffer.len()) < 0 {
        return format!("FFmpeg error {}", error_code);
    }

    CStr::from_ptr(buffer.as_ptr())
        .to_string_lossy()
        .into_owned()
}

unsafe fn free_native_frame(frame: FramePlayerRustNativeFrame) {
    if !frame.pixel_buffer.is_null() {
        frameplayer_rust_ffmpeg_frame_buffer_free(frame.pixel_buffer);
    }
}

unsafe fn free_native_frames(frames: Vec<FramePlayerRustNativeFrame>) {
    for frame in frames {
        free_native_frame(frame);
    }
}

unsafe fn write_window_message(result: *mut FramePlayerRustDecodeWindowResult, message: &str) {
    let bytes = message.as_bytes();
    let byte_count = bytes.len().min(MESSAGE_CAPACITY - 1);
    for (index, byte) in bytes.iter().take(byte_count).enumerate() {
        (*result).message[index] = *byte as c_char;
    }
    (*result).message[byte_count] = 0;
}

unsafe fn write_convert_message(result: *mut FramePlayerRustFrameConvertResult, message: &str) {
    let bytes = message.as_bytes();
    let byte_count = bytes.len().min(MESSAGE_CAPACITY - 1);
    for (index, byte) in bytes.iter().take(byte_count).enumerate() {
        (*result).message[index] = *byte as c_char;
    }
    (*result).message[byte_count] = 0;
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn bgra_layout_enforces_allocation_boundary_before_conversion() {
        let exact_limit = 8_192usize * 8_192usize * 4usize;

        assert_eq!(
            Ok((32_768, exact_limit)),
            resolve_bgra_layout(8_192, 8_192, exact_limit)
        );
        assert_eq!(
            Err(STATUS_RESOURCE_LIMIT_EXCEEDED),
            resolve_bgra_layout(8_192, 8_193, exact_limit)
        );
        assert_eq!(
            Err(STATUS_RESOURCE_LIMIT_EXCEEDED),
            resolve_bgra_layout(16_255, 16_255, DEFAULT_MAX_FRAME_BUFFER_BYTES)
        );
    }

    #[test]
    fn bgra_buffer_allocation_reports_capacity_failure() {
        assert_eq!(Ok(vec![0u8; 4]), allocate_zeroed_buffer(4));
        assert_eq!(
            Err(STATUS_RESOURCE_LIMIT_EXCEEDED),
            allocate_zeroed_buffer(usize::MAX)
        );
    }

    #[test]
    fn decode_window_frame_count_enforces_exact_boundary() {
        assert_eq!(
            Ok(MAX_DECODE_WINDOW_FRAME_COUNT),
            resolve_decode_window_frame_count(249_999, 250_000, usize::MAX)
        );
        assert_eq!(
            Err(STATUS_RESOURCE_LIMIT_EXCEEDED),
            resolve_decode_window_frame_count(250_000, 250_000, usize::MAX)
        );
        assert_eq!(
            Err(STATUS_RESOURCE_LIMIT_EXCEEDED),
            resolve_decode_window_frame_count(0, 0, 1)
        );
    }

    #[test]
    fn decode_window_storage_capacity_enforces_logical_limit() {
        let mut frames = Vec::new();
        for _ in 0..5 {
            assert_eq!(
                Ok(()),
                ensure_decode_window_storage_capacity(&mut frames, 5)
            );
            frames.push(FramePlayerRustNativeFrame::default());
        }

        assert_eq!(
            Err(STATUS_RESOURCE_LIMIT_EXCEEDED),
            ensure_decode_window_storage_capacity(&mut frames, 5)
        );
    }

    #[test]
    fn decode_window_free_releases_unclaimed_pixel_buffers() {
        let before = TEST_FREED_FRAME_BUFFER_COUNT.load(Ordering::SeqCst);
        let mut frames = vec![
            native_test_frame_with_pixels(),
            native_test_frame_with_pixels(),
            FramePlayerRustNativeFrame::default(),
        ]
        .into_boxed_slice();
        let frames_pointer = frames.as_mut_ptr();
        let frame_count = frames.len();
        std::mem::forget(frames);

        unsafe { frameplayer_rust_ffmpeg_decode_window_free(frames_pointer, frame_count) };

        assert!(TEST_FREED_FRAME_BUFFER_COUNT.load(Ordering::SeqCst) >= before + 2);
    }

    #[test]
    fn remember_before_target_releases_frame_when_metadata_reservation_fails() {
        let before = TEST_FREED_FRAME_BUFFER_COUNT.load(Ordering::SeqCst);
        let anchor = FramePlayerRustDecodeCoreIndexEntry {
            absolute_frame_index: 0,
            presentation_timestamp: 0,
            decode_timestamp: 0,
            search_timestamp: 0,
            seek_anchor_frame_index: 0,
            seek_anchor_timestamp: 0,
        };
        let mut state = DecodeWindowState::new(&anchor);

        assert_eq!(Err(STATUS_RESOURCE_LIMIT_EXCEEDED), unsafe {
            state.remember_before_target(native_test_frame_with_pixels(), 0, 0)
        });
        assert!(TEST_FREED_FRAME_BUFFER_COUNT.load(Ordering::SeqCst) > before);
    }

    fn native_test_frame_with_pixels() -> FramePlayerRustNativeFrame {
        let buffer = Box::new(RustFrameBuffer { data: vec![0u8; 4] });
        let pixel_data = buffer.data.as_ptr();
        FramePlayerRustNativeFrame {
            pixel_buffer: Box::into_raw(buffer),
            pixel_data,
            pixel_buffer_len: 4,
            ..FramePlayerRustNativeFrame::default()
        }
    }

    #[test]
    fn persistent_converter_creation_returns_valid_handle() {
        let Some(runtime_directory) = std::env::var_os("FRAMEPLAYER_FFMPEG_RUNTIME_DIR") else {
            return;
        };
        let runtime_path = CString::new(runtime_directory.to_string_lossy().as_bytes())
            .expect("runtime path should not contain NUL");
        let mut converter = std::ptr::null_mut();
        let mut result = FramePlayerRustFrameConvertResult::default();

        let status = unsafe {
            frameplayer_rust_ffmpeg_frame_converter_create(
                runtime_path.as_ptr(),
                &mut converter,
                &mut result,
            )
        };
        let message = unsafe { CStr::from_ptr(result.message.as_ptr()) }
            .to_string_lossy()
            .into_owned();
        let converter_was_initialized = !converter.is_null();

        if converter_was_initialized {
            unsafe { frameplayer_rust_ffmpeg_frame_converter_free(converter) };
        }

        assert_eq!(STATUS_OK, status, "{message}");
        assert_eq!(STATUS_OK, result.status, "{message}");
        assert!(
            converter_was_initialized,
            "converter handle should be initialized"
        );
    }

    #[test]
    fn one_shot_conversion_returns_bgra_frame() {
        let Some(runtime_directory) = std::env::var_os("FRAMEPLAYER_FFMPEG_RUNTIME_DIR") else {
            return;
        };
        let runtime_directory = runtime_directory.to_string_lossy();
        let runtime_path = CString::new(runtime_directory.as_bytes())
            .expect("runtime path should not contain NUL");
        let runtime = unsafe { load_runtime_libraries(Path::new(runtime_directory.as_ref())) }
            .expect("FFmpeg runtime libraries should load");
        let symbols = unsafe { load_symbols(&runtime) }.expect("FFmpeg symbols should load");
        let mut source_frame = unsafe { (symbols.av_frame_alloc)() };
        assert!(
            !source_frame.is_null(),
            "FFmpeg should allocate a source frame"
        );

        let source_pixels = [3u8, 2, 1, 255];
        unsafe {
            write_field::<*const u8>(source_frame, 0, source_pixels.as_ptr());
            write_field::<c_int>(source_frame, 64, 4);
            write_field::<c_int>(source_frame, AVFRAME_WIDTH_OFFSET, 1);
            write_field::<c_int>(source_frame, AVFRAME_HEIGHT_OFFSET, 1);
            write_field::<c_int>(source_frame, AVFRAME_FORMAT_OFFSET, AV_PIX_FMT_BGRA);
        }

        let mut converter = std::ptr::null_mut();
        let mut create_result = FramePlayerRustFrameConvertResult::default();
        let create_status = unsafe {
            frameplayer_rust_ffmpeg_frame_converter_create(
                runtime_path.as_ptr(),
                &mut converter,
                &mut create_result,
            )
        };
        assert_eq!(STATUS_OK, create_status);
        let mut limited_result = FramePlayerRustFrameConvertResult::default();
        let limited_status = unsafe {
            frameplayer_rust_ffmpeg_frame_converter_convert(
                converter,
                source_frame,
                3,
                &mut limited_result,
            )
        };
        unsafe { frameplayer_rust_ffmpeg_frame_converter_free(converter) };
        assert_eq!(STATUS_RESOURCE_LIMIT_EXCEEDED, limited_status);
        assert_eq!(STATUS_RESOURCE_LIMIT_EXCEEDED, limited_result.status);
        assert!(limited_result.frame.pixel_buffer.is_null());

        let mut result = FramePlayerRustFrameConvertResult::default();
        let status = unsafe {
            frameplayer_rust_ffmpeg_convert_frame_to_bgra(
                runtime_path.as_ptr(),
                source_frame,
                &mut result,
            )
        };
        let message = unsafe { CStr::from_ptr(result.message.as_ptr()) }
            .to_string_lossy()
            .into_owned();
        let output_metadata = (
            result.frame.width,
            result.frame.height,
            result.frame.stride,
            result.frame.pixel_buffer_len,
            result.frame.source_pixel_format,
        );
        let output_pixels = if status == STATUS_OK
            && !result.frame.pixel_data.is_null()
            && result.frame.pixel_buffer_len == source_pixels.len()
        {
            unsafe {
                std::slice::from_raw_parts(result.frame.pixel_data, result.frame.pixel_buffer_len)
                    .to_vec()
            }
        } else {
            Vec::new()
        };

        unsafe {
            free_native_frame(std::mem::take(&mut result.frame));
            (symbols.av_frame_free)(&mut source_frame);
        }

        assert_eq!(STATUS_OK, status, "{message}");
        assert_eq!(STATUS_OK, result.status, "{message}");
        assert_eq!((1, 1, 4, 4, AV_PIX_FMT_BGRA), output_metadata);
        assert_eq!(source_pixels, output_pixels.as_slice());
    }
}
