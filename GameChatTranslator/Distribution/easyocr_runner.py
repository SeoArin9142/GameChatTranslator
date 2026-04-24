import argparse
import json
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


def main():
    args = parse_args()
    language_groups = parse_language_groups(args.groups)
    if not language_groups:
        print("EasyOCR language groups are empty.", file=sys.stderr)
        return 2

    use_gpu = str(args.gpu).strip().lower() in ("1", "true", "yes", "y", "on")

    try:
        import easyocr
    except Exception:
        print("EasyOCR module is not installed.", file=sys.stderr)
        return 3

    warnings.filterwarnings(
        "ignore",
        message=".*pin_memory.*",
        category=UserWarning,
    )

    response = {"groups": []}

    for languages in language_groups:
        language_codes = "+".join(languages)
        try:
            reader = easyocr.Reader(languages, gpu=use_gpu, verbose=False)
            results = reader.readtext(args.image, detail=1, paragraph=False)
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

    print(json.dumps(response, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    sys.exit(main())
