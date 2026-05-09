use crate::{
    load_runtime_libraries, load_symbol, MESSAGE_CAPACITY, STATUS_INVALID_ARGUMENT,
    STATUS_LIBRARY_LOAD_FAILED, STATUS_OK, STATUS_RUNTIME_DIRECTORY_MISSING,
    STATUS_SYMBOL_LOAD_FAILED,
};
use std::ffi::{CStr, CString};
use std::os::raw::{c_char, c_int, c_void};
use std::path::Path;

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

#[no_mangle]
pub unsafe extern "C" fn frameplayer_rust_ffmpeg_frame_buffer_free(buffer: *mut RustFrameBuffer) {
    if buffer.is_null() {
        return;
    }

    drop(Box::from_raw(buffer));
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
    result: *mut FramePlayerRustFrameConvertResult,
) -> c_int {
    if converter.is_null() || result.is_null() {
        return STATUS_INVALID_ARGUMENT;
    }

    *result = FramePlayerRustFrameConvertResult::default();
    let status =
        std::panic::catch_unwind(|| convert_with_converter_entry(converter, source_frame, result))
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
            video_stream_index,
            anchor_entry,
            target_entry,
            previous_frame_limit,
            forward_frame_limit,
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

    drop(Box::from_raw(std::slice::from_raw_parts_mut(
        frames,
        frame_count,
    )));
}

unsafe fn create_converter_entry(
    runtime_directory: *const c_char,
    converter: *mut *mut RustFrameConverter,
    result: *mut FramePlayerRustFrameConvertResult,
) -> c_int {
    let runtime_directory = match read_path(runtime_directory) {
        Ok(value) => value,
        Err(status) => {
            write_convert_message(result, "FFmpeg runtime directory was not valid.");
            return status;
        }
    };

    let runtime = match load_runtime_libraries(Path::new(runtime_directory)) {
        Ok(value) => value,
        Err(status) => {
            write_convert_message(result, "Could not load FFmpeg runtime libraries.");
            return status;
        }
    };
    let symbols = match load_symbols(&runtime) {
        Ok(value) => value,
        Err(status) => {
            write_convert_message(result, "Could not resolve FFmpeg runtime symbols.");
            return status;
        }
    };

    *converter = Box::into_raw(Box::new(RustFrameConverter {
        _runtime: runtime,
        symbols,
        sws_context: std::ptr::null_mut(),
    }));
    write_convert_message(result, "Rust frame converter was created.");
    STATUS_OK
}

unsafe fn convert_with_converter_entry(
    converter: *mut RustFrameConverter,
    source_frame: *mut c_void,
    result: *mut FramePlayerRustFrameConvertResult,
) -> c_int {
    if source_frame.is_null() {
        write_convert_message(result, "Source frame pointer was null.");
        return STATUS_INVALID_ARGUMENT;
    }

    match convert_frame(
        &(*converter).symbols,
        &mut (*converter).sws_context,
        std::ptr::null_mut(),
        std::ptr::null_mut(),
        source_frame,
        -1,
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

    let mut converter: *mut RustFrameConverter = std::ptr::null_mut();
    let create_status = create_converter_entry(runtime_directory, &mut converter, result);
    if create_status != STATUS_OK {
        return create_status;
    }

    match convert_frame(
        &(*converter).symbols,
        &mut (*converter).sws_context,
        std::ptr::null_mut(),
        std::ptr::null_mut(),
        source_frame,
        -1,
    ) {
        Ok(frame) => {
            (*result).frame = frame;
            frameplayer_rust_ffmpeg_frame_converter_free(converter);
            write_convert_message(result, "Rust converted decoded frame to native BGRA.");
            STATUS_OK
        }
        Err(status) => {
            frameplayer_rust_ffmpeg_frame_converter_free(converter);
            write_convert_message(result, "Rust frame conversion failed.");
            status
        }
    }
}

unsafe fn decode_window_entry(
    runtime_directory: *const c_char,
    file_path: *const c_char,
    video_stream_index: c_int,
    anchor_entry: FramePlayerRustDecodeCoreIndexEntry,
    target_entry: FramePlayerRustDecodeCoreIndexEntry,
    previous_frame_limit: c_int,
    forward_frame_limit: c_int,
    cancel_flag: *const c_int,
    result: *mut FramePlayerRustDecodeWindowResult,
) -> c_int {
    if video_stream_index < 0 || previous_frame_limit < 0 || forward_frame_limit < 0 {
        write_window_message(result, "Decode window arguments were invalid.");
        return STATUS_INVALID_ARGUMENT;
    }

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

    match decode_window(
        runtime_directory,
        file_path,
        video_stream_index,
        anchor_entry,
        target_entry,
        previous_frame_limit as usize,
        forward_frame_limit as usize,
        cancel_flag,
        result,
    ) {
        Ok(()) => STATUS_OK,
        Err(status) => status,
    }
}

unsafe fn decode_window(
    runtime_directory: &str,
    file_path: &str,
    video_stream_index: c_int,
    anchor_entry: FramePlayerRustDecodeCoreIndexEntry,
    target_entry: FramePlayerRustDecodeCoreIndexEntry,
    previous_frame_limit: usize,
    forward_frame_limit: usize,
    cancel_flag: *const c_int,
    result: *mut FramePlayerRustDecodeWindowResult,
) -> Result<(), c_int> {
    let mut session =
        create_decode_session(runtime_directory, file_path, video_stream_index, result)?;
    seek_decode_session(
        &mut session,
        anchor_entry.seek_anchor_timestamp,
        cancel_flag,
        result,
    )?;

    let mut window_frames = Vec::with_capacity(previous_frame_limit + forward_frame_limit + 1);
    let mut frames_before_target = Vec::with_capacity(previous_frame_limit);
    let mut anchor_reached =
        anchor_entry.absolute_frame_index == 0 && anchor_entry.seek_anchor_timestamp <= 0;
    let mut next_absolute_frame_index = if anchor_reached {
        anchor_entry.absolute_frame_index
    } else {
        -1
    };

    loop {
        if cancellation_requested(cancel_flag) {
            free_native_frames(frames_before_target);
            free_native_frames(window_frames);
            write_window_message(result, "Rust decode window was cancelled.");
            return Err(STATUS_CANCELLED);
        }

        let mut frame = match read_next_frame(&mut session, cancel_flag, result) {
            Ok(Some(value)) => value,
            Ok(None) => {
                free_native_frames(frames_before_target);
                free_native_frames(window_frames);
                write_window_message(
                    result,
                    "Rust decode window could not find the requested target frame.",
                );
                return Err(if anchor_reached {
                    STATUS_TARGET_NOT_FOUND
                } else {
                    STATUS_ANCHOR_NOT_FOUND
                });
            }
            Err(status) => {
                free_native_frames(frames_before_target);
                free_native_frames(window_frames);
                return Err(status);
            }
        };

        if !anchor_reached {
            if !frame_matches_entry(&frame, &anchor_entry) {
                free_native_frame(frame);
                continue;
            }

            anchor_reached = true;
            next_absolute_frame_index = anchor_entry.absolute_frame_index;
        }

        frame.absolute_frame_index = next_absolute_frame_index;

        if next_absolute_frame_index == target_entry.absolute_frame_index {
            window_frames.append(&mut frames_before_target);
            let current_index = window_frames.len() as c_int;
            window_frames.push(frame);

            while window_frames.len() - (current_index as usize) - 1 < forward_frame_limit {
                if cancellation_requested(cancel_flag) {
                    free_native_frames(window_frames);
                    write_window_message(result, "Rust decode window was cancelled.");
                    return Err(STATUS_CANCELLED);
                }

                let mut next_frame = match read_next_frame(&mut session, cancel_flag, result) {
                    Ok(Some(value)) => value,
                    Ok(None) => break,
                    Err(status) => {
                        free_native_frames(window_frames);
                        return Err(status);
                    }
                };
                next_absolute_frame_index += 1;
                next_frame.absolute_frame_index = next_absolute_frame_index;
                window_frames.push(next_frame);
            }

            (*result).frame_count = window_frames.len() as u64;
            (*result).current_index = current_index;
            let mut boxed = window_frames.into_boxed_slice();
            (*result).frames = boxed.as_mut_ptr();
            std::mem::forget(boxed);
            write_window_message(result, "Rust decoded indexed frame window.");
            return Ok(());
        }

        frames_before_target.push(frame);
        while frames_before_target.len() > previous_frame_limit {
            let removed = frames_before_target.remove(0);
            free_native_frame(removed);
        }

        next_absolute_frame_index += 1;
    }
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
    cancel_flag: *const c_int,
    result: *mut FramePlayerRustDecodeWindowResult,
) -> Result<Option<FramePlayerRustNativeFrame>, c_int> {
    loop {
        if cancellation_requested(cancel_flag) {
            write_window_message(result, "Rust decode window was cancelled.");
            return Err(STATUS_CANCELLED);
        }

        match try_receive_frame(session, result)? {
            Some(frame) => return Ok(Some(frame)),
            None => {}
        }

        if session.has_pending_video_packet {
            let send_pending_result =
                (session.symbols.avcodec_send_packet)(session.codec_context, session.packet);
            if send_pending_result == AVERROR_EAGAIN {
                continue;
            }

            if send_pending_result < 0 {
                write_window_message(result, "Rust decode core could not submit packet.");
                return Err(STATUS_PACKET_SEND_FAILED);
            }

            session.has_pending_video_packet = false;
            (session.symbols.av_packet_unref)(session.packet);
            continue;
        }

        if session.input_exhausted {
            if session.flush_packet_sent {
                return Ok(None);
            }

            let flush_result =
                (session.symbols.avcodec_send_packet)(session.codec_context, std::ptr::null());
            if flush_result == AVERROR_EAGAIN {
                continue;
            }
            if flush_result == AVERROR_EOF {
                session.flush_packet_sent = true;
                return Ok(None);
            }
            if flush_result < 0 {
                write_window_message(result, "Rust decode core could not flush decoder.");
                return Err(STATUS_PACKET_SEND_FAILED);
            }
            session.flush_packet_sent = true;
            continue;
        }

        let read_result = (session.symbols.av_read_frame)(session.format_context, session.packet);
        if read_result == AVERROR_EOF {
            session.input_exhausted = true;
            continue;
        }

        if read_result < 0 {
            write_window_message(result, "Rust decode core could not read packet.");
            return Err(STATUS_PACKET_READ_FAILED);
        }

        if read_field::<c_int>(session.packet, AVPACKET_STREAM_INDEX_OFFSET)
            != session.video_stream_index
        {
            (session.symbols.av_packet_unref)(session.packet);
            continue;
        }

        session.has_pending_video_packet = true;
    }
}

unsafe fn try_receive_frame(
    session: &mut DecodeSession,
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
) -> Result<FramePlayerRustNativeFrame, c_int> {
    let width = read_field::<c_int>(source_frame, AVFRAME_WIDTH_OFFSET);
    let height = read_field::<c_int>(source_frame, AVFRAME_HEIGHT_OFFSET);
    let source_format = read_field::<c_int>(source_frame, AVFRAME_FORMAT_OFFSET);
    if width <= 0 || height <= 0 {
        return Err(STATUS_CONVERSION_FAILED);
    }

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

    let stride = width.checked_mul(4).ok_or(STATUS_CONVERSION_FAILED)?;
    let buffer_len = stride.checked_mul(height).ok_or(STATUS_CONVERSION_FAILED)? as usize;
    let mut buffer = Box::new(RustFrameBuffer {
        data: vec![0u8; buffer_len],
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
    !cancel_flag.is_null() && std::ptr::read_volatile(cancel_flag) != 0
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
