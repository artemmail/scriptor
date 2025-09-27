import ast
import json
import os
import sys
import traceback
from pathlib import Path

print('fw_runner: PY', sys.version)
print('fw_runner: CT2_CUDA_DISABLE_CUDNN=', os.getenv('CT2_CUDA_DISABLE_CUDNN'))
print('fw_runner: CT2_CUDA_USE_TF32=', os.getenv('CT2_CUDA_USE_TF32'))

try:
    from faster_whisper import WhisperModel
except Exception as exc:  # pragma: no cover - import error path
    print('IMPORT ERROR faster_whisper:', exc, file=sys.stderr)
    traceback.print_exc()
    sys.exit(3)


def _parse_temperatures(value: str):
    value = (value or '').strip()
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


def _parse_bool(value: str) -> bool:
    return value.strip().lower() in {'1', 'true', 'yes', 'y', 'on'}


def main() -> None:
    if len(sys.argv) != 12:
        print(
            'usage: fw_runner.py <audio> <model> <device> <compute_type> <language> '
            '<output_dir> <temperature> <compression_ratio_threshold> '
            '<log_prob_threshold> <no_speech_threshold> <condition_on_previous_text>',
            file=sys.stderr,
        )
        sys.exit(2)

    (
        audio,
        model,
        device,
        compute_type,
        language,
        out_dir,
        temperature_literal,
        compression_literal,
        log_prob_literal,
        no_speech_literal,
        condition_literal,
    ) = sys.argv[1:12]

    print('fw_runner args:', audio, model, device, compute_type, language, out_dir)

    out = Path(out_dir)
    out.mkdir(parents=True, exist_ok=True)

    try:
        model_instance = WhisperModel(model, device=device, compute_type=compute_type)
    except Exception as exc:  # pragma: no cover - model init path
        print('MODEL INIT ERROR:', exc, file=sys.stderr)
        traceback.print_exc()
        sys.exit(4)

    temperature = _parse_temperatures(temperature_literal)
    compression_ratio_threshold = _parse_float(compression_literal, default=2.4)
    log_prob_threshold = _parse_float(log_prob_literal, default=-1.0)
    no_speech_threshold = _parse_float(no_speech_literal, default=0.6)
    condition_on_previous_text = _parse_bool(condition_literal)

    try:
        segments, info = model_instance.transcribe(
            audio,
            language=language if language and language.lower() != 'auto' else None,
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
            'language': getattr(info, 'language', None),
            'language_probability': float(getattr(info, 'language_probability', 0.0) or 0.0),
            'segments': [],
        }

        for seg in segments:
            item = {
                'start': float(getattr(seg, 'start', 0.0) or 0.0),
                'end': float(getattr(seg, 'end', 0.0) or 0.0),
                'text': (getattr(seg, 'text', '') or '').strip(),
                'words': [],
            }

            for word in getattr(seg, 'words', []) or []:
                item['words'].append(
                    {
                        'start': float(getattr(word, 'start', 0.0) or 0.0),
                        'end': float(getattr(word, 'end', 0.0) or 0.0),
                        'word': (getattr(word, 'word', '') or ''),
                    }
                )

            data['segments'].append(item)

        out_file = out / 'transcript.json'
        out_file.write_text(json.dumps(data, ensure_ascii=False), encoding='utf-8')
        print('fw_runner OK ->', str(out_file))
    except Exception as exc:  # pragma: no cover - inference failure path
        print('INFERENCE ERROR:', exc, file=sys.stderr)
        traceback.print_exc()
        sys.exit(5)


if __name__ == '__main__':
    main()
