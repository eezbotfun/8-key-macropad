# Host guide: USB CDC customised display protocol (`cus`)

This document describes how a host PC (or other USB host) sends framed messages over the device’s **USB CDC ACM** interface to drive the **customised** app mode on the device. The firmware parses these frames in `[main/config.c](../main/config.c)` and renders them via LVGL in `[main/customised_app.c](../main/customised_app.c)`.

For reference, the device may share the CDC port with other framed protocols (for example keyboard configuration with magic `ebf`, and PC status with `pcs`). Host tools should send **complete `cus` frames** as defined below.

For **now playing** metadata and album art (`pcs` cmd `1231`, USB `player/cover.bin`), see the normative spec in the music player plugin repo: `docs/MEDIA_NOW_PLAYING_DEVICE_PROTOCOL.txt`. Binary layout reference: `docs/cover_bin_format.md` and example `docs/cover.bin`.

---

## 0. Display layout (drawable canvas)

On the **reference 480×320** landscape panel, the host draws on a **drawable canvas**. Layout depends on `fullscreen`:

| Mode | Drawable canvas | Key strip (on-screen UI) |
| ---- | --------------- | ------------------------ |
| **Strip** (`fullscreen` false / omitted) | **480×200** (`LV_HOR_RES` × `LV_VER_RES − 120`) | Visible **480×120**; not drawable via `cus` |
| **Full-screen** (`fullscreen` true) | **480×320** (full panel) | Hidden |

### Coordinate system

All JSON geometry (`x`, `y`, `w`, `h`) is in **pixels** with origin at the **top-left of the drawable canvas** `(0, 0)`. Firmware clamps the rectangle to the current canvas (`clamp_rect_custom_area`).

**JSON defaults** when `w` / `h` are omitted: `w = LV_HOR_RES`; `h` = canvas height (**200** strip / **320** full-screen on the reference panel).

Text panels use **6 px** padding for the label; the outer `(x, y, w, h)` box is what you set in JSON.

---

## 1. Wire format

Each logical message is one binary frame:

| Offset | Size | Description |
| ------ | ---- | ----------- |
| 0 | 3 bytes | ASCII magic: `c` `u` `s` (0x63, 0x75, 0x73) |
| 3 | 2 bytes | Payload length **N**, **big-endian** uint16 |
| 5 | **N** bytes | UTF-8 JSON object (see §2) |

- **N** must satisfy **1 ≤ N ≤ 2048** (`PARSER_MAX_DATA_LEN`).
- Practical max: **N ≤ 2043** (`MAX_PRO_BUF_LEN` − 5).

### 1.1 Framing caveats

- Prefer sending the **entire frame in one `write()`**.
- Do not embed **NUL** (0x00) inside the JSON (firmware NUL-terminates for `cJSON_Parse`).
- Keep traffic aligned to intended frame types (`cus` / `pcs` / `ebf`).

---

## 2. JSON schema (payload)

| Field | Type | Required | Description |
| ----- | ---- | -------- | ----------- |
| `x` | integer | no | Left of canvas (default `0`). |
| `y` | integer | no | Top of canvas (default `0`). |
| `w` | integer | no | Width (default full canvas width, e.g. **480**). |
| `h` | integer | no | Height (default full canvas height for current mode). |
| `text` | string | no | UTF-8 label for **one new stacked text panel**. Empty/omit → no text panel. |
| `image` | string | no | PNG **basename** under USB `customised/` (e.g. `"bg.png"`). Empty/omit → no image. See §2.6. |
| `fullscreen` | bool or int | no | `true` → full-screen canvas (hide key strip). Default `false`. |
| `fg` | string | no | Text foreground `#RRGGBB` (default `#FFFFFF`). |
| `bg` | string | no | Text panel background `#RRGGBB` (default `#000000`). |
| `align` | string | no | Text align (default `LEFT`). See §2.1. |
| `long_mode` | string | no | Label long mode (default `WRAP`). See §2.2. |
| `cmd` | string | no | `start` / `stop` / `update`. See §2.5. |
| `activate` | bool or int | no | **Legacy** when `cmd` omitted: true→`start`, false→`update`. |
| `border` | bool or int | no | Panel outline. See §2.3. |
| `border-color` | string | no | Outline `#RRGGBB` when border on (default `#888888`). |
| `border-radius` | integer | no | Corner radius of new text panel. |
| `clear_canvas` | bool or int | no | Remove all stacked overlays (text + images). Does not affect key strip or LEDs. |
| `leds` | object | no | Per-key RGB backlight. Omitted → leave LEDs unchanged. See §2.7. |

**Font:** embedded `regular` (same as micropad captions). No host-selectable font.

### 2.1 `align` (case-insensitive)

| Value | Meaning |
| ----- | ------- |
| `LEFT` / `CENTER` / `RIGHT` / `AUTO` | LVGL 8 text alignment |

Unknown → `LEFT`.

### 2.2 `long_mode` (case-insensitive)

| Value | Meaning |
| ----- | ------- |
| `WRAP` / `SCROLL` / `SCROLL_CIRCULAR` / `CLIP` / `DOT` | LVGL 8 label long mode |

Unknown → `WRAP`.

### 2.3 `border`, `border-color`, `border-radius`

Style the **text panel** rectangle only (not images). Independent: `border-radius` may be set without `border`.

### 2.4 Stacking, `text`, `image`, and `clear_canvas`

- Optional `clear_canvas`, then optionally append **one text panel** (non-empty `text`) and/or **one image** (non-empty `image`) in the same frame.
- Later frames stack; only `clear_canvas` removes host overlays.
- Cap: **20** overlays (`CUS_MAX_CANVAS_OVERLAYS`); oldest deleted first.

### 2.5 `cmd` / `activate`

| `cmd` | Behaviour |
| ----- | --------- |
| `start` | Mark customised plugin active; switch to customised app (unless now-playing). |
| `stop` | Exit customised; restore NVS LED config; deactivate plugin flag. |
| `update` | Apply canvas/LED updates without forcing app switch. |

When `cmd` omitted: `activate` true → `start`; false → `update`.

### 2.6 Images (MSC)

1. Host writes PNG to USB mass storage: `{USB_ROOT}/customised/<name>.png`.
2. Host sends `cus` with `"image":"<name>.png"` (basename only; must end in `.png`; no `/`, `\`, or `..`).
3. Firmware loads `S:/spiflash/customised/<name>.png` via LVGL.

Do **not** put image bytes or base64 inside the `cus` JSON (2048-byte limit). Desktop bridges may accept `image_b64` over EZBF IPC, write MSC, then strip base64 before CDC.

### 2.7 `leds` (per-key RGB backlight)

Controls the **8× SK6812** key lights (not the LCD panel backlight).

| Field | Type | Description |
| ----- | ---- | ----------- |
| `on` | bool or int | Master enable. `false` → all keys off. Default when `leds` present: `true`. |
| `keys` | array | Length **1..8**. Index **0..7** = logical key index. Each entry: `#RRGGBB` string; `null` → leave that key unchanged; `"#000000"` or `false` → that key off. |

While customised LED control is active, host-driven colours win. On `"cmd":"stop"`, firmware restores the saved global LED config (`ws2812_update()`).

---

## 3. Behaviour summary

- Strip mode: canvas + on-screen key strip.
- Full-screen mode: full-panel canvas; key strip UI hidden (physical keys still work).
- `activate`/`cmd:start` switches to `e_app_customised` (unless now-playing).

---

## 4. Building a frame (examples)

### 4.1 Python 3 (pyserial)

```python
import json
import serial

def build_cus_frame(obj: dict) -> bytes:
    payload = json.dumps(obj, separators=(",", ":")).encode("utf-8")
    n = len(payload)
    if n < 1 or n > 2048:
        raise ValueError(f"payload length {n} out of range")
    return b"cus" + n.to_bytes(2, "big") + payload

ser = serial.Serial("COM5", 115200, timeout=1)
frame = build_cus_frame({
    "cmd": "start",
    "fullscreen": True,
    "clear_canvas": True,
    "x": 0, "y": 0, "w": 480, "h": 320,
    "image": "bg.png",
    "leds": {
        "on": True,
        "keys": ["#FF0000","#00FF00","#0000FF","#FFFFFF",
                 "#FFAA00","#00FFFF","#FF00FF","#000000"]
    },
})
ser.write(frame)
ser.flush()
```

### 4.2 Minimal JSON examples

Strip-mode text:

```json
{"x":0,"y":0,"w":480,"h":200,"text":"Loading…","fg":"#EEEEEE","bg":"#111111","align":"CENTER","cmd":"start"}
```

Full-screen image (PNG already on MSC at `customised/bg.png`):

```json
{"cmd":"start","fullscreen":true,"clear_canvas":true,"x":0,"y":0,"w":480,"h":320,"image":"bg.png"}
```

Master LED off:

```json
{"cmd":"update","leds":{"on":false}}
```

Clear overlays:

```json
{"clear_canvas":true,"cmd":"update"}
```

---

## 5. Limits checklist

| Limit | Value | Source |
| ----- | ----- | ------ |
| Strip canvas | **480×200** | `CUS_TOP_H` |
| Full-screen canvas | **480×320** | `LV_HOR_RES` × `LV_VER_RES` |
| Max stacked overlays | **20** | `CUS_MAX_CANVAS_OVERLAYS` |
| Max payload **N** | 2048 | `PARSER_MAX_DATA_LEN` |
| Practical **N** | ≤ 2043 | reassembly buffer − 5 |
| Image path | basename `.png` under `customised/` | MSC + LVGL FS |

---

## 6. Related protocols on the same CDC interface

- **`ebf`** + 1-byte length: keyboard / configuration.
- **`pcs`** + 2-byte BE length: PC status / now playing.

---

## 7. Verification tips

1. Open the device CDC COM port.
2. Send `cus` with `"cmd":"start"` and distinct `fg`/`bg` to confirm customised UI.
3. Toggle `"fullscreen":true` and confirm key strip hides and canvas uses full height.
4. Copy a PNG to `customised/bg.png` on the USB volume, then send `"image":"bg.png"`.
5. Send `leds` colours and confirm per-key RGB; send `"cmd":"stop"` and confirm NVS LED mode returns.

If parsing fails, check logs (tag `cus_app`) and UTF-8/JSON validity.
