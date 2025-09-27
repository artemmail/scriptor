import importlib.util
import os
import sys
import types
from pathlib import Path


def _load_fw_runner_module():
    if "pika" not in sys.modules:
        pika_module = types.ModuleType("pika")
        adapters_module = types.ModuleType("pika.adapters")
        blocking_module = types.ModuleType("pika.adapters.blocking_connection")

        class _BlockingChannel:  # pragma: no cover - stub
            pass

        class _PlainCredentials:  # pragma: no cover - stub
            def __init__(self, *args, **kwargs):
                pass

        class _ConnectionParameters:  # pragma: no cover - stub
            def __init__(self, *args, **kwargs):
                pass

        class _BlockingConnection:  # pragma: no cover - stub
            def __init__(self, *args, **kwargs):
                pass

            def channel(self):  # pragma: no cover - stub
                return types.SimpleNamespace(
                    queue_declare=lambda *args, **kwargs: None,
                    basic_qos=lambda *args, **kwargs: None,
                    basic_consume=lambda *args, **kwargs: None,
                    start_consuming=lambda *args, **kwargs: None,
                    basic_publish=lambda *args, **kwargs: None,
                    basic_ack=lambda *args, **kwargs: None,
                )

            def close(self):  # pragma: no cover - stub
                return None

        blocking_module.BlockingChannel = _BlockingChannel
        adapters_module.blocking_connection = blocking_module

        def _basic_properties(*args, **kwargs):
            return types.SimpleNamespace(**kwargs)

        pika_module.adapters = adapters_module
        pika_module.PlainCredentials = _PlainCredentials
        pika_module.ConnectionParameters = _ConnectionParameters
        pika_module.BlockingConnection = _BlockingConnection
        pika_module.BasicProperties = _basic_properties

        sys.modules["pika"] = pika_module
        sys.modules["pika.adapters"] = adapters_module
        sys.modules["pika.adapters.blocking_connection"] = blocking_module

    if "faster_whisper" not in sys.modules:
        fw_module = types.ModuleType("faster_whisper")

        class _WhisperModel:  # pragma: no cover - stub
            pass

        fw_module.WhisperModel = _WhisperModel
        sys.modules["faster_whisper"] = fw_module

    module_path = Path(__file__).resolve().parents[2] / "services" / "Whisper" / "fw_runner.py"
    spec = importlib.util.spec_from_file_location("fw_runner", module_path)
    module = importlib.util.module_from_spec(spec)
    assert spec and spec.loader  # safety for type checkers
    spec.loader.exec_module(module)
    return module


def test_configure_ffmpeg_binary_sets_and_clears():
    fw_runner = _load_fw_runner_module()
    original = os.environ.get("FFMPEG_BINARY")
    try:
        fw_runner._configure_ffmpeg_binary("/usr/bin/ffmpeg")
        assert os.environ["FFMPEG_BINARY"] == "/usr/bin/ffmpeg"

        fw_runner._configure_ffmpeg_binary("")
        assert "FFMPEG_BINARY" not in os.environ

        os.environ["FFMPEG_BINARY"] = "preset"
        fw_runner._configure_ffmpeg_binary(None)
        assert "FFMPEG_BINARY" not in os.environ
    finally:
        if original is not None:
            os.environ["FFMPEG_BINARY"] = original
        else:
            os.environ.pop("FFMPEG_BINARY", None)
