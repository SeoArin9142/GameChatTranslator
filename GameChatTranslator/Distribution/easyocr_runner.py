import argparse
import json
import sys
import warnings


EASYOCR_MODULE = None
EASYOCR_IMPORT_ERROR = None
READER_CACHE = {}


def parse_args():
    parser = argparse.ArgumentParser()
    parser.add_argument("--image")
    parser.add_argument("--groups")
    parser.add_argument("--gpu", default="false")
    parser.add_argument("--worker", action="store_true")
    return parser.parse_args()


def parse_language_groups(raw_value):
    groups = []
    for chunk in (raw_value or "").split("|"):
        tokens = []
        for token in chunk.replace(",", "+").split("+"):
            value = token.strip()
            if value and value not in tokens:
                tokens.append(value)
        if tokens:
            groups.append(tokens)
    return groups


def make_word(result):
    bbox = result[0]
    text = str(result[1]).strip()
    if not text:
        return None

    xs = [float(point[0]) for point in bbox]
    ys = [float(point[1]) for point in bbox]
    return {
        "left": min(xs),
        "right": max(xs),
        "top": min(ys),
        "bottom": max(ys),
        "text": text,
    }


def is_cjk(char):
    if not char:
        return False

    code = ord(char)
    return (
        0x4E00 <= code <= 0x9FFF
        or 0x3400 <= code <= 0x4DBF
        or 0x3040 <= code <= 0x30FF
        or 0xAC00 <= code <= 0xD7AF
    )


def append_token(current_text, token_text):
    token_text = (token_text or "").strip()
    if not token_text:
        return current_text

    if not current_text:
        return token_text

    previous_char = current_text[-1]
    next_char = token_text[0]

    no_space_before = ")]}:;!?.,%>/"
    no_space_after = "([<{"

    if previous_char in no_space_after or next_char in no_space_before:
        return current_text + token_text

    if is_cjk(previous_char) and is_cjk(next_char):
        return current_text + token_text

    return current_text + " " + token_text


def same_line(word, line):
    word_top = word["top"]
    word_bottom = word["bottom"]
    line_top = line["top"]
    line_bottom = line["bottom"]

    word_height = max(1.0, word_bottom - word_top)
    line_height = max(1.0, line_bottom - line_top)
    overlap = min(word_bottom, line_bottom) - max(word_top, line_top)
    center_delta = abs(((word_top + word_bottom) / 2.0) - ((line_top + line_bottom) / 2.0))

    return overlap >= max(2.0, min(word_height, line_height) * 0.3) or center_delta <= max(word_height, line_height) * 0.6


def build_lines(results):
    words = []
    for result in results:
        word = make_word(result)
        if word is not None:
            words.append(word)

    words.sort(key=lambda item: (item["top"], item["left"]))
    line_groups = []

    for word in words:
        target_line = None
        for line in line_groups:
            if same_line(word, line):
                target_line = line
                break

        if target_line is None:
            target_line = {
                "top": word["top"],
                "bottom": word["bottom"],
                "words": []
            }
            line_groups.append(target_line)

        target_line["top"] = min(target_line["top"], word["top"])
        target_line["bottom"] = max(target_line["bottom"], word["bottom"])
        target_line["words"].append(word)

    line_groups.sort(key=lambda item: item["top"])

    lines = []
    for line in line_groups:
        text = ""
        for word in sorted(line["words"], key=lambda item: item["left"]):
            text = append_token(text, word["text"])

        text = text.strip()
        if text:
            lines.append(
                {
                    "top": line["top"],
                    "bottom": line["bottom"],
                    "text": text,
                }
            )

    return lines


def import_easyocr():
    global EASYOCR_MODULE
    global EASYOCR_IMPORT_ERROR

    if EASYOCR_MODULE is not None:
        return EASYOCR_MODULE

    if EASYOCR_IMPORT_ERROR is not None:
        raise EASYOCR_IMPORT_ERROR

    try:
        import easyocr
        EASYOCR_MODULE = easyocr
        return EASYOCR_MODULE
    except Exception as exc:
        EASYOCR_IMPORT_ERROR = exc
        raise


def get_reader(languages, use_gpu):
    cache_key = ("+".join(languages), bool(use_gpu))
    if cache_key in READER_CACHE:
        return READER_CACHE[cache_key]

    easyocr = import_easyocr()
    reader = easyocr.Reader(languages, gpu=use_gpu, verbose=False)
    READER_CACHE[cache_key] = reader
    return reader


def run_groups(image_path, raw_groups, use_gpu):
    language_groups = parse_language_groups(raw_groups)
    if not language_groups:
        return {
            "ok": False,
            "error": "EasyOCR language groups are empty.",
            "errorCode": "invalid_request",
            "groups": []
        }

    response = {
        "ok": True,
        "error": "",
        "errorCode": "",
        "groups": []
    }

    for languages in language_groups:
        language_codes = "+".join(languages)
        try:
            reader = get_reader(languages, use_gpu)
            results = reader.readtext(image_path, detail=1, paragraph=False)
            response["groups"].append(
                {
                    "language_codes": language_codes,
                    "success": True,
                    "error": "",
                    "lines": build_lines(results),
                }
            )
        except Exception as exc:
            response["groups"].append(
                {
                    "language_codes": language_codes,
                    "success": False,
                    "error": str(exc),
                    "lines": [],
                }
            )

    return response


def write_worker_response(payload):
    sys.stdout.write(json.dumps(payload, ensure_ascii=False) + "\n")
    sys.stdout.flush()


def run_worker():
    warnings.filterwarnings(
        "ignore",
        message=".*pin_memory.*",
        category=UserWarning,
    )

    while True:
        raw_line = sys.stdin.readline()
        if raw_line == "":
            return 0

        raw_line = raw_line.strip()
        if not raw_line:
            continue

        request_id = ""
        try:
            payload = json.loads(raw_line)
            request_id = str(payload.get("requestId", "")).strip()
            image_path = str(payload.get("imagePath", "")).strip()
            raw_groups = str(payload.get("groups", "")).strip()
            use_gpu = bool(payload.get("gpu", False))

            if not image_path:
                write_worker_response(
                    {
                        "requestId": request_id,
                        "ok": False,
                        "error": "EasyOCR image path is empty.",
                        "errorCode": "invalid_request",
                        "groups": []
                    }
                )
                continue

            try:
                response = run_groups(image_path, raw_groups, use_gpu)
            except Exception as exc:
                write_worker_response(
                    {
                        "requestId": request_id,
                        "ok": False,
                        "error": str(exc),
                        "errorCode": "module_missing" if EASYOCR_IMPORT_ERROR is not None else "runtime_error",
                        "groups": []
                    }
                )
                continue

            response["requestId"] = request_id
            write_worker_response(response)
        except Exception as exc:
            write_worker_response(
                {
                    "requestId": request_id,
                    "ok": False,
                    "error": str(exc),
                    "errorCode": "runtime_error",
                    "groups": []
                }
            )


def main():
    args = parse_args()
    use_gpu = str(args.gpu).strip().lower() in ("1", "true", "yes", "y", "on")

    if args.worker:
        return run_worker()

    if not args.image:
        print("EasyOCR image path is empty.", file=sys.stderr)
        return 2

    warnings.filterwarnings(
        "ignore",
        message=".*pin_memory.*",
        category=UserWarning,
    )

    try:
        import_easyocr()
    except Exception:
        print("EasyOCR module is not installed.", file=sys.stderr)
        return 3

    response = run_groups(args.image, args.groups, use_gpu)
    if not response.get("ok", False):
        print(response.get("error", "EasyOCR execution failed."), file=sys.stderr)
        return 4

    print(json.dumps({"groups": response["groups"]}, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    sys.exit(main())
