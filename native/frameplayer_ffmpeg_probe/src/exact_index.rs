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
const AVERROR_EOF: c_int = -541_478_725;
#[cfg(target_os = "macos")]
const AVERROR_EAGAIN: c_int = -35;
#[cfg(not(target_os = "macos"))]
const AVERROR_EAGAIN: c_int = -11;
const AV_NOPTS_VALUE: i64 = i64::MIN;
const AV_FRAME_FLAG_KEY: c_int = 0x0002;

// Field offsets match the pinned FFmpeg.AutoGen 8.x bindings used by the
// bundled FFmpeg 8.1 runtime. Keep parity tests forced to Rust when changing.
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
const AVFRAME_PTS_OFFSET: usize = 136;
const AVFRAME_PKT_DTS_OFFSET: usize = 144;
const AVFRAME_FLAGS_OFFSET: usize = 276;
const AVFRAME_BEST_EFFORT_TIMESTAMP_OFFSET: usize = 304;

type AvStrErrorFn = unsafe extern "C" fn(c_int, *mut c_char, usize) -> c_int;
type AvformatOpenInputFn =
    unsafe extern "C" fn(*mut *mut c_void, *const c_char, *mut c_void, *mut *mut c_void) -> c_int;
type AvformatFindStreamInfoFn = unsafe extern "C" fn(*mut c_void, *mut *mut c_void) -> c_int;
type AvformatCloseInputFn = unsafe extern "C" fn(*mut *mut c_void);
type AvReadFrameFn = unsafe extern "C" fn(*mut c_void, *mut c_void) -> c_int;
type AvGuessFrameRateFn = unsafe extern "C" fn(*mut c_void, *mut c_void, *mut c_void) -> AVRational;
type AvCodecFindDecoderFn = unsafe extern "C" fn(c_int) -> *mut c_void;
type AvCodecAllocContext3Fn = unsafe extern "C" fn(*const c_void) -> *mut c_void;
type AvCodecParametersToContextFn = unsafe extern "C" fn(*mut c_void, *const c_void) -> c_int;
type AvCodecOpen2Fn = unsafe extern "C" fn(*mut c_void, *const c_void, *mut *mut c_void) -> c_int;
type AvCodecFreeContextFn = unsafe extern "C" fn(*mut *mut c_void);
type AvCodecSendPacketFn = unsafe extern "C" fn(*mut c_void, *const c_void) -> c_int;
type AvCodecReceiveFrameFn = unsafe extern "C" fn(*mut c_void, *mut c_void) -> c_int;
type AvPacketAllocFn = unsafe extern "C" fn() -> *mut c_void;
type AvPacketFreeFn = unsafe extern "C" fn(*mut *mut c_void);
type AvPacketUnrefFn = unsafe extern "C" fn(*mut c_void);
type AvFrameAllocFn = unsafe extern "C" fn() -> *mut c_void;
type AvFrameFreeFn = unsafe extern "C" fn(*mut *mut c_void);
type AvFrameUnrefFn = unsafe extern "C" fn(*mut c_void);

#[repr(C)]
#[derive(Clone, Copy)]
pub struct AVRational {
    pub num: c_int,
    pub den: c_int,
}

#[repr(C)]
pub struct FramePlayerRustFfmpegGlobalIndexEntry {
    pub absolute_frame_index: i64,
    pub presentation_timestamp: i64,
    pub decode_timestamp: i64,
    pub search_timestamp: i64,
    pub is_key_frame: c_int,
    pub seek_anchor_frame_index: i64,
    pub seek_anchor_timestamp: i64,
}

#[repr(C)]
pub struct FramePlayerRustFfmpegGlobalIndexResult {
    pub status: c_int,
    pub entries: *mut FramePlayerRustFfmpegGlobalIndexEntry,
    pub entry_count: u64,
    pub time_base_num: c_int,
    pub time_base_den: c_int,
    pub message: [c_char; MESSAGE_CAPACITY],
}

impl Default for FramePlayerRustFfmpegGlobalIndexResult {
    fn default() -> Self {
        Self {
            status: STATUS_INVALID_ARGUMENT,
            entries: std::ptr::null_mut(),
            entry_count: 0,
            time_base_num: 0,
            time_base_den: 0,
            message: [0 as c_char; MESSAGE_CAPACITY],
        }
    }
}

#[derive(Clone, Copy)]
struct Anchor {
    frame_index: i64,
    timestamp: i64,
}

struct Symbols {
    av_strerror: AvStrErrorFn,
    avformat_open_input: AvformatOpenInputFn,
    avformat_find_stream_info: AvformatFindStreamInfoFn,
    avformat_close_input: AvformatCloseInputFn,
    av_read_frame: AvReadFrameFn,
    av_guess_frame_rate: AvGuessFrameRateFn,
    avcodec_find_decoder: AvCodecFindDecoderFn,
    avcodec_alloc_context3: AvCodecAllocContext3Fn,
    avcodec_parameters_to_context: AvCodecParametersToContextFn,
    avcodec_open2: AvCodecOpen2Fn,
    avcodec_free_context: AvCodecFreeContextFn,
    avcodec_send_packet: AvCodecSendPacketFn,
    avcodec_receive_frame: AvCodecReceiveFrameFn,
    av_packet_alloc: AvPacketAllocFn,
    av_packet_free: AvPacketFreeFn,
    av_packet_unref: AvPacketUnrefFn,
    av_frame_alloc: AvFrameAllocFn,
    av_frame_free: AvFrameFreeFn,
    av_frame_unref: AvFrameUnrefFn,
}

struct FormatContextGuard {
    ptr: *mut c_void,
    close: AvformatCloseInputFn,
}

impl Drop for FormatContextGuard {
    fn drop(&mut self) {
        unsafe {
            if !self.ptr.is_null() {
                (self.close)(&mut self.ptr);
            }
        }
    }
}

struct CodecContextGuard {
    ptr: *mut c_void,
    free: AvCodecFreeContextFn,
}

impl Drop for CodecContextGuard {
    fn drop(&mut self) {
        unsafe {
            if !self.ptr.is_null() {
                (self.free)(&mut self.ptr);
            }
        }
    }
}

struct PacketGuard {
    ptr: *mut c_void,
    free: AvPacketFreeFn,
}

impl Drop for PacketGuard {
    fn drop(&mut self) {
        unsafe {
            if !self.ptr.is_null() {
                (self.free)(&mut self.ptr);
            }
        }
    }
}

struct FrameGuard {
    ptr: *mut c_void,
    free: AvFrameFreeFn,
}

impl Drop for FrameGuard {
    fn drop(&mut self) {
        unsafe {
            if !self.ptr.is_null() {
                (self.free)(&mut self.ptr);
            }
        }
    }
}

struct IndexSession {
    _runtime: crate::RuntimeLibraries,
    symbols: Symbols,
    format_context: FormatContextGuard,
    codec_context: CodecContextGuard,
    packet: PacketGuard,
    frame: FrameGuard,
}

enum IndexLoopAction {
    Continue,
    End,
    ReadPacket,
}

#[no_mangle]
pub unsafe extern "C" fn frameplayer_rust_ffmpeg_global_frame_index(
    runtime_directory: *const c_char,
    file_path: *const c_char,
    video_stream_index: c_int,
    cancel_flag: *const c_int,
    result: *mut FramePlayerRustFfmpegGlobalIndexResult,
) -> c_int {
    if result.is_null() {
        return STATUS_INVALID_ARGUMENT;
    }

    *result = FramePlayerRustFfmpegGlobalIndexResult::default();

    let status = std::panic::catch_unwind(|| {
        global_frame_index_inner(
            runtime_directory,
            file_path,
            video_stream_index,
            cancel_flag,
            result,
        )
    })
    .unwrap_or_else(|_| {
        (*result).status = STATUS_INVALID_ARGUMENT;
        write_message(result, "Rust exact frame index builder panicked.");
        STATUS_INVALID_ARGUMENT
    });

    (*result).status = status;
    status
}

#[no_mangle]
pub unsafe extern "C" fn frameplayer_rust_ffmpeg_global_frame_index_free(
    entries: *mut FramePlayerRustFfmpegGlobalIndexEntry,
    entry_count: usize,
) {
    if entries.is_null() || entry_count == 0 {
        return;
    }

    drop(Box::from_raw(std::slice::from_raw_parts_mut(
        entries,
        entry_count,
    )));
}

unsafe fn global_frame_index_inner(
    runtime_directory: *const c_char,
    file_path: *const c_char,
    video_stream_index: c_int,
    cancel_flag: *const c_int,
    result: *mut FramePlayerRustFfmpegGlobalIndexResult,
) -> c_int {
    if runtime_directory.is_null() || file_path.is_null() || video_stream_index < 0 {
        write_message(result, "Exact frame index arguments were invalid.");
        return STATUS_INVALID_ARGUMENT;
    }

    let runtime_directory = match CStr::from_ptr(runtime_directory).to_str() {
        Ok(value) if !value.trim().is_empty() => value,
        _ => {
            write_message(result, "FFmpeg runtime directory was not valid.");
            return STATUS_RUNTIME_DIRECTORY_MISSING;
        }
    };

    let file_path = match CStr::from_ptr(file_path).to_str() {
        Ok(value) if !value.trim().is_empty() => value,
        _ => {
            write_message(result, "Media file path was not valid.");
            return STATUS_INVALID_ARGUMENT;
        }
    };

    match build_global_frame_index(
        Path::new(runtime_directory),
        file_path,
        video_stream_index,
        cancel_flag,
        result,
    ) {
        Ok(()) => STATUS_OK,
        Err(status) => status,
    }
}

unsafe fn build_global_frame_index(
    runtime_directory: &Path,
    file_path: &str,
    video_stream_index: c_int,
    cancel_flag: *const c_int,
    result: *mut FramePlayerRustFfmpegGlobalIndexResult,
) -> Result<(), c_int> {
    ensure_index_not_cancelled(cancel_flag, result)?;

    let session = create_index_session(runtime_directory, file_path, video_stream_index, result)?;
    let entries = decode_index_entries(
        &session.symbols,
        session.format_context.ptr,
        session.codec_context.ptr,
        session.packet.ptr,
        session.frame.ptr,
        video_stream_index,
        cancel_flag,
        result,
    )?;

    (*result).entry_count = entries.len() as u64;
    if entries.is_empty() {
        (*result).entries = std::ptr::null_mut();
    } else {
        let mut boxed = entries.into_boxed_slice();
        (*result).entries = boxed.as_mut_ptr();
        std::mem::forget(boxed);
    }

    write_message(
        result,
        "Rust exact frame index decoded display-order frames.",
    );
    Ok(())
}

unsafe fn create_index_session(
    runtime_directory: &Path,
    file_path: &str,
    video_stream_index: c_int,
    result: *mut FramePlayerRustFfmpegGlobalIndexResult,
) -> Result<IndexSession, c_int> {
    ensure_index_runtime_directory(runtime_directory, result)?;

    let (runtime, symbols) = load_index_runtime_symbols(runtime_directory, result)?;
    let format_context = open_index_format_context(&symbols, file_path, result)?;
    let (video_stream, video_time_base) =
        resolve_index_video_stream(format_context.ptr, video_stream_index, result)?;
    let codec_context = create_index_codec_context(
        &symbols,
        format_context.ptr,
        video_stream,
        video_time_base,
        result,
    )?;
    let packet = allocate_index_packet(&symbols, result)?;
    let frame = allocate_index_frame(&symbols, result)?;

    Ok(IndexSession {
        _runtime: runtime,
        symbols,
        format_context,
        codec_context,
        packet,
        frame,
    })
}

unsafe fn ensure_index_runtime_directory(
    runtime_directory: &Path,
    result: *mut FramePlayerRustFfmpegGlobalIndexResult,
) -> Result<(), c_int> {
    if runtime_directory.is_dir() {
        return Ok(());
    }

    write_message(
        result,
        &format!(
            "FFmpeg runtime directory does not exist: {}",
            runtime_directory.display()
        ),
    );
    Err(STATUS_RUNTIME_DIRECTORY_MISSING)
}

unsafe fn load_index_runtime_symbols(
    runtime_directory: &Path,
    result: *mut FramePlayerRustFfmpegGlobalIndexResult,
) -> Result<(crate::RuntimeLibraries, Symbols), c_int> {
    let runtime = match load_runtime_libraries(runtime_directory) {
        Ok(value) => value,
        Err(STATUS_LIBRARY_LOAD_FAILED) => {
            write_message(result, "Could not load FFmpeg runtime libraries.");
            return Err(STATUS_LIBRARY_LOAD_FAILED);
        }
        Err(STATUS_SYMBOL_LOAD_FAILED) => {
            write_message(result, "Could not resolve FFmpeg runtime symbols.");
            return Err(STATUS_SYMBOL_LOAD_FAILED);
        }
        Err(status) => return Err(status),
    };
    let symbols = load_symbols(&runtime)?;
    Ok((runtime, symbols))
}

unsafe fn open_index_format_context(
    symbols: &Symbols,
    file_path: &str,
    result: *mut FramePlayerRustFfmpegGlobalIndexResult,
) -> Result<FormatContextGuard, c_int> {
    let input_path = match CString::new(file_path) {
        Ok(value) => value,
        Err(_) => {
            write_message(result, "Media file path contained a NUL byte.");
            return Err(STATUS_INVALID_ARGUMENT);
        }
    };

    let mut raw_format_context: *mut c_void = std::ptr::null_mut();
    let open_result = (symbols.avformat_open_input)(
        &mut raw_format_context,
        input_path.as_ptr(),
        std::ptr::null_mut(),
        std::ptr::null_mut(),
    );
    if open_result < 0 {
        if !raw_format_context.is_null() {
            (symbols.avformat_close_input)(&mut raw_format_context);
        }
        write_message(
            result,
            &format!(
                "Could not open media for Rust exact frame index: {}",
                ffmpeg_error(symbols.av_strerror, open_result)
            ),
        );
        return Err(STATUS_FILE_OPEN_FAILED);
    }
    let format_context = FormatContextGuard {
        ptr: raw_format_context,
        close: symbols.avformat_close_input,
    };

    let stream_info_result =
        (symbols.avformat_find_stream_info)(format_context.ptr, std::ptr::null_mut());
    if stream_info_result < 0 {
        write_message(
            result,
            &format!(
                "Could not probe streams for Rust exact frame index: {}",
                ffmpeg_error(symbols.av_strerror, stream_info_result)
            ),
        );
        return Err(STATUS_FILE_OPEN_FAILED);
    }

    Ok(format_context)
}

unsafe fn resolve_index_video_stream(
    format_context: *mut c_void,
    video_stream_index: c_int,
    result: *mut FramePlayerRustFfmpegGlobalIndexResult,
) -> Result<(*mut c_void, AVRational), c_int> {
    let video_stream = get_stream(format_context, video_stream_index, result)?;
    let video_time_base = read_field::<AVRational>(video_stream, AVSTREAM_TIME_BASE_OFFSET);
    (*result).time_base_num = video_time_base.num;
    (*result).time_base_den = video_time_base.den;

    Ok((video_stream, video_time_base))
}

unsafe fn create_index_codec_context(
    symbols: &Symbols,
    format_context: *mut c_void,
    video_stream: *mut c_void,
    video_time_base: AVRational,
    result: *mut FramePlayerRustFfmpegGlobalIndexResult,
) -> Result<CodecContextGuard, c_int> {
    let codec_parameters = require_index_codec_parameters(video_stream, result)?;
    let decoder = find_index_decoder(symbols, codec_parameters, result)?;
    let codec_context = allocate_index_codec_context(symbols, decoder, result)?;

    copy_index_codec_parameters(symbols, codec_context.ptr, codec_parameters, result)?;
    configure_index_codec_context(
        symbols,
        codec_context.ptr,
        format_context,
        video_stream,
        video_time_base,
    );
    open_index_decoder(symbols, codec_context.ptr, decoder, result)?;
    Ok(codec_context)
}

unsafe fn require_index_codec_parameters(
    video_stream: *mut c_void,
    result: *mut FramePlayerRustFfmpegGlobalIndexResult,
) -> Result<*mut c_void, c_int> {
    let codec_parameters = read_field::<*mut c_void>(video_stream, AVSTREAM_CODECPAR_OFFSET);
    if codec_parameters.is_null() {
        write_message(result, "Video stream codec parameters were unavailable.");
        return Err(STATUS_STREAM_UNAVAILABLE);
    }
    Ok(codec_parameters)
}

unsafe fn find_index_decoder(
    symbols: &Symbols,
    codec_parameters: *mut c_void,
    result: *mut FramePlayerRustFfmpegGlobalIndexResult,
) -> Result<*mut c_void, c_int> {
    let codec_id = read_field::<c_int>(codec_parameters, AVCODEC_PARAMETERS_CODEC_ID_OFFSET);
    let decoder = (symbols.avcodec_find_decoder)(codec_id);
    if decoder.is_null() {
        write_message(
            result,
            "No decoder is available for the indexed video stream.",
        );
        return Err(STATUS_DECODER_UNAVAILABLE);
    }
    Ok(decoder)
}

unsafe fn allocate_index_codec_context(
    symbols: &Symbols,
    decoder: *mut c_void,
    result: *mut FramePlayerRustFfmpegGlobalIndexResult,
) -> Result<CodecContextGuard, c_int> {
    let raw_codec_context = (symbols.avcodec_alloc_context3)(decoder);
    if raw_codec_context.is_null() {
        write_message(
            result,
            "Could not allocate the FFmpeg codec context for Rust exact frame indexing.",
        );
        return Err(STATUS_CODEC_CONTEXT_ALLOC_FAILED);
    }
    let codec_context = CodecContextGuard {
        ptr: raw_codec_context,
        free: symbols.avcodec_free_context,
    };
    Ok(codec_context)
}

unsafe fn copy_index_codec_parameters(
    symbols: &Symbols,
    codec_context: *mut c_void,
    codec_parameters: *mut c_void,
    result: *mut FramePlayerRustFfmpegGlobalIndexResult,
) -> Result<(), c_int> {
    let copy_result = (symbols.avcodec_parameters_to_context)(codec_context, codec_parameters);
    if copy_result < 0 {
        write_message(
            result,
            &format!(
                "Could not copy codec parameters for Rust exact frame index: {}",
                ffmpeg_error(symbols.av_strerror, copy_result)
            ),
        );
        return Err(STATUS_CODEC_CONTEXT_FAILED);
    }
    Ok(())
}

unsafe fn configure_index_codec_context(
    symbols: &Symbols,
    codec_context: *mut c_void,
    format_context: *mut c_void,
    video_stream: *mut c_void,
    video_time_base: AVRational,
) {
    write_field(
        codec_context,
        AVCODEC_CONTEXT_PKT_TIMEBASE_OFFSET,
        video_time_base,
    );
    write_field(
        codec_context,
        AVCODEC_CONTEXT_FRAMERATE_OFFSET,
        get_nominal_frame_rate(symbols, format_context, video_stream),
    );
}

unsafe fn open_index_decoder(
    symbols: &Symbols,
    codec_context: *mut c_void,
    decoder: *mut c_void,
    result: *mut FramePlayerRustFfmpegGlobalIndexResult,
) -> Result<(), c_int> {
    let open_decoder_result = (symbols.avcodec_open2)(codec_context, decoder, std::ptr::null_mut());
    if open_decoder_result < 0 {
        write_message(
            result,
            &format!(
                "Could not open decoder for Rust exact frame index: {}",
                ffmpeg_error(symbols.av_strerror, open_decoder_result)
            ),
        );
        return Err(STATUS_CODEC_CONTEXT_FAILED);
    }
    Ok(())
}

unsafe fn allocate_index_packet(
    symbols: &Symbols,
    result: *mut FramePlayerRustFfmpegGlobalIndexResult,
) -> Result<PacketGuard, c_int> {
    let raw_packet = (symbols.av_packet_alloc)();
    if raw_packet.is_null() {
        write_message(
            result,
            "Could not allocate a packet for Rust exact frame index.",
        );
        return Err(STATUS_PACKET_ALLOC_FAILED);
    }
    let packet = PacketGuard {
        ptr: raw_packet,
        free: symbols.av_packet_free,
    };
    Ok(packet)
}

unsafe fn allocate_index_frame(
    symbols: &Symbols,
    result: *mut FramePlayerRustFfmpegGlobalIndexResult,
) -> Result<FrameGuard, c_int> {
    let raw_frame = (symbols.av_frame_alloc)();
    if raw_frame.is_null() {
        write_message(
            result,
            "Could not allocate a frame for Rust exact frame index.",
        );
        return Err(STATUS_FRAME_ALLOC_FAILED);
    }
    let frame = FrameGuard {
        ptr: raw_frame,
        free: symbols.av_frame_free,
    };
    Ok(frame)
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
        av_guess_frame_rate: load_symbol::<AvGuessFrameRateFn>(
            &runtime.avformat,
            "av_guess_frame_rate",
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
        av_packet_alloc: load_symbol::<AvPacketAllocFn>(&runtime.avcodec, "av_packet_alloc")?,
        av_packet_free: load_symbol::<AvPacketFreeFn>(&runtime.avcodec, "av_packet_free")?,
        av_packet_unref: load_symbol::<AvPacketUnrefFn>(&runtime.avcodec, "av_packet_unref")?,
        av_frame_alloc: load_symbol::<AvFrameAllocFn>(&runtime.avutil, "av_frame_alloc")?,
        av_frame_free: load_symbol::<AvFrameFreeFn>(&runtime.avutil, "av_frame_free")?,
        av_frame_unref: load_symbol::<AvFrameUnrefFn>(&runtime.avutil, "av_frame_unref")?,
    })
}

unsafe fn decode_index_entries(
    symbols: &Symbols,
    format_context: *mut c_void,
    codec_context: *mut c_void,
    packet: *mut c_void,
    decoded_frame: *mut c_void,
    video_stream_index: c_int,
    cancel_flag: *const c_int,
    result: *mut FramePlayerRustFfmpegGlobalIndexResult,
) -> Result<Vec<FramePlayerRustFfmpegGlobalIndexEntry>, c_int> {
    let mut entries = Vec::new();
    let mut current_anchor: Option<Anchor> = None;
    let mut absolute_frame_index = 0i64;
    let mut has_pending_video_packet = false;
    let mut input_exhausted = false;
    let mut flush_packet_sent = false;

    loop {
        ensure_index_not_cancelled(cancel_flag, result)?;

        match try_receive_indexed_frame(
            symbols,
            codec_context,
            decoded_frame,
            &mut absolute_frame_index,
            &mut current_anchor,
            result,
        )? {
            Some(entry) => {
                entries.push(entry);
                continue;
            }
            None => {}
        }

        if submit_pending_index_packet(
            symbols,
            codec_context,
            packet,
            &mut has_pending_video_packet,
            result,
        )? {
            continue;
        }

        match flush_index_decoder(
            symbols,
            codec_context,
            &mut input_exhausted,
            &mut flush_packet_sent,
            result,
        )? {
            IndexLoopAction::Continue => continue,
            IndexLoopAction::End => break,
            IndexLoopAction::ReadPacket => {}
        }

        read_index_packet(
            symbols,
            format_context,
            packet,
            video_stream_index,
            &mut input_exhausted,
            &mut has_pending_video_packet,
            result,
        )?;
    }

    if has_pending_video_packet {
        (symbols.av_packet_unref)(packet);
    }

    Ok(entries)
}

unsafe fn ensure_index_not_cancelled(
    cancel_flag: *const c_int,
    result: *mut FramePlayerRustFfmpegGlobalIndexResult,
) -> Result<(), c_int> {
    if cancellation_requested(cancel_flag) {
        write_message(result, "Exact frame index build was cancelled.");
        return Err(STATUS_CANCELLED);
    }

    Ok(())
}

unsafe fn submit_pending_index_packet(
    symbols: &Symbols,
    codec_context: *mut c_void,
    packet: *mut c_void,
    has_pending_video_packet: &mut bool,
    result: *mut FramePlayerRustFfmpegGlobalIndexResult,
) -> Result<bool, c_int> {
    if !*has_pending_video_packet {
        return Ok(false);
    }

    let send_pending_result = (symbols.avcodec_send_packet)(codec_context, packet);
    if send_pending_result == AVERROR_EAGAIN {
        return Ok(true);
    }

    if send_pending_result < 0 {
        write_message(
            result,
            &format!(
                "Could not submit packet for Rust exact frame index: {}",
                ffmpeg_error(symbols.av_strerror, send_pending_result)
            ),
        );
        return Err(STATUS_PACKET_SEND_FAILED);
    }

    *has_pending_video_packet = false;
    (symbols.av_packet_unref)(packet);
    Ok(true)
}

unsafe fn flush_index_decoder(
    symbols: &Symbols,
    codec_context: *mut c_void,
    input_exhausted: &mut bool,
    flush_packet_sent: &mut bool,
    result: *mut FramePlayerRustFfmpegGlobalIndexResult,
) -> Result<IndexLoopAction, c_int> {
    if !*input_exhausted {
        return Ok(IndexLoopAction::ReadPacket);
    }

    if *flush_packet_sent {
        return Ok(IndexLoopAction::End);
    }

    let flush_result = (symbols.avcodec_send_packet)(codec_context, std::ptr::null());
    if flush_result == AVERROR_EAGAIN {
        return Ok(IndexLoopAction::Continue);
    }

    if flush_result == AVERROR_EOF {
        return Ok(IndexLoopAction::End);
    }

    if flush_result < 0 {
        write_message(
            result,
            &format!(
                "Could not flush decoder for Rust exact frame index: {}",
                ffmpeg_error(symbols.av_strerror, flush_result)
            ),
        );
        return Err(STATUS_PACKET_SEND_FAILED);
    }

    *flush_packet_sent = true;
    Ok(IndexLoopAction::Continue)
}

unsafe fn read_index_packet(
    symbols: &Symbols,
    format_context: *mut c_void,
    packet: *mut c_void,
    video_stream_index: c_int,
    input_exhausted: &mut bool,
    has_pending_video_packet: &mut bool,
    result: *mut FramePlayerRustFfmpegGlobalIndexResult,
) -> Result<(), c_int> {
    let read_result = (symbols.av_read_frame)(format_context, packet);
    if read_result == AVERROR_EOF {
        *input_exhausted = true;
        return Ok(());
    }

    if read_result < 0 {
        write_message(
            result,
            &format!(
                "Could not read packet for Rust exact frame index: {}",
                ffmpeg_error(symbols.av_strerror, read_result)
            ),
        );
        return Err(STATUS_PACKET_READ_FAILED);
    }

    if read_field::<c_int>(packet, AVPACKET_STREAM_INDEX_OFFSET) != video_stream_index {
        (symbols.av_packet_unref)(packet);
        return Ok(());
    }

    *has_pending_video_packet = true;
    Ok(())
}

unsafe fn try_receive_indexed_frame(
    symbols: &Symbols,
    codec_context: *mut c_void,
    decoded_frame: *mut c_void,
    absolute_frame_index: &mut i64,
    current_anchor: &mut Option<Anchor>,
    result: *mut FramePlayerRustFfmpegGlobalIndexResult,
) -> Result<Option<FramePlayerRustFfmpegGlobalIndexEntry>, c_int> {
    loop {
        let receive_result = (symbols.avcodec_receive_frame)(codec_context, decoded_frame);
        if receive_result == AVERROR_EAGAIN || receive_result == AVERROR_EOF {
            return Ok(None);
        }

        if receive_result < 0 {
            write_message(
                result,
                &format!(
                    "Could not decode frame for Rust exact frame index: {}",
                    ffmpeg_error(symbols.av_strerror, receive_result)
                ),
            );
            return Err(STATUS_FRAME_RECEIVE_FAILED);
        }

        if read_field::<c_int>(decoded_frame, AVFRAME_WIDTH_OFFSET) <= 0
            || read_field::<c_int>(decoded_frame, AVFRAME_HEIGHT_OFFSET) <= 0
        {
            (symbols.av_frame_unref)(decoded_frame);
            continue;
        }

        let entry = create_index_entry(decoded_frame, *absolute_frame_index, *current_anchor);
        *absolute_frame_index += 1;
        if entry.is_key_frame != 0 && entry.search_timestamp > 0 {
            *current_anchor = Some(Anchor {
                frame_index: entry.absolute_frame_index,
                timestamp: entry.search_timestamp,
            });
        }

        (symbols.av_frame_unref)(decoded_frame);
        return Ok(Some(entry));
    }
}

unsafe fn create_index_entry(
    decoded_frame: *mut c_void,
    absolute_frame_index: i64,
    current_anchor: Option<Anchor>,
) -> FramePlayerRustFfmpegGlobalIndexEntry {
    let presentation_timestamp = best_presentation_timestamp(decoded_frame);
    let decode_timestamp =
        timestamp_or_none(read_field::<i64>(decoded_frame, AVFRAME_PKT_DTS_OFFSET));
    let search_timestamp = presentation_timestamp.or(decode_timestamp);
    let flags = read_field::<c_int>(decoded_frame, AVFRAME_FLAGS_OFFSET);
    let is_key_frame = (flags & AV_FRAME_FLAG_KEY) != 0;

    let mut seek_anchor_frame_index = 0i64;
    let mut seek_anchor_timestamp = 0i64;
    if is_key_frame && search_timestamp.unwrap_or(AV_NOPTS_VALUE) > 0 {
        seek_anchor_frame_index = absolute_frame_index;
        seek_anchor_timestamp = search_timestamp.unwrap_or(0);
    } else if let Some(anchor) = current_anchor {
        seek_anchor_frame_index = anchor.frame_index;
        seek_anchor_timestamp = anchor.timestamp;
    }

    FramePlayerRustFfmpegGlobalIndexEntry {
        absolute_frame_index,
        presentation_timestamp: presentation_timestamp.unwrap_or(AV_NOPTS_VALUE),
        decode_timestamp: decode_timestamp.unwrap_or(AV_NOPTS_VALUE),
        search_timestamp: search_timestamp.unwrap_or(AV_NOPTS_VALUE),
        is_key_frame: if is_key_frame { 1 } else { 0 },
        seek_anchor_frame_index,
        seek_anchor_timestamp,
    }
}

unsafe fn best_presentation_timestamp(decoded_frame: *mut c_void) -> Option<i64> {
    timestamp_or_none(read_field::<i64>(
        decoded_frame,
        AVFRAME_BEST_EFFORT_TIMESTAMP_OFFSET,
    ))
    .or_else(|| timestamp_or_none(read_field::<i64>(decoded_frame, AVFRAME_PTS_OFFSET)))
    .or_else(|| timestamp_or_none(read_field::<i64>(decoded_frame, AVFRAME_PKT_DTS_OFFSET)))
}

unsafe fn get_stream(
    format_context: *mut c_void,
    video_stream_index: c_int,
    result: *mut FramePlayerRustFfmpegGlobalIndexResult,
) -> Result<*mut c_void, c_int> {
    let stream_count = read_field::<u32>(format_context, AVFORMAT_CONTEXT_NB_STREAMS_OFFSET);
    if video_stream_index < 0 || video_stream_index as u32 >= stream_count {
        write_message(
            result,
            "The requested primary video stream is not available for Rust exact frame indexing.",
        );
        return Err(STATUS_STREAM_UNAVAILABLE);
    }

    let streams = read_field::<*mut *mut c_void>(format_context, AVFORMAT_CONTEXT_STREAMS_OFFSET);
    if streams.is_null() {
        write_message(
            result,
            "Media stream table was unavailable for Rust exact frame indexing.",
        );
        return Err(STATUS_STREAM_UNAVAILABLE);
    }

    let stream = *streams.add(video_stream_index as usize);
    if stream.is_null() {
        write_message(
            result,
            "Requested media stream was unavailable for Rust exact frame indexing.",
        );
        return Err(STATUS_STREAM_UNAVAILABLE);
    }

    Ok(stream)
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

fn rational_is_valid(rational: AVRational) -> bool {
    rational.num != 0 && rational.den != 0
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

unsafe fn write_message(result: *mut FramePlayerRustFfmpegGlobalIndexResult, message: &str) {
    let bytes = message.as_bytes();
    let byte_count = bytes.len().min(MESSAGE_CAPACITY - 1);
    for (index, byte) in bytes.iter().take(byte_count).enumerate() {
        (*result).message[index] = *byte as c_char;
    }
    (*result).message[byte_count] = 0;
}
