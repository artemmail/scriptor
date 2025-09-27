import ast
import json
import logging
import os
import sys
import time
import traceback
from pathlib import Path
from typing import Dict, Tuple

import pika

try:
    from faster_whisper import WhisperModel
except Exception as exc:  # pragma: no cover - import error path
    logging.exception("IMPORT ERROR faster_whisper: %s", exc)
    sys.exit(3)

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(message)s",
)

LOGGER = logging.getLogger("fw_runner")

# CTranslate2 defaults for deterministic inference
os.environ.setdefault("CT2_CUDA_DISABLE_CUDNN", "1")
os.environ.setdefault("CT2_CUDA_USE_TF32", "1")
os.environ.setdefault("CT2_VERBOSE", "1")
os.environ.setdefault("HF_HUB_DISABLE_SYMLINKS_WARNING", "1")

BROKER_NAME = os.getenv("EVENTBUS_BROKER", "workspace_development71")
HOST = os.getenv("EVENTBUS_HOST", "192.168.1.8")
USERNAME = os.getenv("EVENTBUS_USERNAME", "admin")
PASSWORD = os.getenv("EVENTBUS_PASSWORD", "121212")
COMMAND_QUEUE = os.getenv("EVENTBUS_COMMAND_QUEUE_NAME", "w.ds_development_cmd")
RESPONSE_QUEUE = os.getenv("EVENTBUS_QUEUE_NAME", "w.ds_develqqqqopmjшшj1")
RETRY_COUNT = int(os.getenv("EVENTBUS_RETRY_COUNT", "10"))
RETRY_DELAY_SECONDS = float(os.getenv("EVENTBUS_RETRY_DELAY", "5"))

MODEL_CACHE: Dict[Tuple[str, str, str], WhisperModel] = {}


def _configure_ffmpeg_binary(ffmpeg_executable) -> None:
    value = str(ffmpeg_executable).strip() if ffmpeg_executable is not None else ""
    if value:
        os.environ["FFMPEG_BINARY"] = value
    else:
        os.environ.pop("FFMPEG_BINARY", None)


def _parse_temperatures(value: str):
    value = (value or "").strip()
    if not value:
        return 0.0

    try:
        parsed = ast.literal_eval(value)
    except Exception:
        parsed = value

    if isinstance(parsed, (list, tuple)):
        result = []
        for item in parsed:
            try:
                result.append(float(item))
            except Exception:
                continue
        if not result:
            return 0.0
        return tuple(result)

    try:
        return float(parsed)
    except Exception:
        try:
            return float(value)
        except Exception:
            return 0.0


def _parse_float(value: str, default: float = 0.0) -> float:
    try:
        return float(value)
    except Exception:
        return default


def _parse_bool(value) -> bool:
    if isinstance(value, bool):
        return value
    return str(value).strip().lower() in {"1", "true", "yes", "y", "on"}


def _ensure_model(model: str, device: str, compute_type: str) -> WhisperModel:
    key = (model, device, compute_type)
    if key not in MODEL_CACHE:
        LOGGER.info("Loading FasterWhisper model=%s device=%s compute_type=%s", model, device, compute_type)
        MODEL_CACHE[key] = WhisperModel(model, device=device, compute_type=compute_type)
    return MODEL_CACHE[key]


def _transcribe(payload: dict) -> Dict[str, object]:
    ffmpeg_executable = payload.get("ffmpegExecutable")
    _configure_ffmpeg_binary(ffmpeg_executable)

    audio = payload.get("audio")
    if not audio:
        raise ValueError("Audio path was not provided")

    audio_path = Path(audio)
    if not audio_path.exists():
        raise FileNotFoundError(f"Audio file not found: {audio_path}")

    model_name = payload.get("model") or "medium"
    device = payload.get("device") or "cpu"
    compute_type = payload.get("computeType") or "int8"
    language = payload.get("language")
    temperature_literal = payload.get("temperature") or "0.0"
    compression_literal = payload.get("compressionRatioThreshold") or "2.4"
    log_prob_literal = payload.get("logProbThreshold") or "-1.0"
    no_speech_literal = payload.get("noSpeechThreshold") or "0.6"
    condition_literal = payload.get("conditionOnPreviousText") or "True"

    model_instance = _ensure_model(model_name, device, compute_type)

    temperature = _parse_temperatures(temperature_literal)
    compression_ratio_threshold = _parse_float(compression_literal, default=2.4)
    log_prob_threshold = _parse_float(log_prob_literal, default=-1.0)
    no_speech_threshold = _parse_float(no_speech_literal, default=0.6)
    condition_on_previous_text = _parse_bool(condition_literal)

    segments, info = model_instance.transcribe(
        str(audio_path),
        language=language if language and str(language).lower() != "auto" else None,
        word_timestamps=True,
        vad_filter=False,
        beam_size=5,
        temperature=temperature,
        compression_ratio_threshold=compression_ratio_threshold,
        log_prob_threshold=log_prob_threshold,
        no_speech_threshold=no_speech_threshold,
        condition_on_previous_text=condition_on_previous_text,
        without_timestamps=False,
    )

    data = {
        "language": getattr(info, "language", None),
        "language_probability": float(getattr(info, "language_probability", 0.0) or 0.0),
        "segments": [],
    }

    for seg in segments:
        item = {
            "start": float(getattr(seg, "start", 0.0) or 0.0),
            "end": float(getattr(seg, "end", 0.0) or 0.0),
            "text": (getattr(seg, "text", "") or "").strip(),
            "words": [],
        }

        for word in getattr(seg, "words", []) or []:
            item["words"].append(
                {
                    "start": float(getattr(word, "start", 0.0) or 0.0),
                    "end": float(getattr(word, "end", 0.0) or 0.0),
                    "word": (getattr(word, "word", "") or ""),
                }
            )

        data["segments"].append(item)

    return data


def _publish_response(channel: pika.adapters.blocking_connection.BlockingChannel, routing_key: str, correlation_id: str, message: dict) -> None:
    body = json.dumps(message, ensure_ascii=False).encode("utf-8")
    properties = pika.BasicProperties(
        correlation_id=correlation_id,
        delivery_mode=2,  # persistent
    )
    channel.basic_publish(exchange="", routing_key=routing_key, properties=properties, body=body)


def _on_message(channel: pika.adapters.blocking_connection.BlockingChannel, method, properties, body: bytes) -> None:
    correlation_id = getattr(properties, "correlation_id", None)
    reply_to = getattr(properties, "reply_to", None) or RESPONSE_QUEUE

    try:
        payload = json.loads(body.decode("utf-8"))
    except Exception as exc:
        LOGGER.exception("Invalid payload received: %s", exc)
        if correlation_id:
            _publish_response(channel, reply_to, correlation_id, {
                "success": False,
                "error": "Invalid payload",
                "diagnostics": traceback.format_exc(),
            })
        channel.basic_ack(delivery_tag=method.delivery_tag)
        return

    try:
        LOGGER.info("Processing transcription request correlation_id=%s audio=%s", correlation_id, payload.get("audio"))
        result = _transcribe(payload)
        response = {
            "success": True,
            "transcriptJson": json.dumps(result, ensure_ascii=False),
            "diagnostics": None,
        }
    except Exception as exc:
        LOGGER.exception("Transcription failed: %s", exc)
        response = {
            "success": False,
            "error": str(exc),
            "diagnostics": traceback.format_exc(),
        }

    if correlation_id:
        try:
            _publish_response(channel, reply_to, correlation_id, response)
        except Exception:
            LOGGER.exception("Failed to publish response for correlation_id=%s", correlation_id)
    else:
        LOGGER.warning("No correlation id provided, skipping response publish")

    channel.basic_ack(delivery_tag=method.delivery_tag)


def _connect() -> pika.BlockingConnection:
    credentials = pika.PlainCredentials(USERNAME, PASSWORD)
    parameters = pika.ConnectionParameters(
        host=HOST,
        credentials=credentials,
        heartbeat=60,
        blocked_connection_timeout=300,
    )
    return pika.BlockingConnection(parameters)


def main() -> None:
    attempts = 0
    while True:
        connection = None
        try:
            connection = _connect()
            channel = connection.channel()
            channel.queue_declare(queue=COMMAND_QUEUE, durable=True)
            channel.queue_declare(queue=RESPONSE_QUEUE, durable=True)
            channel.basic_qos(prefetch_count=1)
            channel.basic_consume(queue=COMMAND_QUEUE, on_message_callback=_on_message)

            LOGGER.info(
                "fw_runner ready. broker=%s host=%s command_queue=%s response_queue=%s",
                BROKER_NAME,
                HOST,
                COMMAND_QUEUE,
                RESPONSE_QUEUE,
            )

            attempts = 0
            channel.start_consuming()
        except KeyboardInterrupt:
            LOGGER.info("Interrupted by user")
            break
        except Exception as exc:
            attempts += 1
            LOGGER.exception("Connection error: %s", exc)
            if attempts > RETRY_COUNT:
                LOGGER.error("Maximum retry attempts exceeded, terminating")
                raise
            time.sleep(RETRY_DELAY_SECONDS)
        finally:
            if connection is not None:
                try:
                    connection.close()
                except Exception:
                    pass


if __name__ == '__main__':
    main()
