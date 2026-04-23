import argparse
import json
import os
import sys
import warnings


def parse_args():
    parser = argparse.ArgumentParser()
    parser.add_argument("--image", required=True)
    parser.add_argument("--groups", required=True)
    parser.add_argument("--gpu", default="false")
    return parser.parse_args()


def parse_language_groups(raw_value):
    groups = []
    seen = set()

    for chunk in (raw_value or "").split("|"):
        for token in chunk.replace(",", "+").split("+"):
            value = token.strip()
            if value and value not in seen:
                groups.append(value)
                seen.add(value)

    return groups


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


def build_lines_from_words(words):
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
                "words": [],
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
                    "top": float(line["top"]),
                    "bottom": float(line["bottom"]),
                    "text": text,
                }
            )

    return lines


def normalize_predict_payload(result_item):
    payload = getattr(result_item, "json", result_item)
    if isinstance(payload, str):
        payload = json.loads(payload)

    if isinstance(payload, dict) and "res" in payload:
        payload = payload["res"]

    return payload if isinstance(payload, dict) else {}


def build_words_from_predict_payload(payload):
    texts = payload.get("rec_texts") or []
    boxes = payload.get("rec_boxes") or []
    polys = payload.get("rec_polys") or []
    scores = payload.get("rec_scores") or []

    words = []

    for index, raw_text in enumerate(texts):
        text = str(raw_text).strip()
        if not text:
            continue

        score = scores[index] if index < len(scores) else None
        if score is not None:
            try:
                if float(score) <= 0.0:
                    continue
            except Exception:
                pass

        if index < len(boxes) and isinstance(boxes[index], (list, tuple)) and len(boxes[index]) >= 4:
            left = float(boxes[index][0])
            top = float(boxes[index][1])
            right = float(boxes[index][2])
            bottom = float(boxes[index][3])
        elif index < len(polys) and polys[index]:
            xs = [float(point[0]) for point in polys[index]]
            ys = [float(point[1]) for point in polys[index]]
            left = min(xs)
            right = max(xs)
            top = min(ys)
            bottom = max(ys)
        else:
            continue

        words.append(
            {
                "left": left,
                "right": right,
                "top": top,
                "bottom": bottom,
                "text": text,
            }
        )

    return words


def build_words_from_legacy_payload(payload):
    if not payload:
        return []

    rows = payload[0] if isinstance(payload, list) and payload and isinstance(payload[0], list) else payload
    words = []

    for row in rows or []:
        if not isinstance(row, (list, tuple)) or len(row) < 2:
            continue

        bbox = row[0]
        result = row[1]
        if not isinstance(result, (list, tuple)) or not result:
            continue

        text = str(result[0]).strip()
        if not text:
            continue

        xs = [float(point[0]) for point in bbox]
        ys = [float(point[1]) for point in bbox]
        words.append(
            {
                "left": min(xs),
                "right": max(xs),
                "top": min(ys),
                "bottom": max(ys),
                "text": text,
            }
        )

    return words


def recognize_with_predict(ocr, image_path):
    words = []
    for result_item in ocr.predict(image_path):
        words.extend(build_words_from_predict_payload(normalize_predict_payload(result_item)))
    return build_lines_from_words(words)


def recognize_with_legacy_ocr(ocr, image_path):
    results = ocr.ocr(image_path, cls=False)
    return build_lines_from_words(build_words_from_legacy_payload(results))


def recognize_image(language_code, image_path, use_gpu):
    # PaddlePaddle 3.3.x CPU inference can fail in PIR/oneDNN conversion.
    # Set this before importing paddleocr so the diagnostic runner stays usable.
    os.environ.setdefault("FLAGS_enable_pir_api", "0")

    from paddleocr import PaddleOCR

    try:
        ocr = PaddleOCR(
            lang=language_code,
            device="gpu" if use_gpu else "cpu",
            use_doc_orientation_classify=False,
            use_doc_unwarping=False,
            use_textline_orientation=False,
        )
    except TypeError:
        ocr = PaddleOCR(
            lang=language_code,
            use_angle_cls=False,
            use_gpu=use_gpu,
            show_log=False,
        )

    if hasattr(ocr, "predict"):
        return recognize_with_predict(ocr, image_path)

    return recognize_with_legacy_ocr(ocr, image_path)


def main():
    args = parse_args()
    language_groups = parse_language_groups(args.groups)
    if not language_groups:
        print("PaddleOCR language groups are empty.", file=sys.stderr)
        return 2

    use_gpu = str(args.gpu).strip().lower() in ("1", "true", "yes", "y", "on")

    try:
        import paddleocr  # noqa: F401
    except Exception:
        print("PaddleOCR module is not installed.", file=sys.stderr)
        return 3

    warnings.filterwarnings("ignore")

    response = {"groups": []}

    for language_code in language_groups:
        try:
            lines = recognize_image(language_code, args.image, use_gpu)
            response["groups"].append(
                {
                    "languageCodes": language_code,
                    "success": True,
                    "error": "",
                    "lines": lines,
                }
            )
        except Exception as exc:
            response["groups"].append(
                {
                    "languageCodes": language_code,
                    "success": False,
                    "error": str(exc),
                    "lines": [],
                }
            )

    print(json.dumps(response, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    sys.exit(main())
